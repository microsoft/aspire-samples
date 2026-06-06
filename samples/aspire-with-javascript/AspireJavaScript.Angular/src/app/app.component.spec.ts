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

  function flushForecast() {
    const req = httpMock.expectOne('api/weatherforecast');
    req.flush([
      { date: '2026-06-07', temperatureC: 21, temperatureF: 70, summary: 'Warm' },
    ]);
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

  it('should render the Aspire Weather masthead', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.masthead-title')?.textContent).toContain(
      'Aspire Weather'
    );
  });

  it('should render a row per forecast', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    flushForecast();
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(1);
  });
});
