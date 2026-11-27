/**
 * UI action spans. `@opentelemetry/api` is a no-op until telemetry.ts registers the SDK, so this
 * costs nothing in production builds. The fetch instrumentation makes each request a child of the
 * action span, so one click is one trace from the browser to the worker that handles the task.
 */
import { SpanStatusCode, trace, type Attributes, type Span } from '@opentelemetry/api';

const tracer = trace.getTracer('task-queue-ui');

export function traced<T>(name: string, attributes: Attributes, fn: (span: Span) => Promise<T>): Promise<T> {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await fn(span);
    } catch (err) {
      span.recordException(err as Error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: (err as Error).message });
      throw err;
    } finally {
      span.end();
    }
  });
}
