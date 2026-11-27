"""OpenTelemetry bootstrap and domain instruments for the Python worker.

Aspire injects the standard OTEL_* environment variables (OTEL_EXPORTER_OTLP_ENDPOINT,
OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES, OTEL_METRIC_EXPORT_INTERVAL, ...). The SDK and
OTLP exporters read them directly, so this module only wires providers together.
"""

import logging
import os

from opentelemetry import _logs, metrics, trace
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.logging.handler import LoggingHandler
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

# Name shared by the domain tracer and meter. The C# and Node.js workers use
# TaskQueue.Worker.CSharp and TaskQueue.Worker.Node with the same instruments.
INSTRUMENTATION_NAME = "TaskQueue.Worker.Python"


def configure_opentelemetry() -> None:
    """Configure OTLP traces, metrics and logs for the worker."""
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
    # Connection chatter from the AMQP client is noise next to the domain logs.
    for name in ("aio_pika", "aiormq"):
        logging.getLogger(name).setLevel(logging.WARNING)


class WorkerMetrics:
    """Domain instruments. Attributes are bounded enums (task type, outcome, error type)."""

    def __init__(self) -> None:
        meter = metrics.get_meter(INSTRUMENTATION_NAME, "1.0.0")
        self.processed = meter.create_counter(
            "taskqueue.tasks.processed",
            unit="{task}",
            description="Tasks this worker finished, by task type and outcome (completed or failed).",
        )
        self.duration = meter.create_histogram(
            "taskqueue.task.duration",
            unit="s",
            description="Time this worker spent on a task, from pickup to result, by task type and outcome.",
            explicit_bucket_boundaries_advisory=[0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.75, 1, 2, 5],
        )
        self.active = meter.create_up_down_counter(
            "taskqueue.tasks.active",
            unit="{task}",
            description="Tasks this worker is processing right now.",
        )
        self.failed = meter.create_counter(
            "taskqueue.tasks.failed",
            unit="{task}",
            description="Tasks this worker could not process, by error type.",
        )
        self.dataset_rows = meter.create_histogram(
            "taskqueue.dataset.rows",
            unit="{row}",
            description="Rows in each dataset analyzed.",
            explicit_bucket_boundaries_advisory=[5, 10, 25, 50, 100, 250, 500, 1000],
        )


tracer = trace.get_tracer(INSTRUMENTATION_NAME, "1.0.0")
