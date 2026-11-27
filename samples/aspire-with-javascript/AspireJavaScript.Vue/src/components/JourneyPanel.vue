<script setup lang="ts">
import { computed } from 'vue'
import { Check, MapPin, Package, PackageCheck, Route, Tag, TriangleAlert, Truck, Weight } from '@lucide/vue'
import { STATUS_LABELS, formatDateTime, fromNow, type ShipmentDetail } from '@/lib/shipments'

const props = defineProps<{ detail: ShipmentDetail }>()

const steps = [
  { label: 'Label created', icon: Tag },
  { label: 'Picked up', icon: Package },
  { label: 'In transit', icon: Route },
  { label: 'Out for delivery', icon: Truck },
  { label: 'Delivered', icon: PackageCheck }
]

const shipment = computed(() => props.detail.shipment)

// Where the journey stepper stops; an exception holds the shipment at the stage it reached.
const current = computed(() => {
  const s = shipment.value
  switch (s.status) {
    case 'label-created':
      return 0
    case 'in-transit':
      return 2
    case 'out-for-delivery':
      return 3
    case 'delivered':
      return 4
    default:
      return s.progress >= 80 ? 3 : s.progress >= 15 ? 2 : 1
  }
})
</script>

<template>
  <article class="journey" :data-status="shipment.status" aria-labelledby="journey-title">
    <header class="journey-head">
      <p class="kicker">Tracking number</p>
      <h2 id="journey-title" class="journey-id">{{ shipment.id }}</h2>
      <p class="journey-route">
        <span>{{ shipment.origin }}</span>
        <span class="arrow" aria-hidden="true">→</span>
        <span>{{ shipment.destination }}</span>
      </p>
      <p class="journey-eta">
        <strong>{{ shipment.status === 'delivered' ? 'Delivered' : 'Arrives' }} {{ fromNow(shipment.eta) }}</strong>
        <span>{{ formatDateTime(shipment.eta) }}</span>
      </p>
    </header>

    <ol class="stepper" :aria-label="`Journey: ${STATUS_LABELS[shipment.status]}`">
      <li
        v-for="(step, i) in steps"
        :key="step.label"
        :class="{
          done: i < current || (i === current && shipment.status === 'delivered'),
          active: i === current,
          halted: i === current && shipment.status === 'exception'
        }"
        :aria-current="i === current ? 'step' : undefined"
      >
        <span class="dot" aria-hidden="true">
          <TriangleAlert v-if="i === current && shipment.status === 'exception'" :size="16" />
          <Check v-else-if="i < current || shipment.status === 'delivered'" :size="16" />
          <component :is="step.icon" v-else :size="16" />
        </span>
        <span class="step-label">{{ step.label }}</span>
      </li>
    </ol>

    <dl class="facts">
      <div>
        <dt><Truck :size="15" aria-hidden="true" /> Carrier</dt>
        <dd>{{ shipment.carrier }}</dd>
      </div>
      <div>
        <dt><Weight :size="15" aria-hidden="true" /> Weight</dt>
        <dd>{{ shipment.weightKg.toFixed(1) }} kg</dd>
      </div>
    </dl>

    <section aria-labelledby="scans-title">
      <h3 id="scans-title" class="scans-title">Scan history</h3>
      <ol class="scans">
        <li v-for="(scan, i) in detail.history" :key="scan.at" :class="{ latest: i === 0 }">
          <p class="scan-what">{{ scan.description }}</p>
          <p class="scan-where">
            <MapPin :size="14" aria-hidden="true" />
            {{ scan.location }} · {{ formatDateTime(scan.at) }}
          </p>
        </li>
      </ol>
    </section>
  </article>
</template>

<style scoped>
.journey {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  padding: 1.5rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-lg);
}

.kicker {
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--muted);
}
.journey-id {
  margin-top: 0.15rem;
  font-size: 1.7rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  font-variant-numeric: tabular-nums;
}
.journey-route {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-top: 0.2rem;
  font-size: 1.05rem;
  font-weight: 600;
}
.arrow {
  color: var(--status);
}
.journey-eta {
  display: flex;
  flex-direction: column;
  margin-top: 0.85rem;
  padding: 0.7rem 0.9rem;
  border-radius: var(--radius-sm);
  background: var(--status-soft);
}
.journey-eta strong {
  color: var(--status-ink);
  font-size: 1.05rem;
}
.journey-eta span {
  color: var(--muted);
  font-size: 0.9rem;
}

/* Journey stepper: done steps in Vue green, the current one in the status colour. */
.stepper {
  list-style: none;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  position: relative;
}
.stepper li {
  position: relative;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.4rem;
  text-align: center;
}
.stepper li + li::before {
  content: '';
  position: absolute;
  top: 16px;
  right: 50%;
  width: 100%;
  height: 3px;
  border-radius: 999px;
  background: var(--border);
  z-index: 0;
}
.stepper li.done + li::before,
.stepper li.done + li.active::before {
  background: var(--vue-green);
}
.dot {
  position: relative;
  z-index: 1;
  display: grid;
  place-items: center;
  width: 34px;
  height: 34px;
  border-radius: 50%;
  color: var(--muted);
  background: var(--surface-2);
  border: 2px solid var(--border-strong);
}
.done .dot {
  color: var(--on-brand);
  background: var(--vue-green);
  border-color: var(--vue-green);
}
.active .dot {
  color: #fff;
  background: var(--status);
  border-color: var(--status);
  box-shadow: 0 0 0 5px var(--status-soft);
}
.step-label {
  font-size: 0.74rem;
  font-weight: 600;
  line-height: 1.2;
  color: var(--muted);
}
.active .step-label {
  color: var(--ink);
  font-weight: 700;
}

.facts {
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 0.75rem;
}
.facts div {
  padding: 0.7rem 0.9rem;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  border: 1px solid var(--border);
}
.facts dt {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--muted);
}
.facts dd {
  margin-top: 0.15rem;
  font-weight: 700;
}

.scans-title {
  margin-bottom: 0.6rem;
  font-size: 1rem;
  font-weight: 700;
}
.scans {
  list-style: none;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.55rem;
}
.scans li {
  padding: 0.55rem 0.8rem;
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.scans li.latest {
  background: var(--status-soft);
  box-shadow: inset 3px 0 0 var(--status);
}
.scan-what {
  font-weight: 600;
}
.scan-where {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.3rem;
  font-size: 0.85rem;
  color: var(--muted);
}

@media (max-width: 480px) {
  .step-label {
    font-size: 0.66rem;
  }
  .facts {
    grid-template-columns: 1fr;
  }
}
</style>