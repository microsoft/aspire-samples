"""OpenTelemetry bootstrap and domain instruments for the task board API.

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
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

# Name shared by the domain tracer and meter.
INSTRUMENTATION_NAME = "Contoso.TaskBoard"

# Lead times range from a few hours to a couple of weeks.
LEAD_TIME_BUCKETS_HOURS = [1, 4, 8, 24, 48, 72, 120, 168, 336]


def configure_opentelemetry() -> None:
    """Configure OTLP traces, metrics, and logs."""
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


def instrument_app(app: FastAPI) -> None:
    """Add server spans and http.server.* metrics for incoming requests."""
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health")


class TaskBoardMetrics:
    """Domain instruments. Tags are bounded enums (priority, status) - never task ids or people."""

    def __init__(self) -> None:
        meter = metrics.get_meter(INSTRUMENTATION_NAME)
        self.created = meter.create_counter(
            "taskboard.tasks.created",
            unit="{task}",
            description="Tasks added to the board, by priority.",
        )
        self.transitions = meter.create_counter(
            "taskboard.task.transitions",
            unit="{transition}",
            description="Status changes, by source and target status.",
        )
        self.lead_time = meter.create_histogram(
            "taskboard.task.lead_time",
            unit="h",
            description="Hours from task creation to completion, by priority.",
            explicit_bucket_boundaries_advisory=LEAD_TIME_BUCKETS_HOURS,
        )
        self.tasks = meter.create_up_down_counter(
            "taskboard.tasks",
            unit="{task}",
            description="Tasks currently on the board, by status.",
        )


tracer = trace.get_tracer(INSTRUMENTATION_NAME)
board_metrics = TaskBoardMetrics()
