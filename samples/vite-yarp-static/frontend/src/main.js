import '@fontsource/press-start-2p';
import './style.css';
import { setupCounter } from './counter.js';

const star = `
  <svg class="marquee__star" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d="M12 2l2.6 6.6L21 11l-6.4 2.4L12 20l-2.6-6.6L3 11l6.4-2.4z" fill="currentColor" />
  </svg>`;

document.querySelector('#app').innerHTML = `
  <a class="skip-link" href="#cabinet">Skip to the game</a>
  <div class="room">
    <header class="marquee" role="banner">
      <span class="marquee__star-wrap" aria-hidden="true">${star}</span>
      <h1 class="marquee__title">Aspire&nbsp;Arcade</h1>
      <span class="marquee__star-wrap" aria-hidden="true">${star}</span>
    </header>

    <main id="cabinet" class="cabinet">
      <div class="screen">
        <div class="screen__scanlines" aria-hidden="true"></div>
        <div class="screen__glow" aria-hidden="true"></div>
        <div class="readout" aria-hidden="true">
          <div class="stat">
            <span class="stat__label">Score</span>
            <span class="stat__value" id="score" data-pop="false">000000</span>
          </div>
          <div class="stat stat--hi">
            <span class="stat__label">Hi-Score</span>
            <span class="stat__value stat__value--hi" id="hiscore">000000</span>
          </div>
        </div>
        <p class="screen__tagline" aria-hidden="true">&#9654;&nbsp;Ready Player One&nbsp;&#9664;</p>
      </div>

      <div class="controls">
        <button id="score-btn" class="arcade-btn" type="button">
          <span class="arcade-btn__cap">Push&nbsp;to&nbsp;score</span>
        </button>
      </div>

      <p id="hint" class="hint">
        Hit the button &mdash; or press <kbd>Space</kbd> &mdash; to rack up points.
        Your best run is kept as the high score.
      </p>

      <p class="sr-only" role="status" aria-live="polite" id="live"></p>
    </main>

    <footer class="credits" role="contentinfo">
      <p>
        Vanilla JavaScript &amp; Vite, served through a YARP reverse proxy in an
        <a href="https://aspire.dev" target="_blank" rel="noopener noreferrer">Aspire</a>
        application.
      </p>
    </footer>
  </div>
`;

setupCounter({
  button: document.querySelector('#score-btn'),
  scoreEl: document.querySelector('#score'),
  hiEl: document.querySelector('#hiscore'),
  liveEl: document.querySelector('#live'),
  screenEl: document.querySelector('.screen'),
});
