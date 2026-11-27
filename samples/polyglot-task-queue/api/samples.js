/**
 * Sample documents for each task type, the "Submit sample tasks" operation, and the dev-only
 * burst simulation behind the "Simulate burst" dashboard command (see apphost.mts).
 */
import { randomInt } from 'node:crypto';
import { context, trace } from '@opentelemetry/api';
import { logger } from './logger.js';
import { TASK_TYPES, submitTask } from './tasks.js';
import { tracer } from './telemetry.js';

const REGIONS = ['West', 'Central', 'East', 'South', 'North'];

/** @param {string[]} months @param {number} scale */
function salesCsv(months, scale = 1) {
  const rows = ['region,month,orders,revenue'];
  REGIONS.forEach((region, r) => {
    months.forEach((month, m) => {
      const orders = Math.round((420 - r * 55) * scale * (1 + m * 0.06) + randomInt(-18, 18));
      const revenue = Math.round(orders * (448 + r * 9 + randomInt(-6, 6)));
      rows.push(`${region},${month},${orders},${revenue}`);
    });
  });
  return rows.join('\n');
}

const operationsReview = ({ quarter, site, onTime, pickBefore, pickAfter, parcels, spotRate }) => `# ${quarter} operations review: ${site} fulfillment

## Highlights
- On-time delivery rose to ${onTime}% against a 95% target.
- Average pick-to-ship time fell from ${pickBefore} h to ${pickAfter} h after the wave-picking rollout.
- The returns backlog is cleared: ${parcels.toLocaleString('en-US')} parcels processed in the last month of the quarter.

## Risks
- Midwest carrier capacity is tight for peak season and spot rates are up ${spotRate}%.
- Two conveyor motors at the Columbus distribution center are past their service life.

## Action items
- [ ] Lock in peak capacity with Contoso Freight by October 15 (owner: Priya Raman).
- [ ] Replace the Columbus conveyor motors before November 1 (owner: Marcus Webb).
- [ ] Extend wave picking to the Dallas distribution center next quarter (owner: Elena Ortiz).
`;

const VENDORS = [
  { name: 'Northwind Logistics', domain: 'northwindlogistics.com', service: 'Linehaul Chicago to Denver' },
  { name: 'Contoso Freight', domain: 'contosofreight.com', service: 'LTL consolidation, Reno hub' },
  { name: 'Fabrikam Packaging', domain: 'fabrikampackaging.com', service: 'Corrugated cartons, 12x10x8 (2,400 units)' },
  { name: 'Tailspin Air Cargo', domain: 'tailspinaircargo.com', service: 'Next-flight-out SEA to ORD' },
  { name: 'Wide World Importers', domain: 'wideworldimporters.com', service: 'Container drayage, Port of Oakland' },
];

const money = (/** @type {number} */ value) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** @param {{ vendor: typeof VENDORS[number], invoice: string, po: string, issued: string, due: string, base: number, extra: number }} input */
function invoiceEmail({ vendor, invoice, po, issued, due, base, extra }) {
  const surcharge = Math.round(base * 0.12 * 100) / 100;
  const total = base + extra + surcharge;
  return `From: billing@${vendor.domain}
To: accounts.payable@fabrikam.com
Subject: Invoice ${invoice} from ${vendor.name}

Hello Accounts Payable,

Please find our invoice for services rendered below.

Invoice number: ${invoice}
Invoice date: ${issued}
Due date: ${due} (Net 30)
PO number: ${po}

${vendor.service} ..... $${money(base)}
Liftgate and inside delivery ..... $${money(extra)}
Fuel surcharge (12%) ..... $${money(surcharge)}

Subtotal: $${money(total)}
Tax: $0.00
Total due: USD ${money(total)}

Remit by ACH to routing 021000021, account ending 4417.
Questions? Reply to billing@${vendor.domain}.

Thank you,
${vendor.name} Billing`;
}

/** One realistic document per task type: what the UI's "Load sample" and "Submit sample tasks" use. */
export const SAMPLE_DOCUMENTS = [
  { type: 'analyze', name: 'q3-regional-sales.csv', data: salesCsv(['2026-07', '2026-08', '2026-09']) },
  {
    type: 'report',
    name: 'q3-operations-review.md',
    data: operationsReview({ quarter: 'Q3', site: 'Reno', onTime: 96.4, pickBefore: 4.1, pickAfter: 3.2, parcels: 1240, spotRate: 18 }),
  },
  {
    type: 'extract',
    name: 'northwind-invoice-INV-2026-0418.txt',
    data: invoiceEmail({ vendor: VENDORS[0], invoice: 'INV-2026-0418', po: 'PO-88412', issued: '2026-11-02', due: '2026-12-02', base: 4250, extra: 310 }),
  },
].map((sample) => ({ ...sample, worker: TASK_TYPES[/** @type {keyof typeof TASK_TYPES} */ (sample.type)].worker }));

/** Queues the three sample documents, one per worker, in the caller's trace. */
export async function submitSamples() {
  const tasks = [];
  for (const { type, name, data } of SAMPLE_DOCUMENTS) {
    tasks.push(await submitTask({ type: /** @type {keyof typeof TASK_TYPES} */ (type), name, data }, { source: 'sample' }));
  }
  return tasks;
}

/**
 * Every 8th burst task is a document its worker can't process, so failures show up too. The
 * offset moves with each burst so the rejected documents rotate across the three workers.
 */
const isPoison = (/** @type {number} */ index, /** @type {number} */ offset) => (index + offset) % 8 === 5;
let burstCount = 0;

/**
 * @param {number} index
 * @param {number} offset
 * @returns {{ type: keyof typeof TASK_TYPES, name: string, data: string }}
 */
function burstDocument(index, offset) {
  const type = /** @type {const} */ (['analyze', 'report', 'extract'])[index % 3];
  const seq = String(index + 1).padStart(2, '0');
  const poison = isPoison(index, offset);
  if (type === 'analyze') {
    if (poison) {
      return { type, name: `customer-feedback-${seq}.csv`, data: 'customer,channel,comment\nAdatum,email,Late delivery\nLitware,phone,Damaged carton\nProseware,chat,Great support' };
    }
    const quarter = randomInt(1, 5);
    const months = [0, 1, 2].map((m) => `2026-${String((quarter - 1) * 3 + m + 1).padStart(2, '0')}`);
    return { type, name: `q${quarter}-regional-sales-${seq}.csv`, data: salesCsv(months, 0.7 + randomInt(0, 60) / 100) };
  }
  if (type === 'report') {
    if (poison) return { type, name: `standup-notes-${seq}.md`, data: '# Standup\n- TBD' };
    const site = ['Reno', 'Columbus', 'Dallas', 'Savannah'][randomInt(0, 4)];
    return {
      type,
      name: `weekly-ops-${site.toLowerCase()}-${seq}.md`,
      data: operationsReview({
        quarter: `Week ${40 + (index % 12)}`,
        site,
        onTime: (93 + randomInt(0, 50) / 10).toFixed(1),
        pickBefore: (3.6 + randomInt(0, 9) / 10).toFixed(1),
        pickAfter: (2.8 + randomInt(0, 7) / 10).toFixed(1),
        parcels: randomInt(400, 1600),
        spotRate: randomInt(6, 24),
      }),
    };
  }
  if (poison) {
    return { type, name: `delivery-note-${seq}.txt`, data: 'Delivery note\nDock 4, 12 pallets received in good condition.\nSigned: R. Alvarez' };
  }
  const vendor = VENDORS[randomInt(0, VENDORS.length)];
  const number = randomInt(1000, 9999);
  return {
    type,
    name: `${vendor.domain.split('.')[0]}-invoice-INV-2026-${number}.txt`,
    data: invoiceEmail({
      vendor,
      invoice: `INV-2026-${number}`,
      po: `PO-${randomInt(80000, 99999)}`,
      issued: `2026-11-${String(randomInt(1, 20)).padStart(2, '0')}`,
      due: `2026-12-${String(randomInt(1, 28)).padStart(2, '0')}`,
      base: randomInt(800, 9000) + randomInt(0, 100) / 100,
      extra: randomInt(50, 600),
    }),
  };
}

/**
 * Queues `count` tasks at once, spread over the three workers. Each task starts its own trace
 * (linked to the burst's span), so the trace list shows one trace per task like real traffic.
 * @param {number} count
 */
export function simulateBurst(count) {
  return tracer.startActiveSpan('simulate burst', { attributes: { 'taskqueue.burst.size': count } }, async (span) => {
    try {
      const offset = burstCount++ % 8;
      const link = { context: span.spanContext() };
      const tasks = [];
      for (let index = 0; index < count; index += 1) {
        const document = burstDocument(index, offset);
        tasks.push(await context.with(trace.deleteSpan(context.active()), () => submitTask(document, { source: 'burst', links: [link] })));
      }

      /** @type {Record<string, number>} */
      const byType = {};
      for (const task of tasks) byType[task.type] = (byType[task.type] ?? 0) + 1;
      const summary = {
        submitted: tasks.length,
        byType,
        expectedFailures: Array.from({ length: count }, (_, i) => i).filter((i) => isPoison(i, offset)).length,
        taskIds: tasks.map((task) => task.id),
      };
      span.setAttributes({ 'taskqueue.burst.expected_failures': summary.expectedFailures });
      logger.info(
        { submitted: summary.submitted, byType, expectedFailures: summary.expectedFailures },
        `Simulated a burst of ${count} tasks (${Object.entries(byType).map(([type, n]) => `${n} ${type}`).join(', ')}); ${summary.expectedFailures} are expected to fail`,
      );
      return summary;
    } finally {
      span.end();
    }
  });
}
