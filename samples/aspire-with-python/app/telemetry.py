"""OpenTelemetry bootstrap and domain instruments for the Contoso Outdoors inventory API.

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
from opentelemetry.instrumentation.redis import RedisInstrumentor
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

# Name shared by the domain tracer and meter.
INSTRUMENTATION_NAME = "Contoso.Inventory"

# Cache hits take about a millisecond; misses wait on the warehouse system for 100+ ms.
LOOKUP_DURATION_BUCKETS_MS = [0.5, 1, 2, 5, 10, 25, 50, 100, 150, 200, 300, 500, 1000]


def configure_opentelemetry() -> None:
    """Configure OTLP traces, metrics, and logs plus Redis client instrumentation."""
    os.environ.setdefault("OTEL_SEMCONV_STABILITY_OPT_IN", "http")
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

    # One CLIENT span per Redis command (GET, SET, DEL, ...). The dashboard matches the
    # span's server address to the `cache` resource, so traces show the hop into Redis.
    RedisInstrumentor().instrument()


def instrument_app(app: FastAPI) -> None:
    """Add server spans and http.server.* metrics for incoming requests."""
    # Skip the per-message ASGI receive/send spans so traces show only the request, domain and Redis spans.
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health", exclude_spans=["receive", "send"])


class InventoryMetrics:
    """Domain instruments. Tags are bounded enums (cache result, outcome, distribution center) - never SKUs."""

    def __init__(self) -> None:
        self.meter = metrics.get_meter(INSTRUMENTATION_NAME)
        self.lookups = self.meter.create_counter(
            "inventory.lookups",
            unit="{lookup}",
            description="Availability lookups, by cache result (hit, miss, bypass).",
        )
        self.lookup_duration = self.meter.create_histogram(
            "inventory.lookup.duration",
            unit="ms",
            description="Time to answer an availability lookup, by cache result.",
            explicit_bucket_boundaries_advisory=LOOKUP_DURATION_BUCKETS_MS,
        )
        self.reservations = self.meter.create_counter(
            "inventory.reservations",
            unit="{reservation}",
            description="Reservation attempts, by outcome (confirmed, rejected) and distribution center.",
        )


tracer = trace.get_tracer(INSTRUMENTATION_NAME)
inventory_metrics = InventoryMetrics()
