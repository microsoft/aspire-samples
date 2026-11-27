/**
 * Redis-backed shopping carts with a sliding expiry.
 *
 * - `contoso-coffee:cart:{id}` is a hash with `createdAt`, the W3C `traceparent` of the request
 *   that created the cart, and one `item:{sku}` field per line holding its quantity. Every read
 *   or write renews the key's TTL (30 minutes by default), so idle carts clean themselves up.
 * - `contoso-coffee:carts:expiry` is a sorted set of cart ids scored by expiry time. A background
 *   sweep uses it to count carts that expired before checkout (abandoned carts) and to report
 *   how many carts are active.
 * - `contoso-coffee:orders:recent` is a capped list of the latest orders.
 */
import { randomInt, randomUUID } from 'node:crypto';
import { context, propagation, ROOT_CONTEXT, SpanStatusCode, trace } from '@opentelemetry/api';
import { createClient } from 'redis';
import { MAX_LINE_QUANTITY, ORDER_LIMIT_CENTS, PRODUCTS, toDollars } from './catalog.js';
import { logger } from './logger.js';
import { ProblemError } from './problems.js';
import {
  activeCarts,
  cartsExpired,
  checkouts,
  itemsAdded,
  itemsRemoved,
  orderValue,
  tracer,
} from './telemetry.js';

export const CART_TTL_SECONDS = Number(process.env.CART_TTL_SECONDS ?? 1800);

const KEY_PREFIX = 'contoso-coffee:';
const EXPIRY_INDEX = `${KEY_PREFIX}carts:expiry`;
const RECENT_ORDERS = `${KEY_PREFIX}orders:recent`;
const RECENT_ORDERS_MAX = 20;
const ITEM_PREFIX = 'item:';
const ORDER_ID_ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

/** @param {string} id */
const cartKey = (id) => `${KEY_PREFIX}cart:${id}`;

// Aspire injects the connection URI (including the password) through `withReference(redis)`.
export const redis = createClient({ url: process.env.REDIS_URI });
redis.on('error', (err) => logger.error({ error: err.message }, 'Redis connection error'));

/** @typedef {import('./catalog.js').Product} Product */

/** @param {string} cartId */
const cartNotFound = (cartId) =>
  new ProblemError(
    404,
    'Cart not found',
    `Cart '${cartId}' doesn't exist or expired after ${Math.round(CART_TTL_SECONDS / 60)} minutes without activity. Create a new cart with POST /carts.`,
  );

/** @param {number} cents */
const formatUsd = (cents) => `$${(cents / 100).toFixed(2)}`;

/**
 * Runs `fn` in a new active span. Expected outcomes (problem details) are left to the caller to
 * describe with events; anything else marks the span as failed.
 * @template T
 * @param {string} name
 * @param {import('@opentelemetry/api').Attributes} attributes
 * @param {(span: import('@opentelemetry/api').Span) => Promise<T>} fn
 * @returns {Promise<T>}
 */
function inSpan(name, attributes, fn) {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await fn(span);
    } catch (err) {
      if (!(err instanceof ProblemError)) {
        span.recordException(/** @type {Error} */ (err));
        span.setStatus({ code: SpanStatusCode.ERROR, message: /** @type {Error} */ (err).message });
      }
      throw err;
    } finally {
      span.end();
    }
  });
}

/**
 * Queues the sliding-expiry refresh (TTL on the hash, score in the expiry index) on a transaction.
 * @param {ReturnType<typeof redis.multi>} multi
 * @param {string} id
 * @param {number} [ttlSeconds]
 */
const touch = (multi, id, ttlSeconds = CART_TTL_SECONDS) =>
  multi.expire(cartKey(id), ttlSeconds).zAdd(EXPIRY_INDEX, { score: Date.now() + ttlSeconds * 1000, value: id });

/** @param {string} id */
async function loadFields(id) {
  const fields = await redis.hGetAll(cartKey(id));
  if (!fields.createdAt) throw cartNotFound(id);
  return /** @type {Record<string, string>} */ ({ ...fields });
}

/** @param {Record<string, string>} fields */
function summarize(fields) {
  const lines = [];
  let itemCount = 0;
  let subtotalCents = 0;
  for (const product of PRODUCTS) {
    const quantity = Number(fields[ITEM_PREFIX + product.sku] ?? 0);
    if (!(quantity > 0)) continue;
    lines.push({
      sku: product.sku,
      name: product.name,
      category: product.category,
      unitPrice: toDollars(product.priceCents),
      quantity,
      lineTotal: toDollars(product.priceCents * quantity),
    });
    itemCount += quantity;
    subtotalCents += product.priceCents * quantity;
  }
  return { lines, itemCount, subtotalCents };
}

/**
 * @param {string} id
 * @param {Record<string, string>} fields
 * @param {number} [ttlSeconds]
 */
function toCart(id, fields, ttlSeconds = CART_TTL_SECONDS) {
  const { lines, itemCount, subtotalCents } = summarize(fields);
  return {
    id,
    lines,
    itemCount,
    subtotal: toDollars(subtotalCents),
    currency: 'USD',
    orderLimit: toDollars(ORDER_LIMIT_CENTS),
    createdAt: fields.createdAt,
    expiresAt: new Date(Date.now() + ttlSeconds * 1000).toISOString(),
    ttlSeconds,
  };
}

/** @param {ReturnType<typeof toCart>} cart */
const cartAttributes = (cart) => ({
  'cart.line_count': cart.lines.length,
  'cart.item_count': cart.itemCount,
  'cart.subtotal': cart.subtotal,
});

export function createCart() {
  return inSpan('cart.create', {}, async (span) => {
    const id = randomUUID();
    // Keep the creating request's trace context with the cart, so checkout can link back to it.
    /** @type {Record<string, string>} */
    const carrier = {};
    propagation.inject(context.active(), carrier);
    const fields = { createdAt: new Date().toISOString(), traceparent: carrier.traceparent ?? '' };
    await touch(redis.multi().hSet(cartKey(id), fields), id).exec();
    span.setAttribute('cart.id', id);
    logger.info({ cartId: id, ttlSeconds: CART_TTL_SECONDS }, 'Cart created');
    return toCart(id, fields);
  });
}

/**
 * Reads a cart and renews its expiry.
 * @param {string} id
 */
export async function getCart(id) {
  const fields = await loadFields(id);
  await touch(redis.multi(), id).exec();
  return toCart(id, fields);
}

/**
 * @param {string} id
 * @param {Product} product
 * @param {number} quantity
 */
export function addItem(id, product, quantity) {
  const attributes = {
    'cart.id': id,
    'product.sku': product.sku,
    'product.category': product.category,
    'cart.item.quantity': quantity,
  };
  return inSpan('cart.add_item', attributes, async (span) => {
    const fields = await loadFields(id);
    const field = ITEM_PREFIX + product.sku;
    const current = Number(fields[field] ?? 0);
    if (current + quantity > MAX_LINE_QUANTITY) {
      span.addEvent('cart.line_limit_reached', { 'cart.line.quantity': current, 'cart.line.max_quantity': MAX_LINE_QUANTITY });
      throw new ProblemError(
        409,
        'Line quantity limit reached',
        `A cart can hold at most ${MAX_LINE_QUANTITY} of '${product.name}', and this cart already has ${current}.`,
        { sku: product.sku, quantity: current, maxQuantity: MAX_LINE_QUANTITY },
      );
    }
    fields[field] = String(current + quantity);
    await touch(redis.multi().hSet(cartKey(id), field, fields[field]), id).exec();

    const cart = toCart(id, fields);
    itemsAdded.add(quantity, { 'product.category': product.category });
    span.setAttributes(cartAttributes(cart));
    logger.info(
      { cartId: id, sku: product.sku, quantity, lineQuantity: current + quantity, subtotal: cart.subtotal },
      `Added ${quantity} × ${product.name} to cart`,
    );
    return cart;
  });
}

/**
 * Sets a line's quantity; 0 removes the line.
 * @param {string} id
 * @param {Product} product
 * @param {number} quantity
 */
export function setItemQuantity(id, product, quantity) {
  const attributes = {
    'cart.id': id,
    'product.sku': product.sku,
    'product.category': product.category,
    'cart.item.quantity': quantity,
  };
  return inSpan('cart.update_item', attributes, async (span) => {
    const fields = await loadFields(id);
    const field = ITEM_PREFIX + product.sku;
    const previous = Number(fields[field] ?? 0);
    const multi = redis.multi();
    if (quantity === 0) {
      multi.hDel(cartKey(id), field);
      delete fields[field];
    } else {
      fields[field] = String(quantity);
      multi.hSet(cartKey(id), field, fields[field]);
    }
    await touch(multi, id).exec();

    const delta = quantity - previous;
    const category = { 'product.category': product.category };
    if (delta > 0) itemsAdded.add(delta, category);
    if (delta < 0) itemsRemoved.add(-delta, category);
    const cart = toCart(id, fields);
    span.setAttributes(cartAttributes(cart));
    logger.info(
      { cartId: id, sku: product.sku, previousQuantity: previous, quantity, subtotal: cart.subtotal },
      `Changed ${product.name} from ${previous} to ${quantity}`,
    );
    return cart;
  });
}

/**
 * @param {string} id
 * @param {Product} product
 */
export function removeItem(id, product) {
  const attributes = { 'cart.id': id, 'product.sku': product.sku, 'product.category': product.category };
  return inSpan('cart.remove_item', attributes, async (span) => {
    const fields = await loadFields(id);
    const field = ITEM_PREFIX + product.sku;
    const previous = Number(fields[field] ?? 0);
    if (!previous) {
      throw new ProblemError(404, 'Item not in cart', `Cart '${id}' has no '${product.name}' line.`, { sku: product.sku });
    }
    delete fields[field];
    await touch(redis.multi().hDel(cartKey(id), field), id).exec();

    itemsRemoved.add(previous, { 'product.category': product.category });
    const cart = toCart(id, fields);
    span.setAttributes(cartAttributes(cart));
    logger.info({ cartId: id, sku: product.sku, quantity: previous, subtotal: cart.subtotal }, `Removed ${product.name} from cart`);
    return cart;
  });
}

/** @param {string} id */
export function deleteCart(id) {
  return inSpan('cart.delete', { 'cart.id': id }, async () => {
    const [deleted] = await redis.multi().del(cartKey(id)).zRem(EXPIRY_INDEX, id).exec();
    if (!deleted) throw cartNotFound(id);
    logger.info({ cartId: id }, 'Cart deleted');
  });
}

/** Links the checkout span to the trace that created the cart (stored in Redis as `traceparent`). */
function linkToCreation(span, traceparent) {
  if (!traceparent) return;
  const created = trace.getSpanContext(propagation.extract(ROOT_CONTEXT, { traceparent }));
  if (created) span.addLink({ context: created, attributes: { 'cart.link.reason': 'cart created' } });
}

const newOrderId = () =>
  `CC-${Array.from({ length: 6 }, () => ORDER_ID_ALPHABET[randomInt(ORDER_ID_ALPHABET.length)]).join('')}`;

/** @param {string} id */
export function checkout(id) {
  return inSpan('cart.checkout', { 'cart.id': id }, async (span) => {
    const fields = await loadFields(id);
    linkToCreation(span, fields.traceparent);
    const { lines, itemCount, subtotalCents } = summarize(fields);
    const subtotal = toDollars(subtotalCents);
    const ageSeconds = Math.max(0, Math.round((Date.now() - Date.parse(fields.createdAt)) / 1000));
    span.setAttributes({
      'cart.line_count': lines.length,
      'cart.item_count': itemCount,
      'cart.subtotal': subtotal,
      'cart.age_seconds': ageSeconds,
    });

    if (lines.length === 0) {
      checkouts.add(1, { 'cart.checkout.outcome': 'empty' });
      span.addEvent('checkout.rejected', { 'checkout.reason': 'empty' });
      throw new ProblemError(409, 'Cart is empty', `Cart '${id}' has no items to check out.`);
    }
    if (subtotalCents > ORDER_LIMIT_CENTS) {
      const overBy = subtotalCents - ORDER_LIMIT_CENTS;
      checkouts.add(1, { 'cart.checkout.outcome': 'over_limit' });
      span.addEvent('checkout.rejected', {
        'checkout.reason': 'over_limit',
        'cart.subtotal': subtotal,
        'order.limit': toDollars(ORDER_LIMIT_CENTS),
      });
      logger.warn(
        { cartId: id, subtotal, orderLimit: toDollars(ORDER_LIMIT_CENTS), itemCount },
        `Checkout rejected: ${formatUsd(subtotalCents)} is over the ${formatUsd(ORDER_LIMIT_CENTS)} order limit`,
      );
      throw new ProblemError(
        409,
        'Order limit exceeded',
        `Orders are limited to ${formatUsd(ORDER_LIMIT_CENTS)} and this cart comes to ${formatUsd(subtotalCents)}. Remove at least ${formatUsd(overBy)} of items to check out.`,
        { subtotal, orderLimit: toDollars(ORDER_LIMIT_CENTS) },
      );
    }

    const order = {
      orderId: newOrderId(),
      cartId: id,
      placedAt: new Date().toISOString(),
      lines,
      itemCount,
      subtotal,
      currency: 'USD',
    };
    // The cart becomes an order atomically: drop the cart and its index entry, record the order.
    await redis
      .multi()
      .del(cartKey(id))
      .zRem(EXPIRY_INDEX, id)
      .lPush(RECENT_ORDERS, JSON.stringify(order))
      .lTrim(RECENT_ORDERS, 0, RECENT_ORDERS_MAX - 1)
      .exec();

    checkouts.add(1, { 'cart.checkout.outcome': 'completed' });
    orderValue.record(subtotal);
    span.setAttribute('order.id', order.orderId);
    span.addEvent('order.placed', { 'order.id': order.orderId, 'order.total': subtotal, 'order.item_count': itemCount });
    logger.info(
      { orderId: order.orderId, cartId: id, itemCount, lineCount: lines.length, subtotal, cartAgeSeconds: ageSeconds },
      `Order ${order.orderId} placed: ${itemCount} items, ${formatUsd(subtotalCents)}`,
    );
    return order;
  });
}

/** @param {number} limit */
export async function recentOrders(limit) {
  const entries = await redis.lRange(RECENT_ORDERS, 0, limit - 1);
  return entries.map((entry) => JSON.parse(entry));
}

/**
 * Shortens a cart's expiry (used by the shopper simulation for abandoned carts).
 * @param {string} id
 * @param {number} ttlSeconds
 */
export async function expireSoon(id, ttlSeconds) {
  await touch(redis.multi(), id, ttlSeconds).exec();
}

let activeCartCount = 0;
activeCarts.addCallback((result) => result.observe(activeCartCount));

/**
 * Counts carts whose expiry passed (Redis already dropped their hashes) and refreshes the
 * active-cart gauge. Redis calls outside a span aren't traced (requireParentSpan), so idle
 * sweeps stay out of the trace list; a sweep that finds expired carts gets its own trace.
 */
async function sweepExpiredCarts() {
  const expired = await redis.zRange(EXPIRY_INDEX, '-inf', Date.now(), {
    BY: 'SCORE',
    LIMIT: { offset: 0, count: 100 },
  });
  if (expired.length) {
    await inSpan('cart.expire', { 'cart.expired.candidates': expired.length }, async (span) => {
      // ZREM reports how many members this process removed, so replicas never double count.
      const removed = await redis.zRem(EXPIRY_INDEX, expired);
      span.setAttribute('cart.expired.count', removed);
      for (const cartId of expired) span.addEvent('cart.expired', { 'cart.id': cartId });
      if (removed > 0) {
        cartsExpired.add(removed);
        logger.info(
          { expiredCarts: removed, ttlSeconds: CART_TTL_SECONDS },
          `${removed} abandoned ${removed === 1 ? 'cart' : 'carts'} expired before checkout`,
        );
      }
    });
  }
  activeCartCount = await redis.zCard(EXPIRY_INDEX);
}

export function startExpirySweep(intervalMs = 5000) {
  const run = () => sweepExpiredCarts().catch((err) => logger.warn({ error: err.message }, 'Expired-cart sweep failed'));
  setInterval(run, intervalMs).unref();
  return run();
}
