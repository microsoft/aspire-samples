/**
 * OpenAPI 3.1 description of the cart API, served raw at `/openapi.json` and rendered by Scalar
 * at `/scalar`. Keep it in sync with the handlers in index.js and the shapes built in carts.js.
 */
import { MAX_LINE_QUANTITY, PRODUCTS, SKUS, toProductDto } from './catalog.js';

const ref = (name) => ({ $ref: `#/components/schemas/${name}` });
const json = (schema, example) => ({ 'application/json': { schema, ...(example !== undefined && { example }) } });
const problemJson = (examples) => ({
  'application/problem+json': {
    schema: ref('ProblemDetails'),
    ...(Array.isArray(examples)
      ? { examples: Object.fromEntries(examples.map(({ name, summary, value }) => [name, { summary, value }])) }
      : { example: examples }),
  },
});

const traceId = '0af7651916cd43dd8448eb211c80319c';
const cartId = '3f2b8c1e-9d4a-4e7b-a6c5-1f0e2d3c4b5a';
const problemType = {
  400: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
  404: 'https://tools.ietf.org/html/rfc9110#section-15.5.5',
  409: 'https://tools.ietf.org/html/rfc9110#section-15.5.10',
  503: 'https://tools.ietf.org/html/rfc9110#section-15.6.4',
};

const line = (sku, quantity) => {
  const product = PRODUCTS.find((p) => p.sku === sku);
  return {
    sku,
    name: product.name,
    category: product.category,
    unitPrice: product.priceCents / 100,
    quantity,
    lineTotal: (product.priceCents * quantity) / 100,
  };
};

const exampleLines = [
  line('founders-espresso', 3),
  line('yirgacheffe', 2),
  line('huila-decaf', 2),
  line('pour-over-dripper', 1),
  line('burr-grinder', 1),
];

const exampleCart = {
  id: cartId,
  lines: exampleLines,
  itemCount: 9,
  subtotal: 309,
  currency: 'USD',
  orderLimit: 500,
  createdAt: '2026-11-27T15:02:11.418Z',
  expiresAt: '2026-11-27T15:38:47.902Z',
  ttlSeconds: 1800,
};

const exampleOrder = {
  orderId: 'CC-7QK4MZ',
  cartId,
  placedAt: '2026-11-27T15:09:02.377Z',
  lines: exampleLines,
  itemCount: 9,
  subtotal: 309,
  currency: 'USD',
};

const cartNotFoundExample = {
  type: problemType[404],
  title: 'Cart not found',
  status: 404,
  detail: `Cart '${cartId}' doesn't exist or expired after 30 minutes without activity. Create a new cart with POST /carts.`,
  instance: `/carts/${cartId}`,
  traceId,
};

/** @param {{ simulation: boolean }} options */
export function buildOpenApiDocument({ simulation }) {
  const document = {
    openapi: '3.1.0',
    info: {
      title: 'Contoso Coffee Cart API',
      version: '1.0.0',
      summary: 'Redis-backed shopping carts with a sliding expiry.',
      description: [
        'Shopping carts for the Contoso Coffee storefront. Create a cart, add products from the catalog, change quantities and check out.',
        '',
        '**Carts expire.** Each cart is a Redis hash with a 30-minute *sliding* expiry: every read or write renews it, so carts that shoppers walk away from clean themselves up. A request for an expired cart returns `404 Cart not found`.',
        '',
        '| Redis key | Type | Holds |',
        '| --- | --- | --- |',
        '| `contoso-coffee:cart:{cartId}` | hash (TTL 30 min) | `createdAt`, the `traceparent` of the request that created the cart, and `item:{sku}` → quantity |',
        '| `contoso-coffee:carts:expiry` | sorted set | cart ids scored by expiry time, used to count abandoned carts |',
        '| `contoso-coffee:orders:recent` | list (capped at 20) | the latest orders |',
        '',
        `**Limits.** A cart line holds at most ${MAX_LINE_QUANTITY} units, and orders over $500.00 are rejected at checkout with \`409 Order limit exceeded\`.`,
        '',
        'Errors use [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with a `traceId` you can paste into the Aspire dashboard.',
      ].join('\n'),
      license: { name: 'MIT', identifier: 'MIT' },
    },
    servers: [{ url: '/', description: 'This API instance (the storefront reaches it through YARP at /api)' }],
    tags: [
      { name: 'Catalog', description: 'The products Contoso Coffee sells.' },
      { name: 'Carts', description: 'Redis-backed carts with a 30-minute sliding expiry.' },
      { name: 'Checkout', description: 'Turn a cart into an order.' },
      ...(simulation
        ? [{ name: 'Simulation', description: 'Development-only traffic generator (enabled in run mode by the AppHost).' }]
        : []),
      { name: 'System', description: 'Operational endpoints.' },
    ],
    paths: {
      '/products': {
        get: {
          tags: ['Catalog'],
          operationId: 'listProducts',
          summary: 'List products',
          description: 'Returns the coffee and brewing gear in the catalog with prices in US dollars.',
          responses: {
            200: {
              description: 'The catalog.',
              content: json({ type: 'array', items: ref('Product') }, PRODUCTS.slice(0, 2).map(toProductDto)),
            },
          },
        },
      },
      '/carts': {
        post: {
          tags: ['Carts'],
          operationId: 'createCart',
          summary: 'Create a cart',
          description: 'Creates an empty cart that expires after 30 minutes without activity. Keep the returned `id` for the other cart operations.',
          responses: {
            201: {
              description: 'The new, empty cart.',
              headers: { Location: { description: 'The cart URL.', schema: { type: 'string' }, example: `/carts/${cartId}` } },
              content: json(ref('Cart'), { ...exampleCart, lines: [], itemCount: 0, subtotal: 0 }),
            },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      '/carts/{cartId}': {
        parameters: [{ $ref: '#/components/parameters/cartId' }],
        get: {
          tags: ['Carts'],
          operationId: 'getCart',
          summary: 'Get a cart',
          description: 'Returns the cart with its lines, item count and subtotal, and renews its 30-minute expiry.',
          responses: {
            200: { description: 'The cart.', content: json(ref('Cart'), exampleCart) },
            400: { $ref: '#/components/responses/InvalidCartId' },
            404: { $ref: '#/components/responses/CartNotFound' },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
        delete: {
          tags: ['Carts'],
          operationId: 'deleteCart',
          summary: 'Delete a cart',
          description: 'Empties and deletes the cart right away instead of waiting for it to expire.',
          responses: {
            204: { description: 'The cart was deleted.' },
            400: { $ref: '#/components/responses/InvalidCartId' },
            404: { $ref: '#/components/responses/CartNotFound' },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      '/carts/{cartId}/items': {
        parameters: [{ $ref: '#/components/parameters/cartId' }],
        post: {
          tags: ['Carts'],
          operationId: 'addCartItem',
          summary: 'Add a product to a cart',
          description: `Adds units of a product, merging them into its existing line. A line holds at most ${MAX_LINE_QUANTITY} units. Renews the cart's expiry.`,
          requestBody: {
            required: true,
            content: json(ref('AddItemRequest'), { sku: 'founders-espresso', quantity: 2 }),
          },
          responses: {
            200: { description: 'The updated cart.', content: json(ref('Cart'), exampleCart) },
            400: {
              description: 'The cart id, SKU or quantity is invalid, or the body is not valid JSON.',
              content: problemJson({
                type: problemType[400],
                title: 'One or more validation errors occurred.',
                status: 400,
                detail: 'See errors for the invalid fields.',
                instance: `/carts/${cartId}/items`,
                traceId,
                errors: { quantity: [`quantity must be a whole number from 1 to ${MAX_LINE_QUANTITY}.`] },
              }),
            },
            404: { $ref: '#/components/responses/CartNotFound' },
            409: {
              description: `The line would hold more than ${MAX_LINE_QUANTITY} units.`,
              content: problemJson({
                type: problemType[409],
                title: 'Line quantity limit reached',
                status: 409,
                detail: `A cart can hold at most ${MAX_LINE_QUANTITY} of 'Founders Espresso Blend', and this cart already has 9.`,
                instance: `/carts/${cartId}/items`,
                traceId,
                sku: 'founders-espresso',
                quantity: 9,
                maxQuantity: MAX_LINE_QUANTITY,
              }),
            },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      '/carts/{cartId}/items/{sku}': {
        parameters: [{ $ref: '#/components/parameters/cartId' }, { $ref: '#/components/parameters/sku' }],
        put: {
          tags: ['Carts'],
          operationId: 'setCartItemQuantity',
          summary: 'Set a line quantity',
          description: `Sets the quantity of a product in the cart (0 to ${MAX_LINE_QUANTITY}). A quantity of 0 removes the line. Renews the cart's expiry.`,
          requestBody: { required: true, content: json(ref('SetQuantityRequest'), { quantity: 3 }) },
          responses: {
            200: { description: 'The updated cart.', content: json(ref('Cart'), exampleCart) },
            400: { $ref: '#/components/responses/ValidationProblem' },
            404: { $ref: '#/components/responses/CartOrProductNotFound' },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
        delete: {
          tags: ['Carts'],
          operationId: 'removeCartItem',
          summary: 'Remove a line',
          description: "Removes a product's line from the cart. Renews the cart's expiry.",
          responses: {
            200: { description: 'The updated cart.', content: json(ref('Cart'), { ...exampleCart, lines: exampleLines.slice(0, 4), itemCount: 8, subtotal: 160 }) },
            400: { $ref: '#/components/responses/InvalidCartId' },
            404: {
              description: "The cart doesn't exist or expired, the SKU is unknown, or the cart has no line for it.",
              content: problemJson({
                type: problemType[404],
                title: 'Item not in cart',
                status: 404,
                detail: `Cart '${cartId}' has no 'Cold Brew Starter Kit' line.`,
                instance: `/carts/${cartId}/items/cold-brew-kit`,
                traceId,
                sku: 'cold-brew-kit',
              }),
            },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      '/carts/{cartId}/checkout': {
        parameters: [{ $ref: '#/components/parameters/cartId' }],
        post: {
          tags: ['Checkout'],
          operationId: 'checkoutCart',
          summary: 'Check out a cart',
          description: [
            'Turns the cart into an order in one Redis transaction: the cart and its expiry entry are deleted and the order is pushed onto the recent-orders list.',
            '',
            "The checkout span links back to the trace that created the cart (its `traceparent` is stored in the cart's hash), so the dashboard can jump from the order to the cart's first request.",
          ].join('\n'),
          responses: {
            201: { description: 'The order was placed.', content: json(ref('Order'), exampleOrder) },
            400: { $ref: '#/components/responses/InvalidCartId' },
            404: { $ref: '#/components/responses/CartNotFound' },
            409: {
              description: 'The cart is empty, or its subtotal is over the $500.00 order limit.',
              content: problemJson([
                {
                  name: 'orderLimitExceeded',
                  summary: 'Over the order limit',
                  value: {
                    type: problemType[409],
                    title: 'Order limit exceeded',
                    status: 409,
                    detail: 'Orders are limited to $500.00 and this cart comes to $613.25. Remove at least $113.25 of items to check out.',
                    instance: `/carts/${cartId}/checkout`,
                    traceId,
                    subtotal: 613.25,
                    orderLimit: 500,
                  },
                },
                {
                  name: 'emptyCart',
                  summary: 'Empty cart',
                  value: {
                    type: problemType[409],
                    title: 'Cart is empty',
                    status: 409,
                    detail: `Cart '${cartId}' has no items to check out.`,
                    instance: `/carts/${cartId}/checkout`,
                    traceId,
                  },
                },
              ]),
            },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      '/orders/recent': {
        get: {
          tags: ['Checkout'],
          operationId: 'listRecentOrders',
          summary: 'List recent orders',
          description: 'Returns the latest orders, newest first (up to 20 are kept).',
          parameters: [
            {
              name: 'limit',
              in: 'query',
              required: false,
              description: 'How many orders to return.',
              schema: { type: 'integer', minimum: 1, maximum: 20, default: 5 },
              example: 5,
            },
          ],
          responses: {
            200: { description: 'The latest orders.', content: json({ type: 'array', items: ref('Order') }, [exampleOrder]) },
            400: { $ref: '#/components/responses/ValidationProblem' },
            503: { $ref: '#/components/responses/StoreUnavailable' },
          },
        },
      },
      ...(simulation && {
        '/simulate/shoppers': {
          post: {
            tags: ['Simulation'],
            operationId: 'simulateShoppers',
            summary: 'Simulate shoppers',
            description: [
              'Runs a burst of concurrent shoppers through the same cart operations as the storefront: most fill a cart and check out, one in eight abandons the cart, and one in eight goes over the order limit, drops a grinder and checks out on the second try.',
              '',
              'Abandoned simulated carts expire after 45 seconds instead of 30 minutes, so `cart.carts.expired` moves quickly. The Aspire dashboard runs this as the **Simulate shoppers** command on the `api` resource. Only available when `ENABLE_SHOPPER_SIMULATION=true` (run mode).',
            ].join('\n'),
            parameters: [
              {
                name: 'shoppers',
                in: 'query',
                required: false,
                description: 'How many shoppers to simulate.',
                schema: { type: 'integer', minimum: 1, maximum: 25, default: 8 },
                example: 8,
              },
            ],
            responses: {
              200: {
                description: 'What the shoppers did.',
                content: json(ref('SimulationSummary'), {
                  shoppers: 8,
                  ordersPlaced: 7,
                  abandonedCarts: 1,
                  orderLimitRejections: 1,
                  revenue: 1186.75,
                  abandonedCartTtlSeconds: 45,
                  orders: [
                    { orderId: 'CC-7QK4MZ', itemCount: 9, subtotal: 309 },
                    { orderId: 'CC-W2HN8R', itemCount: 4, subtotal: 77.25 },
                  ],
                }),
              },
              400: { $ref: '#/components/responses/ValidationProblem' },
            },
          },
        },
      }),
      '/health': {
        get: {
          tags: ['System'],
          operationId: 'health',
          summary: 'Health probe',
          description: 'Pings Redis. The AppHost uses it as the resource health check.',
          responses: {
            200: { description: 'Redis is reachable.', content: json(ref('Health'), { status: 'healthy', redis: 'connected' }) },
            503: { description: 'Redis is not reachable.', content: json(ref('Health'), { status: 'unhealthy', redis: 'disconnected' }) },
          },
        },
      },
    },
    components: {
      parameters: {
        cartId: {
          name: 'cartId',
          in: 'path',
          required: true,
          description: 'Cart id returned by `POST /carts`.',
          schema: { type: 'string', format: 'uuid' },
          example: cartId,
        },
        sku: {
          name: 'sku',
          in: 'path',
          required: true,
          description: 'Product SKU from `GET /products`.',
          schema: { type: 'string', enum: SKUS },
          example: 'yirgacheffe',
        },
      },
      responses: {
        ValidationProblem: {
          description: 'A parameter or the request body is invalid.',
          content: problemJson({
            type: problemType[400],
            title: 'One or more validation errors occurred.',
            status: 400,
            detail: 'See errors for the invalid fields.',
            instance: `/carts/${cartId}/items/yirgacheffe`,
            traceId,
            errors: { quantity: [`quantity must be a whole number from 0 to ${MAX_LINE_QUANTITY}.`] },
          }),
        },
        InvalidCartId: {
          description: 'The cart id is not a UUID.',
          content: problemJson({
            type: problemType[400],
            title: 'One or more validation errors occurred.',
            status: 400,
            detail: 'See errors for the invalid fields.',
            instance: '/carts/my-cart',
            traceId,
            errors: { cartId: ['cartId must be a UUID returned by POST /carts.'] },
          }),
        },
        CartNotFound: {
          description: "The cart doesn't exist or expired.",
          content: problemJson(cartNotFoundExample),
        },
        CartOrProductNotFound: {
          description: "The cart doesn't exist or expired, or the SKU is unknown.",
          content: problemJson([
            { name: 'cartNotFound', summary: 'Cart expired', value: cartNotFoundExample },
            {
              name: 'productNotFound',
              summary: 'Unknown SKU',
              value: {
                type: problemType[404],
                title: 'Product not found',
                status: 404,
                detail: "No product has the SKU 'french-press'. GET /products lists them.",
                instance: `/carts/${cartId}/items/french-press`,
                traceId,
              },
            },
          ]),
        },
        StoreUnavailable: {
          description: 'Redis is not reachable.',
          content: problemJson({
            type: problemType[503],
            title: 'Cart store unavailable',
            status: 503,
            detail: 'Redis is not reachable. Try again shortly.',
            instance: `/carts/${cartId}`,
            traceId,
          }),
        },
      },
      schemas: {
        Product: {
          type: 'object',
          properties: {
            sku: { type: 'string', enum: SKUS },
            name: { type: 'string', examples: ['Founders Espresso Blend'] },
            category: { type: 'string', enum: ['coffee', 'brewing'] },
            description: { type: 'string' },
            size: { type: 'string', examples: ['1 lb whole bean'] },
            price: { type: 'number', description: 'Unit price in US dollars.', examples: [18.5] },
          },
          required: ['sku', 'name', 'category', 'description', 'size', 'price'],
        },
        CartLine: {
          type: 'object',
          properties: {
            sku: { type: 'string', enum: SKUS },
            name: { type: 'string' },
            category: { type: 'string', enum: ['coffee', 'brewing'] },
            unitPrice: { type: 'number', examples: [18.5] },
            quantity: { type: 'integer', minimum: 1, maximum: MAX_LINE_QUANTITY },
            lineTotal: { type: 'number', examples: [55.5] },
          },
          required: ['sku', 'name', 'category', 'unitPrice', 'quantity', 'lineTotal'],
        },
        Cart: {
          type: 'object',
          properties: {
            id: { type: 'string', format: 'uuid' },
            lines: { type: 'array', items: ref('CartLine'), description: 'One line per product, in catalog order.' },
            itemCount: { type: 'integer', minimum: 0, description: 'Total units across all lines.' },
            subtotal: { type: 'number', minimum: 0, description: 'Sum of the line totals in US dollars.' },
            currency: { type: 'string', const: 'USD' },
            orderLimit: { type: 'number', description: 'The largest subtotal that can be checked out.', examples: [500] },
            createdAt: { type: 'string', format: 'date-time' },
            expiresAt: {
              type: 'string',
              format: 'date-time',
              description: 'When the cart expires unless it is read or changed again.',
            },
            ttlSeconds: { type: 'integer', description: 'The sliding expiry in seconds.', examples: [1800] },
          },
          required: ['id', 'lines', 'itemCount', 'subtotal', 'currency', 'orderLimit', 'createdAt', 'expiresAt', 'ttlSeconds'],
        },
        AddItemRequest: {
          type: 'object',
          properties: {
            sku: { type: 'string', enum: SKUS, description: 'Product SKU from `GET /products`.' },
            quantity: { type: 'integer', minimum: 1, maximum: MAX_LINE_QUANTITY, default: 1 },
          },
          required: ['sku'],
        },
        SetQuantityRequest: {
          type: 'object',
          properties: {
            quantity: { type: 'integer', minimum: 0, maximum: MAX_LINE_QUANTITY, description: '0 removes the line.' },
          },
          required: ['quantity'],
        },
        Order: {
          type: 'object',
          properties: {
            orderId: { type: 'string', pattern: '^CC-[A-Z2-9]{6}$', examples: ['CC-7QK4MZ'] },
            cartId: { type: 'string', format: 'uuid' },
            placedAt: { type: 'string', format: 'date-time' },
            lines: { type: 'array', items: ref('CartLine') },
            itemCount: { type: 'integer', minimum: 1 },
            subtotal: { type: 'number', maximum: 500 },
            currency: { type: 'string', const: 'USD' },
          },
          required: ['orderId', 'cartId', 'placedAt', 'lines', 'itemCount', 'subtotal', 'currency'],
        },
        ...(simulation && {
          SimulationSummary: {
            type: 'object',
            properties: {
              shoppers: { type: 'integer' },
              ordersPlaced: { type: 'integer' },
              abandonedCarts: { type: 'integer' },
              orderLimitRejections: { type: 'integer', description: 'Checkouts rejected with 409 before the shopper retried.' },
              revenue: { type: 'number', description: 'Total of the placed orders in US dollars.' },
              abandonedCartTtlSeconds: { type: 'integer', examples: [45] },
              orders: {
                type: 'array',
                items: {
                  type: 'object',
                  properties: { orderId: { type: 'string' }, itemCount: { type: 'integer' }, subtotal: { type: 'number' } },
                  required: ['orderId', 'itemCount', 'subtotal'],
                },
              },
            },
            required: ['shoppers', 'ordersPlaced', 'abandonedCarts', 'orderLimitRejections', 'revenue', 'abandonedCartTtlSeconds', 'orders'],
          },
        }),
        Health: {
          type: 'object',
          properties: {
            status: { type: 'string', enum: ['healthy', 'unhealthy'] },
            redis: { type: 'string', enum: ['connected', 'disconnected'] },
          },
          required: ['status', 'redis'],
        },
        ProblemDetails: {
          type: 'object',
          description: 'RFC 9457 problem details.',
          properties: {
            type: { type: 'string', format: 'uri' },
            title: { type: 'string' },
            status: { type: 'integer' },
            detail: { type: 'string' },
            instance: { type: 'string' },
            traceId: { type: 'string', description: 'W3C trace id of the failed request; search for it in the Aspire dashboard.' },
            errors: {
              type: 'object',
              description: 'Validation messages keyed by field (400 only).',
              additionalProperties: { type: 'array', items: { type: 'string' } },
            },
          },
          required: ['type', 'title', 'status'],
          additionalProperties: true,
        },
      },
    },
  };
  return document;
}
