package main

import (
	"context"
	"errors"
	"log/slog"
	"os"
	"strings"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/log/global"
	"go.opentelemetry.io/otel/metric"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// instrumentationName names the domain tracer, meter, and logger (<Company>.<Service>).
const instrumentationName = "Contoso.FeatureFlags"

var tracer = otel.Tracer(instrumentationName)

// Semantic-convention attribute keys for feature flag evaluations.
const (
	attrFlagKey      = attribute.Key("feature_flag.key")
	attrVariant      = attribute.Key("feature_flag.result.variant")
	attrReason       = attribute.Key("feature_flag.result.reason")
	attrValue        = attribute.Key("feature_flag.result.value")
	attrVersion      = attribute.Key("feature_flag.version")
	attrProvider     = attribute.Key("feature_flag.provider.name")
	attrContextID    = attribute.Key("feature_flag.context.id")
	attrErrorType    = attribute.Key("error.type")
	attrChangeType   = attribute.Key("featureflags.change.type")
	attrRequestKind  = attribute.Key("featureflags.request.kind")
	attrContextAttrs = "featureflags.context."
)

// setupTelemetry configures OpenTelemetry traces, metrics, and logs from the OTEL_* environment variables that
// Aspire injects (endpoint, headers, export intervals, service name, and resource attributes). Aspire also trusts
// its development certificate inside the container (WithDeveloperCertificateTrust), so the gRPC exporters can
// verify the dashboard's TLS endpoint. Without an OTLP endpoint the app only logs to stdout.
func setupTelemetry(ctx context.Context) (*slog.Logger, func(context.Context) error, error) {
	console := slog.NewTextHandler(os.Stdout, nil)
	if os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") == "" || strings.EqualFold(os.Getenv("OTEL_SDK_DISABLED"), "true") {
		return slog.New(console), func(context.Context) error { return nil }, nil
	}

	res, err := resource.New(ctx, resource.WithFromEnv(), resource.WithTelemetrySDK(), resource.WithProcessRuntimeDescription())
	if err != nil {
		return nil, nil, err
	}
	traceExporter, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, nil, err
	}
	metricExporter, err := otlpmetricgrpc.New(ctx)
	if err != nil {
		return nil, nil, err
	}
	logExporter, err := otlploggrpc.New(ctx)
	if err != nil {
		return nil, nil, err
	}

	tracerProvider := sdktrace.NewTracerProvider(sdktrace.WithBatcher(traceExporter), sdktrace.WithResource(res))
	meterProvider := sdkmetric.NewMeterProvider(sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExporter)), sdkmetric.WithResource(res))
	loggerProvider := sdklog.NewLoggerProvider(sdklog.WithProcessor(sdklog.NewBatchProcessor(logExporter)), sdklog.WithResource(res))

	otel.SetTracerProvider(tracerProvider)
	otel.SetMeterProvider(meterProvider)
	global.SetLoggerProvider(loggerProvider)
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(propagation.TraceContext{}, propagation.Baggage{}))

	// Console logs for `docker logs` and the dashboard's console view; OTLP logs carry the active trace and span IDs.
	logger := slog.New(slog.NewMultiHandler(console, otelslog.NewHandler(instrumentationName, otelslog.WithLoggerProvider(loggerProvider))))

	shutdown := func(ctx context.Context) error {
		return errors.Join(tracerProvider.Shutdown(ctx), meterProvider.Shutdown(ctx), loggerProvider.Shutdown(ctx))
	}
	return logger, shutdown, nil
}

// flagMetrics holds the Contoso.FeatureFlags meter's instruments. Attributes are bounded: flag keys come from the
// catalog (unknown keys are recorded without a key), variants from flag definitions, and reasons from a fixed set.
type flagMetrics struct {
	evaluations metric.Int64Counter
	duration    metric.Float64Histogram
	changes     metric.Int64Counter
}

func newFlagMetrics(store *FlagStore) (*flagMetrics, error) {
	meter := otel.Meter(instrumentationName)
	evaluations, err := meter.Int64Counter("featureflags.evaluations",
		metric.WithUnit("{evaluation}"),
		metric.WithDescription("Flag evaluations served, by flag, variant, and reason."))
	if err != nil {
		return nil, err
	}
	duration, err := meter.Float64Histogram("featureflags.evaluation.duration",
		metric.WithUnit("ms"),
		metric.WithDescription("Time to resolve the flags requested by one evaluation call."),
		metric.WithExplicitBucketBoundaries(0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1))
	if err != nil {
		return nil, err
	}
	changes, err := meter.Int64Counter("featureflags.changes",
		metric.WithUnit("{change}"),
		metric.WithDescription("Changes to the flag catalog, by change type."))
	if err != nil {
		return nil, err
	}
	_, err = meter.Int64ObservableGauge("featureflags.rollout.percentage",
		metric.WithUnit("%"),
		metric.WithDescription("Share of traffic that each flag's percentage rollout sends to a non-default variant."),
		metric.WithInt64Callback(func(_ context.Context, o metric.Int64Observer) error {
			for key, percentage := range store.RolloutPercentages() {
				o.Observe(int64(percentage), metric.WithAttributes(attrFlagKey.String(key)))
			}
			return nil
		}))
	if err != nil {
		return nil, err
	}
	return &flagMetrics{evaluations: evaluations, duration: duration, changes: changes}, nil
}
