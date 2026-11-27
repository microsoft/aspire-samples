import { isDevMode } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import type { TelemetryConfig } from './telemetry';

/**
 * In development (`ng serve` under Aspire) the dev server reports whether an OTLP endpoint is available.
 * When it is, the OpenTelemetry SDK is lazy-loaded and started before bootstrap so the board's first
 * requests are traced. Production builds skip this entirely and never download the SDK.
 */
async function startTelemetry(): Promise<void> {
  if (!isDevMode()) return;
  try {
    const response = await fetch('/otel-config.json');
    if (!response.ok) return;
    const config = (await response.json()) as TelemetryConfig;
    if (!config.enabled) return;
    const { initializeTelemetry } = await import('./telemetry');
    initializeTelemetry(config);
  } catch (err) {
    console.warn('[telemetry] disabled:', err);
  }
}

startTelemetry()
  .then(() => bootstrapApplication(AppComponent, appConfig))
  .catch((err) => console.error(err));