/// <reference types="vite/client" />

interface ImportMetaEnv {
  // Defined in vite.config.ts from the OTEL_* variables Aspire injects in run mode.
  readonly VITE_OTEL_ENABLED: boolean
  readonly VITE_OTEL_SERVICE_NAME: string
  readonly VITE_OTEL_RESOURCE_ATTRIBUTES: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
