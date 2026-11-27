package main

import (
	"context"
	"errors"
	"log/slog"
	"os"
	"strings"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/contrib/instrumentation/runtime"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploghttp"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetrichttp"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracehttp"
	"go.opentelemetry.io/otel/log/global"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
)

// instrumentationName names the domain tracer, meter, and logger, following the <Company>.<Service> convention.
const instrumentationName = "FieldService.WorkOrders"

// setupTelemetry configures OpenTelemetry traces, metrics, and logs from the standard OTEL_* environment
// variables that Aspire injects (endpoint, headers, protocol, service name, and resource attributes).
// Without an OTLP endpoint, the API only logs to stdout.
func setupTelemetry(ctx context.Context) (*slog.Logger, func(context.Context) error, error) {
	console := slog.NewTextHandler(os.Stdout, nil)
	if os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") == "" {
		return slog.New(console), func(context.Context) error { return nil }, nil
	}

	res, err := resource.New(ctx, resource.WithFromEnv(), resource.WithTelemetrySDK(), resource.WithProcessRuntimeDescription())
	if err != nil {
		return nil, nil, err
	}

	// Aspire defaults to gRPC; resources configured with WithOtlpExporter(HttpProtobuf) get http/protobuf.
	useHTTP := strings.HasPrefix(os.Getenv("OTEL_EXPORTER_OTLP_PROTOCOL"), "http/")
	var (
		traceExporter  sdktrace.SpanExporter
		metricExporter sdkmetric.Exporter
		logExporter    sdklog.Exporter
	)
	if useHTTP {
		traceExporter, err = otlptracehttp.New(ctx)
		if err == nil {
			metricExporter, err = otlpmetrichttp.New(ctx)
		}
		if err == nil {
			logExporter, err = otlploghttp.New(ctx)
		}
	} else {
		traceExporter, err = otlptracegrpc.New(ctx)
		if err == nil {
			metricExporter, err = otlpmetricgrpc.New(ctx)
		}
		if err == nil {
			logExporter, err = otlploggrpc.New(ctx)
		}
	}
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

	if err := runtime.Start(runtime.WithMeterProvider(meterProvider)); err != nil {
		return nil, nil, err
	}

	// Log to stdout (console logs) and over OTLP (structured logs, correlated with the active span).
	logger := slog.New(slog.NewMultiHandler(console, otelslog.NewHandler(instrumentationName, otelslog.WithLoggerProvider(loggerProvider))))

	shutdown := func(ctx context.Context) error {
		return errors.Join(tracerProvider.Shutdown(ctx), meterProvider.Shutdown(ctx), loggerProvider.Shutdown(ctx))
	}
	return logger, shutdown, nil
}
