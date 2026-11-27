/**
 * UI action spans. `@opentelemetry/api` is a no-op until telemetry.js registers the SDK, so this costs
 * nothing in production builds.
 *
 * The SDK's StackContextManager only keeps a span active synchronously, so `fn` must start its fetch
 * before its first `await` for the request to become a child of the span.
 */
import { SpanStatusCode, trace } from "@opentelemetry/api";

const tracer = trace.getTracer("react-tracking-board");

export function traced(name, attributes, fn) {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await fn(span);
    } catch (err) {
      span.recordException(err);
      span.setStatus({ code: SpanStatusCode.ERROR, message: err.message });
      throw err;
    } finally {
      span.end();
    }
  });
}