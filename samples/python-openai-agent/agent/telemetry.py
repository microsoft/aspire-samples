"""OpenTelemetry bootstrap for the support agent.

Aspire injects the standard OTEL_* environment variables (OTEL_EXPORTER_OTLP_ENDPOINT,
OTEL_EXPORTER_OTLP_PROTOCOL, OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES,
OTEL_METRIC_EXPORT_INTERVAL, ...). The SDK and OTLP exporters read them directly, so
this module only wires providers, exporters, and instrumentations together.
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
from opentelemetry.instrumentation.openai_v2 import OpenAIInstrumentor
from opentelemetry.sdk._logs import LoggerProvider
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

# Name shared by the custom tracer and meter (the "SupportAgent" domain signals).
INSTRUMENTATION_NAME = "SupportAgent"


def configure_opentelemetry() -> None:
    """Configure OTLP traces, metrics, and logs plus library instrumentation."""
    # Emit stable HTTP semantic conventions (http.server.request.duration in seconds).
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

    # Console output for `aspire logs` / container logs, OTLP for the dashboard's
    # structured logs page. `extra={...}` fields become log record attributes and the
    # active span's trace/span ids are attached automatically.
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    console = logging.StreamHandler()
    console.setFormatter(logging.Formatter("%(levelname)s %(name)s: %(message)s"))
    root.addHandler(console)
    root.addHandler(LoggingHandler(level=logging.INFO, logger_provider=logger_provider))
    # The gen_ai/HTTP spans already record each model call; skip the SDK's per-request INFO line.
    logging.getLogger("httpx2").setLevel(logging.WARNING)

    # openai-v2 emits gen_ai.* chat spans plus the
    # gen_ai.client.token.usage and gen_ai.client.operation.duration metrics.
    OpenAIInstrumentor().instrument()


def instrument_app(app: FastAPI) -> None:
    """Add server spans and http.server.* metrics for incoming requests."""
    # Skip the per-message ASGI receive/send spans so traces show only the request and domain spans.
    FastAPIInstrumentor.instrument_app(app, excluded_urls="health", exclude_spans=["receive", "send"])


def capture_message_content() -> bool:
    """Mirror the GenAI content-capture switch so the custom agent and tool spans follow the same policy.
    Only the modes that put content on spans enable it; anything else (including unknown values) fails closed."""
    mode = os.getenv("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "").strip().lower()
    return mode in ("span_only", "span_and_event")
