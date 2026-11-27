"""OpenTelemetry bootstrap and domain instruments for the Contoso Customers API.

Aspire injects the standard OTEL_* environment variables (OTEL_EXPORTER_OTLP_ENDPOINT,
OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES, OTEL_METRIC_EXPORT_INTERVAL, ...). The SDK
and OTLP exporters read them directly, so this module only wires providers together.
"""

import logging
import os

from fastapi import FastAPI
from opentelemetry import _logs, metrics, trace
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.fastapi import FastAPIInstrumentor
from opentelemetry.instrumentation.logging.handler import LoggingHandler
from opentelemetry.instrumentation.psycopg import PsycopgInstrumentor
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

# Name shared by the domain tracer and meter.
INSTRUMENTATION_NAME = "Contoso.Customers"

# Deals range from a few hundred dollars (small business) to seven figures (enterprise).
DEAL_SIZE_BUCKETS_USD = [1_000, 5_000, 10_000, 25_000, 50_000, 100_000, 250_000, 500_000, 1_000_000]


def configure_opentelemetry() -> None:
    """Configure OTLP traces, metrics, and logs plus PostgreSQL client instrumentation."""
    # Stable HTTP conventions. database/dup adds the stable db.client.operation.duration metric
    # while keeping the classic db.system/db.statement span attributes.
    os.environ.setdefault("OTEL_SEMCONV_STABILITY_OPT_IN", "http,database/dup")
    # Resource.create() reads OTEL_SERVICE_NAME and OTEL_RESOURCE_ATTRIBUTES.
    resource = Resource.create()
    export = bool(os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT"))

    tracer_provider = TracerProvider(resource=resource)
    metric_readers = []
    logger_provider = LoggerProvider(resource=resource)
    if export:
        tracer_provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))
        metric_readers.append(PeriodicExportingMetricReader(OTLPMetricExporter()))
        logger_provider.add_log_record_processor(BatchLogRecordProcessor(OTLPLogExporter()))

    trace.set_tracer_provider(tracer_provider)
    metrics.set_meter_provider(MeterProvider(resource=resource, metric_readers=metric_readers))
    _logs.set_logger_provider(logger_provider)

    # Console output for `aspire logs`, OTLP for the dashboard's structured logs page.
    # `extra={...}` fields become log attributes; trace/span ids are attached automatically.
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    console = logging.StreamHandler()
    console.setFormatter(logging.Formatter("%(levelname)s %(name)s: %(message)s"))
    root.addHandler(console)
    root.addHandler(LoggingHandler(level=logging.INFO, logger_provider=logger_provider))
    # The pool's connection churn is noise next to the domain logs.
    logging.getLogger("psycopg.pool").setLevel(logging.WARNING)

    # One CLIENT span per SQL statement (db.system=postgresql, db.statement with placeholders only).
    PsycopgInstrumentor().instrument()


def instrument_app(app: FastAPI) -> None:
    """Add server spans and http.server.* metrics for incoming requests."""
    # Skip the per-message ASGI receive/send spans so traces show only the request, domain and SQL spans.
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health", exclude_spans=["receive", "send"])


class CustomerMetrics:
    """Domain instruments. Tags are bounded enums (segment, status) - never customer ids or emails."""

    def __init__(self) -> None:
        meter = metrics.get_meter(INSTRUMENTATION_NAME)
        self.created = meter.create_counter(
            "customers.created",
            unit="{customer}",
            description="Customer accounts created, by segment.",
        )
        self.status_changes = meter.create_counter(
            "customers.status_changes",
            unit="{change}",
            description="Lifecycle moves (lead -> active, active -> churned, ...), by source and target status.",
        )
        self.revenue = meter.create_histogram(
            "customers.revenue",
            unit="{dollar}",
            description="Revenue booked per deal in US dollars (new accounts and lifetime value increases), "
            "by segment.",
            explicit_bucket_boundaries_advisory=DEAL_SIZE_BUCKETS_USD,
        )
        self.count = meter.create_up_down_counter(
            "customers.count",
            unit="{customer}",
            description="Customers in the database, by lifecycle status.",
        )


tracer = trace.get_tracer(INSTRUMENTATION_NAME)
customer_metrics = CustomerMetrics()
