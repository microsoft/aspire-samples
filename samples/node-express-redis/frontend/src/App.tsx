import { useEffect, useState } from 'react';
import type { ComponentType } from 'react';
import {
  IconAlertTriangle,
  IconCircleCheck,
  IconFilter,
  IconFlame,
  IconHourglassHigh,
  IconInfoCircle,
  IconLeaf,
  IconMinus,
  IconMoon,
  IconPlus,
  IconReceipt,
  IconSettings,
  IconShoppingCart,
  IconSnowflake,
  IconTrash,
  IconX,
} from '@tabler/icons-react';
import { useStorefront, type Notice } from './hooks/useStorefront';
import type { Cart, CartLine, Order, Product } from './lib/api';
import { formatAgo, formatCountdown, formatUsd, plural } from './lib/format';
import styles from './App.module.css';

type IconType = ComponentType<{ size?: number; stroke?: number }>;

const PRODUCT_LOOK: Record<string, { Icon: IconType; color: string; note: string }> = {
  'founders-espresso': { Icon: IconFlame, color: '--c-orange', note: 'Dark roast' },
  yirgacheffe: { Icon: IconLeaf, color: '--c-yellow', note: 'Light roast' },
  'huila-decaf': { Icon: IconMoon, color: '--c-violet', note: 'Decaf' },
  'pour-over-dripper': { Icon: IconFilter, color: '--c-sky', note: 'Brewing' },
  'cold-brew-kit': { Icon: IconSnowflake, color: '--c-green', note: 'Brewing' },
  'burr-grinder': { Icon: IconSettings, color: '--c-pink', note: 'Equipment' },
};

const MAX_LINE_QUANTITY = 10;

const look = (sku: string) => PRODUCT_LOOK[sku] ?? { Icon: IconShoppingCart, color: '--c-sky', note: '' };

function useNow(intervalMs: number) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}

function App() {
  const store = useStorefront();
  const now = useNow(1000);
  const quantities = new Map(store.cart?.lines.map((line) => [line.sku, line.quantity]));

  return (
    <>
      <a className="skip-link" href="#products">
        Skip to products
      </a>

      <div className={styles.page}>
        <header className={styles.header}>
          <div>
            <p className={styles.kicker}>Aspire · Redis-backed cart</p>
            <h1 className={styles.title}>
              Contoso <span className={styles.titleAccent}>Coffee</span>
            </h1>
          </div>
          <p className={styles.subtitle}>
            Small-batch beans and brewing gear. Your cart lives in Redis and is held for 30 minutes after your last
            change.
          </p>
        </header>

        <div className={styles.layout}>
          <main id="products" className={styles.main}>
            <h2 className={styles.sectionTitle}>Shop</h2>
            <ul className={styles.grid} aria-busy={!store.loaded}>
              {store.products.map((product) => (
                <ProductCard
                  key={product.sku}
                  product={product}
                  inCart={quantities.get(product.sku) ?? 0}
                  busy={store.busy === `add:${product.sku}`}
                  disabled={store.busy !== null}
                  onAdd={() => store.addToCart(product)}
                />
              ))}
            </ul>
          </main>

          <aside className={styles.cartColumn} aria-label="Your cart">
            <CartPanel
              cart={store.cart}
              lastOrder={store.lastOrder}
              notice={store.notice}
              busy={store.busy}
              now={now}
              onQuantity={store.setQuantity}
              onRemove={store.removeLine}
              onCheckout={store.checkout}
              onExpired={store.markExpired}
              onDismissNotice={store.dismissNotice}
              onDismissOrder={store.dismissOrder}
            />
          </aside>

          <RecentOrders orders={store.recentOrders} now={now} />
        </div>
      </div>
    </>
  );
}

function ProductCard({
  product,
  inCart,
  busy,
  disabled,
  onAdd,
}: {
  product: Product;
  inCart: number;
  busy: boolean;
  disabled: boolean;
  onAdd: () => void;
}) {
  const { Icon, color, note } = look(product.sku);
  const full = inCart >= MAX_LINE_QUANTITY;
  return (
    <li className={styles.card} style={{ ['--card' as string]: `var(${color})` }}>
      <div className={styles.cardTop}>
        <span className={styles.cardIcon} aria-hidden="true">
          <Icon size={26} stroke={2.2} />
        </span>
        <span className={styles.cardNote}>{note}</span>
        {inCart > 0 && <span className={styles.inCart}>{inCart} in cart</span>}
      </div>
      <h3 className={styles.cardName}>{product.name}</h3>
      <p className={styles.cardSize}>{product.size}</p>
      <p className={styles.cardDescription}>{product.description}</p>
      <div className={styles.cardFoot}>
        <span className={styles.price}>{formatUsd(product.price)}</span>
        <button
          type="button"
          className={styles.addButton}
          onClick={onAdd}
          disabled={disabled || full}
          aria-label={`Add ${product.name} to cart`}
        >
          <IconPlus size={18} stroke={2.6} aria-hidden="true" />
          {busy ? 'Adding…' : full ? 'Max 10' : 'Add'}
        </button>
      </div>
    </li>
  );
}

function CartPanel({
  cart,
  lastOrder,
  notice,
  busy,
  now,
  onQuantity,
  onRemove,
  onCheckout,
  onExpired,
  onDismissNotice,
  onDismissOrder,
}: {
  cart: Cart | null;
  lastOrder: Order | null;
  notice: Notice | null;
  busy: string | null;
  now: number;
  onQuantity: (sku: string, quantity: number) => void;
  onRemove: (sku: string) => void;
  onCheckout: () => void;
  onExpired: () => void;
  onDismissNotice: () => void;
  onDismissOrder: () => void;
}) {
  const remaining = cart ? Date.parse(cart.expiresAt) - now : 0;
  const expired = cart !== null && remaining <= 0;
  useEffect(() => {
    if (expired) onExpired();
  }, [expired, onExpired]);

  const lines = cart?.lines ?? [];
  return (
    <section className={styles.cart}>
      <div className={styles.cartHead}>
        <h2 className={styles.cartTitle}>
          <IconShoppingCart size={24} stroke={2.4} aria-hidden="true" />
          Your cart
        </h2>
        {cart && <span className={styles.countPill}>{plural(cart.itemCount, 'item')}</span>}
      </div>

      {notice && <NoticeBanner notice={notice} onDismiss={onDismissNotice} />}

      {lastOrder && !cart && <OrderConfirmation order={lastOrder} onDismiss={onDismissOrder} />}

      {lines.length === 0 && !lastOrder && (
        <p className={styles.empty}>
          Your cart is empty. Add a bag of beans to start one. It's kept in Redis for 30 minutes after your last change.
        </p>
      )}

      {cart && lines.length > 0 && (
        <>
          <ul className={styles.lines}>
            {lines.map((line) => (
              <LineRow
                key={line.sku}
                line={line}
                disabled={busy !== null}
                onQuantity={(quantity) => onQuantity(line.sku, quantity)}
                onRemove={() => onRemove(line.sku)}
              />
            ))}
          </ul>

          <div className={styles.summary}>
            <div className={styles.subtotalRow}>
              <span>Subtotal</span>
              <strong>{formatUsd(cart.subtotal)}</strong>
            </div>
            <LimitMeter subtotal={cart.subtotal} limit={cart.orderLimit} />
          </div>

          <p className={styles.expiry} role="timer" aria-live="off">
            <IconHourglassHigh size={20} stroke={2.2} aria-hidden="true" />
            <span>
              Held in Redis for <strong className={styles.countdown}>{formatCountdown(remaining)}</strong>
              <span className={styles.expiryHint}>Every change resets the 30-minute expiry.</span>
            </span>
          </p>

          <button type="button" className={styles.checkout} onClick={onCheckout} disabled={busy !== null}>
            {busy === 'checkout' ? 'Placing order…' : `Check out · ${formatUsd(cart.subtotal)}`}
          </button>
        </>
      )}
    </section>
  );
}

function LineRow({
  line,
  disabled,
  onQuantity,
  onRemove,
}: {
  line: CartLine;
  disabled: boolean;
  onQuantity: (quantity: number) => void;
  onRemove: () => void;
}) {
  const { Icon, color } = look(line.sku);
  return (
    <li className={styles.line}>
      <span className={styles.lineIcon} style={{ ['--card' as string]: `var(${color})` }} aria-hidden="true">
        <Icon size={22} stroke={2.2} />
      </span>
      <span className={styles.lineName}>{line.name}</span>
      <span className={styles.lineTotal}>{formatUsd(line.lineTotal)}</span>
      <span className={styles.linePrice}>{formatUsd(line.unitPrice)} each</span>
      <div className={styles.lineControls}>
        <div className={styles.stepper} role="group" aria-label={`Quantity of ${line.name}`}>
          <button
            type="button"
            onClick={() => onQuantity(line.quantity - 1)}
            disabled={disabled}
            aria-label={`Remove one ${line.name}`}
          >
            <IconMinus size={16} stroke={2.6} aria-hidden="true" />
          </button>
          <span className={styles.quantity} aria-live="polite">
            {line.quantity}
          </span>
          <button
            type="button"
            onClick={() => onQuantity(line.quantity + 1)}
            disabled={disabled || line.quantity >= MAX_LINE_QUANTITY}
            aria-label={`Add one ${line.name}`}
          >
            <IconPlus size={16} stroke={2.6} aria-hidden="true" />
          </button>
        </div>
        <button type="button" className={styles.remove} onClick={onRemove} disabled={disabled} aria-label={`Remove ${line.name}`}>
          <IconTrash size={18} stroke={2.2} aria-hidden="true" />
        </button>
      </div>
    </li>
  );
}

function LimitMeter({ subtotal, limit }: { subtotal: number; limit: number }) {
  const ratio = subtotal / limit;
  const over = subtotal > limit;
  return (
    <div className={styles.limit}>
      <div
        className={styles.meter}
        role="meter"
        aria-valuemin={0}
        aria-valuemax={limit}
        aria-valuenow={subtotal}
        aria-label="Order limit used"
        data-level={over ? 'over' : ratio >= 0.8 ? 'near' : 'ok'}
      >
        <span style={{ width: `${Math.min(100, ratio * 100)}%` }} />
      </div>
      <span className={styles.limitText}>
        {over
          ? `${formatUsd(subtotal - limit)} over the ${formatUsd(limit)} order limit`
          : `${formatUsd(limit - subtotal)} left of the ${formatUsd(limit)} order limit`}
      </span>
    </div>
  );
}

function NoticeBanner({ notice, onDismiss }: { notice: Notice; onDismiss: () => void }) {
  const Icon = notice.tone === 'info' ? IconInfoCircle : IconAlertTriangle;
  return (
    <div className={styles.notice} data-tone={notice.tone} role={notice.tone === 'info' ? 'status' : 'alert'}>
      <Icon size={22} stroke={2.2} aria-hidden="true" />
      <div>
        <strong>{notice.title}</strong>
        <p>{notice.detail}</p>
      </div>
      <button type="button" onClick={onDismiss} aria-label="Dismiss">
        <IconX size={16} stroke={2.6} aria-hidden="true" />
      </button>
    </div>
  );
}

function OrderConfirmation({ order, onDismiss }: { order: Order; onDismiss: () => void }) {
  return (
    <div className={styles.confirmation} role="status">
      <IconCircleCheck size={34} stroke={2.2} aria-hidden="true" />
      <div>
        <strong>Order {order.orderId} placed</strong>
        <p>
          {plural(order.itemCount, 'item')} · {formatUsd(order.subtotal)}. The cart was removed from Redis.
        </p>
      </div>
      <button type="button" onClick={onDismiss}>
        Keep shopping
      </button>
    </div>
  );
}

function RecentOrders({ orders, now }: { orders: Order[]; now: number }) {
  if (orders.length === 0) return null;
  return (
    <section className={styles.orders} aria-label="Recent orders">
      <h2 className={styles.sectionTitle}>Recent orders</h2>
      <ul>
        {orders.map((order) => (
          <li key={order.orderId} className={styles.order}>
            <IconReceipt size={20} stroke={2.2} aria-hidden="true" />
            <span className={styles.orderId}>{order.orderId}</span>
            <span className={styles.orderItems}>{order.lines.map((line) => `${line.quantity}× ${line.name}`).join(', ')}</span>
            <span className={styles.orderTotal}>{formatUsd(order.subtotal)}</span>
            <time className={styles.orderTime} dateTime={order.placedAt}>
              {formatAgo(order.placedAt, now)}
            </time>
          </li>
        ))}
      </ul>
    </section>
  );
}

export default App;
