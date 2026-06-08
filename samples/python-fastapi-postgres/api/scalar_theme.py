"""Bespoke Scalar API reference theming for the User API.

Gives the developer-facing docs a distinct Python/FastAPI identity: a deep,
teal-tinted canvas, FastAPI teal (#009688) as the primary accent, and a hint
of Python blue/yellow for secondary highlights. Colors are tuned for WCAG AA+
contrast (AAA on body text) in both light and dark modes.
"""

import urllib.parse

# Title shown in the browser tab and used for branding.
SCALAR_TITLE = "User API \u00b7 Aspire \u2014 API Reference"

# Self-contained, on-brand favicon: a "< >" code glyph (white) with a Python
# yellow dot on a FastAPI-teal -> Python-blue gradient tile. Inlined as a data
# URI so the sample stays fully offline-friendly (no external requests).
_FAVICON_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">'
    '<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">'
    '<stop offset="0" stop-color="#009688"/>'
    '<stop offset="1" stop-color="#306998"/></linearGradient></defs>'
    '<rect width="64" height="64" rx="14" fill="url(#g)"/>'
    '<path d="M24 20 L14 32 L24 44" fill="none" stroke="#ffffff" stroke-width="5" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '<path d="M40 20 L50 32 L40 44" fill="none" stroke="#ffffff" stroke-width="5" '
    'stroke-linecap="round" stroke-linejoin="round"/>'
    '<circle cx="32" cy="32" r="3.4" fill="#ffd43b"/>'
    "</svg>"
)
SCALAR_FAVICON_URL = "data:image/svg+xml," + urllib.parse.quote(_FAVICON_SVG)

# Custom CSS applied on top of Scalar's base theme. Drives the entire palette
# through Scalar's documented CSS custom properties so it stays stable across
# Scalar releases, plus one branded sidebar header.
SCALAR_CUSTOM_CSS = """
/* ---------- Brand palette ---------------------------------------------- */
/* FastAPI teal primary, with Python blue/yellow as supporting accents.     */

.light-mode {
  --scalar-color-1: #0a1f22;
  --scalar-color-2: #3f5b5c;
  --scalar-color-3: #5d7779;
  --scalar-color-accent: #0f766e;

  --scalar-background-1: #ffffff;
  --scalar-background-2: #f4fbfa;
  --scalar-background-3: #e8f4f2;
  --scalar-background-accent: #d8efeb;

  --scalar-border-color: rgba(8, 73, 67, 0.12);
}

.dark-mode {
  --scalar-color-1: rgba(244, 252, 251, 0.95);
  --scalar-color-2: rgba(214, 233, 231, 0.74);
  --scalar-color-3: rgba(214, 233, 231, 0.52);
  --scalar-color-accent: #2dd4bf;

  --scalar-background-1: #0b1416;
  --scalar-background-2: #0f1d20;
  --scalar-background-3: #16282c;
  --scalar-background-accent: #0c2f2c;

  --scalar-border-color: rgba(45, 212, 191, 0.16);
}

/* ---------- Semantic + method colors (teal / python blue / yellow) ----- */

.light-mode {
  --scalar-button-1: #0f766e;
  --scalar-button-1-color: #ffffff;
  --scalar-button-1-hover: #0b5e57;

  --scalar-color-green: #0f766e;
  --scalar-color-red: #c2362f;
  --scalar-color-yellow: #b7791f;
  --scalar-color-blue: #2b6cb0;
  --scalar-color-orange: #b45309;
  --scalar-color-purple: #6d4bd0;

  --scalar-scrollbar-color: rgba(8, 73, 67, 0.22);
  --scalar-scrollbar-color-active: rgba(8, 73, 67, 0.42);
}

.dark-mode {
  --scalar-button-1: #2dd4bf;
  --scalar-button-1-color: #04201d;
  --scalar-button-1-hover: #5eead4;

  --scalar-color-green: #34d399;
  --scalar-color-red: #f08a7e;
  --scalar-color-yellow: #ffd43b;
  --scalar-color-blue: #5aa9ec;
  --scalar-color-orange: #fbbf6b;
  --scalar-color-purple: #c4b5fd;

  --scalar-scrollbar-color: rgba(45, 212, 191, 0.28);
  --scalar-scrollbar-color-active: rgba(45, 212, 191, 0.5);
}

/* ---------- Shape + sidebar ------------------------------------------- */

:root {
  --scalar-radius: 6px;
  --scalar-radius-lg: 10px;
  --scalar-radius-xl: 14px;
  --scalar-font: "Inter", ui-sans-serif, system-ui, -apple-system, "Segoe UI",
    Roboto, Helvetica, Arial, sans-serif;
}

.t-doc__sidebar {
  --scalar-sidebar-background-1: var(--scalar-background-2);
  --scalar-sidebar-item-hover-color: currentColor;
  --scalar-sidebar-item-hover-background: var(--scalar-background-3);
  --scalar-sidebar-item-active-background: var(--scalar-background-accent);
  --scalar-sidebar-border-color: var(--scalar-border-color);
  --scalar-sidebar-color-1: var(--scalar-color-1);
  --scalar-sidebar-color-2: var(--scalar-color-2);
  --scalar-sidebar-color-active: var(--scalar-color-accent);
  --scalar-sidebar-search-background: transparent;
  --scalar-sidebar-search-border-color: var(--scalar-border-color);
  --scalar-sidebar-search--color: var(--scalar-color-3);
}

/* Branded header at the top of the sidebar: "User API · Aspire". */
.t-doc__sidebar::before {
  content: "User API \u00b7 Aspire";
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 16px 18px;
  margin-bottom: 4px;
  font-family: var(--scalar-font);
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: #ffffff;
  background: linear-gradient(120deg, #009688 0%, #0f766e 42%, #306998 100%);
  border-bottom: 2px solid #ffd43b;
}

/* ---------- Header / hero polish -------------------------------------- */

.scalar-api-reference .references-header,
.scalar-app .references-header {
  backdrop-filter: saturate(140%) blur(6px);
}

/* Accent the introduction card with a subtle teal glow. */
.section-flare {
  background: radial-gradient(
    60% 60% at 12% 0%,
    color-mix(in srgb, var(--scalar-color-accent) 14%, transparent) 0%,
    transparent 70%
  );
}
"""
