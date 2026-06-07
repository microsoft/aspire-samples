import { useState, useEffect } from 'react'
import './App.css'

interface WeatherForecast {
  date: string
  temperatureC: number
  temperatureF: number
  summary: string
}

// Domain bounds used to normalise the temperature data bars. Matches the
// range the FastAPI service generates (-20°C..55°C).
const TEMP_MIN_C = -20
const TEMP_MAX_C = 55

function App() {
  const [weatherData, setWeatherData] = useState<WeatherForecast[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [useCelsius, setUseCelsius] = useState(true)
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null)

  const requestForecast = async (): Promise<WeatherForecast[]> => {
    const response = await fetch('/api/weatherforecast')
    if (!response.ok) {
      throw new Error(`Request failed — status ${response.status}`)
    }
    return response.json()
  }

  const refresh = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await requestForecast()
      setWeatherData(data)
      setUpdatedAt(new Date())
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to fetch weather data')
      console.error('Error fetching weather forecast:', err)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    requestForecast()
      .then((data) => {
        if (cancelled) return
        setWeatherData(data)
        setUpdatedAt(new Date())
      })
      .catch((err) => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : 'Failed to fetch weather data')
        console.error('Error fetching weather forecast:', err)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const formatDate = (dateString: string) =>
    new Date(dateString).toLocaleDateString(undefined, {
      weekday: 'short',
      month: 'short',
      day: '2-digit',
    })

  const formatTime = (date: Date) =>
    date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

  const barWidth = (tempC: number) => {
    const pct = ((tempC - TEMP_MIN_C) / (TEMP_MAX_C - TEMP_MIN_C)) * 100
    return Math.max(2, Math.min(100, pct))
  }

  const unitLabel = useCelsius ? 'Celsius' : 'Fahrenheit'

  return (
    <div className="sheet">
      <a className="skip-link" href="#data">Skip to forecast data</a>

      <header className="masthead">
        <div className="masthead__brand">
          <span className="mark" aria-hidden="true" />
          <span className="wordmark">Aspire</span>
        </div>
        <p className="masthead__meta">
          <span>Meteorological Data Service</span>
          <span aria-hidden="true">/</span>
          <span>FastAPI&nbsp;&times;&nbsp;React</span>
        </p>
      </header>

      <main>
        <section className="hero" aria-labelledby="title">
          <h1 id="title" className="hero__title">
            Weather<br />Forecast
          </h1>
          <p className="hero__lede">
            A five&#8209;day outlook generated from randomised sample data.
          </p>
        </section>

        <div className="controlbar">
          <h2 className="controlbar__heading" id="data-heading">
            Five&#8209;day&nbsp;outlook
          </h2>
          <div className="controlbar__actions">
            <fieldset className="segmented" aria-label="Temperature unit">
              <legend className="visually-hidden">Temperature unit</legend>
              <button
                type="button"
                className="segmented__option"
                aria-pressed={useCelsius}
                onClick={() => setUseCelsius(true)}
              >
                <span aria-hidden="true">°C</span>
                <span className="visually-hidden">Celsius</span>
              </button>
              <button
                type="button"
                className="segmented__option"
                aria-pressed={!useCelsius}
                onClick={() => setUseCelsius(false)}
              >
                <span aria-hidden="true">°F</span>
                <span className="visually-hidden">Fahrenheit</span>
              </button>
            </fieldset>
            <button
              type="button"
              className="refresh"
              onClick={refresh}
              disabled={loading}
              aria-label={loading ? 'Loading weather forecast' : 'Refresh weather forecast'}
            >
              <svg
                className={`refresh__icon ${loading ? 'is-spinning' : ''}`}
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
              <span>{loading ? 'Loading' : 'Refresh'}</span>
            </button>
          </div>
        </div>

        <section id="data" aria-labelledby="data-heading" aria-busy={loading}>
          {error && (
            <p className="notice notice--error" role="alert">
              <span className="notice__tag" aria-hidden="true">ERR</span>
              {error}
            </p>
          )}

          {loading && weatherData.length === 0 && (
            <div className="skeleton" role="status">
              {[...Array(5)].map((_, i) => (
                <div key={i} className="skeleton__row" aria-hidden="true" />
              ))}
              <span className="visually-hidden">Loading weather forecast data…</span>
            </div>
          )}

          {weatherData.length > 0 && (
            <ol className="ledger" aria-label="Five day weather forecast">
              <li className="ledger__head" aria-hidden="true">
                <span className="col col--index">№</span>
                <span className="col col--date">Date</span>
                <span className="col col--summary">Condition</span>
                <span className="col col--bar">Range&nbsp;({TEMP_MIN_C}…{TEMP_MAX_C}°C)</span>
                <span className="col col--temp">Temp</span>
              </li>
              {weatherData.map((forecast, index) => {
                const value = useCelsius ? forecast.temperatureC : forecast.temperatureF
                return (
                  <li
                    className="ledger__row"
                    key={`${forecast.date}-${index}`}
                  >
                    <span className="col col--index">{String(index + 1).padStart(2, '0')}</span>
                    <span className="col col--date">
                      <time dateTime={forecast.date}>{formatDate(forecast.date)}</time>
                    </span>
                    <span className="col col--summary">{forecast.summary}</span>
                    <span className="col col--bar">
                      <span className="dbar" aria-hidden="true">
                        <span
                          className="dbar__fill"
                          style={{ width: `${barWidth(forecast.temperatureC)}%` }}
                        />
                      </span>
                    </span>
                    <span className="col col--temp">
                      <span
                        className="temp"
                        aria-label={`${value} degrees ${unitLabel}`}
                      >
                        <span className="temp__value">{value}</span>
                        <span className="temp__unit" aria-hidden="true">°{useCelsius ? 'C' : 'F'}</span>
                      </span>
                    </span>
                  </li>
                )
              })}
            </ol>
          )}

          <p className="updated" aria-live="polite">
            {updatedAt
              ? `Updated ${formatTime(updatedAt)} · showing ${unitLabel}`
              : 'Awaiting first reading'}
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
        <p className="colophon__set">Aspire weather forecast sample</p>
      </footer>
    </div>
  )
}

export default App
