const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

export const formatUsd = (amount: number) => usd.format(amount);

/** `29:41` style countdown from milliseconds. */
export function formatCountdown(ms: number) {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

/** "just now", "4 minutes ago", "2 hours ago". */
export function formatAgo(iso: string, now: number) {
  const seconds = Math.round((Date.parse(iso) - now) / 1000);
  if (seconds > -45) return 'just now';
  const minutes = Math.round(seconds / 60);
  if (minutes > -60) return relative.format(minutes, 'minute');
  const hours = Math.round(minutes / 60);
  if (hours > -24) return relative.format(hours, 'hour');
  return relative.format(Math.round(hours / 24), 'day');
}

export const plural = (count: number, one: string, many = `${one}s`) => `${count} ${count === 1 ? one : many}`;
