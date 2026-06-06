<script setup lang="ts">
import { CloudSun, Sparkles } from '@lucide/vue'
import ForecastTable from './components/ForecastTable.vue'
</script>

<template>
  <a class="skip-link" href="#main">Skip to forecast</a>

  <div class="shell">
    <header class="masthead">
      <span class="glyph" aria-hidden="true">
        <CloudSun :size="30" :stroke-width="2.25" />
      </span>
      <div class="wordmark">
        <p class="kicker">
          <Sparkles :size="14" aria-hidden="true" /> Aspire · Vue + Vite
        </p>
        <h1>Aurora Weather</h1>
      </div>
    </header>

    <main id="main" class="main">
      <ForecastTable />
    </main>

    <footer class="colophon">
      <p>Frosted glass over a living sky &mdash; powered by <strong>Aspire</strong>.</p>
    </footer>
  </div>
</template>

<style scoped>
.skip-link {
  position: absolute;
  left: -9999px;
  top: 0;
  z-index: 50;
  background: var(--accent);
  color: var(--accent-ink);
  padding: 0.6rem 1rem;
  border-radius: 0 0 12px 0;
  font-weight: 600;
  text-decoration: none;
}
.skip-link:focus {
  left: 0;
}

.shell {
  max-width: 820px;
  margin: 0 auto;
  padding: clamp(1.75rem, 5vw, 4rem) 1.25rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.75rem;
}

.masthead {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.glyph {
  display: grid;
  place-items: center;
  width: 56px;
  height: 56px;
  border-radius: 18px;
  color: #fff;
  background: linear-gradient(140deg, var(--accent), var(--teal));
  box-shadow: 0 16px 36px -14px var(--glass-shadow);
}

.kicker {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.74rem;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  font-weight: 600;
  color: var(--muted);
}

h1 {
  font-size: clamp(2rem, 6vw, 3rem);
  font-weight: 700;
  letter-spacing: -0.02em;
  line-height: 1.05;
}

.main {
  view-transition-name: forecast-panel;
}

.colophon {
  text-align: center;
  color: var(--muted);
  font-size: 0.9rem;
}
.colophon strong {
  color: var(--ink);
  font-weight: 700;
}
</style>
