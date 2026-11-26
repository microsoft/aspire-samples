import { SpanStatusCode, context, trace } from '@opentelemetry/api'

const tracer = trace.getTracer('handbook-ui')

/**
 * Runs `fn` inside a UI span. `fetch` calls made synchronously within `fn` become children,
 * so a click, its API request, and the server-side work share one trace.
 */
export async function traced(name, attributes, fn) {
  const span = tracer.startSpan(name, { attributes })
  try {
    return await context.with(trace.setSpan(context.active(), span), fn)
  } catch (err) {
    span.recordException(err)
    span.setStatus({ code: SpanStatusCode.ERROR, message: err.message })
    throw err
  } finally {
    span.end()
  }
}
