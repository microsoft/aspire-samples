export type ShipmentStatus = 'label-created' | 'in-transit' | 'out-for-delivery' | 'delivered' | 'exception'

export interface ScanEvent {
  at: string
  location: string
  description: string
}

export interface Shipment {
  id: string
  origin: string
  destination: string
  carrier: string
  status: ShipmentStatus
  eta: string
  weightKg: number
  progress: number
  lastScan: ScanEvent
}

export interface ShipmentDetail {
  shipment: Shipment
  history: ScanEvent[]
}

export interface ShipmentBoard {
  status: ShipmentStatus | null
  total: number
  statusCounts: { status: ShipmentStatus; count: number }[]
  shipments: Shipment[]
}

export const STATUS_LABELS: Record<ShipmentStatus, string> = {
  'label-created': 'Label created',
  'in-transit': 'In transit',
  'out-for-delivery': 'Out for delivery',
  delivered: 'Delivered',
  exception: 'Exception'
}

export const STATUSES = Object.keys(STATUS_LABELS) as ShipmentStatus[]

const dateTime = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))

/** e.g. "in 9 hours" or "3 hours ago". */
export function fromNow(iso: string): string {
  const minutes = Math.round((new Date(iso).getTime() - Date.now()) / 60_000)
  if (Math.abs(minutes) < 60) return relative.format(minutes, 'minute')
  const hours = Math.round(minutes / 60)
  if (Math.abs(hours) < 48) return relative.format(hours, 'hour')
  return relative.format(Math.round(hours / 24), 'day')
}

export async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url)
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}`)
  }
  return (await response.json()) as T
}