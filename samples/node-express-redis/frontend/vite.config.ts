import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Aspire injects these when the AppHost calls withOtlpExporter(HttpProtobuf) on the frontend.
const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;

/** Parses OTEL_EXPORTER_OTLP_HEADERS (`x-otlp-api-key=...`) into a header object. */
function otlpHeaders(): Record<string, string> {
  return Object.fromEntries(
    (process.env.OTEL_EXPORTER_OTLP_HEADERS ?? '')
      .split(',')
      .map((pair) => pair.split('=').map((part) => part.trim()))
      .filter(([key, value]) => key && value),
  );
}

export default defineConfig({
  plugins: [react()],
  define: {
    // Only non-secret values are exposed to the browser bundle.
    'import.meta.env.VITE_OTEL_ENABLED': JSON.stringify(Boolean(otlpEndpoint)),
    'import.meta.env.VITE_OTEL_SERVICE_NAME': JSON.stringify(process.env.OTEL_SERVICE_NAME ?? 'frontend'),
    'import.meta.env.VITE_OTEL_RESOURCE_ATTRIBUTES': JSON.stringify(process.env.OTEL_RESOURCE_ATTRIBUTES ?? ''),
  },
  server: {
    // YARP (a container) reaches the dev server through these host names.
    allowedHosts: ['host.docker.internal', 'aspire.dev.internal'],
    host: true,
    proxy: {
      // Used when browsing the Vite endpoint directly; through YARP, `/api` never reaches Vite.
      '/api': {
        target: process.env.API_HTTPS || process.env.API_HTTP,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
      // Browser telemetry -> Aspire dashboard OTLP/HTTP. The API key header is added
      // here, server-side, instead of being shipped to the browser.
      ...(otlpEndpoint && {
        '/otlp': {
          target: otlpEndpoint,
          changeOrigin: true,
          // The dashboard uses the ASP.NET Core development certificate locally.
          secure: false,
          headers: otlpHeaders(),
          rewrite: (path: string) => path.replace(/^\/otlp/, ''),
        },
      }),
    },
  },
});
