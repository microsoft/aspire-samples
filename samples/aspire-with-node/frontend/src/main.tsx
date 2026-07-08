import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';

// Catch anything that slips past component-level handling so failures are recorded in one
// place (and picked up by Aspire's browser-log capture) instead of vanishing silently.
window.addEventListener('error', (event) => {
  console.error('[uncaught:error]', event.error ?? event.message);
});
window.addEventListener('unhandledrejection', (event) => {
  console.error('[uncaught:rejection]', event.reason);
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
