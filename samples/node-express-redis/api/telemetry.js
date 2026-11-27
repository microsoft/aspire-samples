/**
 * Domain telemetry for the cart service: one tracer and one meter named `Contoso.Cart`.
 * The global providers are registered by instrumentation.js; without it these are no-ops.
 *
 * Metric attributes only use small, fixed value sets (product category, checkout outcome).
 * Cart ids, order ids and SKUs go on spans and logs instead.
 */
import { metrics, trace } from '@opentelemetry/api';

const name = 'Contoso.Cart';
const version = '1.0.0';

export const tracer = trace.getTracer(name, version);
const meter = metrics.getMeter(name, version);

export const itemsAdded = meter.createCounter('cart.items.added', {
  description: 'Units added to carts, by product category.',
  unit: '{item}',
});

export const itemsRemoved = meter.createCounter('cart.items.removed', {
  description: 'Units removed from carts, by product category.',
  unit: '{item}',
});

export const checkouts = meter.createCounter('cart.checkouts', {
  description: 'Checkout attempts, by outcome (completed, over_limit or empty).',
  unit: '{checkout}',
});

export const orderValue = meter.createHistogram('cart.order.value', {
  description: 'Subtotal of completed orders.',
  unit: 'USD',
  advice: { explicitBucketBoundaries: [10, 25, 50, 75, 100, 150, 200, 300, 400, 500] },
});

export const cartsExpired = meter.createCounter('cart.carts.expired', {
  description: 'Carts whose 30-minute sliding expiry lapsed before checkout (abandoned carts).',
  unit: '{cart}',
});

export const activeCarts = meter.createObservableGauge('cart.carts.active', {
  description: 'Carts currently held in Redis (created and not yet checked out, deleted or expired).',
  unit: '{cart}',
});
