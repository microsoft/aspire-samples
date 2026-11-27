"""Python worker: consumes `tasks.analyze` and profiles CSV datasets with pandas.

Each task continues the trace the API started: the W3C trace context comes in the message headers,
and the worker's `publish task_status` and `publish results` spans carry it back to the API.
"""

import asyncio
import json
import logging
import os
import random
import re
import sys
import time
import uuid
from datetime import datetime, timezone
from urllib.parse import urlparse

from telemetry import WorkerMetrics, configure_opentelemetry, tracer

# Configure providers before anything creates loggers or instruments.
configure_opentelemetry()

from aio_pika import DeliveryMode, Message, connect_robust  # noqa: E402
from aio_pika.abc import AbstractChannel, AbstractIncomingMessage  # noqa: E402
from opentelemetry import propagate  # noqa: E402
from opentelemetry.propagators.textmap import Getter  # noqa: E402
from opentelemetry.trace import Span, SpanKind, Status, StatusCode  # noqa: E402

from analysis import DatasetError, analyze, load  # noqa: E402

WORKER = "worker-python"
TASK_TYPE = "analyze"
QUEUE = "tasks.analyze"
STATUS_QUEUE = "task_status"
RESULTS_QUEUE = "results"
PREFETCH = 4
MAX_DATA_LENGTH = 10_000
TASK_ID = re.compile(r"^task-[0-9a-f]{8}$")

logger = logging.getLogger("taskqueue.worker")
worker_metrics = WorkerMetrics()
broker = urlparse(os.environ.get("MESSAGING_URI", "amqp://localhost:5672"))


class InvalidTaskError(ValueError):
    """The message isn't a task this worker can process."""


class HeaderGetter(Getter):
    """AMQP header values arrive as str or bytes, depending on the publisher's client library."""

    def get(self, carrier, key):
        value = carrier.get(key)
        if value is None:
            return None
        return [value.decode() if isinstance(value, bytes) else str(value)]

    def keys(self, carrier):
        return list(carrier.keys())


HEADER_GETTER = HeaderGetter()


def messaging_attributes(operation: str, queue: str, message_id: str | None, task_id: str | None, size: int) -> dict:
    """Messaging semantic-convention attributes. Messages use the default exchange with the queue
    name as the routing key, so the destination is the routing key alone."""
    attributes = {
        "messaging.system": "rabbitmq",
        "messaging.operation.type": operation,
        "messaging.operation.name": "publish" if operation == "send" else "process",
        "messaging.destination.name": queue,
        "messaging.rabbitmq.destination.routing_key": queue,
        "messaging.message.body.size": size,
        "server.address": broker.hostname or "localhost",
        "server.port": broker.port or 5672,
    }
    if message_id:
        attributes["messaging.message.id"] = message_id
    if task_id:
        attributes["messaging.message.conversation_id"] = task_id
    return attributes


async def publish(channel: AbstractChannel, queue: str, payload: dict) -> None:
    """Publishes JSON under a PRODUCER span whose context is injected into the message headers."""
    body = json.dumps(payload).encode()
    message_id = uuid.uuid4().hex
    attributes = messaging_attributes("send", queue, message_id, payload["taskId"], len(body))
    with tracer.start_as_current_span(f"publish {queue}", kind=SpanKind.PRODUCER, attributes=attributes):
        headers: dict[str, str] = {}
        propagate.inject(headers)
        # Publisher confirms are on by default, so this waits for the broker to accept the message.
        await channel.default_exchange.publish(
            Message(
                body,
                content_type="application/json",
                delivery_mode=DeliveryMode.PERSISTENT,
                message_id=message_id,
                correlation_id=payload["taskId"],
                timestamp=datetime.now(timezone.utc),
                headers=headers,
            ),
            routing_key=queue,
        )


def validate(task: object) -> tuple[str, str | None]:
    """Returns the task id and a validation error, if any."""
    if not isinstance(task, dict) or not isinstance(task.get("taskId"), str) or not TASK_ID.match(task["taskId"]):
        raise InvalidTaskError("The message has no valid taskId.")
    if task.get("type") != TASK_TYPE:
        return task["taskId"], f"Unsupported task type '{task.get('type')}'; this worker handles '{TASK_TYPE}'."
    data = task.get("data")
    if not isinstance(data, str) or not data.strip():
        return task["taskId"], "The document is empty."
    if len(data) > MAX_DATA_LENGTH:
        return task["taskId"], f"The document is longer than {MAX_DATA_LENGTH} characters."
    return task["taskId"], None


async def analyze_dataset(task_id: str, name: str, data: str) -> dict:
    """The domain span: loads the dataset (simulated latency) and profiles it with pandas."""
    with tracer.start_as_current_span(
        "analyze dataset", attributes={"taskqueue.task.id": task_id, "taskqueue.document.name": name}
    ) as span:
        # Stands in for downloading the dataset from storage before pandas reads it.
        await asyncio.sleep(random.uniform(0.25, 0.75))
        frame = load(data)
        span.add_event("dataset.loaded", {"taskqueue.dataset.rows": len(frame), "taskqueue.dataset.columns": len(frame.columns)})
        worker_metrics.dataset_rows.record(len(frame))

        result = analyze(frame)
        details = result["details"]
        span.set_attributes(
            {
                "taskqueue.dataset.rows": details["rows"],
                "taskqueue.dataset.columns": len(details["columns"]),
                "taskqueue.dataset.measure": details["measure"],
            }
        )
        span.add_event("dataset.profiled", {"taskqueue.dataset.numeric_columns": len(details["numeric"])})
        return result


async def handle_task(channel: AbstractChannel, task: dict, span: Span) -> None:
    task_id, invalid = validate(task)
    name = str(task.get("name") or "dataset.csv")
    span.set_attributes({"taskqueue.task.id": task_id, "taskqueue.task.type": TASK_TYPE, "taskqueue.document.name": name})

    attributes = {"taskqueue.task.type": TASK_TYPE}
    started = time.perf_counter()
    worker_metrics.active.add(1, attributes)
    await publish(channel, STATUS_QUEUE, {"taskId": task_id, "status": "processing", "worker": WORKER, "startedAt": datetime.now(timezone.utc).isoformat()})

    result, error = None, None
    try:
        if invalid:
            raise InvalidTaskError(invalid)
        result = await analyze_dataset(task_id, name, task["data"])
        details = result["details"]
        logger.info(
            "Analyzed %s: %s",
            name,
            result["headline"],
            extra={
                "task_id": task_id,
                "document_name": name,
                "rows": details["rows"],
                "columns": len(details["columns"]),
                "measure": details["measure"],
                "measure_total": details["numeric"][details["measure"]]["sum"],
            },
        )
    except Exception as exc:
        error = {"type": type(exc).__name__, "message": str(exc)}
        worker_metrics.failed.add(1, {**attributes, "error.type": error["type"]})
        span.add_event("task.failed", {"error.type": error["type"]})
        # Bad documents are expected; anything else is a bug worth an error with a stack trace.
        expected = isinstance(exc, (DatasetError, InvalidTaskError))
        logger.log(
            logging.WARNING if expected else logging.ERROR,
            "Could not analyze %s: %s",
            name,
            exc,
            exc_info=None if expected else exc,
            extra={"task_id": task_id, "document_name": name, "error_type": error["type"]},
        )
    finally:
        worker_metrics.active.add(-1, attributes)

    outcome = "failed" if error else "completed"
    duration_ms = round((time.perf_counter() - started) * 1000)
    worker_metrics.processed.add(1, {**attributes, "taskqueue.task.outcome": outcome})
    worker_metrics.duration.record(duration_ms / 1000, {**attributes, "taskqueue.task.outcome": outcome})
    await publish(
        channel,
        RESULTS_QUEUE,
        {
            "taskId": task_id,
            "status": outcome,
            "worker": WORKER,
            "completedAt": datetime.now(timezone.utc).isoformat(),
            "durationMs": duration_ms,
            "result": result,
            "error": error,
        },
    )


async def on_message(channel: AbstractChannel, message: AbstractIncomingMessage) -> None:
    """Handles one delivery under a CONSUMER span parented to the API's publish span."""
    parent = propagate.extract(message.headers or {}, getter=HEADER_GETTER)
    attributes = messaging_attributes("process", QUEUE, message.message_id, message.correlation_id, len(message.body))
    attributes["messaging.rabbitmq.message.delivery_tag"] = message.delivery_tag or 0
    with tracer.start_as_current_span(
        f"process {QUEUE}", context=parent, kind=SpanKind.CONSUMER, attributes=attributes, record_exception=False
    ) as span:
        try:
            await handle_task(channel, json.loads(message.body), span)
            await message.ack()
        except Exception as exc:
            # Drop messages that can't be handled instead of redelivering them forever.
            span.record_exception(exc)
            span.set_status(Status(StatusCode.ERROR, str(exc)))
            span.set_attribute("error.type", type(exc).__name__)
            logger.error("Dropped a %s message that could not be processed: %s", QUEUE, exc, extra={"error_type": type(exc).__name__})
            await message.reject(requeue=False)


async def main() -> None:
    if "MESSAGING_URI" not in os.environ:
        sys.exit("MESSAGING_URI is not set; run the worker from the Aspire AppHost.")

    connection = await connect_robust(os.environ["MESSAGING_URI"])
    async with connection:
        channel = await connection.channel()
        await channel.set_qos(prefetch_count=PREFETCH)
        queue = await channel.declare_queue(QUEUE, durable=True)
        await channel.declare_queue(STATUS_QUEUE, durable=True)
        await channel.declare_queue(RESULTS_QUEUE, durable=True)

        await queue.consume(lambda message: on_message(channel, message))
        logger.info("Python worker consuming %s (up to %d tasks at a time)", QUEUE, PREFETCH, extra={"queue": QUEUE, "prefetch": PREFETCH})
        await asyncio.Future()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
