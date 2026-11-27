import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { SpanStatusCode, trace, type Attributes } from '@opentelemetry/api'
import './App.css'

type StockStatus = 'in_stock' | 'low_stock' | 'out_of_stock'
type CacheResult = 'hit' | 'miss' | 'bypass'

interface Product {
  sku: string
  name: string
  category: string
  price_usd: number
  reorder_point: number
}

interface Availability {
  sku: string
  status: StockStatus
  units_available: number
  reorder_point: number
  distribution_centers: { code: string; name: string; units: number }[]
  restock_eta: string | null
  cache: { result: CacheResult; age_seconds: number; ttl_seconds: number }
  lookup_ms: number
}

interface Lookup {
  result: CacheResult
  ms: number
}

const DISTRIBUTION_CENTERS = [
  { code: 'SEA', city: 'Seattle' },
  { code: 'DEN', city: 'Denver' },
  { code: 'ATL', city: 'Atlanta' },
]

const STATUS_LABEL: Record<StockStatus, string> = {
  in_stock: 'In stock',
  low_stock: 'Low stock',
  out_of_stock: 'Out of stock',
}

// No-op until telemetry.ts registers a provider (run mode only).
const tracer = trace.getTracer('contoso-outdoors-frontend')

/**
 * Runs a UI action inside its own span. `action` must start its fetches synchronously so the
 * fetch spans (and the `traceparent` sent to the API) are parented to this span.
 */
function traced<T>(name: string, attributes: Attributes, action: () => Promise<T>): Promise<T> {
  return tracer.startActiveSpan(name, { attributes }, async (span) => {
    try {
      return await action()
    } catch (err) {
      span.setStatus({ code: SpanStatusCode.ERROR, message: err instanceof Error ? err.message : String(err) })
      throw err
    } finally {
      span.end()
    }
  })
}

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { headers: { Accept: 'application/json' } })
  if (!response.ok) {
    // Errors are RFC 9457 problem details.
    const problem = await response.json().catch(() => null)
    throw new Error(problem?.detail ?? `Request failed with status ${response.status}`)
  }
  return response.json()
}

const usd = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })

const median = (values: number[]): number | null => {
  if (values.length === 0) return null
  const sorted = [...values].sort((a, b) => a - b)
  const mid = Math.floor(sorted.length / 2)
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2
}

const formatMs = (ms: number | null) => (ms === null ? '—' : ms < 10 ? ms.toFixed(1) : Math.round(ms).toString())

const formatDay = (isoDate: string) =>
  new Date(`${isoDate}T00:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })

const formatTime = (date: Date) => date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

function cacheNote(availability: Availability) {
  const { result, age_seconds, ttl_seconds } = availability.cache
  if (result === 'hit') return `Redis · age ${Math.round(age_seconds)} s`
  if (result === 'miss') return `Warehouse · cached ${ttl_seconds} s`
  return 'Warehouse · Redis down'
}

function App() {
  const [products, setProducts] = useState<Product[]>([])
  const [availability, setAvailability] = useState<Record<string, Availability>>({})
  const [checking, setChecking] = useState<ReadonlySet<string>>(new Set())
  const [lookups, setLookups] = useState<Lookup[]>([])
  const [error, setError] = useState<string | null>(null)
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null)
  const started = useRef(false)

  const check = useCallback(async (skus: string[], spanName: string, attributes: Attributes) => {
    setChecking((current) => new Set([...current, ...skus]))
    setError(null)
    try {
      const results = await traced(spanName, attributes, () =>
        Promise.all(skus.map((sku) => getJson<Availability>(`/api/inventory/${sku}`))),
      )
      setAvailability((current) => ({ ...current, ...Object.fromEntries(results.map((a) => [a.sku, a])) }))
      setLookups((current) => [...current, ...results.map((a) => ({ result: a.cache.result, ms: a.lookup_ms }))])
      setUpdatedAt(new Date())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Availability lookup failed')
    } finally {
      setChecking((current) => new Set([...current].filter((sku) => !skus.includes(sku))))
    }
  }, [])

  const checkAll = useCallback(
    (skus: string[]) => check(skus, 'check all availability', { 'inventory.sku_count': skus.length }),
    [check],
  )

  useEffect(() => {
    // StrictMode runs effects twice in development; load the catalog once.
    if (started.current) return
    started.current = true
    getJson<Product[]>('/api/products')
      .then((items) => {
        setProducts(items)
        return checkAll(items.map((p) => p.sku))
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load the catalog'))
  }, [checkAll])

  const stats = useMemo(() => {
    const hits = lookups.filter((l) => l.result === 'hit').map((l) => l.ms)
    const misses = lookups.filter((l) => l.result !== 'hit').map((l) => l.ms)
    const hitMs = median(hits)
    const missMs = median(misses)
    return {
      total: lookups.length,
      hitRatio: lookups.length ? Math.round((hits.length / lookups.length) * 100) : null,
      hitMs,
      missMs,
      speedup: hitMs && missMs ? Math.round(missMs / hitMs) : null,
    }
  }, [lookups])

  const summary = useMemo(() => {
    const items = Object.values(availability)
    return {
      low: items.filter((a) => a.status === 'low_stock').length,
      out: items.filter((a) => a.status === 'out_of_stock').length,
      ttl: items[0]?.cache.ttl_seconds,
    }
  }, [availability])

  const busy = checking.size > 0

  return (
    <div className="sheet">
      <a className="skip-link" href="#catalog">Skip to availability</a>

      <header className="masthead">
        <div className="masthead__brand">
          <span className="mark" aria-hidden="true" />
          <span className="wordmark">Contoso Outdoors</span>
        </div>
        <p className="masthead__meta">
          <span>Inventory service</span>
          <span aria-hidden="true">/</span>
          <span>FastAPI&nbsp;&times;&nbsp;Redis&nbsp;&times;&nbsp;React</span>
        </p>
      </header>

      <main>
        <section className="hero" aria-labelledby="title">
          <h1 id="title" className="hero__title">
            Stock<br />Availability
          </h1>
          <p className="hero__lede">
            Units on hand at the Seattle, Denver and Atlanta distribution centers. Every lookup reads Redis first;
            a miss queries the warehouse system and caches the answer
            {summary.ttl ? ` for ${summary.ttl} seconds` : ''}.
          </p>
        </section>

        <dl className="stats" aria-label="Cache performance for this browser session">
          <div className="stat">
            <dt>Lookups</dt>
            <dd>{stats.total}</dd>
          </div>
          <div className="stat">
            <dt>Cache hit ratio</dt>
            <dd>
              {stats.hitRatio ?? '—'}
              {stats.hitRatio !== null && <small>%</small>}
            </dd>
          </div>
          <div className="stat">
            <dt>Median hit</dt>
            <dd>
              {formatMs(stats.hitMs)}
              {stats.hitMs !== null && <small>ms</small>}
            </dd>
          </div>
          <div className="stat">
            <dt>Median miss</dt>
            <dd>
              {formatMs(stats.missMs)}
              {stats.missMs !== null && <small>ms</small>}
            </dd>
          </div>
          <div className="stat stat--accent">
            <dt>Faster on a hit</dt>
            <dd>
              {stats.speedup ?? '—'}
              {stats.speedup !== null && <small>&times;</small>}
            </dd>
          </div>
        </dl>

        <div className="controlbar">
          <h2 className="controlbar__heading" id="catalog-heading">
            Catalog&nbsp;&middot; {products.length}&nbsp;SKUs
            {summary.low + summary.out > 0 && (
              <span className="controlbar__flags">
                {summary.low} low &middot; {summary.out} out
              </span>
            )}
          </h2>
          <button
            type="button"
            className="refresh"
            onClick={() => checkAll(products.map((p) => p.sku))}
            disabled={busy || products.length === 0}
          >
            <RefreshIcon spinning={busy} />
            <span>{busy ? 'Checking' : 'Check all'}</span>
          </button>
        </div>

        <section id="catalog" aria-labelledby="catalog-heading" aria-busy={busy}>
          {error && (
            <p className="notice notice--error" role="alert">
              <span className="notice__tag" aria-hidden="true">ERR</span>
              {error}
            </p>
          )}

          {products.length === 0 && !error && (
            <div className="skeleton" role="status">
              {[...Array(6)].map((_, i) => (
                <div key={i} className="skeleton__row" aria-hidden="true" />
              ))}
              <span className="visually-hidden">Loading the catalog…</span>
            </div>
          )}

          {products.length > 0 && (
            <ol className="ledger" aria-label="Stock availability by product">
              <li className="ledger__head" aria-hidden="true">
                <span className="col col--index">№</span>
                <span className="col col--product">Product</span>
                {DISTRIBUTION_CENTERS.map((dc) => (
                  <span key={dc.code} className="col col--dc" title={dc.city}>
                    {dc.code}
                  </span>
                ))}
                <span className="col col--total">Total</span>
                <span className="col col--status">Status</span>
                <span className="col col--cache">Cache</span>
                <span className="col col--action" />
              </li>
              {products.map((product, index) => {
                const a = availability[product.sku]
                const isChecking = checking.has(product.sku)
                return (
                  <li className="ledger__row" key={product.sku} aria-busy={isChecking}>
                    <span className="col col--index">{String(index + 1).padStart(2, '0')}</span>
                    <span className="col col--product">
                      <span className="product__name">{product.name}</span>
                      <span className="product__meta">
                        <span className="sku">{product.sku}</span> · {product.category} · {usd.format(product.price_usd)}
                      </span>
                    </span>
                    {DISTRIBUTION_CENTERS.map((dc) => {
                      const units = a?.distribution_centers.find((d) => d.code === dc.code)?.units
                      return (
                        <span
                          key={dc.code}
                          className={`col col--dc ${units === 0 ? 'is-empty' : ''}`}
                          aria-label={`${dc.city}: ${units ?? 'unknown'} units`}
                        >
                          <span className="dc__label" aria-hidden="true">{dc.code}</span>
                          {units ?? '–'}
                        </span>
                      )
                    })}
                    <span className="col col--total">{a ? a.units_available : '–'}</span>
                    <span className="col col--status">
                      {a && (
                        <>
                          <span className={`status status--${a.status}`}>{STATUS_LABEL[a.status]}</span>
                          {a.restock_eta ? (
                            <span className="status__note">Restock {formatDay(a.restock_eta)}</span>
                          ) : (
                            a.status === 'low_stock' && <span className="status__note">Reorder at {a.reorder_point}</span>
                          )}
                        </>
                      )}
                    </span>
                    <span className="col col--cache">
                      {a && (
                        <>
                          <span className="cache__line">
                            <span className={`cache cache--${a.cache.result}`}>{a.cache.result}</span>
                            <span className="cache__ms">{formatMs(a.lookup_ms)} ms</span>
                          </span>
                          <span className="cache__note">{cacheNote(a)}</span>
                        </>
                      )}
                    </span>
                    <span className="col col--action">
                      <button
                        type="button"
                        className="icon-button"
                        onClick={() => check([product.sku], 'check availability', { 'inventory.sku': product.sku })}
                        disabled={isChecking}
                        aria-label={`Check availability of ${product.name}`}
                        title="Check availability"
                      >
                        <RefreshIcon spinning={isChecking} />
                      </button>
                    </span>
                  </li>
                )
              })}
            </ol>
          )}

          <p className="updated" aria-live="polite">
            {updatedAt
              ? `Checked ${formatTime(updatedAt)} · ${summary.ttl ?? 30}-second Redis TTL`
              : 'Checking availability…'}
          </p>
        </section>
      </main>

      <footer className="colophon">
        <nav aria-label="Resources">
          <a href="https://aspire.dev" target="_blank" rel="noopener noreferrer">
            aspire.dev<span className="visually-hidden"> (opens in new tab)</span>
          </a>
          <a href="https://github.com/dotnet/aspire" target="_blank" rel="noopener noreferrer">
            github.com/dotnet/aspire<span className="visually-hidden"> (opens in new tab)</span>
          </a>
        </nav>
        <p className="colophon__set">Contoso Outdoors · Aspire Python sample</p>
      </footer>
    </div>
  )
}

function RefreshIcon({ spinning }: { spinning: boolean }) {
  return (
    <svg
      className={`refresh__icon ${spinning ? 'is-spinning' : ''}`}
      width="16"
      height="16"
      viewBox="0 0 16 16"
      aria-hidden="true"
      focusable="false"
    >
      <path
        d="M13.5 3.2V6.5H10.2M2.5 12.8V9.5H5.8M3 6.5A5 5 0 0 1 12.6 5.4M13 9.5A5 5 0 0 1 3.4 10.6"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeLinecap="square"
      />
    </svg>
  )
}

export default App
