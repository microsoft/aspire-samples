import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AppComponent } from './app.component';

describe('AppComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushForecast(
    forecast: ReadonlyArray<{
      date: string;
      temperatureC: number;
      temperatureF: number;
      summary: string;
    }> = [
      { date: '2026-06-07', temperatureC: 21, temperatureF: 70, summary: 'Warm' },
      { date: '2026-06-08', temperatureC: 4, temperatureF: 39, summary: 'Cool' },
      { date: '2026-06-09', temperatureC: 33, temperatureF: 91, summary: 'Hot' },
    ],
  ) {
    const req = httpMock.expectOne('api/weatherforecast');
    req.flush(forecast);
  }

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it("should have the 'weather' title", () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    expect(fixture.componentInstance['title']).toEqual('weather');
  });

  it('should render the Angular Weather masthead', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.masthead-title')?.textContent).toContain(
      'Angular Weather',
    );
  });

  it('should feature the first day and list the remaining days', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.hero')).toBeTruthy();
    // 3 days flushed: 1 featured in the hero + 2 in the grid.
    const cards = compiled.querySelectorAll('.forecast-grid .grid-item');
    expect(cards.length).toBe(2);
  });
});
