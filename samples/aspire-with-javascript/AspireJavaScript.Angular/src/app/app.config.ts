import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    // Use fetch so the OpenTelemetry fetch instrumentation traces API calls in run mode.
    provideHttpClient(withFetch()),
    provideAnimationsAsync()
  ]
};