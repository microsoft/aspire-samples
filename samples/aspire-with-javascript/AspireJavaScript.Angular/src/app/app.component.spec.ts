import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { AppComponent } from './app.component';
import { Shipment, ShipmentBoard } from '../types/shipment';

const scan = { at: '2026-06-07T09:30:00Z', location: 'Buffalo, NY', description: 'Arrived at sort facility' };
const shipment = (id: string, status: Shipment['status']): Shipment => ({
  id,
  origin: 'Chicago, IL',
  destination: 'Boston, MA',
  carrier: 'Northwind Express',
  status,
  eta: '2026-06-08T17:00:00Z',
  weightKg: 8.6,
  progress: 55,
  lastScan: scan,
});

describe('AppComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function render() {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const board: ShipmentBoard = {
      status: null,
      total: 2,
      statusCounts: [
        { status: 'in-transit', count: 1 },
        { status: 'exception', count: 1 },
      ],
      shipments: [shipment('CTS-48213922', 'in-transit'), shipment('CTS-48214101', 'exception')],
    };
    httpMock.expectOne('api/shipments').flush(board);
    httpMock.expectOne('api/shipments/CTS-48213922').flush({ shipment: board.shipments[0], history: [scan] });
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('renders the masthead and one card per shipment', () => {
    const { el } = render();
    expect(el.querySelector('.masthead-title')?.textContent).toContain('Angular Tracking');
    expect(el.querySelectorAll('.shipments .shipment').length).toBe(2);
    expect(el.querySelector('.shipment.selected')?.textContent).toContain('CTS-48213922');
  });

  it('shows the selected shipment detail with its scan history', () => {
    const { el } = render();
    expect(el.querySelector('app-shipment-detail .hero-id')?.textContent).toContain('CTS-48213922');
    expect(el.querySelectorAll('app-shipment-detail .timeline li').length).toBe(1);
  });

  it('requests the filtered board when a status chip is chosen', () => {
    const { fixture } = render();
    fixture.componentInstance['setFilter']('exception');
    httpMock.expectOne('api/shipments?status=exception').flush({
      status: 'exception',
      total: 2,
      statusCounts: [],
      shipments: [shipment('CTS-48214101', 'exception')],
    });
    httpMock
      .expectOne('api/shipments/CTS-48214101')
      .flush({ shipment: shipment('CTS-48214101', 'exception'), history: [scan] });
    expect(fixture.componentInstance['selectedId']()).toBe('CTS-48214101');
  });
});