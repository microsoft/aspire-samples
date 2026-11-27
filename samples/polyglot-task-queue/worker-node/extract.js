/**
 * Invoice field extraction: the Node.js worker's job. Emailed invoices arrive as text (in a real
 * pipeline, after OCR); regular expressions pull out the fields accounts payable needs.
 */

export class ExtractionError extends Error {
  name = 'ExtractionError';
}

const DATE = String.raw`(\d{4}-\d{2}-\d{2})`;
const AMOUNT = String.raw`([\d,]+\.\d{2})`;

const PATTERNS = {
  invoiceNumber: /invoice\s*(?:number|no\.?|#)\s*[:#]?\s*([A-Z]{2,}-[\w-]+)/i,
  poNumber: /\bPO\s*(?:number|no\.?|#)\s*[:#]?\s*([A-Z]{2,}-?\d+)/i,
  invoiceDate: new RegExp(String.raw`invoice date\s*:\s*${DATE}`, 'i'),
  dueDate: new RegExp(String.raw`due(?: date)?\s*:\s*${DATE}`, 'i'),
  paymentTerms: /\b(Net \d+)\b/i,
  total: new RegExp(String.raw`\btotal(?: due)?\s*:\s*(?:([A-Z]{3})\s*)?\$?\s*${AMOUNT}`, 'i'),
  vendor: /^subject:.*\bfrom\s+(.+?)\s*$/im,
  sender: /^from:\s*\S+@([\w.-]+)/im,
};
const LINE_ITEM = new RegExp(String.raw`^(.+?)\s*\.{3,}\s*\$${AMOUNT}\s*$`, 'gm');
const EMAIL = /[\w.+-]+@[\w-]+(?:\.[\w-]+)+/g;

const toNumber = (/** @type {string} */ amount) => Number(amount.replaceAll(',', ''));
const money = (/** @type {number} */ value, /** @type {string} */ currency) =>
  `${currency} ${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

/**
 * @param {string} text
 * @returns {{ fields: Record<string, string | number>, lineItems: { description: string, amount: number }[], emails: string[] }}
 */
export function extractInvoice(text) {
  const match = (/** @type {RegExp} */ pattern) => pattern.exec(text) ?? undefined;
  const total = match(PATTERNS.total);
  const vendor = match(PATTERNS.vendor)?.[1] ?? match(PATTERNS.sender)?.[1];

  /** @type {Record<string, string | number>} */
  const fields = {};
  const set = (/** @type {string} */ key, /** @type {string | number | undefined} */ value) => {
    if (value !== undefined) fields[key] = value;
  };
  set('invoiceNumber', match(PATTERNS.invoiceNumber)?.[1]);
  set('poNumber', match(PATTERNS.poNumber)?.[1]);
  set('invoiceDate', match(PATTERNS.invoiceDate)?.[1]);
  set('dueDate', match(PATTERNS.dueDate)?.[1]);
  set('paymentTerms', match(PATTERNS.paymentTerms)?.[1]);
  set('vendor', vendor);
  if (total) {
    set('currency', total[1]?.toUpperCase() ?? 'USD');
    set('total', toNumber(total[2]));
  }

  if (fields.invoiceNumber === undefined && fields.total === undefined) {
    throw new ExtractionError("No invoice number or total found; the document doesn't look like an invoice.");
  }

  const lineItems = [...text.matchAll(LINE_ITEM)].map(([, description, amount]) => ({
    description: description.trim(),
    amount: toNumber(amount),
  }));
  const emails = [...new Set(text.match(EMAIL) ?? [])];
  return { fields, lineItems, emails };
}

/**
 * The result envelope every worker returns: a headline, display-ready facts and the raw details.
 * @param {ReturnType<typeof extractInvoice>} extraction
 */
export function toResult({ fields, lineItems, emails }) {
  const currency = String(fields.currency ?? 'USD');
  const total = typeof fields.total === 'number' ? money(fields.total, currency) : undefined;
  const headline = [
    `Invoice ${fields.invoiceNumber ?? '(no number)'}`,
    fields.vendor && ` from ${fields.vendor}`,
    total && `: ${total}`,
    fields.dueDate && ` due ${fields.dueDate}`,
  ]
    .filter(Boolean)
    .join('');

  const facts = [
    { label: 'Invoice', value: fields.invoiceNumber },
    { label: 'PO', value: fields.poNumber },
    { label: 'Total', value: total },
    { label: 'Due', value: fields.dueDate },
    { label: 'Line items', value: lineItems.length ? String(lineItems.length) : undefined },
  ].filter((fact) => fact.value !== undefined);

  return { headline, facts, details: { ...fields, lineItems, emails } };
}
