/**
 * Contoso Coffee cart API: Redis-backed shopping carts with a 30-minute sliding expiry.
 *
 * instrumentation.js is preloaded through NODE_OPTIONS (see apphost.mts). In run mode YARP
 * forwards `/api/*` here with the prefix removed, so routes have no `/api` prefix.
 */
import { apiReference } from '@scalar/express-api-reference';
import express from 'express';
import { findProduct, MAX_LINE_QUANTITY, PRODUCTS, SKUS, toProductDto } from './catalog.js';
import {
  addItem,
  checkout,
  createCart,
  deleteCart,
  getCart,
  recentOrders,
  redis,
  removeItem,
  setItemQuantity,
  startExpirySweep,
} from './carts.js';
import { logger } from './logger.js';
import { buildOpenApiDocument } from './openapi.js';
import { ProblemError, validationProblem, writeProblem } from './problems.js';
import { simulateShoppers } from './simulate.js';

const app = express();
const port = Number(process.env.PORT ?? 3000);
const simulationEnabled = process.env.ENABLE_SHOPPER_SIMULATION === 'true';
const openApiDocument = buildOpenApiDocument({ simulation: simulationEnabled });

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

app.disable('x-powered-by');
app.use(express.json({ limit: '10kb' }));

/** @param {import('express').Request} req */
function cartIdOf(req) {
  const { cartId } = req.params;
  if (!UUID.test(cartId)) throw validationProblem({ cartId: ['cartId must be a UUID returned by POST /carts.'] });
  return cartId;
}

/** @param {string} sku */
function productOf(sku) {
  const product = findProduct(sku);
  if (!product) throw new ProblemError(404, 'Product not found', `No product has the SKU '${sku}'. GET /products lists them.`);
  return product;
}

/**
 * Parses an optional whole number within bounds, recording a validation error otherwise.
 * @param {unknown} value
 * @param {{ name: string, min: number, max: number, fallback?: number }} options
 * @param {Record<string, string[]>} errors
 */
function wholeNumber(value, { name, min, max, fallback }, errors) {
  if (value === undefined && fallback !== undefined) return fallback;
  const number = typeof value === 'string' && value.trim() !== '' ? Number(value) : value;
  if (typeof number !== 'number' || !Number.isInteger(number) || number < min || number > max) {
    errors[name] = [`${name} must be a whole number from ${min} to ${max}.`];
    return min;
  }
  return number;
}

/** @param {Record<string, string[]>} errors */
function throwIfInvalid(errors) {
  if (Object.keys(errors).length) throw validationProblem(errors);
}

/**
 * Wraps a handler so expected problems (4xx) are written right away. They don't reach Express's
 * error handling, so the request-handler span isn't marked as failed for a client error.
 * @param {(req: import('express').Request, res: import('express').Response) => Promise<void> | void} handler
 * @returns {import('express').RequestHandler}
 */
const route = (handler) => async (req, res, next) => {
  try {
    await handler(req, res);
  } catch (err) {
    if (err instanceof ProblemError && err.status < 500) writeProblem(req, res, err);
    else next(err);
  }
};

app.get('/', (_req, res) => {
  res.redirect('/scalar');
});

app.get('/health', async (_req, res) => {
  try {
    await redis.ping();
    res.json({ status: 'healthy', redis: 'connected' });
  } catch {
    res.status(503).json({ status: 'unhealthy', redis: 'disconnected' });
  }
});

app.get('/products', (_req, res) => {
  res.json(PRODUCTS.map(toProductDto));
});

app.post('/carts', route(async (_req, res) => {
  const cart = await createCart();
  res.status(201).location(`/carts/${cart.id}`).json(cart);
}));

app.get('/carts/:cartId', route(async (req, res) => {
  res.json(await getCart(cartIdOf(req)));
}));

app.delete('/carts/:cartId', route(async (req, res) => {
  await deleteCart(cartIdOf(req));
  res.status(204).end();
}));

app.post('/carts/:cartId/items', route(async (req, res) => {
  const cartId = cartIdOf(req);
  const body = req.body ?? {};
  /** @type {Record<string, string[]>} */
  const errors = {};
  if (!SKUS.includes(body.sku)) errors.sku = [`sku must be one of: ${SKUS.join(', ')}.`];
  const quantity = wholeNumber(body.quantity, { name: 'quantity', min: 1, max: MAX_LINE_QUANTITY, fallback: 1 }, errors);
  throwIfInvalid(errors);
  res.json(await addItem(cartId, productOf(body.sku), quantity));
}));

app.put('/carts/:cartId/items/:sku', route(async (req, res) => {
  const cartId = cartIdOf(req);
  const product = productOf(req.params.sku);
  /** @type {Record<string, string[]>} */
  const errors = {};
  const quantity = wholeNumber(req.body?.quantity, { name: 'quantity', min: 0, max: MAX_LINE_QUANTITY }, errors);
  throwIfInvalid(errors);
  res.json(await setItemQuantity(cartId, product, quantity));
}));

app.delete('/carts/:cartId/items/:sku', route(async (req, res) => {
  res.json(await removeItem(cartIdOf(req), productOf(req.params.sku)));
}));

app.post('/carts/:cartId/checkout', route(async (req, res) => {
  const order = await checkout(cartIdOf(req));
  res.status(201).json(order);
}));

app.get('/orders/recent', route(async (req, res) => {
  /** @type {Record<string, string[]>} */
  const errors = {};
  const limit = wholeNumber(req.query.limit, { name: 'limit', min: 1, max: 20, fallback: 5 }, errors);
  throwIfInvalid(errors);
  res.json(await recentOrders(limit));
}));

if (simulationEnabled) {
  // Also exposed as the "Simulate shoppers" dashboard command (run mode only, see apphost.mts).
  app.post('/simulate/shoppers', route(async (req, res) => {
    /** @type {Record<string, string[]>} */
    const errors = {};
    const shoppers = wholeNumber(req.query.shoppers, { name: 'shoppers', min: 1, max: 25, fallback: 8 }, errors);
    throwIfInvalid(errors);
    res.json(await simulateShoppers(shoppers));
  }));
}

app.get('/openapi.json', (_req, res) => {
  res.json(openApiDocument);
});

app.use(
  '/scalar',
  apiReference({
    content: openApiDocument,
    theme: 'purple',
    _integration: 'express',
    metaData: { title: 'Contoso Coffee Cart API reference' },
  }),
);

app.use((req, res) => {
  writeProblem(req, res, new ProblemError(404, 'Not found', `${req.method} ${req.originalUrl} is not an API operation. See /scalar.`));
});

/** @type {import('express').ErrorRequestHandler} */
const errorHandler = (err, req, res, _next) => {
  if (err instanceof ProblemError) {
    writeProblem(req, res, err);
    return;
  }
  // Body parser errors (malformed JSON, oversized body) carry a 4xx status.
  if (typeof err?.status === 'number' && err.status >= 400 && err.status < 500) {
    const message = err.type === 'entity.parse.failed' ? 'The request body is not valid JSON.' : String(err.message);
    writeProblem(req, res, validationProblem({ body: [message] }));
    return;
  }
  if (!redis.isReady) {
    logger.error({ error: err?.message }, 'Cart store unavailable');
    writeProblem(req, res, new ProblemError(503, 'Cart store unavailable', 'Redis is not reachable. Try again shortly.'));
    return;
  }
  logger.error({ error: err?.message }, 'Unhandled error');
  writeProblem(req, res, new ProblemError(500, 'Unexpected error', 'The server failed to process the request.'));
};
app.use(errorHandler);

await redis.connect();
await startExpirySweep();

app.listen(port, () => {
  logger.info({ port, simulationEnabled }, `Contoso Coffee cart API listening on port ${port}`);
});
