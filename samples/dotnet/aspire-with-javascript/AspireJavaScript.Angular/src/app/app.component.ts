import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { WeatherForecast, WeatherForecasts } from '../types/weatherForecast';

type LoadState = 'loading' | 'ready' | 'error';
type TempTone = 'cold' | 'cool' | 'mild' | 'warm' | 'hot';

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

  /** The featured (first) day, shown large in the hero. */
  protected readonly lead = computed<WeatherForecast | null>(
    () => this.forecasts()[0] ?? null,
  );
  /** The remaining days, shown in the responsive card grid. */
  protected readonly rest = computed<WeatherForecasts>(() =>
    this.forecasts().slice(1),
  );

  private readonly http = inject(HttpClient);

  private readonly weekdayLong = new Intl.DateTimeFormat(undefined, {
    weekday: 'long',
  });
  private readonly weekdayShort = new Intl.DateTimeFormat(undefined, {
    weekday: 'short',
  });
  private readonly monthDay = new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
  });

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
        this.forecasts.set([]);
        this.state.set('error');
      },
    });
  }

  protected summaryIcon(summary: string): string {
    return SUMMARY_ICONS[summary] ?? 'thermostat';
  }

  protected tempTone(celsius: number): TempTone {
    if (celsius < -5) return 'cold';
    if (celsius < 6) return 'cool';
    if (celsius < 19) return 'mild';
    if (celsius < 31) return 'warm';
    return 'hot';
  }

  /** Full weekday (e.g. "Monday"); used for the featured day. */
  protected weekday(iso: string): string {
    return this.format(iso, this.weekdayLong);
  }

  /** Short weekday (e.g. "Mon"); used for the grid cards. */
  protected shortWeekday(iso: string): string {
    return this.format(iso, this.weekdayShort);
  }

  /** Calendar date (e.g. "Jun 8"). */
  protected calendarDate(iso: string): string {
    return this.format(iso, this.monthDay);
  }

  protected get statusText(): string {
    switch (this.state()) {
      case 'loading':
        return 'Loading the latest forecast…';
      case 'error':
        return 'Could not reach the weather service. Select refresh to try again.';
      default:
        return `Showing a ${this.forecasts().length}-day forecast.`;
    }
  }

  // Parse the API's "yyyy-MM-dd" string as a *local* date so the weekday never
  // shifts a day in time zones behind UTC.
  private format(iso: string, formatter: Intl.DateTimeFormat): string {
    const [year, month, day] = (iso ?? '').split('-').map(Number);
    if (!year || !month || !day) {
      return iso ?? '';
    }
    return formatter.format(new Date(year, month - 1, day));
  }
}
