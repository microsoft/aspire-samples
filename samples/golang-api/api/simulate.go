package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"math/rand/v2"
	"net/http"
	"strconv"
	"time"

	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/trace"
)

var technicians = []string{"Aiko Tanaka", "Dev Patel", "Jordan Reyes", "Lena Fischer", "Maria Gonzalez", "Sam Okafor"}

// sampleBacklog is a realistic mid-week queue for a commercial HVAC, refrigeration, electrical, and
// plumbing contractor.
func sampleBacklog(now time.Time) []WorkOrder {
	type job struct {
		title    string
		priority Priority
		status   Status
		assignee string
		reported time.Duration
		dueIn    int
	}
	jobs := []job{
		{"Walk-in freezer alarm — Harbor Bistro", PriorityUrgent, StatusInProgress, "Aiko Tanaka", 2 * time.Hour, 0},
		{"Tripped breaker on server room circuit — Contoso HQ", PriorityUrgent, StatusOpen, "", 45 * time.Minute, 0},
		{"Rooftop unit not cooling — Mercy Clinic", PriorityHigh, StatusOpen, "Dev Patel", 9 * time.Hour, 1},
		{"Leaking water heater in staff kitchen — Northgate Offices", PriorityHigh, StatusInProgress, "Lena Fischer", 20 * time.Hour, 0},
		{"Grease trap backup — Pho Saigon, Market St", PriorityHigh, StatusOpen, "Maria Gonzalez", 5 * time.Hour, 1},
		{"Ice machine producing cloudy ice — Bayview Hotel", PriorityNormal, StatusOpen, "Sam Okafor", 30 * time.Hour, 2},
		{"Noisy compressor on reach-in cooler — Corner Grocer", PriorityNormal, StatusInProgress, "Jordan Reyes", 52 * time.Hour, -1},
		{"Replace failed emergency exit sign — Riverside Library", PriorityNormal, StatusOpen, "", 72 * time.Hour, 1},
		{"Thermostat replacement in conference rooms — Fabrikam Labs", PriorityNormal, StatusCompleted, "Dev Patel", 96 * time.Hour, -1},
		{"Quarterly boiler inspection — Oakwood Apartments", PriorityLow, StatusOpen, "Jordan Reyes", 144 * time.Hour, 5},
		{"Annual fire sprinkler test — Lakeside Elementary", PriorityLow, StatusOpen, "", 48 * time.Hour, 12},
		{"Flickering parking-lot lights — Westfield Plaza", PriorityLow, StatusCancelled, "Sam Okafor", 192 * time.Hour, 3},
	}

	orders := make([]WorkOrder, 0, len(jobs))
	for _, j := range jobs {
		reported := now.Add(-j.reported).Truncate(time.Minute)
		o := WorkOrder{
			Title:      j.title,
			Priority:   j.priority,
			Status:     j.status,
			Assignee:   j.assignee,
			DueDate:    now.AddDate(0, 0, j.dueIn).Format(dateLayout),
			ReportedAt: reported,
			CreatedAt:  reported.Add(10 * time.Minute),
			UpdatedAt:  now.Add(-j.reported / 3).Truncate(time.Minute),
		}
		if o.Status == StatusCompleted {
			completed := o.UpdatedAt
			o.CompletedAt = &completed
		}
		orders = append(orders, o)
	}
	return orders
}

var (
	issues = []string{
		"Walk-in cooler running warm", "No heat in east wing", "Water leak under prep sink", "Emergency lighting test failed",
		"Rooftop unit short-cycling", "Ice machine not dispensing", "Breaker trips when ovens start", "Backflow preventer inspection",
		"Rattle from air handler", "Water heater pilot out", "Exit sign battery replacement", "Clogged floor drain in kitchen",
		"Thermostat unresponsive", "Dishwasher not reaching sanitize temperature", "Generator monthly load test",
	}
	sites = []string{
		"Harbor Bistro", "Mercy Clinic", "Northgate Offices", "Bayview Hotel", "Lakeside Elementary", "Contoso HQ",
		"Fabrikam Labs", "Corner Grocer", "Westfield Plaza", "Pho Saigon", "Riverside Library", "Oakwood Apartments",
	}
)

// reportedLag models how long ago the customer reported an issue before dispatch opened the work order
// (for example, an after-hours call), so resolution times land on a realistic hour scale.
func reportedLag(p Priority) time.Duration {
	hours := map[Priority][2]float64{
		PriorityUrgent: {0.5, 4}, PriorityHigh: {2, 16}, PriorityNormal: {6, 48}, PriorityLow: {24, 120},
	}[p]
	return time.Duration((hours[0] + rand.Float64()*(hours[1]-hours[0])) * float64(time.Hour))
}

func randomPriority() Priority {
	switch n := rand.IntN(100); {
	case n < 15:
		return PriorityUrgent
	case n < 45:
		return PriorityHigh
	case n < 85:
		return PriorityNormal
	default:
		return PriorityLow
	}
}

// SimulationResult summarizes one run of simulated dispatch traffic.
type SimulationResult struct {
	Requests  int            `json:"requests"`
	Created   int            `json:"created"`
	Started   int            `json:"started"`
	Completed int            `json:"completed"`
	Rejected  int            `json:"rejected"`
	Statuses  map[string]int `json:"statuses"`
}

type routeKey struct{}

// Simulator acts like a dispatch console: it calls the API over HTTP so each request produces a client
// span, a server span, and a domain span in one trace.
type Simulator struct {
	baseURL string
	client  *http.Client
	tracer  trace.Tracer
	log     *slog.Logger
}

func NewSimulator(baseURL string, svc *Service) *Simulator {
	transport := otelhttp.NewTransport(http.DefaultTransport,
		otelhttp.WithSpanNameFormatter(func(_ string, r *http.Request) string {
			if route, ok := r.Context().Value(routeKey{}).(string); ok {
				return r.Method + " " + route
			}
			return r.Method
		}))
	return &Simulator{baseURL: baseURL, client: &http.Client{Transport: transport, Timeout: 5 * time.Second}, tracer: svc.tracer, log: svc.log}
}

func (s *Simulator) Run(ctx context.Context, steps int) (SimulationResult, error) {
	ctx, span := s.tracer.Start(ctx, "work_orders.simulate", trace.WithAttributes(attribute.Int("simulation.steps", steps)))
	defer span.End()

	run := &simulation{Simulator: s, result: SimulationResult{Statuses: map[string]int{}}}
	for i := range steps {
		if i > 0 {
			// Dispatcher think time spreads requests out in the trace waterfall.
			select {
			case <-ctx.Done():
				return run.result, ctx.Err()
			case <-time.After(time.Duration(10+rand.IntN(30)) * time.Millisecond):
			}
		}
		if err := run.step(ctx); err != nil {
			span.RecordError(err)
			return run.result, err
		}
	}

	r := run.result
	span.SetAttributes(attribute.Int("simulation.requests", r.Requests), attribute.Int("simulation.created", r.Created),
		attribute.Int("simulation.started", r.Started), attribute.Int("simulation.completed", r.Completed),
		attribute.Int("simulation.rejected", r.Rejected))
	s.log.InfoContext(ctx, "Simulated field-service traffic",
		"simulation.requests", r.Requests, "simulation.created", r.Created, "simulation.started", r.Started,
		"simulation.completed", r.Completed, "simulation.rejected", r.Rejected)
	return r, nil
}

type simulation struct {
	*Simulator
	result SimulationResult
}

func (s *simulation) step(ctx context.Context) error {
	switch n := rand.IntN(100); {
	case n < 30:
		return s.open(ctx)
	case n < 52:
		return s.start(ctx)
	case n < 77:
		return s.complete(ctx)
	case n < 87:
		return s.browse(ctx)
	case n < 92:
		_, err := s.call(ctx, http.MethodGet, "/work-orders/{id}", fmt.Sprintf("/work-orders/%d", 9000+rand.IntN(1000)), nil, nil)
		return err
	case n < 96:
		return s.openInvalid(ctx)
	default:
		return s.reopenClosed(ctx)
	}
}

func (s *simulation) open(ctx context.Context) error {
	p := randomPriority()
	reported := time.Now().UTC().Add(-reportedLag(p))
	req := CreateWorkOrderRequest{
		Title:      issues[rand.IntN(len(issues))] + " — " + sites[rand.IntN(len(sites))],
		Priority:   p,
		DueDate:    reported.AddDate(0, 0, map[Priority]int{PriorityUrgent: 0, PriorityHigh: 1, PriorityNormal: 3, PriorityLow: 7}[p]).Format(dateLayout),
		ReportedAt: &reported,
	}
	if rand.IntN(100) < 40 {
		req.Assignee = technicians[rand.IntN(len(technicians))]
	}
	status, err := s.call(ctx, http.MethodPost, "/work-orders", "/work-orders", req, nil)
	if status == http.StatusCreated {
		s.result.Created++
	}
	return err
}

func (s *simulation) openInvalid(ctx context.Context) error {
	bodies := []map[string]any{
		{"title": "", "priority": "critical"},
		{"title": "Replace air filters — Bayview Hotel", "priority": "asap"},
		{"title": "Inspect fire dampers — Mercy Clinic", "dueDate": "next Tuesday"},
	}
	_, err := s.call(ctx, http.MethodPost, "/work-orders", "/work-orders", bodies[rand.IntN(len(bodies))], nil)
	return err
}

func (s *simulation) start(ctx context.Context) error {
	order, ok, err := s.pick(ctx, StatusOpen)
	if err != nil || !ok {
		return err
	}
	update := map[string]any{"status": StatusInProgress}
	if order.Assignee == "" {
		update["assignee"] = technicians[rand.IntN(len(technicians))]
	}
	status, err := s.call(ctx, http.MethodPatch, "/work-orders/{id}", "/work-orders/"+strconv.Itoa(order.ID), update, nil)
	if status == http.StatusOK {
		s.result.Started++
	}
	return err
}

func (s *simulation) complete(ctx context.Context) error {
	order, ok, err := s.pick(ctx, StatusInProgress)
	if err != nil || !ok {
		return err
	}
	status, err := s.call(ctx, http.MethodPatch, "/work-orders/{id}", "/work-orders/"+strconv.Itoa(order.ID), map[string]any{"status": StatusCompleted}, nil)
	if status == http.StatusOK {
		s.result.Completed++
	}
	return err
}

// reopenClosed tries to restart a completed job, which the dispatch rules reject with 409 Conflict.
func (s *simulation) reopenClosed(ctx context.Context) error {
	order, ok, err := s.pick(ctx, StatusCompleted)
	if err != nil || !ok {
		return err
	}
	_, err = s.call(ctx, http.MethodPatch, "/work-orders/{id}", "/work-orders/"+strconv.Itoa(order.ID), map[string]any{"status": StatusInProgress}, nil)
	return err
}

func (s *simulation) browse(ctx context.Context) error {
	queries := []string{"?status=open&priority=urgent", "?status=in_progress", "?assignee=" + technicians[rand.IntN(len(technicians))], "?status=open"}
	_, err := s.call(ctx, http.MethodGet, "/work-orders", "/work-orders"+queries[rand.IntN(len(queries))], nil, nil)
	return err
}

func (s *simulation) pick(ctx context.Context, status Status) (WorkOrder, bool, error) {
	var orders []WorkOrder
	if _, err := s.call(ctx, http.MethodGet, "/work-orders", "/work-orders?status="+string(status), nil, &orders); err != nil || len(orders) == 0 {
		return WorkOrder{}, false, err
	}
	// The queue is sorted by priority, so dispatch favors the top of it.
	return orders[rand.IntN(min(len(orders), 4))], true, nil
}

// call sends one request to the API. route is the URL template used to name the client span.
func (s *simulation) call(ctx context.Context, method, route, path string, body, out any) (int, error) {
	var reader io.Reader
	if body != nil {
		data, err := json.Marshal(body)
		if err != nil {
			return 0, err
		}
		reader = bytes.NewReader(data)
	}
	req, err := http.NewRequestWithContext(context.WithValue(ctx, routeKey{}, route), method, s.baseURL+path, reader)
	if err != nil {
		return 0, err
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	resp, err := s.client.Do(req)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()

	s.result.Requests++
	s.result.Statuses[strconv.Itoa(resp.StatusCode)]++
	if resp.StatusCode >= http.StatusBadRequest {
		s.result.Rejected++
	}
	if out != nil && resp.StatusCode == http.StatusOK {
		return resp.StatusCode, json.NewDecoder(resp.Body).Decode(out)
	}
	_, err = io.Copy(io.Discard, resp.Body)
	return resp.StatusCode, err
}
