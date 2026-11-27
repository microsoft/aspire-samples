package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"math/rand/v2"
	"net/http"
	"strconv"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/codes"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/trace"
)

// adminAPI hosts the development-only endpoints behind the AppHost's dashboard commands.
type adminAPI struct {
	api     *flagAPI
	baseURL string
	client  *http.Client
}

func newAdminAPI(api *flagAPI, port int) *adminAPI {
	return &adminAPI{
		api:     api,
		baseURL: fmt.Sprintf("http://127.0.0.1:%d", port),
		// The instrumented transport creates client spans and injects the W3C traceparent header, so each simulated
		// request is traced from the simulation span through the API's server and evaluation spans.
		client: &http.Client{
			Timeout: 10 * time.Second,
			Transport: otelhttp.NewTransport(http.DefaultTransport, otelhttp.WithSpanNameFormatter(
				func(_ string, r *http.Request) string { return r.Method + " " + r.URL.Path })),
		},
	}
}

type simulationSummary struct {
	Shoppers    int                       `json:"shoppers"`
	Requests    int                       `json:"requests"`
	Evaluations int                       `json:"evaluations"`
	Errors      int                       `json:"errors"`
	Served      map[string]map[string]int `json:"served"`
	DurationMs  int64                     `json:"durationMs"`
}

type shopper struct {
	EvaluationContext
	page string
}

// Storefront pages and the flags they need. Checkout pages resolve flags one at a time, like a server-side SDK.
var pageFlags = map[string][]string{
	"home":     nil,
	"product":  {"search.semantic-ranking", "catalog.recommendations", "mobile.ar-tent-preview", "loyalty.points-multiplier"},
	"checkout": {"checkout.express-pay", "payments.fraud-screening-v3", "shipping.free-threshold-usd"},
}

// retiredFlag is still requested by an outdated mobile app build, which produces realistic 404s.
const retiredFlag = "checkout.gift-wrap-upsell"

func (a *adminAPI) simulate(c *gin.Context) {
	shoppers := 40
	if raw := c.Query("shoppers"); raw != "" {
		n, err := strconv.Atoi(raw)
		if err != nil || n < 1 || n > 500 {
			writeProblem(c, http.StatusBadRequest, "The 'shoppers' query parameter must be between 1 and 500.",
				map[string][]string{"shoppers": {"Expected a number between 1 and 500."}})
			return
		}
		shoppers = n
	}

	ctx, span := tracer.Start(c.Request.Context(), "simulate storefront traffic", trace.WithAttributes(attribute.Int("featureflags.simulation.shoppers", shoppers)))
	defer span.End()

	start := time.Now()
	summary := simulationSummary{Shoppers: shoppers, Served: map[string]map[string]int{}}
	var mu sync.Mutex
	visits := make(chan shopper)
	var wg sync.WaitGroup
	for range 4 {
		wg.Go(func() {
			for s := range visits {
				requests, served, errs := a.visit(ctx, s)
				mu.Lock()
				summary.Requests += requests
				summary.Errors += errs
				for _, e := range served {
					if summary.Served[e.FlagKey] == nil {
						summary.Served[e.FlagKey] = map[string]int{}
					}
					summary.Served[e.FlagKey][e.Variant]++
					summary.Evaluations++
				}
				mu.Unlock()
			}
		})
	}
	for range shoppers {
		visits <- newShopper()
	}
	close(visits)
	wg.Wait()
	summary.DurationMs = time.Since(start).Milliseconds()

	span.SetAttributes(attribute.Int("featureflags.simulation.requests", summary.Requests),
		attribute.Int("featureflags.simulation.evaluations", summary.Evaluations),
		attribute.Int("featureflags.simulation.errors", summary.Errors))
	a.api.logger.InfoContext(ctx, "Simulated storefront traffic", "shoppers", shoppers, "requests", summary.Requests,
		"evaluations", summary.Evaluations, "errors", summary.Errors, "duration_ms", summary.DurationMs)
	c.JSON(http.StatusOK, summary)
}

// visit simulates one shopper's page view and returns the requests made, the evaluations served, and the errors.
func (a *adminAPI) visit(ctx context.Context, s shopper) (requests int, served []Evaluation, errs int) {
	ctx, span := tracer.Start(ctx, "storefront visit", trace.WithAttributes(
		attribute.String("featureflags.simulation.page", s.page),
		attrContextID.String(s.TargetingKey),
		attribute.String(attrContextAttrs+"plan", s.Attributes["plan"]),
		attribute.String(attrContextAttrs+"country", s.Attributes["country"]),
		attribute.String(attrContextAttrs+"platform", s.Attributes["platform"])))
	defer span.End()

	call := func(path string, body any, result any) {
		requests++
		if err := a.post(ctx, path, body, result); err != nil {
			errs++
			span.AddEvent("request failed", trace.WithAttributes(attribute.String("url.path", path), attribute.String("error.message", err.Error())))
		}
	}

	if flags := pageFlags[s.page]; s.page != "checkout" {
		var response bulkEvaluation
		call("/evaluate", bulkEvaluationRequest{Context: s.EvaluationContext, Flags: flags}, &response)
		served = append(served, response.Flags...)
	} else {
		for _, key := range flags {
			var e Evaluation
			call("/flags/"+key+"/evaluate", evaluationRequest{Context: s.EvaluationContext}, &e)
			if e.FlagKey != "" {
				served = append(served, e)
			}
		}
	}
	if s.Attributes["platform"] == "ios" && rand.IntN(4) == 0 {
		call("/flags/"+retiredFlag+"/evaluate", evaluationRequest{Context: s.EvaluationContext}, nil)
	}
	if errs > 0 {
		span.SetStatus(codes.Error, fmt.Sprintf("%d of %d requests failed", errs, requests))
	}
	return requests, served, errs
}

func (a *adminAPI) post(ctx context.Context, path string, body, result any) error {
	payload, err := json.Marshal(body)
	if err != nil {
		return err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, a.baseURL+path, bytes.NewReader(payload))
	if err != nil {
		return err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("User-Agent", "contoso-storefront-simulator/1.0")
	resp, err := a.client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode >= 400 {
		return fmt.Errorf("%s returned %d", path, resp.StatusCode)
	}
	if result == nil {
		return nil
	}
	return json.NewDecoder(resp.Body).Decode(result)
}

func (a *adminAPI) reset(c *gin.Context) {
	ctx, span := tracer.Start(c.Request.Context(), "reset flags")
	defer span.End()

	count := a.api.store.Reset(seedFlags)
	span.SetAttributes(attribute.Int("featureflags.flags", count))
	a.api.metrics.changes.Add(ctx, 1, metric.WithAttributes(attrChangeType.String("reset")))
	a.api.logger.WarnContext(ctx, "Flag catalog reset to the seeded storefront flags", "flags", count)
	c.JSON(http.StatusOK, gin.H{"flags": count, "message": fmt.Sprintf("Restored %d storefront flags.", count)})
}

func newShopper() shopper {
	return shopper{
		EvaluationContext: EvaluationContext{
			TargetingKey: fmt.Sprintf("shopper-%05d", rand.IntN(100000)),
			Attributes: map[string]string{
				"plan":     pick(map[string]int{"free": 55, "plus": 30, "premium": 15}),
				"country":  pick(map[string]int{"US": 40, "CA": 10, "GB": 12, "DE": 10, "FR": 8, "AU": 8, "JP": 6, "BR": 6}),
				"platform": pick(map[string]int{"web": 55, "ios": 25, "android": 20}),
			},
		},
		page: pick(map[string]int{"home": 40, "product": 35, "checkout": 25}),
	}
}

// pick returns a key with probability proportional to its weight.
func pick(weights map[string]int) string {
	total := 0
	for _, w := range weights {
		total += w
	}
	n := rand.IntN(total)
	for key, w := range weights {
		if n < w {
			return key
		}
		n -= w
	}
	panic("unreachable")
}
