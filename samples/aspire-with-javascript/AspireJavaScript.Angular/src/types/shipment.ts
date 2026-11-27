export type ShipmentStatus = 'label-created' | 'in-transit' | 'out-for-delivery' | 'delivered' | 'exception';

export interface ScanEvent {
  at: string;
  location: string;
  description: string;
}

export interface Shipment {
  id: string;
  origin: string;
  destination: string;
  carrier: string;
  status: ShipmentStatus;
  eta: string;
  weightKg: number;
  progress: number;
  lastScan: ScanEvent;
}

export interface ShipmentDetail {
  shipment: Shipment;
  history: ScanEvent[];
}

export interface StatusCount {
  status: ShipmentStatus;
  count: number;
}

export interface ShipmentBoard {
  status: ShipmentStatus | null;
  total: number;
  statusCounts: StatusCount[];
  shipments: Shipment[];
}

export const STATUS_LABELS: Record<ShipmentStatus, string> = {
  'label-created': 'Label created',
  'in-transit': 'In transit',
  'out-for-delivery': 'Out for delivery',
  delivered: 'Delivered',
  exception: 'Exception',
};
