import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { WeatherForecasts } from '../types/weatherForecast';

type LoadState = 'loading' | 'ready' | 'error';

const SUMMARY_ICONS: Record<string, string> = {
  Freezing: 'severe_cold',
  Bracing: 'air',
  Chilly: 'weather_snowy',
  Cool: 'cloud',
  Mild: 'partly_cloudy_day',
  Warm: 'sunny',
  Balmy: 'clear_day',
  Hot: 'device_thermostat',
  Sweltering: 'local_fire_department',
  Scorching: 'whatshot',
};

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    MatToolbarModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppComponent implements OnInit {
  protected readonly title = 'weather';
  protected readonly forecasts = signal<WeatherForecasts>([]);
  protected readonly state = signal<LoadState>('loading');
  protected readonly displayedColumns = [
    'date',
    'temperatureC',
    'temperatureF',
    'summary',
  ] as const;

  private readonly http = inject(HttpClient);

  constructor(iconRegistry: MatIconRegistry) {
    // Render every <mat-icon> with the Material Symbols (outlined) font.
    iconRegistry.setDefaultFontSetClass('material-symbols-outlined');
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.http.get<WeatherForecasts>('api/weatherforecast').subscribe({
      next: (result) => {
        this.forecasts.set(result ?? []);
        this.state.set('ready');
      },
      error: (err) => {
        console.error('Failed to load weather forecast', err);
        this.state.set('error');
      },
    });
  }

  protected summaryIcon(summary: string): string {
    return SUMMARY_ICONS[summary] ?? 'thermostat';
  }

  protected tempTone(celsius: number): string {
    if (celsius < -5) return 'cold';
    if (celsius < 6) return 'cool';
    if (celsius < 19) return 'mild';
    if (celsius < 31) return 'warm';
    return 'hot';
  }

  protected get statusText(): string {
    switch (this.state()) {
      case 'loading':
        return 'Loading the latest forecast…';
      case 'error':
        return 'Could not load the forecast. Select refresh to try again.';
      default:
        return `Showing a ${this.forecasts().length}-day forecast.`;
    }
  }
}
