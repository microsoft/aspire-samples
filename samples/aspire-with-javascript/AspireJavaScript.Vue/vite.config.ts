import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// Aspire injects these when the AppHost calls WithOtlpExporter(OtlpProtocol.HttpProtobuf).
const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT

/** Parses `key=value,key2=value2` (the OTEL_EXPORTER_OTLP_HEADERS format) into a header object. */
function parseDelimited(value: string | undefined): Record<string, string> {
  return Object.fromEntries(
    (value ?? '')
      .split(',')
      .map((pair) => pair.split('=').map((part) => part.trim()))
      .filter(([key, val]) => key && val)
  )
}

// https://vitejs.dev/config/
export default defineConfig(({ command }) => ({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  define: {
    // Only non-secret values are exposed to the browser bundle, and only when serving under Aspire.
    'import.meta.env.VITE_OTEL_ENABLED': JSON.stringify(command === 'serve' && Boolean(otlpEndpoint)),
    'import.meta.env.VITE_OTEL_SERVICE_NAME': JSON.stringify(process.env.OTEL_SERVICE_NAME ?? 'vue'),
    'import.meta.env.VITE_OTEL_RESOURCE_ATTRIBUTES': JSON.stringify(process.env.OTEL_RESOURCE_ATTRIBUTES ?? '')
  },
  server: {
    host: true,
    port: parseInt(process.env.PORT ?? '5173'),
    proxy: {
      // Same origin as the page, so the fetch instrumentation's `traceparent` header reaches the API.
      '/api': {
        target: process.env.services__shipmentsapi__https__0 || process.env.services__shipmentsapi__http__0,
        changeOrigin: true,
        secure: false
      },
      // Browser telemetry -> Aspire dashboard OTLP/HTTP. The API key header is added here,
      // server-side, instead of being shipped to the browser.
      ...(otlpEndpoint && {
        '/otlp': {
          target: otlpEndpoint,
          changeOrigin: true,
          // The dashboard uses the ASP.NET Core development certificate locally.
          secure: false,
          headers: parseDelimited(process.env.OTEL_EXPORTER_OTLP_HEADERS),
          rewrite: (path: string) => path.replace(/^\/otlp/, '')
        }
      })
    }
  }
}))