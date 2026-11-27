/**
 * UI action spans. `@opentelemetry/api` is a no-op until telemetry.ts registers the SDK, so this costs
 * nothing in production builds.
 *
 * The SDK's StackContextManager only keeps a span active synchronously, so `fn` must start its fetch
 * before its first `await` for the request to become a child of the span.
 */
import { SpanStatusCode, trace, type Attributes, type Span } from '@opentelemetry/api';

const tracer = trace.getTracer('reactvite-tracking-board');

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