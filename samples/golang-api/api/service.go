package main

import (
	"context"
	"errors"
	"log/slog"
	"maps"
	"math"
	"slices"
	"strings"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
)

// Rejection reasons, used as the bounded "reason" attribute on workorders.rejected and in logs.
const (
	reasonValidation = "validation"
	reasonNotFound   = "not_found"
	reasonClosed     = "closed"
	reasonCapacity   = "capacity"
)

// Service runs work-order operations against the store and records the domain telemetry for each:
// a span with work_order.* attributes and lifecycle events, FieldService.WorkOrders metrics, and
// structured logs that carry the active trace context.
type Service struct {
	store  *Store
	log    *slog.Logger
	tracer trace.Tracer
	now    func() time.Time

	created     metric.Int64Counter
	transitions metric.Int64Counter
	rejected    metric.Int64Counter
	resolution  metric.Float64Histogram
	active      metric.Int64UpDownCounter
}

func NewService(store *Store, logger *slog.Logger) (*Service, error) {
	meter := otel.Meter(instrumentationName)
	s := &Service{store: store, log: logger, tracer: otel.Tracer(instrumentationName), now: func() time.Time { return time.Now().UTC() }}

	var errs [5]error
	s.created, errs[0] = meter.Int64Counter("workorders.created",
		metric.WithUnit("{work_order}"), metric.WithDescription("Work orders opened, by priority."))
	s.transitions, errs[1] = meter.Int64Counter("workorders.status_transitions",
		metric.WithUnit("{transition}"), metric.WithDescription("Work-order status changes, by previous and new status."))
	s.rejected, errs[2] = meter.Int64Counter("workorders.rejected",
		metric.WithUnit("{request}"), metric.WithDescription("Requests rejected by validation or dispatch rules, by reason."))
	s.resolution, errs[3] = meter.Float64Histogram("workorders.resolution.duration",
		metric.WithUnit("h"), metric.WithDescription("Time from an issue being reported to its work order being completed."),
		metric.WithExplicitBucketBoundaries(1, 2, 4, 8, 12, 24, 48, 72, 120, 168))
	s.active, errs[4] = meter.Int64UpDownCounter("workorders.active",
		metric.WithUnit("{work_order}"), metric.WithDescription("Open and in-progress work orders, by priority."))
	return s, errors.Join(errs[:]...)
}

func priorityAttr(p Priority) metric.MeasurementOption {
	return metric.WithAttributes(attribute.String("work_order.priority", string(p)))
}

func orderAttributes(o WorkOrder) []attribute.KeyValue {
	attrs := []attribute.KeyValue{
		attribute.Int("work_order.id", o.ID),
		attribute.String("work_order.priority", string(o.Priority)),
		attribute.String("work_order.status", string(o.Status)),
		attribute.Bool("work_order.overdue", o.Overdue),
	}
	if o.Assignee != "" {
		attrs = append(attrs, attribute.String("work_order.assignee", o.Assignee))
	}
	return attrs
}

// reject records a refused request on the span, the workorders.rejected counter, and a warning log.
func (s *Service) reject(ctx context.Context, span trace.Span, reason string, err error, msg string, args ...any) error {
	span.AddEvent("work_order.rejected", trace.WithAttributes(attribute.String("reason", reason), attribute.String("detail", err.Error())))
	span.SetAttributes(attribute.String("error.type", reason))
	span.SetStatus(codes.Error, err.Error())
	s.rejected.Add(ctx, 1, metric.WithAttributes(attribute.String("reason", reason)))
	s.log.WarnContext(ctx, msg, append(args, "reason", reason)...)
	return err
}

func (s *Service) rejectInvalid(ctx context.Context, span trace.Span, err error, args ...any) error {
	var invalid *ValidationError
	if errors.As(err, &invalid) {
		fields := slices.Sorted(maps.Keys(invalid.Errors))
		span.SetAttributes(attribute.StringSlice("work_order.invalid_fields", fields))
		args = append(args, "work_order.invalid_fields", strings.Join(fields, ","))
	}
	return s.reject(ctx, span, reasonValidation, err, "Work order rejected by validation", args...)
}

func (s *Service) List(ctx context.Context, f Filter) ([]WorkOrder, error) {
	ctx, span := s.tracer.Start(ctx, "work_orders.list")
	defer span.End()

	v := &ValidationError{}
	if f.Status != "" && !f.Status.valid() {
		v.add("status", "The status filter must be one of open, in_progress, completed, or cancelled.")
	}
	if f.Priority != "" && !f.Priority.valid() {
		v.add("priority", "The priority filter must be one of low, normal, high, or urgent.")
	}
	if err := v.orNil(); err != nil {
		return nil, s.rejectInvalid(ctx, span, err)
	}

	orders := s.store.List(f, s.now())
	if f.Status != "" {
		span.SetAttributes(attribute.String("work_order.filter.status", string(f.Status)))
	}
	if f.Priority != "" {
		span.SetAttributes(attribute.String("work_order.filter.priority", string(f.Priority)))
	}
	if f.Assignee != "" {
		span.SetAttributes(attribute.String("work_order.filter.assignee", f.Assignee))
	}
	span.SetAttributes(attribute.Int("work_order.result_count", len(orders)))
	return orders, nil
}

func (s *Service) Get(ctx context.Context, id int) (WorkOrder, error) {
	ctx, span := s.tracer.Start(ctx, "work_order.get", trace.WithAttributes(attribute.Int("work_order.id", id)))
	defer span.End()

	o, err := s.store.Get(id, s.now())
	if err != nil {
		return o, s.reject(ctx, span, reasonNotFound, err, "Work order lookup failed", "work_order.id", id)
	}
	span.SetAttributes(orderAttributes(o)...)
	return o, nil
}

func (s *Service) Create(ctx context.Context, req CreateWorkOrderRequest) (WorkOrder, error) {
	ctx, span := s.tracer.Start(ctx, "work_order.create")
	defer span.End()

	now := s.now()
	if err := req.validate(now); err != nil {
		return WorkOrder{}, s.rejectInvalid(ctx, span, err)
	}

	o := WorkOrder{
		Title:      strings.TrimSpace(req.Title),
		Priority:   req.Priority,
		Status:     StatusOpen,
		Assignee:   strings.TrimSpace(req.Assignee),
		DueDate:    req.DueDate,
		ReportedAt: now,
		CreatedAt:  now,
		UpdatedAt:  now,
	}
	if o.Priority == "" {
		o.Priority = PriorityNormal
	}
	if req.ReportedAt != nil {
		o.ReportedAt = req.ReportedAt.UTC()
	}

	o, err := s.store.Create(o, now)
	if err != nil {
		return o, s.reject(ctx, span, reasonCapacity, err, "Work order rejected because the backlog is at capacity")
	}

	span.SetAttributes(orderAttributes(o)...)
	span.AddEvent("work_order.created", trace.WithAttributes(attribute.String("work_order.priority", string(o.Priority))))
	if o.Assignee != "" {
		span.AddEvent("work_order.assigned", trace.WithAttributes(attribute.String("work_order.assignee", o.Assignee)))
	}
	s.created.Add(ctx, 1, priorityAttr(o.Priority))
	s.active.Add(ctx, 1, priorityAttr(o.Priority))
	s.log.InfoContext(ctx, "Work order opened",
		"work_order.id", o.ID, "work_order.priority", string(o.Priority), "work_order.assignee", o.Assignee, "work_order.due_date", o.DueDate)
	return o, nil
}

func (s *Service) Update(ctx context.Context, id int, req UpdateWorkOrderRequest) (WorkOrder, error) {
	ctx, span := s.tracer.Start(ctx, "work_order.update", trace.WithAttributes(attribute.Int("work_order.id", id)))
	defer span.End()

	if err := req.validate(); err != nil {
		return WorkOrder{}, s.rejectInvalid(ctx, span, err, "work_order.id", id)
	}

	now := s.now()
	before, after, err := s.store.Update(id, now, func(o *WorkOrder) error { return req.apply(o, now) })
	switch {
	case errors.Is(err, errNotFound):
		return after, s.reject(ctx, span, reasonNotFound, err, "Work order lookup failed", "work_order.id", id)
	case errors.Is(err, errClosed):
		span.SetAttributes(orderAttributes(before)...)
		return after, s.reject(ctx, span, reasonClosed, err, "Work order update rejected",
			"work_order.id", id, "work_order.status", string(before.Status))
	case err != nil:
		return after, s.rejectInvalid(ctx, span, err, "work_order.id", id)
	}

	span.SetAttributes(orderAttributes(after)...)
	if before.Assignee != after.Assignee {
		span.AddEvent("work_order.assigned", trace.WithAttributes(
			attribute.String("work_order.assignee.previous", before.Assignee), attribute.String("work_order.assignee", after.Assignee)))
		s.log.InfoContext(ctx, "Work order assigned",
			"work_order.id", id, "work_order.assignee.previous", before.Assignee, "work_order.assignee", after.Assignee)
	}
	if after.Status.closed() || before.Priority != after.Priority {
		s.active.Add(ctx, -1, priorityAttr(before.Priority))
	}
	if !after.Status.closed() && before.Priority != after.Priority {
		s.active.Add(ctx, 1, priorityAttr(after.Priority))
	}
	if before.Status == after.Status {
		return after, nil
	}

	span.AddEvent("work_order.status_changed", trace.WithAttributes(
		attribute.String("work_order.status.from", string(before.Status)), attribute.String("work_order.status.to", string(after.Status))))
	s.transitions.Add(ctx, 1, metric.WithAttributes(
		attribute.String("work_order.status.from", string(before.Status)), attribute.String("work_order.status.to", string(after.Status))))

	if after.Status != StatusCompleted {
		s.log.InfoContext(ctx, "Work order status changed",
			"work_order.id", id, "work_order.status.from", string(before.Status), "work_order.status.to", string(after.Status))
		return after, nil
	}

	// Resolution time is measured from when the customer reported the issue, not from when the work order was created.
	hours := math.Round(now.Sub(after.ReportedAt).Hours()*100) / 100
	span.SetAttributes(attribute.Float64("work_order.resolution_hours", hours))
	s.resolution.Record(ctx, hours, priorityAttr(after.Priority))
	s.log.InfoContext(ctx, "Work order completed",
		"work_order.id", id, "work_order.priority", string(after.Priority), "work_order.assignee", after.Assignee,
		"work_order.resolution_hours", hours)
	return after, nil
}

func (s *Service) Delete(ctx context.Context, id int) error {
	ctx, span := s.tracer.Start(ctx, "work_order.delete", trace.WithAttributes(attribute.Int("work_order.id", id)))
	defer span.End()

	o, err := s.store.Delete(id, s.now())
	if err != nil {
		return s.reject(ctx, span, reasonNotFound, err, "Work order lookup failed", "work_order.id", id)
	}
	span.SetAttributes(orderAttributes(o)...)
	span.AddEvent("work_order.deleted")
	if !o.Status.closed() {
		s.active.Add(ctx, -1, priorityAttr(o.Priority))
	}
	s.log.InfoContext(ctx, "Work order deleted", "work_order.id", id, "work_order.status", string(o.Status))
	return nil
}

// Seed replaces every work order with the sample backlog.
func (s *Service) Seed(ctx context.Context) int {
	ctx, span := s.tracer.Start(ctx, "work_orders.seed")
	defer span.End()

	now := s.now()
	removed, added := s.store.Reset(sampleBacklog(now), now)
	for _, o := range removed {
		if !o.Status.closed() {
			s.active.Add(ctx, -1, priorityAttr(o.Priority))
		}
	}
	for _, o := range added {
		if !o.Status.closed() {
			s.active.Add(ctx, 1, priorityAttr(o.Priority))
		}
	}
	span.SetAttributes(attribute.Int("work_order.removed_count", len(removed)), attribute.Int("work_order.seeded_count", len(added)))
	s.log.InfoContext(ctx, "Seeded work-order backlog", "work_order.seeded_count", len(added), "work_order.removed_count", len(removed))
	return len(added)
}
