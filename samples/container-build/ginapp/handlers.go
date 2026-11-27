package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"maps"
	"net/http"
	"slices"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
)

// flagAPI implements the flag management and evaluation endpoints.
type flagAPI struct {
	store   *FlagStore
	metrics *flagMetrics
	logger  *slog.Logger
}

type evaluationRequest struct {
	Context EvaluationContext `json:"context"`
}

type bulkEvaluationRequest struct {
	Context EvaluationContext `json:"context"`
	Flags   []string          `json:"flags"`
}

type bulkEvaluation struct {
	TargetingKey string       `json:"targetingKey"`
	EvaluatedAt  time.Time    `json:"evaluatedAt"`
	Flags        []Evaluation `json:"flags"`
}

type rolloutAdvance struct {
	FlagKey string `json:"flagKey"`
	Variant string `json:"variant"`
	From    int    `json:"from"`
	To      int    `json:"to"`
	Flag    Flag   `json:"flag"`
}

func (api *flagAPI) listFlags(c *gin.Context) {
	var enabled *bool
	if raw, ok := c.GetQuery("enabled"); ok {
		value, err := strconv.ParseBool(raw)
		if err != nil {
			writeProblem(c, http.StatusBadRequest, "The 'enabled' filter must be true or false.",
				map[string][]string{"enabled": {"Expected true or false."}})
			return
		}
		enabled = &value
	}
	c.JSON(http.StatusOK, api.store.List(enabled))
}

func (api *flagAPI) getFlag(c *gin.Context) {
	flag, ok := api.store.Get(c.Param("key"))
	if !ok {
		api.flagNotFound(c, c.Param("key"))
		return
	}
	c.JSON(http.StatusOK, flag)
}

func (api *flagAPI) createFlag(c *gin.Context) {
	ctx, span := tracer.Start(c.Request.Context(), "create flag")
	defer span.End()

	var flag Flag
	if !bindJSON(c, &flag) {
		span.SetStatus(codes.Error, "invalid request body")
		return
	}
	span.SetAttributes(attrFlagKey.String(flag.Key))
	if problems := flag.Validate(); len(problems) > 0 {
		api.rejected(c, span, flag.Key, problems)
		return
	}
	created, err := api.store.Create(flag)
	if errors.Is(err, ErrFlagExists) {
		span.SetStatus(codes.Error, err.Error())
		writeProblem(c, http.StatusConflict, fmt.Sprintf("A flag with key '%s' already exists. Use PATCH to change it.", flag.Key), nil)
		return
	}
	api.metrics.changes.Add(ctx, 1, metric.WithAttributes(attrChangeType.String("created")))
	span.AddEvent("feature_flag.created", trace.WithAttributes(attrVersion.String("1"), attribute.Int("featureflags.variants", len(created.Variants))))
	api.logger.InfoContext(ctx, "Feature flag created", "feature_flag.key", created.Key, "enabled", created.Enabled,
		"variants", len(created.Variants), "rollout_percentage", created.RolloutPercentage())
	c.Header("Location", "/flags/"+created.Key)
	c.JSON(http.StatusCreated, created)
}

func (api *flagAPI) updateFlag(c *gin.Context) {
	key := c.Param("key")
	ctx, span := tracer.Start(c.Request.Context(), "update flag", trace.WithAttributes(attrFlagKey.String(key)))
	defer span.End()

	var patch FlagPatch
	if !bindJSON(c, &patch) {
		span.SetStatus(codes.Error, "invalid request body")
		return
	}
	before, after, problems, err := api.store.Update(key, patch)
	switch {
	case errors.Is(err, ErrFlagNotFound):
		span.SetStatus(codes.Error, err.Error())
		api.flagNotFound(c, key)
		return
	case len(problems) > 0:
		api.rejected(c, span, key, problems)
		return
	}

	changed := changedFields(before, after)
	span.SetAttributes(attrVersion.String(strconv.Itoa(after.Version)), attribute.StringSlice("featureflags.changed_fields", changed))
	span.AddEvent("feature_flag.updated", trace.WithAttributes(attribute.StringSlice("featureflags.changed_fields", changed)))
	api.metrics.changes.Add(ctx, 1, metric.WithAttributes(attrChangeType.String("updated")))
	switch {
	case before.Enabled && !after.Enabled:
		api.logger.WarnContext(ctx, "Feature flag disabled; all traffic now gets the off variant",
			"feature_flag.key", key, "off_variant", after.OffVariant, "version", after.Version)
	case !before.Enabled && after.Enabled:
		api.logger.InfoContext(ctx, "Feature flag enabled", "feature_flag.key", key, "version", after.Version)
	}
	api.logger.InfoContext(ctx, "Feature flag updated", "feature_flag.key", key, "version", after.Version,
		"changed_fields", strings.Join(changed, ","), "rollout_percentage", after.RolloutPercentage())
	c.JSON(http.StatusOK, after)
}

func (api *flagAPI) deleteFlag(c *gin.Context) {
	key := c.Param("key")
	ctx, span := tracer.Start(c.Request.Context(), "delete flag", trace.WithAttributes(attrFlagKey.String(key)))
	defer span.End()

	if !api.store.Delete(key) {
		span.SetStatus(codes.Error, ErrFlagNotFound.Error())
		api.flagNotFound(c, key)
		return
	}
	api.metrics.changes.Add(ctx, 1, metric.WithAttributes(attrChangeType.String("deleted")))
	api.logger.InfoContext(ctx, "Feature flag deleted", "feature_flag.key", key)
	c.Status(http.StatusNoContent)
}

func (api *flagAPI) advanceRollout(c *gin.Context) {
	key := c.Param("key")
	ctx, span := tracer.Start(c.Request.Context(), "advance rollout", trace.WithAttributes(attrFlagKey.String(key)))
	defer span.End()

	before, after, err := api.store.AdvanceRollout(key)
	switch {
	case errors.Is(err, ErrFlagNotFound):
		span.SetStatus(codes.Error, err.Error())
		api.flagNotFound(c, key)
		return
	case err != nil:
		span.SetStatus(codes.Error, err.Error())
		writeProblem(c, http.StatusConflict, fmt.Sprintf("Can't advance '%s': %s.", key, err), nil)
		return
	}

	variant := after.Rollout[slices.IndexFunc(after.Rollout, func(a Allocation) bool { return a.Variant != after.DefaultVariant })].Variant
	from, to := 100-weightOf(before.Rollout, before.DefaultVariant), 100-weightOf(after.Rollout, after.DefaultVariant)
	span.SetAttributes(attrVariant.String(variant), attribute.Int("featureflags.rollout.from", from), attribute.Int("featureflags.rollout.to", to))
	span.AddEvent("feature_flag.rollout_advanced", trace.WithAttributes(attribute.Int("featureflags.rollout.from", from), attribute.Int("featureflags.rollout.to", to)))
	api.metrics.changes.Add(ctx, 1, metric.WithAttributes(attrChangeType.String("rollout_advanced")))
	api.logger.InfoContext(ctx, "Rollout advanced", "feature_flag.key", key, "variant", variant, "from_percentage", from,
		"to_percentage", to, "version", after.Version)
	c.JSON(http.StatusOK, rolloutAdvance{FlagKey: key, Variant: variant, From: from, To: to, Flag: after})
}

func (api *flagAPI) evaluateFlag(c *gin.Context) {
	key := c.Param("key")
	ctx, span := tracer.Start(c.Request.Context(), "evaluate flag", trace.WithAttributes(attrFlagKey.String(key), attrProvider.String(instrumentationName)))
	defer span.End()

	var request evaluationRequest
	if !bindJSON(c, &request) || !api.validContext(c, span, request.Context) {
		return
	}
	setContextAttributes(span, request.Context)
	flag, ok := api.store.Get(key)
	if !ok {
		span.SetAttributes(attrErrorType.String("flag_not_found"), attrReason.String("error"))
		span.SetStatus(codes.Error, ErrFlagNotFound.Error())
		api.metrics.evaluations.Add(ctx, 1, metric.WithAttributes(attrReason.String("error"), attrErrorType.String("flag_not_found")))
		api.flagNotFound(c, key)
		return
	}

	start := time.Now()
	evaluation := flag.Evaluate(request.Context)
	api.recordEvaluations(ctx, span, "single", start, evaluation)
	span.SetAttributes(attrVariant.String(evaluation.Variant), attrReason.String(strings.ToLower(evaluation.Reason)), attrVersion.String(strconv.Itoa(evaluation.FlagVersion)))
	c.JSON(http.StatusOK, evaluation)
}

func (api *flagAPI) evaluateFlags(c *gin.Context) {
	ctx, span := tracer.Start(c.Request.Context(), "evaluate flags", trace.WithAttributes(attrProvider.String(instrumentationName)))
	defer span.End()

	var request bulkEvaluationRequest
	if !bindJSON(c, &request) || !api.validContext(c, span, request.Context) {
		return
	}
	setContextAttributes(span, request.Context)

	start := time.Now()
	var evaluations []Evaluation
	if len(request.Flags) == 0 {
		for _, flag := range api.store.List(nil) {
			evaluations = append(evaluations, flag.Evaluate(request.Context))
		}
	} else {
		for _, key := range request.Flags {
			if flag, ok := api.store.Get(key); ok {
				evaluations = append(evaluations, flag.Evaluate(request.Context))
			} else {
				evaluations = append(evaluations, Evaluation{FlagKey: key, Reason: ReasonError, ErrorCode: ErrorFlagNotFound})
			}
		}
	}
	api.recordEvaluations(ctx, span, "bulk", start, evaluations...)
	span.SetAttributes(attribute.Int("featureflags.flags.evaluated", len(evaluations)))
	c.JSON(http.StatusOK, bulkEvaluation{TargetingKey: request.Context.TargetingKey, EvaluatedAt: time.Now().UTC(), Flags: evaluations})
}

// recordEvaluations adds a feature_flag.evaluation span event per flag (OpenTelemetry semantic conventions) and
// records the evaluation counter and duration histogram.
func (api *flagAPI) recordEvaluations(ctx context.Context, span trace.Span, kind string, start time.Time, evaluations ...Evaluation) {
	elapsed := float64(time.Since(start).Nanoseconds()) / 1e6
	for _, e := range evaluations {
		reason := strings.ToLower(e.Reason)
		eventAttrs := []attribute.KeyValue{attrFlagKey.String(e.FlagKey), attrReason.String(reason), attrProvider.String(instrumentationName)}
		metricAttrs := []attribute.KeyValue{attrReason.String(reason)}
		if e.ErrorCode != "" {
			eventAttrs = append(eventAttrs, attrErrorType.String(strings.ToLower(e.ErrorCode)))
			metricAttrs = append(metricAttrs, attrErrorType.String(strings.ToLower(e.ErrorCode)))
		} else {
			eventAttrs = append(eventAttrs, attrVariant.String(e.Variant), attrValue.String(fmt.Sprint(e.Value)), attrVersion.String(strconv.Itoa(e.FlagVersion)))
			metricAttrs = append(metricAttrs, attrFlagKey.String(e.FlagKey), attrVariant.String(e.Variant))
		}
		span.AddEvent("feature_flag.evaluation", trace.WithAttributes(eventAttrs...))
		api.metrics.evaluations.Add(ctx, 1, metric.WithAttributes(metricAttrs...))
	}
	api.metrics.duration.Record(ctx, elapsed, metric.WithAttributes(attrRequestKind.String(kind)))
}

func (api *flagAPI) validContext(c *gin.Context, span trace.Span, ctx EvaluationContext) bool {
	errs := map[string][]string{}
	switch {
	case strings.TrimSpace(ctx.TargetingKey) == "":
		errs["context.targetingKey"] = []string{"A targeting key (for example a shopper ID) is required so rollouts stay sticky."}
	case len(ctx.TargetingKey) > 128:
		errs["context.targetingKey"] = []string{"The targeting key must be at most 128 characters."}
	}
	if len(ctx.Attributes) > 20 {
		errs["context.attributes"] = []string{"Send at most 20 context attributes."}
	}
	if len(errs) == 0 {
		return true
	}
	span.SetAttributes(attrErrorType.String("invalid_context"))
	span.SetStatus(codes.Error, "invalid evaluation context")
	writeProblem(c, http.StatusBadRequest, "The evaluation context is invalid.", errs)
	return false
}

func (api *flagAPI) rejected(c *gin.Context, span trace.Span, key string, problems map[string][]string) {
	fields := slices.Sorted(maps.Keys(problems))
	span.SetStatus(codes.Error, "invalid flag definition")
	span.AddEvent("feature_flag.rejected", trace.WithAttributes(attribute.StringSlice("featureflags.invalid_fields", fields)))
	api.metrics.changes.Add(c.Request.Context(), 1, metric.WithAttributes(attrChangeType.String("rejected")))
	api.logger.WarnContext(trace.ContextWithSpan(c.Request.Context(), span), "Rejected invalid flag definition",
		"feature_flag.key", key, "invalid_fields", strings.Join(fields, ","))
	writeProblem(c, http.StatusBadRequest, "The flag definition is invalid.", problems)
}

func (api *flagAPI) flagNotFound(c *gin.Context, key string) {
	api.logger.WarnContext(c.Request.Context(), "Unknown feature flag requested", "feature_flag.key", key)
	writeProblem(c, http.StatusNotFound, fmt.Sprintf("No feature flag has the key '%s'.", key), nil)
}

func setContextAttributes(span trace.Span, ctx EvaluationContext) {
	attrs := []attribute.KeyValue{attrContextID.String(ctx.TargetingKey)}
	for name, value := range ctx.Attributes {
		attrs = append(attrs, attribute.String(attrContextAttrs+name, value))
	}
	span.SetAttributes(attrs...)
}

func changedFields(before, after Flag) []string {
	var changed []string
	add := func(field string, differs bool) {
		if differs {
			changed = append(changed, field)
		}
	}
	add("description", before.Description != after.Description)
	add("enabled", before.Enabled != after.Enabled)
	add("defaultVariant", before.DefaultVariant != after.DefaultVariant)
	add("offVariant", before.OffVariant != after.OffVariant)
	add("rules", fmt.Sprint(before.Rules) != fmt.Sprint(after.Rules))
	add("rollout", !slices.Equal(before.Rollout, after.Rollout))
	return changed
}
