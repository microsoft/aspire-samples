export const STATUS_LABELS = {
  "label-created": "Label created",
  "in-transit": "In transit",
  "out-for-delivery": "Out for delivery",
  delivered: "Delivered",
  exception: "Exception",
};

export const STATUSES = Object.keys(STATUS_LABELS);

const dateTime = new Intl.DateTimeFormat(undefined, {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export const formatDateTime = (iso) => dateTime.format(new Date(iso));

export async function getJson(url) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`${url} returned HTTP ${response.status}`);
  }
  return response.json();
}