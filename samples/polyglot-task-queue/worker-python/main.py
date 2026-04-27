import asyncio
import json
import os
import sys
from datetime import datetime
import pandas as pd
import numpy as np
from aio_pika import connect_robust, Message, DeliveryMode
from aio_pika.abc import AbstractIncomingMessage

# OpenTelemetry imports
from opentelemetry import trace, propagate
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource, SERVICE_NAME, SERVICE_VERSION
from opentelemetry.trace import Status, StatusCode

# Initialize OpenTelemetry
resource = Resource(attributes={
    SERVICE_NAME: os.environ.get('OTEL_SERVICE_NAME', 'worker-python'),
    SERVICE_VERSION: '1.0.0'
})

provider = TracerProvider(resource=resource)
processor = BatchSpanProcessor(OTLPSpanExporter(
    endpoint=os.environ.get('OTEL_EXPORTER_OTLP_ENDPOINT', 'http://localhost:4317'),
    insecure=True
))
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)

tracer = trace.get_tracer(__name__, '1.0.0')

print(f"[{datetime.now().isoformat()}] 📊 OpenTelemetry initialized")
print(f"[{datetime.now().isoformat()}] 📤 Exporting to: {os.environ.get('OTEL_EXPORTER_OTLP_ENDPOINT', 'http://localhost:4317')}")

TASKS_QUEUE = 'tasks'
RESULTS_QUEUE = 'results'
TASK_STATUS_QUEUE = 'task_status'
WORKER_NAME = 'python-worker'

async def publish_task_status(channel, task_id, status, **additional_data):
    """Publish task status update to RabbitMQ with trace context."""
    with tracer.start_as_current_span(
        'rabbitmq.publish task_status',
        kind=trace.SpanKind.PRODUCER
    ) as span:
        try:
            # Messaging semantic conventions
            span.set_attribute('messaging.system', 'rabbitmq')
            span.set_attribute('messaging.destination.name', TASK_STATUS_QUEUE)
            span.set_attribute('messaging.operation', 'publish')
            span.set_attribute('task.id', task_id)
            span.set_attribute('task.status', status)

            status_message = {
                'taskId': task_id,
                'status': status,
                'worker': WORKER_NAME,
                'timestamp': datetime.now().isoformat(),
                **additional_data
            }

            # Inject trace context into message headers
            headers = {}
            propagate.inject(headers)

            await channel.default_exchange.publish(
                Message(
                    body=json.dumps(status_message).encode(),
                    delivery_mode=DeliveryMode.PERSISTENT,
                    headers=headers
                ),
                routing_key=TASK_STATUS_QUEUE
            )

            print(f"[{datetime.now().isoformat()}] Status update published: {task_id} -> {status}")
            span.set_status(Status(StatusCode.OK))
        except Exception as e:
            print(f"[{datetime.now().isoformat()}] Error publishing status: {e}", file=sys.stderr)
            span.record_exception(e)
            span.set_status(Status(StatusCode.ERROR, str(e)))

async def process_task(task_data: dict) -> dict:
    """Process data analysis tasks using pandas."""
    try:
        # Parse the input data (expecting CSV or JSON)
        data_str = task_data.get('data', '')

        # Try to parse as CSV first
        try:
            from io import StringIO
            df = pd.read_csv(StringIO(data_str))
        except Exception:
            # If CSV fails, try JSON
            try:
                df = pd.DataFrame(json.loads(data_str))
            except Exception:
                # Fall back to treating as simple text
                lines = [line.strip() for line in data_str.strip().split('\n') if line.strip()]
                df = pd.DataFrame({'values': lines})

        # Perform data analysis
        result = {
            'row_count': len(df),
            'column_count': len(df.columns),
            'columns': list(df.columns),
            'summary': {}
        }

        # Generate statistics for numeric columns
        numeric_cols = df.select_dtypes(include=[np.number]).columns
        if len(numeric_cols) > 0:
            result['summary']['numeric'] = {}
            for col in numeric_cols:
                result['summary']['numeric'][col] = {
                    'mean': float(df[col].mean()),
                    'median': float(df[col].median()),
                    'std': float(df[col].std()),
                    'min': float(df[col].min()),
                    'max': float(df[col].max())
                }

        # Count for categorical columns
        categorical_cols = df.select_dtypes(include=['object']).columns
        if len(categorical_cols) > 0:
            result['summary']['categorical'] = {}
            for col in categorical_cols:
                value_counts = df[col].value_counts().head(5).to_dict()
                result['summary']['categorical'][col] = {
                    'unique_count': int(df[col].nunique()),
                    'top_values': {str(k): int(v) for k, v in value_counts.items()}
                }

        # Add first few rows as preview
        result['preview'] = df.head(5).to_dict(orient='records')

        return result

    except Exception as e:
        return {
            'error': str(e),
            'error_type': type(e).__name__
        }

async def on_message(message: AbstractIncomingMessage, channel):
    """Handle incoming task messages with distributed tracing."""
    async with message.process():
        # Extract trace context from message headers
        headers_dict = dict(message.headers) if message.headers else {}
        ctx = propagate.extract(headers_dict)

        with tracer.start_as_current_span(
            'rabbitmq.process task',
            context=ctx,
            kind=trace.SpanKind.CONSUMER
        ) as span:
            try:
                # Messaging semantic conventions
                span.set_attribute('messaging.system', 'rabbitmq')
                span.set_attribute('messaging.source.name', TASKS_QUEUE)
                span.set_attribute('messaging.operation', 'process')

                task = json.loads(message.body.decode())
                task_id = task.get('taskId')
                task_type = task.get('type')

                span.set_attribute('task.id', task_id)
                span.set_attribute('task.type', task_type)
                span.set_attribute('messaging.message.id', task_id)

                print(f"[{datetime.now().isoformat()}] Processing task {task_id} (type: {task_type})")

                # Publish processing status
                await publish_task_status(channel, task_id, 'processing')

                # Only process 'analyze' tasks
                if task_type != 'analyze':
                    print(f"[{datetime.now().isoformat()}] Skipping task {task_id} - not an analyze task")
                    await publish_task_status(channel, task_id, 'skipped', reason='not an analyze task')
                    span.add_event('task.skipped', {'reason': 'not an analyze task'})
                    span.set_status(Status(StatusCode.OK))
                    return

                # Process the task with a child span
                with tracer.start_as_current_span('task.process_data') as process_span:
                    process_span.set_attribute('task.id', task_id)
                    result = await process_task(task)

                    if 'error' in result:
                        process_span.set_attribute('task.error', result['error'])
                        process_span.set_status(Status(StatusCode.ERROR, result['error']))
                    else:
                        process_span.set_status(Status(StatusCode.OK))

                # Send result back with trace context
                with tracer.start_as_current_span(
                    'rabbitmq.publish results',
                    kind=trace.SpanKind.PRODUCER
                ) as publish_span:
                    publish_span.set_attribute('messaging.system', 'rabbitmq')
                    publish_span.set_attribute('messaging.destination.name', RESULTS_QUEUE)
                    publish_span.set_attribute('messaging.operation', 'publish')
                    publish_span.set_attribute('task.id', task_id)

                    result_message = {
                        'taskId': task_id,
                        'worker': WORKER_NAME,
                        'result': result,
                        'completedAt': datetime.now().isoformat()
                    }

                    # Inject trace context into result message
                    result_headers = {}
                    propagate.inject(result_headers)

                    await channel.default_exchange.publish(
                        Message(
                            body=json.dumps(result_message).encode(),
                            delivery_mode=DeliveryMode.PERSISTENT,
                            headers=result_headers
                        ),
                        routing_key=RESULTS_QUEUE
                    )

                    print(f"[{datetime.now().isoformat()}] Completed task {task_id}")
                    publish_span.set_status(Status(StatusCode.OK))

                span.add_event('task.completed')
                span.set_status(Status(StatusCode.OK))

            except Exception as e:
                print(f"[{datetime.now().isoformat()}] Error processing message: {e}", file=sys.stderr)
                span.record_exception(e)
                span.set_status(Status(StatusCode.ERROR, str(e)))
                # Publish error status
                if 'task_id' in locals():
                    await publish_task_status(channel, task_id, 'error', error=str(e))

async def main():
    """Main worker loop."""
    rabbitmq_url = os.environ.get('MESSAGING_URI')

    if not rabbitmq_url:
        print("Error: MESSAGING_URI environment variable not set", file=sys.stderr)
        sys.exit(1)

    print(f"[{datetime.now().isoformat()}] Connecting to RabbitMQ...")

    # Connect with automatic reconnection
    connection = await connect_robust(rabbitmq_url)

    async with connection:
        channel = await connection.channel()

        # Set prefetch count to process one message at a time
        await channel.set_qos(prefetch_count=1)

        # Declare queues
        tasks_queue = await channel.declare_queue(TASKS_QUEUE, durable=True)
        await channel.declare_queue(RESULTS_QUEUE, durable=True)
        await channel.declare_queue(TASK_STATUS_QUEUE, durable=True)

        print(f"[{datetime.now().isoformat()}] Python worker started. Waiting for tasks...")

        # Start consuming
        await tasks_queue.consume(lambda msg: on_message(msg, channel))

        # Keep the worker running
        await asyncio.Future()

if __name__ == '__main__':
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print(f"\n[{datetime.now().isoformat()}] Worker stopped")
    except Exception as e:
        print(f"[{datetime.now().isoformat()}] Fatal error: {e}", file=sys.stderr)
        sys.exit(1)
