import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

function render() {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

// Start browser telemetry before the first render so the first fetches are traced.
// The SDK is loaded lazily, so production builds (telemetry disabled) don't ship it.
if (import.meta.env.VITE_OTEL_ENABLED) {
  import('./telemetry.ts')
    .then(({ initializeTelemetry }) => initializeTelemetry())
    .catch((err) => console.warn('[telemetry] disabled:', err))
    .finally(render)
} else {
  render()
}
