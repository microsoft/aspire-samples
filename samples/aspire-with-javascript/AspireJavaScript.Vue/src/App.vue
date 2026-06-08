<script setup lang="ts">
import ForecastCarousel from './components/ForecastCarousel.vue'
</script>

<template>
  <a class="skip-link" href="#main">Skip to forecast</a>

  <div class="shell">
    <header class="masthead">
      <svg class="mark" viewBox="0 0 48 40" role="img" aria-label="Vue">
        <polygon points="0,0 9.5,0 24,25 38.5,0 48,0 24,40" fill="#35495e" />
        <polygon points="9.5,0 18,0 24,10 30,0 38.5,0 24,25" fill="#42b883" />
      </svg>
      <div class="wordmark">
        <p class="kicker">Aspire &middot; Vue + Vite</p>
        <h1>Vue Weather</h1>
      </div>
    </header>

    <main id="main" class="main">
      <ForecastCarousel />
    </main>

    <footer class="colophon">
      <p>Five-day weather forecast demo &middot; powered by <strong>Aspire</strong>.</p>
    </footer>
  </div>
</template>

<style scoped>
.skip-link {
  position: absolute;
  left: -9999px;
  top: 0;
  z-index: 50;
  background: var(--vue-green);
  color: var(--on-brand);
  padding: 0.6rem 1rem;
  border-radius: 0 0 12px 0;
  font-weight: 700;
  text-decoration: none;
}
.skip-link:focus {
  left: 0;
}

.shell {
  max-width: 940px;
  margin: 0 auto;
  padding: clamp(1.75rem, 5vw, 4rem) 1.25rem 3rem;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.masthead {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.mark {
  width: 48px;
  height: 40px;
  filter: drop-shadow(0 8px 16px rgba(53, 73, 94, 0.28));
}

.kicker {
  font-size: 0.74rem;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  font-weight: 700;
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
