/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** True in run mode when Aspire provides an OTLP endpoint (set in vite.config.ts). */
  readonly VITE_OTEL_ENABLED: boolean;
  readonly VITE_OTEL_SERVICE_NAME: string;
  readonly VITE_OTEL_RESOURCE_ATTRIBUTES: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
