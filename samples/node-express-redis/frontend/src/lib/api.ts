/**
 * Cart API client. Requests go to the same-origin `/api` path (YARP strips the prefix and
 * forwards to the API; Vite proxies it when browsing the dev server directly), so the browser's
 * fetch spans add a `traceparent` header without any CORS setup.
 */

export type Category = 'coffee' | 'brewing';

export interface Product {
  sku: string;
  name: string;
  category: Category;
  description: string;
  size: string;
  price: number;
}

export interface CartLine {
  sku: string;
  name: string;
  category: Category;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface Cart {
  id: string;
  lines: CartLine[];
  itemCount: number;
  subtotal: number;
  currency: string;
  orderLimit: number;
  createdAt: string;
  expiresAt: string;
  ttlSeconds: number;
}

export interface Order {
  orderId: string;
  cartId: string;
  placedAt: string;
  lines: CartLine[];
  itemCount: number;
  subtotal: number;
  currency: string;
}

/** An RFC 9457 problem details response from the API. */
export class ApiError extends Error {
  readonly status: number;
  readonly title: string;

  constructor(status: number, title: string, detail: string) {
    super(detail);
    this.status = status;
    this.title = title;
  }
}

/** Starts the request synchronously, so it joins the caller's active span. */
async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init);
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null;
    throw new ApiError(
      response.status,
      problem?.title ?? 'Request failed',
      problem?.detail ?? `The cart service returned HTTP ${response.status}.`,
    );
  }
  return (response.status === 204 ? undefined : await response.json()) as T;
}

const send = (method: string, body?: unknown): RequestInit => ({
  method,
  ...(body !== undefined && { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }),
});

const cartUrl = (cartId: string) => `/api/carts/${encodeURIComponent(cartId)}`;

export const api = {
  products: () => request<Product[]>('/api/products'),
  recentOrders: (limit = 5) => request<Order[]>(`/api/orders/recent?limit=${limit}`),
  createCart: () => request<Cart>('/api/carts', send('POST')),
  getCart: (cartId: string) => request<Cart>(cartUrl(cartId)),
  addItem: (cartId: string, sku: string, quantity = 1) =>
    request<Cart>(`${cartUrl(cartId)}/items`, send('POST', { sku, quantity })),
  setQuantity: (cartId: string, sku: string, quantity: number) =>
    request<Cart>(`${cartUrl(cartId)}/items/${encodeURIComponent(sku)}`, send('PUT', { quantity })),
  removeItem: (cartId: string, sku: string) =>
    request<Cart>(`${cartUrl(cartId)}/items/${encodeURIComponent(sku)}`, send('DELETE')),
  checkout: (cartId: string) => request<Order>(`${cartUrl(cartId)}/checkout`, send('POST')),
};
