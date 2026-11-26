import './app.css'
import App from './App.svelte'
import { mount } from 'svelte'

// Start browser telemetry before the first render so the app's first fetches are traced.
// The SDK is loaded lazily, so production builds (telemetry disabled) don't ship it.
if (import.meta.env.VITE_OTEL_ENABLED) {
  try {
    const { initializeTelemetry } = await import('./telemetry.js')
    initializeTelemetry()
  } catch (err) {
    console.warn('[telemetry] disabled:', err)
  }
}

const app = mount(App, {
  target: document.getElementById('app')
})

export default app
