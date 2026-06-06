<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Snowflake,
  Wind,
  CloudSnow,
  Cloud,
  CloudSun,
  Sun,
  SunMedium,
  Thermometer,
  Flame,
  RefreshCw,
} from '@lucide/vue'
import type { Component } from 'vue'

interface WeatherForecast {
  date: string
  temperatureC: number
  temperatureF: number
  summary: string
}

const summaryIcons: Record<string, Component> = {
  Freezing: Snowflake,
  Bracing: Wind,
  Chilly: CloudSnow,
  Cool: Cloud,
  Mild: CloudSun,
  Warm: Sun,
  Balmy: SunMedium,
  Hot: Thermometer,
  Sweltering: Flame,
  Scorching: Flame,
}

const iconFor = (summary: string): Component => summaryIcons[summary] ?? CloudSun

const tempClass = (c: number): string => {
  if (c < -5) return 'cold'
  if (c < 6) return 'cool'
  if (c < 19) return 'mild'
  if (c < 31) return 'warm'
  return 'hot'
}

const forecasts = ref<WeatherForecast[]>([])
const state = ref<'loading' | 'ready' | 'error'>('loading')

const load = async () => {
  state.value = 'loading'
  try {
    const response = await fetch('api/weatherforecast')
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    forecasts.value = (await response.json()) as WeatherForecast[]
    state.value = 'ready'
  } catch (error) {
    console.error('Failed to load weather forecast', error)
    state.value = 'error'
  }
}

onMounted(load)

const statusText = computed(() => {
  if (state.value === 'loading') return 'Gathering the latest skies…'
  if (state.value === 'error') return 'The forecast drifted away. Try refreshing.'
  return `Five-day outlook · ${forecasts.value.length} days`
})
</script>

<template>
  <section class="panel" aria-labelledby="forecast-heading">
    <div class="panel-head">
      <div>
        <p class="eyebrow">Weather, four ways</p>
        <h2 id="forecast-heading">Vue forecast</h2>
      </div>
      <button type="button" class="refresh" :disabled="state === 'loading'" @click="load">
        <RefreshCw :size="18" aria-hidden="true" :class="{ spin: state === 'loading' }" />
        <span>{{ state === 'loading' ? 'Refreshing' : 'Refresh' }}</span>
      </button>
    </div>

    <p class="status" :class="{ error: state === 'error' }" role="status" aria-live="polite">
      {{ statusText }}
    </p>

    <div class="table-wrap">
      <table>
        <caption class="sr-only">Randomly generated weather forecast data, five days ahead.</caption>
        <thead>
          <tr>
            <th scope="col">Date</th>
            <th scope="col">Temp. (&deg;C)</th>
            <th scope="col">Temp. (&deg;F)</th>
            <th scope="col">Summary</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="forecasts.length === 0">
            <td>&mdash;</td>
            <td>&mdash;</td>
            <td>&mdash;</td>
            <td>{{ state === 'loading' ? 'Loading…' : 'No forecasts' }}</td>
          </tr>
          <tr v-for="forecast in forecasts" :key="forecast.date">
            <td class="date">{{ forecast.date }}</td>
            <td>
              <span class="temp">
                <span class="bead" :class="tempClass(forecast.temperatureC)" aria-hidden="true" />
                {{ forecast.temperatureC }}&deg;
              </span>
            </td>
            <td><span class="temp">{{ forecast.temperatureF }}&deg;</span></td>
            <td>
              <span class="summary">
                <span class="summary-icon" :class="tempClass(forecast.temperatureC)">
                  <component :is="iconFor(forecast.summary)" :size="18" :stroke-width="2.25" aria-hidden="true" />
                </span>
                {{ forecast.summary }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.panel {
  background: var(--glass);
  border: 1px solid var(--glass-border);
  border-radius: var(--radius);
  box-shadow: 0 24px 60px -24px var(--glass-shadow);
  -webkit-backdrop-filter: blur(18px) saturate(150%);
  backdrop-filter: blur(18px) saturate(150%);
  overflow: hidden;
}

.panel-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
  padding: 1.4rem 1.6rem 1rem;
}

.eyebrow {
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  font-weight: 600;
  color: var(--accent);
}

h2 {
  font-size: clamp(1.5rem, 4vw, 2rem);
  font-weight: 600;
  letter-spacing: -0.01em;
}

.refresh {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font: inherit;
  font-weight: 600;
  color: var(--accent-ink);
  background: var(--accent);
  border: 1px solid transparent;
  border-radius: 999px;
  padding: 0.55rem 1rem;
  cursor: pointer;
  transition: transform 160ms ease, filter 160ms ease;
}
.refresh:hover {
  filter: brightness(1.06);
  transform: translateY(-1px);
}
.refresh:focus-visible {
  outline: 3px solid var(--accent);
  outline-offset: 3px;
}
.refresh:disabled {
  cursor: progress;
  opacity: 0.75;
}

.status {
  padding: 0 1.6rem 0.6rem;
  color: var(--muted);
  font-weight: 500;
}
.status.error {
  color: #b3203a;
  font-weight: 600;
}
@media (prefers-color-scheme: dark) {
  .status.error {
    color: #ff9bb0;
  }
}

.table-wrap {
  overflow-x: auto;
  padding: 0 0.5rem 0.5rem;
}

table {
  width: 100%;
  border-collapse: collapse;
}

caption.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

th,
td {
  text-align: left;
  padding: 0.85rem 1.1rem;
}

thead th {
  font-size: 0.72rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--muted);
  font-weight: 700;
  border-bottom: 1px solid var(--glass-border);
}

tbody tr {
  transition: background 150ms ease;
}
tbody tr + tr td {
  border-top: 1px solid color-mix(in srgb, var(--glass-border) 60%, transparent);
}
tbody tr:hover {
  background: var(--glass-2);
}

.date {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
  white-space: nowrap;
}

.temp {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}

.bead {
  width: 11px;
  height: 11px;
  border-radius: 999px;
  box-shadow: 0 0 0 3px color-mix(in srgb, currentColor 22%, transparent);
}

.summary {
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  font-weight: 500;
}

.summary-icon {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border-radius: 10px;
  color: #fff;
}

.bead.cold,
.summary-icon.cold {
  background: linear-gradient(135deg, #4f86ff, #2f6bff);
}
.bead.cool,
.summary-icon.cool {
  background: linear-gradient(135deg, #34d6c2, #0f9f8f);
}
.bead.mild,
.summary-icon.mild {
  background: linear-gradient(135deg, #8a78ff, #5b46e5);
}
.bead.warm,
.summary-icon.warm {
  background: linear-gradient(135deg, #ffb74d, #ef8a17);
}
.bead.hot,
.summary-icon.hot {
  background: linear-gradient(135deg, #ff6a8b, #e0315b);
}
.bead.cold {
  color: #2f6bff;
}
.bead.cool {
  color: #0f9f8f;
}
.bead.mild {
  color: #5b46e5;
}
.bead.warm {
  color: #ef8a17;
}
.bead.hot {
  color: #e0315b;
}

.spin {
  animation: spin 900ms linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .refresh {
    transition: none;
  }
  .refresh:hover {
    transform: none;
  }
  .spin {
    animation: none;
  }
}
</style>
