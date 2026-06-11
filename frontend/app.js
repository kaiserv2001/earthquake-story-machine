// Earthquake Story Machine — single-page story card browser.
// Vanilla ES module. No framework, no build step. Served as-is by Azure Static Web Apps.
//
// Data flow:
//   #/            -> list view   (GET /api/cards)
//   #/card/{id}   -> detail view (GET /api/cards/{id})
//
// All dynamic API text is assigned via textContent / element properties, never
// concatenated into innerHTML — wiki extracts, place names, and photographer
// names are third-party strings.

// --- Configuration -----------------------------------------------------------

// Static Web Apps proxies /api/* to the linked Functions backend. Locally the
// Functions host runs on :7071, so point there when developing off localhost.
const API_BASE =
  location.hostname === 'localhost' || location.hostname === '127.0.0.1'
    ? 'http://localhost:7071'
    : '';

// Develop without a running API by flipping this to true: serves mock-cards.json
// (a fixture matching _workspace/api-contract.md) for both endpoints. Defaults off.
const USE_MOCK = false;

const app = document.getElementById('app');

// --- Magnitude tier (the ONLY place the thresholds live) ---------------------

// Returns the tier key used for the badge color. Thresholds per frontend-spec:
// 4.5–5.4 moderate (amber), 5.5–6.4 strong (orange), 6.5+ major (red).
function magnitudeTier(magnitude) {
  if (magnitude >= 6.5) return 'major';
  if (magnitude >= 5.5) return 'strong';
  if (magnitude >= 4.5) return 'moderate';
  return 'minor';
}

// --- Date / number formatting ------------------------------------------------

const relTimeFmt = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
const localDateFmt = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
});
const utcDateFmt = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'UTC',
});

// Compact relative time ("3 h ago") from an ISO string. Walks units largest-first.
function relativeTime(isoString) {
  const then = new Date(isoString);
  if (isNaN(then)) return '';
  const seconds = (then.getTime() - Date.now()) / 1000;
  const units = [
    ['year', 60 * 60 * 24 * 365],
    ['month', 60 * 60 * 24 * 30],
    ['day', 60 * 60 * 24],
    ['hour', 60 * 60],
    ['minute', 60],
    ['second', 1],
  ];
  for (const [unit, secondsPerUnit] of units) {
    if (Math.abs(seconds) >= secondsPerUnit || unit === 'second') {
      return relTimeFmt.format(Math.round(seconds / secondsPerUnit), unit);
    }
  }
  return '';
}

function formatNumber(value, digits = 1) {
  return Number(value).toLocaleString(undefined, {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
}

// --- Small DOM helpers -------------------------------------------------------

// Create an element with optional class, text, and attributes. text goes through
// textContent, so it is always safe for third-party strings.
function el(tag, { className, text, attrs } = {}) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text != null) node.textContent = text;
  if (attrs) {
    for (const [key, val] of Object.entries(attrs)) {
      if (val != null) node.setAttribute(key, val);
    }
  }
  return node;
}

function clear(node) {
  node.replaceChildren();
}

function magnitudeBadge(magnitude) {
  const badge = el('span', {
    className: `mag-badge mag-badge--${magnitudeTier(magnitude)}`,
    text: `M ${formatNumber(magnitude, 1)}`,
  });
  return badge;
}

// --- Data fetching -----------------------------------------------------------

async function fetchCards() {
  if (USE_MOCK) {
    const res = await fetch('mock-cards.json');
    if (!res.ok) throw new Error(`mock fixture failed: ${res.status}`);
    const data = await res.json();
    return data.cards;
  }
  const res = await fetch(`${API_BASE}/api/cards`);
  if (!res.ok) throw new Error(`/api/cards returned ${res.status}`);
  return res.json();
}

async function fetchCard(quakeId) {
  if (USE_MOCK) {
    const res = await fetch('mock-cards.json');
    if (!res.ok) throw new Error(`mock fixture failed: ${res.status}`);
    const data = await res.json();
    const card = data.details[quakeId];
    if (!card) {
      const err = new Error('not found in fixture');
      err.notFound = true;
      throw err;
    }
    return card;
  }
  const res = await fetch(`${API_BASE}/api/cards/${encodeURIComponent(quakeId)}`);
  if (res.status === 404) {
    const err = new Error('card not found');
    err.notFound = true;
    throw err;
  }
  if (!res.ok) throw new Error(`/api/cards/${quakeId} returned ${res.status}`);
  return res.json();
}

// --- Shared state views ------------------------------------------------------

function renderLoading(label) {
  clear(app);
  const wrap = el('div', { className: 'state state--loading' });
  wrap.append(
    el('div', { className: 'spinner', attrs: { 'aria-hidden': 'true' } }),
    el('p', { text: label || 'Loading…' }),
  );
  app.append(wrap);
}

// Empty state — the first thing anyone sees on a fresh deploy. Intentional, not blank.
function renderEmpty() {
  clear(app);
  const wrap = el('div', { className: 'state state--empty' });
  wrap.append(
    el('div', { className: 'state__icon', text: '🌍', attrs: { 'aria-hidden': 'true' } }),
    el('h2', { text: 'No story cards yet' }),
    el('p', {
      className: 'state__detail',
      text: 'The machine is listening for earthquakes. Cards appear here as quakes are detected and enriched.',
    }),
  );
  app.append(wrap);
}

// Failure state — banner with a retry button. Never a blank page.
function renderError(message, onRetry) {
  clear(app);
  const wrap = el('div', { className: 'state state--error' });
  wrap.append(
    el('div', { className: 'state__icon', text: '⚠️', attrs: { 'aria-hidden': 'true' } }),
    el('h2', { text: 'Could not load story cards' }),
    el('p', { className: 'state__detail', text: message }),
  );
  const retry = el('button', { className: 'btn btn--retry', text: 'Try again' });
  retry.addEventListener('click', onRetry);
  wrap.append(retry);
  app.append(wrap);
}

// --- List view ---------------------------------------------------------------

function buildCardTile(summary) {
  const tile = el('a', {
    className: 'card-tile',
    attrs: { href: `#/card/${encodeURIComponent(summary.quakeId)}` },
  });

  const top = el('div', { className: 'card-tile__top' });
  top.append(magnitudeBadge(summary.magnitude));
  top.append(
    el('time', {
      className: 'card-tile__time',
      text: relativeTime(summary.occurredUtc),
      attrs: { datetime: summary.occurredUtc },
    }),
  );
  tile.append(top);

  tile.append(el('h2', { className: 'card-tile__place', text: summary.place }));

  // city / country line — both nullable when geocoding missed (e.g. ocean epicenter).
  const locationParts = [summary.city, summary.country].filter(Boolean);
  tile.append(
    el('p', {
      className: 'card-tile__location',
      text: locationParts.length ? locationParts.join(', ') : 'Location unavailable',
    }),
  );

  return tile;
}

async function renderList() {
  renderLoading('Loading recent quakes…');
  let cards;
  try {
    cards = await fetchCards();
  } catch (err) {
    renderError(err.message, renderList);
    return;
  }

  if (!Array.isArray(cards) || cards.length === 0) {
    renderEmpty();
    return;
  }

  clear(app);
  const grid = el('div', { className: 'card-grid' });
  for (const summary of cards) {
    grid.append(buildCardTile(summary));
  }
  app.append(grid);
}

// --- Detail view -------------------------------------------------------------
// EVERY enrichment section is null-safe and built by its own helper. Any section
// may be null; the card degrades but never breaks. A card with only quake facts
// must still look complete.

function buildHero(photos) {
  if (!Array.isArray(photos) || photos.length === 0) {
    // Quake-themed fallback band so the detail page still has a header presence.
    return el('div', { className: 'hero hero--placeholder', attrs: { 'aria-hidden': 'true' } });
  }
  const photo = photos[0];
  const hero = el('div', { className: 'hero' });
  const img = el('img', {
    className: 'hero__img',
    attrs: { src: photo.imageUrl, alt: '', loading: 'eager' },
  });
  hero.append(img);

  // Unsplash attribution is mandatory: photographer name linking to their profile.
  if (photo.photographerName) {
    const credit = el('div', { className: 'hero__credit' });
    credit.append(document.createTextNode('Photo: '));
    if (photo.photographerUrl) {
      credit.append(
        el('a', {
          text: photo.photographerName,
          attrs: { href: photo.photographerUrl, target: '_blank', rel: 'noopener noreferrer' },
        }),
      );
    } else {
      credit.append(el('span', { text: photo.photographerName }));
    }
    credit.append(document.createTextNode(' / Unsplash'));
    hero.append(credit);
  }
  return hero;
}

function buildTitle(quake) {
  const head = el('div', { className: 'detail__title' });
  head.append(magnitudeBadge(quake.magnitude));
  head.append(el('h1', { className: 'detail__place', text: quake.place }));
  return head;
}

// Fact strip — always renders, sourced entirely from the always-present quake object.
function buildFactStrip(quake) {
  const strip = el('div', { className: 'fact-strip' });

  const facts = [
    ['Depth', `${formatNumber(quake.depthKm, 1)} km`],
    ['Time (UTC)', utcDateFmt.format(new Date(quake.occurredUtc))],
    ['Time (local)', localDateFmt.format(new Date(quake.occurredUtc))],
    ['Coordinates', `${formatNumber(quake.latitude, 3)}, ${formatNumber(quake.longitude, 3)}`],
  ];
  for (const [label, value] of facts) {
    const fact = el('div', { className: 'fact' });
    fact.append(el('span', { className: 'fact__label', text: label }));
    fact.append(el('span', { className: 'fact__value', text: value }));
    strip.append(fact);
  }

  if (quake.url) {
    const link = el('a', {
      className: 'fact__link',
      text: 'USGS event page ↗',
      attrs: { href: quake.url, target: '_blank', rel: 'noopener noreferrer' },
    });
    strip.append(link);
  }
  return strip;
}

// weather chip — null-safe; returns null when the section is absent.
function buildWeather(weather) {
  if (!weather) return null;
  const chip = el('div', { className: 'weather-chip' });
  chip.append(el('span', { className: 'weather-chip__temp', text: `${formatNumber(weather.temperatureC, 1)}°C` }));
  // description is a human label for the WMO code; weatherCode -1 means unknown.
  if (weather.description) {
    chip.append(el('span', { className: 'weather-chip__desc', text: weather.description }));
  }
  chip.append(el('span', { className: 'weather-chip__wind', text: `Wind ${formatNumber(weather.windSpeedKmh, 1)} km/h` }));
  return chip;
}

// wiki extract section — null-safe; skipped when wiki is null or extract is empty.
function buildWiki(wiki) {
  if (!wiki || !wiki.extract) return null;
  const section = el('section', { className: 'detail-section detail-section--wiki' });
  section.append(el('h2', { className: 'detail-section__title', text: wiki.title || 'About this place' }));
  section.append(el('p', { className: 'wiki__extract', text: wiki.extract }));
  if (wiki.pageUrl) {
    section.append(
      el('a', {
        className: 'wiki__more',
        text: 'Read more on Wikipedia ↗',
        attrs: { href: wiki.pageUrl, target: '_blank', rel: 'noopener noreferrer' },
      }),
    );
  }
  return section;
}

// history strip — null-safe; maxMagnitudeLastYear may itself be null.
function buildHistory(history) {
  if (!history) return null;
  const strip = el('div', { className: 'history-strip' });
  const count = history.quakesLast30DaysWithin300Km;
  const quakeWord = count === 1 ? 'quake' : 'quakes';
  strip.append(
    el('span', {
      className: 'history-strip__count',
      text: `${count} ${quakeWord} within 300 km in the last 30 days`,
    }),
  );
  if (history.maxMagnitudeLastYear != null) {
    strip.append(
      el('span', {
        className: 'history-strip__max',
        text: `Strongest in the past year: M ${formatNumber(history.maxMagnitudeLastYear, 1)}`,
      }),
    );
  }
  return strip;
}

// photo thumbnails — each carries its own Unsplash attribution.
function buildThumbnails(photos) {
  if (!Array.isArray(photos) || photos.length <= 1) return null;
  const section = el('section', { className: 'detail-section detail-section--photos' });
  section.append(el('h2', { className: 'detail-section__title', text: 'More photos' }));
  const grid = el('div', { className: 'thumb-grid' });
  for (const photo of photos) {
    const figure = el('figure', { className: 'thumb' });
    figure.append(
      el('img', {
        className: 'thumb__img',
        attrs: { src: photo.thumbUrl || photo.imageUrl, alt: '', loading: 'lazy' },
      }),
    );
    if (photo.photographerName) {
      const cap = el('figcaption', { className: 'thumb__credit' });
      if (photo.photographerUrl) {
        cap.append(
          el('a', {
            text: photo.photographerName,
            attrs: { href: photo.photographerUrl, target: '_blank', rel: 'noopener noreferrer' },
          }),
        );
      } else {
        cap.append(el('span', { text: photo.photographerName }));
      }
      figure.append(cap);
    }
    grid.append(figure);
  }
  section.append(grid);
  return section;
}

function backLink() {
  return el('a', { className: 'back-link', text: '← All quakes', attrs: { href: '#/' } });
}

async function renderDetail(quakeId) {
  renderLoading('Loading story card…');
  let card;
  try {
    card = await fetchCard(quakeId);
  } catch (err) {
    if (err.notFound) {
      clear(app);
      const wrap = el('div', { className: 'state state--empty' });
      wrap.append(
        el('div', { className: 'state__icon', text: '🔍', attrs: { 'aria-hidden': 'true' } }),
        el('h2', { text: 'Story card not found' }),
        el('p', { className: 'state__detail', text: 'This quake has no stored card yet, or the id is unknown.' }),
        backLink(),
      );
      app.append(wrap);
      return;
    }
    renderError(err.message, () => renderDetail(quakeId));
    return;
  }

  clear(app);
  const article = el('article', { className: 'detail' });

  article.append(buildHero(card.photos));

  const body = el('div', { className: 'detail__body' });
  body.append(backLink());
  body.append(buildTitle(card.quake));
  body.append(buildFactStrip(card.quake));

  // Optional sections — appended only when their helper produced content.
  const optional = [
    buildWeather(card.weather),
    buildWiki(card.wiki),
    buildHistory(card.history),
    buildThumbnails(card.photos),
  ];
  for (const section of optional) {
    if (section) body.append(section);
  }

  article.append(body);
  app.append(article);
  window.scrollTo(0, 0);
}

// --- Router ------------------------------------------------------------------

function router() {
  const hash = location.hash || '#/';
  const match = hash.match(/^#\/card\/(.+)$/);
  if (match) {
    renderDetail(decodeURIComponent(match[1]));
  } else {
    renderList();
  }
}

window.addEventListener('hashchange', router);
window.addEventListener('DOMContentLoaded', router);
// DOMContentLoaded may have already fired by the time this module evaluates.
if (document.readyState !== 'loading') router();
