import './assets/main.css'
import '@fontsource-variable/outfit'

import { createApp } from 'vue'
import App from './App.vue'

const mount = () => createApp(App).mount('#app')

// Start browser telemetry before mounting so the first fetches are traced.
// The SDK is loaded lazily, so production builds (telemetry disabled) don't ship it.
if (import.meta.env.VITE_OTEL_ENABLED) {
  import('./telemetry')
    .then(({ initializeTelemetry }) => initializeTelemetry())
    .catch((err) => console.warn('[telemetry] disabled:', err))
    .finally(mount)
} else {
  mount()
}