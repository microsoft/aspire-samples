<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
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
  CloudOff,
  ChevronLeft,
  ChevronRight,
  RefreshCw
} from '@lucide/vue'
import type { Component } from 'vue'

interface WeatherForecast {
  date: string
  temperatureC: number
  temperatureF: number
  summary: string
}

type Band = 'cold' | 'cool' | 'mild' | 'warm' | 'hot'

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
  Scorching: Flame
}

const iconFor = (summary: string): Component => summaryIcons[summary] ?? CloudSun

const bandFor = (c: number): Band => {
  if (c < -5) return 'cold'
  if (c < 6) return 'cool'
  if (c < 19) return 'mild'
  if (c < 31) return 'warm'
  return 'hot'
}

const dayParts = (iso: string) => {
  const date = new Date(`${iso}T00:00:00`)
  if (Number.isNaN(date.getTime())) {
    return { weekday: iso, weekdayShort: iso, dateLabel: '' }
  }
  return {
    weekday: date.toLocaleDateString(undefined, { weekday: 'long' }),
    weekdayShort: date.toLocaleDateString(undefined, { weekday: 'short' }),
    dateLabel: date.toLocaleDateString(undefined, { month: 'long', day: 'numeric' })
  }
}

const forecasts = ref<WeatherForecast[]>([])
const state = ref<'loading' | 'ready' | 'error'>('loading')
const activeIndex = ref(0)
const liveMessage = ref('')

const track = ref<HTMLElement | null>(null)
let slideEls: HTMLElement[] = []
const setSlideRef = (el: unknown, index: number) => {
  if (el instanceof HTMLElement) {
    slideEls[index] = el
  }
}

const reduceMotion = ref(false)
let motionQuery: MediaQueryList | undefined

const cards = computed(() =>
  forecasts.value.map((forecast, index) => {
    const parts = dayParts(forecast.date)
    return {
      ...forecast,
      index,
      icon: iconFor(forecast.summary),
      band: bandFor(forecast.temperatureC),
      weekday: parts.weekday,
      weekdayShort: parts.weekdayShort,
      dateLabel: parts.dateLabel,
      dotLabel: `${parts.weekday}, ${parts.dateLabel}`,
      slideLabel: `${parts.weekday}, ${parts.dateLabel} — ${index + 1} of ${forecasts.value.length}`
    }
  })
)

const count = computed(() => cards.value.length)
const atStart = computed(() => activeIndex.value <= 0)
const atEnd = computed(() => activeIndex.value >= count.value - 1)

const statusText = computed(() => {
  if (state.value === 'loading') return 'Fetching the latest forecast…'
  if (state.value === 'error') return 'Could not reach the forecast service. Try refreshing.'
  return `Five-day outlook · ${count.value} days`
})

const announce = (index: number) => {
  const card = cards.value[index]
  if (!card) return
  liveMessage.value =
    `Showing ${card.weekday}, ${card.dateLabel}. ${card.summary}, ` +
    `${card.temperatureC} degrees Celsius, ${card.temperatureF} degrees Fahrenheit. ` +
    `Day ${index + 1} of ${count.value}.`
}

const scrollToIndex = (index: number) => {
  const el = slideEls[index]
  const el0 = track.value
  if (!el || !el0) return
  const left = el.offsetLeft - (el0.clientWidth - el.clientWidth) / 2
  el0.scrollTo({ left, behavior: reduceMotion.value ? 'auto' : 'smooth' })
}

const goTo = (index: number, options: { announce?: boolean } = {}) => {
  const clamped = Math.max(0, Math.min(index, count.value - 1))
  activeIndex.value = clamped
  if (options.announce) announce(clamped)
  nextTick(() => scrollToIndex(clamped))
}

const next = () => {
  if (!atEnd.value) goTo(activeIndex.value + 1, { announce: true })
}
const prev = () => {
  if (!atStart.value) goTo(activeIndex.value - 1, { announce: true })
}

const onTrackKeydown = (event: KeyboardEvent) => {
  if (event.target !== track.value) return
  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowDown':
      event.preventDefault()
      next()
      break
    case 'ArrowLeft':
    case 'ArrowUp':
      event.preventDefault()
      prev()
      break
    case 'Home':
      event.preventDefault()
      goTo(0, { announce: true })
      break
    case 'End':
      event.preventDefault()
      goTo(count.value - 1, { announce: true })
      break
  }
}

const nearestIndex = (): number => {
  const el0 = track.value
  if (!el0) return activeIndex.value
  const center = el0.scrollLeft + el0.clientWidth / 2
  let best = activeIndex.value
  let bestDistance = Number.POSITIVE_INFINITY
  slideEls.forEach((el, index) => {
    if (!el) return
    const distance = Math.abs(el.offsetLeft + el.clientWidth / 2 - center)
    if (distance < bestDistance) {
      bestDistance = distance
      best = index
    }
  })
  return best
}

let settleTimer: number | undefined
const onScroll = () => {
  if (settleTimer) window.clearTimeout(settleTimer)
  settleTimer = window.setTimeout(() => {
    const index = nearestIndex()
    if (index !== activeIndex.value) {
      activeIndex.value = index
      announce(index)
    }
  }, 120)
}

const applyForecasts = async (data: WeatherForecast[]) => {
  slideEls = []
  forecasts.value = data
  await nextTick()
}

const load = async () => {
  state.value = 'loading'
  try {
    const response = await fetch('api/weatherforecast')
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const data = (await response.json()) as WeatherForecast[]

    const startViewTransition = (
      document as Document & {
        startViewTransition?: (cb: () => Promise<void> | void) => { finished: Promise<void> }
      }
    ).startViewTransition

    if (startViewTransition && !reduceMotion.value) {
      await startViewTransition.call(document, () => applyForecasts(data)).finished
    } else {
      await applyForecasts(data)
    }

    state.value = 'ready'
    activeIndex.value = Math.min(activeIndex.value, Math.max(0, count.value - 1))
    await nextTick()
    scrollToIndex(activeIndex.value)
  } catch (error) {
    console.error('Failed to load weather forecast', error)
    state.value = 'error'
  }
}

const onMotionChange = (event: MediaQueryListEvent) => {
  reduceMotion.value = event.matches
}

onMounted(() => {
  motionQuery = window.matchMedia('(prefers-reduced-motion: reduce)')
  reduceMotion.value = motionQuery.matches
  motionQuery.addEventListener('change', onMotionChange)
  track.value?.addEventListener('scroll', onScroll, { passive: true })
  load()
})

onBeforeUnmount(() => {
  motionQuery?.removeEventListener('change', onMotionChange)
  track.value?.removeEventListener('scroll', onScroll)
  if (settleTimer) window.clearTimeout(settleTimer)
})
</script>

<template>
  <section class="panel" aria-labelledby="forecast-heading">
    <div class="panel-head">
      <div class="titles">
        <p class="eyebrow">Five-day outlook</p>
        <h2 id="forecast-heading">Weather forecast</h2>
      </div>
      <button type="button" class="refresh" :disabled="state === 'loading'" @click="load">
        <RefreshCw :size="18" aria-hidden="true" :class="{ spin: state === 'loading' }" />
        <span>{{ state === 'loading' ? 'Refreshing' : 'Refresh' }}</span>
      </button>
    </div>

    <p class="status" :class="{ error: state === 'error' }" role="status" aria-live="polite">
      {{ statusText }}
    </p>

    <div class="sr-only" aria-live="polite" aria-atomic="true">{{ liveMessage }}</div>

    <section
      class="carousel"
      role="group"
      aria-roledescription="carousel"
      aria-label="Five-day weather forecast"
    >
      <div class="viewport">
        <div
          ref="track"
          class="track"
          role="group"
          tabindex="0"
          aria-label="Weather forecast cards. Use the left and right arrow keys to browse days."
          @keydown="onTrackKeydown"
        >
          <div class="spacer" aria-hidden="true"></div>

          <template v-if="count">
            <div
              v-for="card in cards"
              :key="card.date"
              :ref="(el) => setSlideRef(el, card.index)"
              class="slide"
              :class="{ active: card.index === activeIndex }"
              role="group"
              aria-roledescription="slide"
              :aria-label="card.slideLabel"
            >
              <article class="card" :data-band="card.band">
                <header class="card-top">
                  <span class="dow">{{ card.weekday }}</span>
                  <span class="date">{{ card.dateLabel }}</span>
                </header>
                <div class="icon-tile" :data-band="card.band" aria-hidden="true">
                  <component :is="card.icon" :size="36" :stroke-width="2" />
                </div>
                <div class="temps">
                  <span class="temp-c"
                    >{{ card.temperatureC }}<span class="deg">°</span
                    ><span class="unit">C</span></span
                  >
                  <span class="temp-f">{{ card.temperatureF }}°F</span>
                </div>
                <p class="summary">{{ card.summary }}</p>
              </article>
            </div>
          </template>

          <div
            v-else
            class="slide"
            role="group"
            aria-roledescription="slide"
            aria-label="Forecast status"
          >
            <article class="card placeholder">
              <div
                class="icon-tile"
                :data-band="state === 'error' ? 'cold' : 'mild'"
                aria-hidden="true"
              >
                <component
                  :is="state === 'error' ? CloudOff : RefreshCw"
                  :size="34"
                  :class="{ spin: state === 'loading' }"
                />
              </div>
              <p class="placeholder-text">
                {{ state === 'error' ? 'Forecast unavailable' : 'Loading the forecast…' }}
              </p>
            </article>
          </div>

          <div class="spacer" aria-hidden="true"></div>
        </div>
      </div>

      <div v-if="count > 1" class="controls">
        <button
          type="button"
          class="nav"
          aria-label="Previous day"
          :aria-disabled="atStart || undefined"
          @click="prev"
        >
          <ChevronLeft :size="22" aria-hidden="true" />
        </button>

        <div class="dots" role="group" aria-label="Choose a forecast day">
          <button
            v-for="card in cards"
            :key="card.date"
            type="button"
            class="dot"
            :class="{ active: card.index === activeIndex }"
            :aria-current="card.index === activeIndex ? 'true' : undefined"
            :aria-label="card.dotLabel"
            @click="goTo(card.index, { announce: true })"
          >
            <span class="dot-day" aria-hidden="true">{{ card.weekdayShort }}</span>
          </button>
        </div>

        <button
          type="button"
          class="nav"
          aria-label="Next day"
          :aria-disabled="atEnd || undefined"
          @click="next"
        >
          <ChevronRight :size="22" aria-hidden="true" />
        </button>
      </div>
    </section>
  </section>
</template>

<style scoped>
.panel {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.panel-head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.eyebrow {
  font-size: 0.72rem;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  font-weight: 700;
  color: var(--vue-green-strong);
}

h2 {
  font-size: clamp(1.5rem, 4vw, 2.1rem);
  font-weight: 700;
  letter-spacing: -0.01em;
}

.refresh {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  font: inherit;
  font-weight: 700;
  color: var(--on-brand);
  background: var(--vue-green);
  border: 1px solid transparent;
  border-radius: 999px;
  padding: 0.55rem 1.1rem;
  cursor: pointer;
  box-shadow: var(--shadow-sm);
  transition:
    transform 160ms ease,
    filter 160ms ease,
    box-shadow 160ms ease;
}
.refresh:hover {
  filter: brightness(1.05);
  transform: translateY(-1px);
  box-shadow: var(--shadow-md);
}
.refresh:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 3px;
}
.refresh:disabled {
  cursor: progress;
  opacity: 0.7;
  transform: none;
}

.status {
  color: var(--muted);
  font-weight: 500;
  min-height: 1.4em;
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

.carousel {
  margin-top: 0.4rem;
  view-transition-name: forecast-carousel;
}

.viewport {
  position: relative;
}

.track {
  --card-w: clamp(15rem, 72vw, 17rem);
  position: relative;
  display: flex;
  gap: 1rem;
  overflow-x: auto;
  scroll-snap-type: x mandatory;
  padding: 1.5rem 0 1.75rem;
  scrollbar-width: none;
  border-radius: var(--radius);
}
.track::-webkit-scrollbar {
  display: none;
}
.track:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 4px;
}

.spacer {
  flex: 0 0 calc((100% - var(--card-w)) / 2);
}

.slide {
  flex: 0 0 var(--card-w);
  scroll-snap-align: center;
  transition:
    transform 280ms ease,
    opacity 280ms ease;
  transform: scale(0.9);
  opacity: 0.55;
}
.slide.active {
  transform: scale(1);
  opacity: 1;
}

.card {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.85rem;
  text-align: center;
  padding: 1.6rem 1.4rem 1.8rem;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-card);
  box-shadow: var(--shadow-md);
  position: relative;
  overflow: hidden;
}
.card::before {
  content: '';
  position: absolute;
  inset: 0 0 auto 0;
  height: 6px;
  background: var(--band-gradient, linear-gradient(90deg, var(--vue-green), var(--aspire)));
}
.slide.active .card {
  box-shadow: var(--shadow-lg);
  border-color: color-mix(in srgb, var(--vue-green) 45%, var(--border));
}

.card-top {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  margin-top: 0.3rem;
}
.dow {
  font-size: 1.1rem;
  font-weight: 700;
  letter-spacing: -0.01em;
  color: var(--ink);
}
.date {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--muted);
  font-variant-numeric: tabular-nums;
}

.icon-tile {
  display: grid;
  place-items: center;
  width: 76px;
  height: 76px;
  border-radius: 20px;
  color: #ffffff;
  box-shadow: var(--shadow-sm);
}

.temps {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.1rem;
}
.temp-c {
  font-size: 2.6rem;
  line-height: 1;
  font-weight: 700;
  letter-spacing: -0.02em;
  font-variant-numeric: tabular-nums;
  color: var(--ink);
}
.temp-c .deg {
  font-weight: 600;
}
.temp-c .unit {
  font-size: 1.3rem;
  font-weight: 600;
  margin-left: 0.1rem;
  color: var(--muted);
}
.temp-f {
  font-size: 0.92rem;
  font-weight: 600;
  color: var(--muted);
  font-variant-numeric: tabular-nums;
}

.summary {
  display: inline-flex;
  align-items: center;
  padding: 0.35rem 0.85rem;
  border-radius: 999px;
  background: var(--surface-3);
  color: var(--ink);
  font-weight: 600;
  font-size: 0.95rem;
}

.placeholder {
  justify-content: center;
  gap: 1.1rem;
  min-height: 16rem;
}
.placeholder-text {
  color: var(--muted);
  font-weight: 600;
}

/* Temperature bands */
.icon-tile[data-band='cold'] {
  background: linear-gradient(140deg, #5a8dee, #3b6fd4);
}
.icon-tile[data-band='cool'] {
  background: linear-gradient(140deg, #2bb7c4, #1f93a8);
}
.icon-tile[data-band='mild'] {
  background: linear-gradient(140deg, #4fd09a, #2f9d6e);
}
.icon-tile[data-band='warm'] {
  background: linear-gradient(140deg, #f4b14a, #e2851a);
}
.icon-tile[data-band='hot'] {
  background: linear-gradient(140deg, #f0779b, #d6486f);
}
.card[data-band='cold'] {
  --band-gradient: linear-gradient(90deg, #5a8dee, #3b6fd4);
}
.card[data-band='cool'] {
  --band-gradient: linear-gradient(90deg, #2bb7c4, #1f93a8);
}
.card[data-band='mild'] {
  --band-gradient: linear-gradient(90deg, #4fd09a, #2f9d6e);
}
.card[data-band='warm'] {
  --band-gradient: linear-gradient(90deg, #f4b14a, #e2851a);
}
.card[data-band='hot'] {
  --band-gradient: linear-gradient(90deg, #f0779b, #d6486f);
}

.controls {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.85rem;
}

.nav {
  display: grid;
  place-items: center;
  width: 46px;
  height: 46px;
  border-radius: 999px;
  border: 1px solid var(--border-strong);
  background: var(--surface);
  color: var(--ink);
  cursor: pointer;
  box-shadow: var(--shadow-sm);
  transition:
    transform 160ms ease,
    background 160ms ease,
    color 160ms ease;
}
.nav:hover {
  background: var(--vue-green);
  color: var(--on-brand);
  border-color: transparent;
  transform: translateY(-1px);
}
.nav:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 3px;
}
.nav[aria-disabled='true'] {
  opacity: 0.4;
  cursor: not-allowed;
  box-shadow: none;
}
.nav[aria-disabled='true']:hover {
  background: var(--surface);
  color: var(--ink);
  border-color: var(--border-strong);
  transform: none;
}

.dots {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}
.dot {
  display: grid;
  place-items: center;
  min-width: 2.4rem;
  height: 2.1rem;
  padding: 0 0.5rem;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--surface-2);
  color: var(--muted);
  font: inherit;
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0.02em;
  cursor: pointer;
  transition:
    transform 160ms ease,
    background 160ms ease,
    color 160ms ease,
    border-color 160ms ease;
}
.dot:hover {
  transform: translateY(-1px);
  border-color: var(--border-strong);
  color: var(--ink);
}
.dot:focus-visible {
  outline: 3px solid var(--focus-ring);
  outline-offset: 3px;
}
.dot.active {
  background: var(--vue-green);
  color: var(--on-brand);
  border-color: transparent;
  box-shadow: var(--shadow-sm);
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
  .slide {
    transition: none;
  }
  .refresh,
  .nav,
  .dot {
    transition: none;
  }
  .refresh:hover,
  .nav:hover,
  .dot:hover {
    transform: none;
  }
  .spin {
    animation: none;
  }
}
</style>
