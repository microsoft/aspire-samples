/**
 * UI action spans. `@opentelemetry/api` is a no-op until telemetry.ts registers the SDK, so this
 * costs nothing in production builds.
 *
 * The web SDK's default context manager only keeps a span active synchronously. `fn` receives
 * `step`, which runs a callback with the action span active again, so fetches started after an
 * `await` still become children of the action (one click, one trace).
 */
import { context, SpanStatusCode, trace, type Attributes, type Span } from '@opentelemetry/api';

const tracer = trace.getTracer('contoso-coffee-storefront');

export type Step = <T>(fn: () => T) => T;

export function traced<T>(name: string, attributes: Attributes, fn: (span: Span, step: Step) => Promise<T>): Promise<T> {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    const spanContext = trace.setSpan(context.active(), span);
    const step: Step = (callback) => context.with(spanContext, callback);
    try {
      return await fn(span, step);
    } catch (err) {
      span.recordException(err as Error);
      span.setStatus({ code: SpanStatusCode.ERROR, message: (err as Error).message });
      throw err;
    } finally {
      span.end();
    }
  });
}
