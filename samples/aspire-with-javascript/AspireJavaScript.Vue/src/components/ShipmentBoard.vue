<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ArrowRight, Clock, PackageCheck, RefreshCw, Route, Tag, TriangleAlert, Truck } from '@lucide/vue'
import type { Component } from 'vue'
import JourneyPanel from './JourneyPanel.vue'
import {
  STATUSES,
  STATUS_LABELS,
  fromNow,
  getJson,
  type ShipmentBoard,
  type ShipmentDetail,
  type ShipmentStatus
} from '@/lib/shipments'
import { traced } from '@/lib/tracing'

const statusIcons: Record<ShipmentStatus, Component> = {
  'label-created': Tag,
  'in-transit': Route,
  'out-for-delivery': Truck,
  delivered: PackageCheck,
  exception: TriangleAlert
}

const board = ref<ShipmentBoard | null>(null)
const filter = ref<ShipmentStatus | null>(null)
const state = ref<'loading' | 'ready' | 'error'>('loading')
const selectedId = ref<string | null>(null)
const detail = ref<ShipmentDetail | null>(null)

const counts = computed(() => Object.fromEntries((board.value?.statusCounts ?? []).map((c) => [c.status, c.count])))
const shipments = computed(() => board.value?.shipments ?? [])

const statusText = computed(() => {
  if (state.value === 'loading') return 'Loading shipments…'
  if (state.value === 'error') return 'Couldn’t reach the shipments API. Try refreshing.'
  const n = shipments.value.length
  return `${n} ${n === 1 ? 'shipment' : 'shipments'}${filter.value ? ` · ${STATUS_LABELS[filter.value]}` : ' on the move'}`
})

async function load() {
  state.value = 'loading'
  const status = filter.value
  try {
    const query = status ? `?status=${encodeURIComponent(status)}` : ''
    board.value = await traced('load shipment board', { 'contoso.shipment.status_filter': status ?? 'all' }, () =>
      getJson<ShipmentBoard>(`api/shipments${query}`)
    )
    state.value = 'ready'
    if (!board.value.shipments.some((s) => s.id === selectedId.value)) {
      select(board.value.shipments[0]?.id ?? null)
    }
  } catch (err) {
    console.error('Failed to load shipments', err)
    state.value = 'error'
  }
}

function setFilter(status: ShipmentStatus | null) {
  if (filter.value === status) return
  filter.value = status
  load()
}

async function select(id: string | null) {
  selectedId.value = id
  if (!id) {
    detail.value = null
    return
  }
  try {
    // "track shipment" -> GET -> GET /api/shipments/{id} -> shipments.track -> carrier.track, as one trace.
    const result = await traced('track shipment', { 'contoso.shipment.id': id }, () =>
      getJson<ShipmentDetail>(`api/shipments/${encodeURIComponent(id)}`)
    )
    if (selectedId.value === id) detail.value = result
  } catch (err) {
    console.error(`Failed to load shipment ${id}`, err)
  }
}

onMounted(load)
</script>

<template>
  <section class="board" aria-labelledby="board-title">
    <div class="board-head">
      <div>
        <p class="eyebrow">Shipment board</p>
        <h2 id="board-title">Every parcel, every stop</h2>
        <p class="status" role="status" aria-live="polite">{{ statusText }}</p>
      </div>
      <button type="button" class="refresh" :disabled="state === 'loading'" @click="load">
        <RefreshCw :size="18" aria-hidden="true" :class="{ spin: state === 'loading' }" />
        Refresh
      </button>
    </div>

    <div class="pills" role="group" aria-label="Filter by status">
      <button type="button" class="pill" :aria-pressed="filter === null" @click="setFilter(null)">
        All <span class="pill-count">{{ board?.total ?? '–' }}</span>
      </button>
      <button
        v-for="status in STATUSES"
        :key="status"
        type="button"
        class="pill"
        :data-status="status"
        :aria-pressed="filter === status"
        @click="setFilter(status)"
      >
        <component :is="statusIcons[status]" :size="15" aria-hidden="true" />
        {{ STATUS_LABELS[status] }}
        <span class="pill-count">{{ counts[status] ?? '–' }}</span>
      </button>
    </div>

    <div class="layout">
      <ul class="cards" aria-label="Shipments">
        <li v-for="s in shipments" :key="s.id">
          <button
            type="button"
            class="card"
            :data-status="s.status"
            :class="{ selected: s.id === selectedId }"
            :aria-pressed="s.id === selectedId"
            @click="select(s.id)"
          >
            <span class="card-top">
              <span class="card-id">{{ s.id }}</span>
              <span class="badge">
                <component :is="statusIcons[s.status]" :size="13" aria-hidden="true" />
                {{ STATUS_LABELS[s.status] }}
              </span>
            </span>
            <span class="card-route">
              {{ s.origin }} <ArrowRight :size="15" aria-label="to" /> {{ s.destination }}
            </span>
            <span
              class="progress"
              role="progressbar"
              aria-label="Route progress"
              :aria-valuenow="s.progress"
              aria-valuemin="0"
              aria-valuemax="100"
            >
              <span class="progress-fill" :style="{ width: `${Math.max(s.progress, 4)}%` }"></span>
            </span>
            <span class="card-meta">
              <span>{{ s.carrier }}</span>
              <span class="card-eta">
                <Clock :size="13" aria-hidden="true" />
                {{ s.status === 'delivered' ? 'Delivered' : 'ETA' }} {{ fromNow(s.eta) }}
              </span>
            </span>
          </button>
        </li>
        <li v-if="state === 'ready' && shipments.length === 0" class="empty">No shipments with this status.</li>
      </ul>

      <aside class="detail" aria-live="polite">
        <JourneyPanel v-if="detail" :detail="detail" />
        <p v-else class="empty">Select a shipment to follow its journey.</p>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.board {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.board-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}
.eyebrow {
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--vue-green-strong);
}
h2 {
  font-size: clamp(1.6rem, 4vw, 2.2rem);
  font-weight: 700;
  letter-spacing: -0.02em;
  line-height: 1.1;
}
.status {
  margin-top: 0.3rem;
  color: var(--muted);
}

.refresh {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.7rem 1.25rem;
  font: inherit;
  font-weight: 700;
  color: var(--on-brand);
  background: var(--vue-green);
  border: 0;
  border-radius: 999px;
  box-shadow: var(--shadow-md);
  cursor: pointer;
}
.refresh:disabled {
  opacity: 0.7;
  cursor: progress;
}
.refresh:focus-visible,
.pill:focus-visible,
.card:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 3px;
}
.spin {
  animation: spin 900ms linear infinite;
}

.pills {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.pill {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.45rem 0.6rem 0.45rem 0.9rem;
  font: inherit;
  font-size: 0.92rem;
  font-weight: 600;
  color: var(--ink);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 999px;
  box-shadow: var(--shadow-sm);
  cursor: pointer;
}
.pill[data-status] {
  color: var(--status-ink);
}
.pill[aria-pressed='true'] {
  color: #fff;
  background: var(--status, var(--slate));
  border-color: transparent;
}
.pill-count {
  display: inline-grid;
  place-items: center;
  min-width: 1.6rem;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--ink);
  background: var(--surface-3);
}
.pill[aria-pressed='true'] .pill-count {
  color: inherit;
  background: rgba(255, 255, 255, 0.22);
}

.layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 25rem);
  gap: 1.5rem;
  align-items: start;
}
.cards {
  list-style: none;
  padding: 0;
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(17rem, 1fr));
  gap: 1rem;
}
.card {
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding: 1rem 1.1rem;
  font: inherit;
  text-align: left;
  color: var(--ink);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-card);
  box-shadow: var(--shadow-sm);
  cursor: pointer;
  transition:
    transform 160ms ease,
    box-shadow 160ms ease;
}
.card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-md);
}
.card.selected {
  border-color: var(--status);
  box-shadow:
    0 0 0 3px var(--status-soft),
    var(--shadow-md);
}
.card-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}
.card-id {
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.badge {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.15rem 0.55rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--status-ink);
  background: var(--status-soft);
}
.card-route {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.3rem;
  font-weight: 600;
}
.progress {
  display: block;
  height: 8px;
  border-radius: 999px;
  background: var(--surface-3);
  overflow: hidden;
}
.progress-fill {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--status-soft-strong), var(--status));
}
.card-meta {
  display: flex;
  justify-content: space-between;
  gap: 0.5rem;
  font-size: 0.85rem;
  color: var(--muted);
}
.card-eta {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
}

.detail {
  position: sticky;
  top: 1rem;
}
.empty {
  padding: 2rem 1rem;
  color: var(--muted);
  text-align: center;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

@media (max-width: 960px) {
  .layout {
    grid-template-columns: 1fr;
  }
  .detail {
    position: static;
  }
}

@media (prefers-reduced-motion: reduce) {
  .card,
  .card:hover {
    transition: none;
    transform: none;
  }
  .spin {
    animation: none;
  }
}
</style>