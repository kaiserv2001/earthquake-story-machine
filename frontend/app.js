// Earthquake Story Machine — single-page story card browser.
// Vanilla ES module. No framework, no build step. Served as-is by Azure Static Web Apps.
//
// Data flow:
//   #/            -> list view   (GET /api/cards)
//   #/map         -> map view    (GET /api/cards, then GET /api/cards/{id} for coords)
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
// Returns the node so list and map views can share one consistent empty state.
function buildEmptyState(detail) {
  const wrap = el('div', { className: 'state state--empty' });
  wrap.append(
    el('div', { className: 'state__icon', text: '🌍', attrs: { 'aria-hidden': 'true' } }),
    el('h2', { text: 'No story cards yet' }),
    el('p', {
      className: 'state__detail',
      text:
        detail ||
        'The machine is listening for earthquakes. Cards appear here as quakes are detected and enriched.',
    }),
  );
  return wrap;
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

// --- View toggle (List ⇄ Map) ------------------------------------------------

// Segmented control linking the list and map views. `active` is 'list' | 'map'.
// Plain anchors so the router (hashchange) drives navigation — no JS handlers.
function buildViewToggle(active) {
  const toggle = el('nav', { className: 'view-toggle', attrs: { 'aria-label': 'View' } });
  const options = [
    ['list', '#/', 'List'],
    ['map', '#/map', 'Map'],
  ];
  for (const [key, href, label] of options) {
    const isActive = key === active;
    const link = el('a', {
      className: `view-toggle__btn${isActive ? ' view-toggle__btn--active' : ''}`,
      text: label,
      attrs: { href, 'aria-current': isActive ? 'page' : null },
    });
    toggle.append(link);
  }
  return toggle;
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

  clear(app);
  app.append(buildViewToggle('list'));

  if (!Array.isArray(cards) || cards.length === 0) {
    app.append(buildEmptyState());
    return;
  }

  const grid = el('div', { className: 'card-grid' });
  for (const summary of cards) {
    grid.append(buildCardTile(summary));
  }
  app.append(grid);
}

// --- Map view ----------------------------------------------------------------
// Plots story-card quakes on a Leaflet map (CDN, no build step). The list endpoint
// (GET /api/cards) carries no coordinates per the FINAL contract, so the map reads
// lat/lon from each card's detail (GET /api/cards/{id}, quake.latitude/longitude),
// fetched with bounded concurrency. Cards whose detail fails or lacks finite coords
// are silently excluded — a missing coordinate degrades the map, never crashes it.

const MAP_CONCURRENCY = 6;

// Carto dark-matter raster tiles — free, on-theme for the seismic dark UI. Both
// CARTO and OpenStreetMap attribution are required and rendered by Leaflet.
const TILE_URL = 'https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png';
const TILE_ATTRIBUTION =
  '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>';

// Tier colors mirror the CSS magnitude scale (kept in sync with styles.css tokens).
const TIER_COLOR = {
  major: '#f85149',
  strong: '#f0883e',
  moderate: '#d29922',
  minor: '#58a6ff',
};

// Load Leaflet's JS + CSS from the CDN exactly once. Resolves to the global `L`.
let leafletPromise = null;
function loadLeaflet() {
  if (window.L) return Promise.resolve(window.L);
  if (leafletPromise) return leafletPromise;
  leafletPromise = new Promise((resolve, reject) => {
    const css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
    css.integrity = 'sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=';
    css.crossOrigin = '';
    document.head.append(css);

    const script = document.createElement('script');
    script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
    script.integrity = 'sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=';
    script.crossOrigin = '';
    script.onload = () => (window.L ? resolve(window.L) : reject(new Error('Leaflet loaded but window.L is missing')));
    script.onerror = () => reject(new Error('Failed to load Leaflet from CDN'));
    document.head.append(script);
  });
  return leafletPromise;
}

function hasFiniteCoords(quake) {
  return (
    quake &&
    Number.isFinite(quake.latitude) &&
    Number.isFinite(quake.longitude) &&
    Math.abs(quake.latitude) <= 90 &&
    Math.abs(quake.longitude) <= 180
  );
}

// Resolve each summary to {summary, quake} by fetching its detail card, with a
// concurrency cap. Per-card failures resolve to null (excluded) rather than reject —
// one dead detail must not blank the whole map.
async function fetchCoordsForCards(cards) {
  const results = new Array(cards.length).fill(null);
  let cursor = 0;
  async function worker() {
    while (cursor < cards.length) {
      const index = cursor++;
      const summary = cards[index];
      try {
        const card = await fetchCard(summary.quakeId);
        if (card && hasFiniteCoords(card.quake)) {
          results[index] = { summary, quake: card.quake };
        }
      } catch {
        // swallow — this card is simply absent from the map
      }
    }
  }
  const pool = Array.from({ length: Math.min(MAP_CONCURRENCY, cards.length) }, worker);
  await Promise.all(pool);
  return results.filter(Boolean);
}

// Popup body for a marker. Built as DOM (textContent throughout) then handed to
// Leaflet as an element, so third-party place names never touch innerHTML.
function buildMarkerPopup(summary, quake) {
  const wrap = el('div', { className: 'map-popup' });
  const head = el('div', { className: 'map-popup__head' });
  head.append(magnitudeBadge(quake.magnitude));
  head.append(el('span', { className: 'map-popup__time', text: relativeTime(summary.occurredUtc) }));
  wrap.append(head);

  wrap.append(el('p', { className: 'map-popup__place', text: quake.place || summary.place }));

  const locationParts = [summary.city, summary.country].filter(Boolean);
  if (locationParts.length) {
    wrap.append(el('p', { className: 'map-popup__loc', text: locationParts.join(', ') }));
  }

  wrap.append(
    el('a', {
      className: 'map-popup__link',
      text: 'View story card →',
      attrs: { href: `#/card/${encodeURIComponent(summary.quakeId)}` },
    }),
  );
  return wrap;
}

async function renderMap() {
  renderLoading('Loading quake map…');

  let cards;
  try {
    cards = await fetchCards();
  } catch (err) {
    clear(app);
    app.append(buildViewToggle('map'));
    const banner = el('div', { className: 'state state--error' });
    banner.append(
      el('div', { className: 'state__icon', text: '⚠️', attrs: { 'aria-hidden': 'true' } }),
      el('h2', { text: 'Could not load the map' }),
      el('p', { className: 'state__detail', text: err.message }),
    );
    const retry = el('button', { className: 'btn btn--retry', text: 'Try again' });
    retry.addEventListener('click', renderMap);
    banner.append(retry);
    app.append(banner);
    return;
  }

  // Empty data → consistent empty state (with the toggle, so the user can switch back).
  if (!Array.isArray(cards) || cards.length === 0) {
    clear(app);
    app.append(buildViewToggle('map'));
    app.append(buildEmptyState('No quakes to plot yet. The map fills in as cards are enriched.'));
    return;
  }

  // Load Leaflet and resolve coordinates in parallel.
  let L, plotted;
  try {
    [L, plotted] = await Promise.all([loadLeaflet(), fetchCoordsForCards(cards)]);
  } catch (err) {
    clear(app);
    app.append(buildViewToggle('map'));
    const banner = el('div', { className: 'state state--error' });
    banner.append(
      el('div', { className: 'state__icon', text: '⚠️', attrs: { 'aria-hidden': 'true' } }),
      el('h2', { text: 'Could not load the map' }),
      el('p', { className: 'state__detail', text: err.message }),
    );
    const retry = el('button', { className: 'btn btn--retry', text: 'Try again' });
    retry.addEventListener('click', renderMap);
    app.append(banner);
    return;
  }

  clear(app);
  app.append(buildViewToggle('map'));

  // Status line: how many of the loaded cards had usable coordinates.
  const excluded = cards.length - plotted.length;
  const status = el('p', { className: 'map-status' });
  if (plotted.length === 0) {
    status.textContent = 'None of the current cards have coordinates to plot.';
  } else {
    let text = `Showing ${plotted.length} ${plotted.length === 1 ? 'quake' : 'quakes'}`;
    if (excluded > 0) text += ` · ${excluded} without coordinates not shown`;
    status.textContent = text;
  }
  app.append(status);

  const mapEl = el('div', { className: 'map', attrs: { id: 'map', role: 'application', 'aria-label': 'Map of recent earthquakes' } });
  app.append(mapEl);

  // No plottable cards → keep the toggle + status; skip the (empty) map canvas.
  if (plotted.length === 0) {
    mapEl.classList.add('map--empty');
    mapEl.append(buildEmptyState('No coordinates available for the current cards.'));
    return;
  }

  const map = L.map(mapEl, { worldCopyJump: true, scrollWheelZoom: true }).setView([20, 0], 2);
  L.tileLayer(TILE_URL, { maxZoom: 19, attribution: TILE_ATTRIBUTION }).addTo(map);

  const latlngs = [];
  for (const { summary, quake } of plotted) {
    const tier = magnitudeTier(quake.magnitude);
    const color = TIER_COLOR[tier] || TIER_COLOR.minor;
    // Radius scales gently with magnitude so bigger quakes read as bigger dots.
    const radius = 5 + Math.max(0, quake.magnitude - 4) * 2.2;
    const marker = L.circleMarker([quake.latitude, quake.longitude], {
      radius,
      color,
      weight: 2,
      fillColor: color,
      fillOpacity: 0.55,
    }).addTo(map);
    marker.bindPopup(buildMarkerPopup(summary, quake), { className: 'map-popup-shell' });
    latlngs.push([quake.latitude, quake.longitude]);
  }

  // Frame all markers. Single marker → a sensible zoom rather than max zoom-in.
  if (latlngs.length === 1) {
    map.setView(latlngs[0], 5);
  } else {
    map.fitBounds(latlngs, { padding: [40, 40], maxZoom: 6 });
  }

  // Leaflet measures the container on init; if it was display:none or just inserted,
  // nudge it to recompute once layout settles.
  requestAnimationFrame(() => map.invalidateSize());
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
  const cardMatch = hash.match(/^#\/card\/(.+)$/);
  if (cardMatch) {
    renderDetail(decodeURIComponent(cardMatch[1]));
  } else if (hash === '#/map') {
    renderMap();
  } else {
    renderList();
  }
}

window.addEventListener('hashchange', router);
window.addEventListener('DOMContentLoaded', router);
// DOMContentLoaded may have already fired by the time this module evaluates.
if (document.readyState !== 'loading') router();
