/**
 * Dev-only shopper simulation behind the "Simulate shoppers" dashboard command (see apphost.mts).
 * Shoppers go through the same cart functions as the HTTP API, so their traces, metrics and logs
 * look like real traffic: most check out, one abandons the cart and one runs into the order limit.
 */
import { randomInt } from 'node:crypto';
import { findProduct, PRODUCTS } from './catalog.js';
import { addItem, checkout, createCart, expireSoon, removeItem, setItemQuantity } from './carts.js';
import { logger } from './logger.js';
import { ProblemError } from './problems.js';
import { tracer } from './telemetry.js';

/** Abandoned simulated carts expire after this instead of 30 minutes, so the expiry metric moves. */
export const ABANDONED_CART_TTL_SECONDS = 45;

const COFFEES = PRODUCTS.filter((product) => product.category === 'coffee');
const GEAR = PRODUCTS.filter((product) => product.category === 'brewing' && product.sku !== 'burr-grinder');
const GRINDER = /** @type {import('./catalog.js').Product} */ (findProduct('burr-grinder'));

/** @template T @param {readonly T[]} items @param {number} count */
function pick(items, count) {
  const pool = [...items];
  return Array.from({ length: Math.min(count, pool.length) }, () => pool.splice(randomInt(pool.length), 1)[0]);
}

/** A plausible basket: 1-3 coffees (1-3 bags each) and sometimes a piece of brewing gear. */
function basket() {
  const lines = pick(COFFEES, 1 + randomInt(3)).map((product) => ({ product, quantity: 1 + randomInt(3) }));
  if (randomInt(100) < 45) lines.push({ product: pick(GEAR, 1)[0], quantity: 1 });
  if (randomInt(100) < 15) lines.push({ product: GRINDER, quantity: 1 });
  return lines;
}

/** @param {number} index */
const planFor = (index) => (index % 8 === 5 ? 'abandon' : index % 8 === 6 ? 'over_limit' : 'checkout');

/** @param {number} index */
function shop(index) {
  const plan = planFor(index);
  return tracer.startActiveSpan(
    'cart.simulate_shopper',
    { attributes: { 'simulation.shopper': index + 1, 'simulation.plan': plan } },
    async (span) => {
      try {
        let cart = await createCart();
        const lines = plan === 'over_limit' ? [...basket(), { product: GRINDER, quantity: 4 }] : basket();
        for (const { product, quantity } of lines) cart = await addItem(cart.id, product, quantity);

        if (plan === 'abandon') {
          await expireSoon(cart.id, ABANDONED_CART_TTL_SECONDS);
          return { plan, order: null, rejected: false };
        }

        // Shoppers change their minds now and then.
        if (cart.lines.length > 2 && randomInt(100) < 30) {
          cart = await removeItem(cart.id, /** @type {import('./catalog.js').Product} */ (findProduct(cart.lines[0].sku)));
        } else if (randomInt(100) < 30) {
          const line = cart.lines[randomInt(cart.lines.length)];
          const product = /** @type {import('./catalog.js').Product} */ (findProduct(line.sku));
          if (line.quantity < 3) cart = await setItemQuantity(cart.id, product, line.quantity + 1);
        }

        let rejected = false;
        try {
          return { plan, order: await checkout(cart.id), rejected };
        } catch (err) {
          if (!(err instanceof ProblemError) || err.status !== 409) throw err;
          // Over the order limit: keep one grinder and try again.
          rejected = true;
          await setItemQuantity(cart.id, GRINDER, 1);
          return { plan, order: await checkout(cart.id), rejected };
        }
      } finally {
        span.end();
      }
    },
  );
}

/** @param {number} shoppers */
export function simulateShoppers(shoppers) {
  return tracer.startActiveSpan(
    'cart.simulate_shoppers',
    { attributes: { 'simulation.shoppers': shoppers } },
    async (span) => {
      try {
        const results = await Promise.all(Array.from({ length: shoppers }, (_, i) => shop(i)));
        const orders = results.flatMap((result) => (result.order ? [result.order] : []));
        const revenueCents = orders.reduce((sum, order) => sum + Math.round(order.subtotal * 100), 0);
        const summary = {
          shoppers,
          ordersPlaced: orders.length,
          abandonedCarts: results.filter((result) => result.plan === 'abandon').length,
          orderLimitRejections: results.filter((result) => result.rejected).length,
          revenue: revenueCents / 100,
          abandonedCartTtlSeconds: ABANDONED_CART_TTL_SECONDS,
          orders: orders.map(({ orderId, itemCount, subtotal }) => ({ orderId, itemCount, subtotal })),
        };
        span.setAttributes({
          'simulation.orders_placed': summary.ordersPlaced,
          'simulation.abandoned_carts': summary.abandonedCarts,
          'simulation.order_limit_rejections': summary.orderLimitRejections,
        });
        logger.info(
          { ...summary, orders: undefined },
          `Simulated ${shoppers} shoppers: ${summary.ordersPlaced} orders ($${summary.revenue.toFixed(2)}), ${summary.abandonedCarts} abandoned, ${summary.orderLimitRejections} over the order limit`,
        );
        return summary;
      } finally {
        span.end();
      }
    },
  );
}
