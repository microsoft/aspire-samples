/**
 * The Contoso Coffee catalog. It's small and fixed, so it lives in code; only carts and orders
 * are stored in Redis. Prices are in cents to keep the arithmetic exact.
 */

/** @typedef {'coffee' | 'brewing'} Category */
/** @typedef {{ sku: string, name: string, category: Category, description: string, size: string, priceCents: number }} Product */

/** @type {readonly Product[]} */
export const PRODUCTS = Object.freeze([
  {
    sku: 'founders-espresso',
    name: 'Founders Espresso Blend',
    category: 'coffee',
    description: 'Dark chocolate, molasses and orange zest. Our house espresso since 2009.',
    size: '1 lb whole bean',
    priceCents: 1850,
  },
  {
    sku: 'yirgacheffe',
    name: 'Ethiopia Yirgacheffe',
    category: 'coffee',
    description: 'Washed single origin with jasmine, bergamot and lemon curd notes.',
    size: '12 oz whole bean',
    priceCents: 2100,
  },
  {
    sku: 'huila-decaf',
    name: 'Huila Swiss Water Decaf',
    category: 'coffee',
    description: 'Caramel, red apple and cocoa, decaffeinated without solvents.',
    size: '12 oz whole bean',
    priceCents: 1725,
  },
  {
    sku: 'pour-over-dripper',
    name: 'Ceramic Pour-Over Dripper',
    category: 'brewing',
    description: 'Glazed stoneware cone with spiral ribs for an even, clean extraction.',
    size: 'Fits 1-2 cups',
    priceCents: 2800,
  },
  {
    sku: 'cold-brew-kit',
    name: 'Cold Brew Starter Kit',
    category: 'brewing',
    description: '1.5 L glass carafe, stainless filter and a bag of coarse-ground blend.',
    size: '1.5 L carafe',
    priceCents: 3400,
  },
  {
    sku: 'burr-grinder',
    name: 'Conical Burr Grinder',
    category: 'brewing',
    description: '40 mm steel burrs with 30 grind settings, from espresso to French press.',
    size: 'Electric, 30 settings',
    priceCents: 14900,
  },
]);

const bySku = new Map(PRODUCTS.map((product) => [product.sku, product]));

/** @param {string} sku */
export const findProduct = (sku) => bySku.get(sku);

export const SKUS = PRODUCTS.map((product) => product.sku);

/** Orders above this subtotal are rejected at checkout (409). */
export const ORDER_LIMIT_CENTS = 50_000;

/** The most units of one product a cart line can hold. */
export const MAX_LINE_QUANTITY = 10;

/** @param {number} cents */
export const toDollars = (cents) => Math.round(cents) / 100;

/** @param {Product} product */
export const toProductDto = ({ priceCents, ...product }) => ({ ...product, price: toDollars(priceCents) });
