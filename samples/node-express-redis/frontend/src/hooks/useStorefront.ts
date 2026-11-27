/**
 * Storefront state: the catalog, the shopper's Redis-backed cart, recent orders and notices.
 * Every action that calls the API runs inside a UI span (see lib/tracing.ts), so one click shows
 * up in the dashboard as one browser → YARP → API → Redis trace.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError, type Cart, type Order, type Product } from '../lib/api';
import { traced, type Step } from '../lib/tracing';

const CART_ID_KEY = 'contoso-coffee.cartId';

export interface Notice {
  tone: 'info' | 'warning' | 'error';
  title: string;
  detail: string;
}

const expiredNotice: Notice = {
  tone: 'info',
  title: 'Your previous cart expired',
  detail: 'Carts stay in Redis for 30 minutes after your last change. Add something to start a new one.',
};

/** The cart expired (or was checked out elsewhere): Redis no longer has it. */
const isCartGone = (err: unknown) => err instanceof ApiError && err.status === 404 && err.title === 'Cart not found';

const toNotice = (err: unknown): Notice =>
  err instanceof ApiError
    ? { tone: err.status === 409 ? 'warning' : 'error', title: err.title, detail: err.message }
    : { tone: 'error', title: 'Something went wrong', detail: err instanceof Error ? err.message : String(err) };

const cartAttributes = (cart: Cart) => ({
  'cart.id': cart.id,
  'cart.item_count': cart.itemCount,
  'cart.subtotal': cart.subtotal,
});

export function useStorefront() {
  const [products, setProducts] = useState<Product[]>([]);
  const [cart, setCart] = useState<Cart | null>(null);
  const [recentOrders, setRecentOrders] = useState<Order[]>([]);
  const [lastOrder, setLastOrder] = useState<Order | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const cartRef = useRef<Cart | null>(null);

  const updateCart = useCallback((next: Cart | null) => {
    cartRef.current = next;
    setCart(next);
    if (next) localStorage.setItem(CART_ID_KEY, next.id);
    else localStorage.removeItem(CART_ID_KEY);
  }, []);

  // Load the catalog, recent orders and the saved cart once (the ref guards StrictMode's double effect).
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    const savedId = localStorage.getItem(CART_ID_KEY);
    traced('load storefront', { 'cart.saved': Boolean(savedId) }, async (span) => {
      const [catalog, orders, saved] = await Promise.all([
        api.products(),
        api.recentOrders(),
        savedId ? api.getCart(savedId).catch((err) => (isCartGone(err) ? null : Promise.reject(err))) : null,
      ]);
      setProducts(catalog);
      setRecentOrders(orders);
      updateCart(saved);
      if (savedId && !saved) {
        span.addEvent('cart.expired', { 'cart.id': savedId });
        setNotice(expiredNotice);
      }
    })
      .catch((err) => setNotice(toNotice(err)))
      .finally(() => setLoaded(true));
  }, [updateCart]);

  /** Runs a cart action in a UI span and shows API problems as a notice. */
  const run = useCallback(
    (key: string, name: string, attributes: Record<string, string | number>, action: (step: Step) => Promise<void>) => {
      setBusy(key);
      setNotice(null);
      return traced(name, attributes, (_span, step) => action(step))
        .catch((err) => {
          if (isCartGone(err) && cartRef.current) {
            updateCart(null);
            setNotice(expiredNotice);
          } else {
            setNotice(toNotice(err));
          }
        })
        .finally(() => setBusy(null));
    },
    [updateCart],
  );

  const addToCart = useCallback(
    (product: Product) =>
      run(`add:${product.sku}`, 'add to cart', { 'product.sku': product.sku, 'product.category': product.category }, async (step) => {
        setLastOrder(null);
        let cartId = cartRef.current?.id;
        if (!cartId) cartId = (await step(() => api.createCart())).id;
        const id = cartId;
        try {
          updateCart(await step(() => api.addItem(id, product.sku, 1)));
        } catch (err) {
          if (!isCartGone(err)) throw err;
          // The saved cart expired in Redis since the page loaded: start a new one.
          const fresh = await step(() => api.createCart());
          updateCart(await step(() => api.addItem(fresh.id, product.sku, 1)));
          setNotice(expiredNotice);
        }
      }),
    [run, updateCart],
  );

  const setQuantity = useCallback(
    (sku: string, quantity: number) => {
      const current = cartRef.current;
      if (!current) return Promise.resolve();
      return run(`qty:${sku}`, 'change quantity', { 'product.sku': sku, 'cart.item.quantity': quantity }, async (step) => {
        updateCart(await step(() => api.setQuantity(current.id, sku, quantity)));
      });
    },
    [run, updateCart],
  );

  const removeLine = useCallback(
    (sku: string) => {
      const current = cartRef.current;
      if (!current) return Promise.resolve();
      return run(`remove:${sku}`, 'remove item', { 'product.sku': sku }, async (step) => {
        updateCart(await step(() => api.removeItem(current.id, sku)));
      });
    },
    [run, updateCart],
  );

  const checkout = useCallback(() => {
    const current = cartRef.current;
    if (!current) return Promise.resolve();
    return run('checkout', 'checkout', cartAttributes(current), async (step) => {
      const order = await step(() => api.checkout(current.id));
      updateCart(null);
      setLastOrder(order);
      setRecentOrders((orders) => [order, ...orders].slice(0, 5));
    });
  }, [run, updateCart]);

  /** Called by the countdown when the cart's sliding expiry passes without activity. */
  const markExpired = useCallback(() => {
    if (!cartRef.current) return;
    updateCart(null);
    setNotice(expiredNotice);
  }, [updateCart]);

  const dismissNotice = useCallback(() => setNotice(null), []);
  const dismissOrder = useCallback(() => setLastOrder(null), []);

  return {
    products,
    cart,
    recentOrders,
    lastOrder,
    notice,
    busy,
    loaded,
    addToCart,
    setQuantity,
    removeLine,
    checkout,
    markExpired,
    dismissNotice,
    dismissOrder,
  };
}
