import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Use fetch so the OpenTelemetry fetch instrumentation traces API calls in run mode.
    provideHttpClient(withFetch()),
    provideRouter(routes),
  ],
};
