const HI_KEY = 'aspire-arcade-hiscore';
const PAD = 6;

const format = (n) => String(n).padStart(PAD, '0');

function readHiScore() {
  try {
    const raw = window.localStorage.getItem(HI_KEY);
    const value = Number.parseInt(raw ?? '', 10);
    return Number.isFinite(value) && value > 0 ? value : 0;
  } catch {
    return 0;
  }
}

function writeHiScore(value) {
  try {
    window.localStorage.setItem(HI_KEY, String(value));
  } catch {
    // Ignore storage failures (private mode, disabled storage); the run still works.
  }
}

export function setupCounter({ button, scoreEl, hiEl, liveEl, screenEl }) {
  let score = 0;
  let hiScore = readHiScore();

  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

  const render = () => {
    scoreEl.textContent = format(score);
    hiEl.textContent = format(hiScore);
  };

  const pop = () => {
    if (reduceMotion.matches) return;
    scoreEl.dataset.pop = 'true';
    scoreEl.addEventListener(
      'animationend',
      () => {
        scoreEl.dataset.pop = 'false';
      },
      { once: true }
    );
    spawnFloater();
  };

  const spawnFloater = () => {
    if (!screenEl) return;
    const floater = document.createElement('span');
    floater.className = 'floater';
    floater.setAttribute('aria-hidden', 'true');
    floater.textContent = '+1';
    screenEl.appendChild(floater);
    floater.addEventListener('animationend', () => floater.remove(), { once: true });
  };

  const announce = () => {
    const best = score >= hiScore ? ' New high score!' : '';
    liveEl.textContent = `Score ${score} point${score === 1 ? '' : 's'}.${best}`;
  };

  const increment = () => {
    score += 1;
    if (score > hiScore) {
      hiScore = score;
      writeHiScore(hiScore);
    }
    render();
    pop();
    announce();
  };

  button.addEventListener('click', increment);
  render();
}
