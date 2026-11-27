import { Attributes, Context, SpanStatusCode, context, trace } from '@opentelemetry/api';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

// A no-op tracer unless src/telemetry.ts registered a provider (run mode under Aspire only).
const tracer = trace.getTracer('petclinic-frontend');

/**
 * Subscribes to `work` inside `ctx`. Use it for follow-up requests that start asynchronously (for example in a
 * switchMap after a response), which would otherwise lose the span that `traced` made active.
 */
export function resume<T>(ctx: Context, work: () => Observable<T>): Observable<T> {
  return new Observable<T>((subscriber) => context.with(ctx, () => work().subscribe(subscriber)));
}

/**
 * Runs a UI action in its own span. `work` is subscribed while the span is the active context, so the fetch
 * instrumentation parents every request it starts synchronously (and adds `traceparent`, which Spring continues).
 * The span ends when the observable completes or fails.
 */
export function traced<T>(
  name: string,
  attributes: Attributes,
  work: () => Observable<T>,
): Observable<T> {
  return new Observable<T>((subscriber) => {
    const span = tracer.startSpan(name, { attributes });
    const subscription = context.with(trace.setSpan(context.active(), span), () =>
      work().subscribe({
        next: (value) => subscriber.next(value),
        error: (error: unknown) => {
          const status = error instanceof HttpErrorResponse ? error.status : undefined;
          if (status !== undefined) {
            span.setAttribute('http.response.status_code', status);
          }
          span.setStatus({
            code: SpanStatusCode.ERROR,
            message: (error as { message?: string })?.message ?? String(error),
          });
          span.end();
          subscriber.error(error);
        },
        complete: () => {
          span.end();
          subscriber.complete();
        },
      }),
    );
    return () => subscription.unsubscribe();
  });
}
