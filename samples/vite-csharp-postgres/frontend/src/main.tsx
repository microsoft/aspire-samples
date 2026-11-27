import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './style.css';

function render() {
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}

// Start browser telemetry before the first render so the first fetches are traced.
// The SDK is loaded lazily, so production builds (telemetry disabled) don't ship it.
if (import.meta.env.VITE_OTEL_ENABLED) {
  import('./telemetry')
    .then(({ initializeTelemetry }) => initializeTelemetry())
    .catch((err) => console.warn('[telemetry] disabled:', err))
    .finally(render);
} else {
  render();
}