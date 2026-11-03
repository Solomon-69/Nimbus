/* Nimbus engine: state, saving, live updates from Open-Meteo, and the link to the native shell.
   The same file runs inside the Mac shell (WKWebView) and a plain browser (tests). */
(function () {
  const C = window.WxCore;
  const KEY = 'weather.state.v1';
  const OPEN_EVERY = 5 * 60e3, CLOSED_EVERY = 15 * 60e3, RETRY_AFTER = 60e3, STALE_ON_OPEN = 20e3, FAR_STALE = 30 * 60e3, CHUNK = 6;

  const host = (() => {
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.wx) return { kind: 'mac', post: (m) => window.webkit.messageHandlers.wx.postMessage(m) };
    return { kind: 'web', post: () => {} };
  })();

  const defaults = () => ({ settings: { theme: 'auto', unit: 'c', speed: 'kmh', bar: 'both', login: false, sky: 'blue', rain: true, autoUnits: true }, places: [], selected: null, models: {}, seenWelcome: false, alerted: {} });
  function load() {
    let raw = window.__WX_STORE__;
    if (!raw) { try { raw = localStorage.getItem(KEY); } catch (e) { raw = null; } }
    try { const s = JSON.parse(raw); if (s && s.settings) return Object.assign(defaults(), s, { settings: Object.assign(defaults().settings, s.settings) }); } catch (e) { /* first run */ }
    return defaults();
  }

  if (host.kind === 'mac') document.documentElement.classList.add('glass');
  const S = load();
  const R = { loading: false, offline: false, open: false, lastAttempt: 0, lastOk: 0, nextRetry: 0, loc: 'unknown', hostInfo: { canLocate: host.kind !== 'web' || !!navigator.geolocation, canNotify: false, login: false, platform: host.kind } };
  const listeners = new Set();
  const emit = (what) => listeners.forEach((f) => f(what));

  let saveTimer = null;
  function save() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      const json = JSON.stringify(S);
      try { localStorage.setItem(KEY, json); } catch (e) { /* private mode */ }
      host.post({ a: 'save', json });
    }, 200);
  }

  const sameSpot = (a, b) => a.lat != null && b.lat != null && Math.abs(a.lat - b.lat) < 0.15 && Math.abs(a.lon - b.lon) < 0.15 && String(a.name).toLowerCase() === String(b.name).toLowerCase();
  /* One entry per real place: a saved city that is also "my location" folds into the location entry. */
  function dedupe() {
    const here = S.places.find((p) => p.current);
    if (!here) return;
    const drop = S.places.filter((p) => p !== here && sameSpot(p, here));
    if (!drop.length) return;
    S.places = S.places.filter((p) => !drop.includes(p));
    drop.forEach((p) => { delete S.models[p.id]; if (S.selected === p.id) S.selected = here.id; });
  }

  const primary = () => S.places.find((p) => p.current) || S.places[0] || null;
  dedupe();
  const selectedPlace = () => S.places.find((p) => p.id === S.selected) || primary();

  function pushBar() {
    const p = primary(), m = p && S.models[p.id];
    const s = S.settings;
    if (!m) { host.post({ a: 'bar', text: '', kind: 'cloud', style: s.bar, theme: s.theme, empty: true }); return; }
    const d = window.wxDescribe(m.now.code, m.now.isDay);
    host.post({ a: 'bar', text: C.temp(m.now.temp, s.unit) + (s.unit === 'f' ? 'F' : 'C'), kind: d.kind, style: s.bar, theme: s.theme, tip: `${p.name}: ${C.temp(m.now.temp, s.unit)}, ${d.text}` });
  }

  const withTimeout = (url, ms) => {
    const ctl = new AbortController(), t = setTimeout(() => ctl.abort(), ms);
    return fetch(url, { signal: ctl.signal, cache: 'no-store' }).then((r) => { clearTimeout(t); if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); }, (e) => { clearTimeout(t); throw e; });
  };

  async function fetchPlace(p) {
    const [raw, air] = await Promise.all([withTimeout(C.forecastUrl(p.lat, p.lon), 12000), withTimeout(C.airUrl(p.lat, p.lon), 12000).catch(() => null)]);
    S.models[p.id] = C.normalize(raw, air, p, Date.now());
  }

  const wet = (c) => (c >= 51 && c <= 67) || (c >= 80 && c <= 82) || c >= 95;
  /* Rain alert: dry now, rain likely within the next three hours, at most one alert per three hours. */
  function checkRain() {
    if (!S.settings.rain || !R.hostInfo.canNotify) return;
    const p = primary(), m = p && S.models[p.id];
    if (!m || wet(m.now.code) || m.now.precip > 0) return;
    const soon = m.hours.slice(1, 4).find((h) => wet(h.code) && (h.pop || 0) >= 55);
    if (!soon) return;
    const key = p.id + ':' + soon.time.slice(0, 13), now = Date.now(), seen = S.alerted || {};
    const last = Math.max(0, ...Object.values(seen));
    if (seen[key] || now - last < 3 * 3600e3) return;
    const keep = Object.entries(seen).sort((a, b) => b[1] - a[1]).slice(0, 9);
    S.alerted = Object.fromEntries(keep.concat([[key, now]]));
    save();
    host.post({ a: 'notify', title: 'Rain soon', body: `Rain is expected around ${String(soon.hour).padStart(2, '0')}:00 in ${p.name}.` });
  }

  async function refresh(why) {
    if (R.loading) return;
    // Always the main place and the one on screen; any other saved place only when its forecast is getting old.
    const keep = new Set([(primary() || {}).id, S.selected]);
    const places = S.places.filter((p) => p.lat != null && (keep.has(p.id) || !S.models[p.id] || Date.now() - (S.models[p.id].updated || 0) > FAR_STALE));
    if (!places.length) return;
    R.loading = true; R.lastAttempt = Date.now(); emit('status');
    const results = [];
    for (let i = 0; i < places.length; i += CHUNK) results.push(...await Promise.allSettled(places.slice(i, i + CHUNK).map(fetchPlace)));
    const ok = results.some((r) => r.status === 'fulfilled');
    R.loading = false;
    R.offline = !ok;
    if (ok) { R.lastOk = Date.now(); R.nextRetry = 0; } else { R.nextRetry = Date.now() + RETRY_AFTER; }
    save(); pushBar(); checkRain(); emit('data'); emit('status');
  }

  function due() {
    const now = Date.now();
    if (R.loading || !S.places.length) return false;
    if (R.nextRetry && now < R.nextRetry) return false;
    return now - R.lastAttempt >= (R.open ? OPEN_EVERY : CLOSED_EVERY);
  }

  /* Same idea as Ludus: the panel keeps its last picture while closed, so the page is hidden then and only
     shown again (a quick fade) once the fresh screen has really been drawn, two frames after opening. */
  let revealSeq = 0;
  function reveal() {
    const root = document.documentElement, my = ++revealSeq;
    const show = () => { if (my === revealSeq) root.classList.remove('vacant'); };
    requestAnimationFrame(() => requestAnimationFrame(show));
    setTimeout(show, 300);   // never stay hidden
  }

  const api = {
    host, S, R, subscribe: (f) => { listeners.add(f); return () => listeners.delete(f); },
    primary, selectedPlace, checkRain,
    refresh, tick() { if (due()) refresh('timer'); emit('clock'); },
    opened() { R.open = true; reveal(); if (Date.now() - R.lastAttempt > STALE_ON_OPEN) refresh('open'); emit('opened'); },
    closed() { R.open = false; document.documentElement.classList.add('vacant'); emit('closed'); },
    wake() { R.nextRetry = 0; refresh('wake'); },
    setSettings(patch) {
      if ('unit' in patch || 'speed' in patch) patch = Object.assign({ autoUnits: false }, patch);
      Object.assign(S.settings, patch); save(); pushBar(); emit('settings');
      if ('login' in patch) host.post({ a: 'login', on: !!patch.login });
      if (patch.rain) host.post({ a: 'notifyPermission' });
    },
    select(id) { if (S.places.some((p) => p.id === id) && S.selected !== id) { S.selected = id; save(); emit('select'); const m = S.models[id]; if (!m || Date.now() - m.updated > 5 * 60e3) refresh('select'); } },
    addPlace(r) {
      const id = 'p' + r.id;
      if (S.places.some((p) => p.id === id)) { api.select(id); return true; }
      const twin = S.places.find((p) => sameSpot(p, { name: r.name, lat: r.latitude, lon: r.longitude }));
      if (twin) { api.select(twin.id); return true; }
      const sub = [r.admin1, r.country].filter(Boolean).join(', ');
      S.places.push({ id, name: r.name, sub, lat: r.latitude, lon: r.longitude });
      S.selected = id; save(); emit('places'); refresh('add');
      return true;
    },
    removePlace(id) {
      const p = S.places.find((x) => x.id === id);
      if (!p) return;
      S.places = S.places.filter((x) => x.id !== id); delete S.models[id];
      if (S.selected === id) S.selected = primary() ? primary().id : null;
      save(); pushBar(); emit('places');
    },
    async search(q) {
      const r = await withTimeout(C.geoUrl(q), 8000);
      return (r.results || []).map((x) => ({ id: x.id, name: x.name, admin1: x.admin1, country: x.country, latitude: x.latitude, longitude: x.longitude }));
    },
    locate() {
      R.loc = 'waiting'; emit('loc');
      if (host.kind === 'web' && navigator.geolocation) {
        navigator.geolocation.getCurrentPosition((g) => window.wx.location({ ok: true, lat: g.coords.latitude, lon: g.coords.longitude, name: 'Current location' }), () => window.wx.location({ ok: false, reason: 'denied' }), { timeout: 10000 });
      } else host.post({ a: 'locate' });
      setTimeout(() => { if (R.loc === 'waiting') { R.loc = 'failed'; emit('loc'); } }, 25000);
    },
    quit() { host.post({ a: 'quit' }); },
    updCheck() { host.post({ a: 'updCheck' }); },
    updInstall() { host.post({ a: 'updInstall' }); },
    dismissWelcome() { S.seenWelcome = true; save(); emit('places'); },
  };

  /* calls from the native shell */
  window.wx = {
    opened: () => api.opened(), closed: () => api.closed(), tick: () => api.tick(), wake: () => api.wake(), online: () => { R.nextRetry = 0; refresh('online'); },
    hostInfo(info) {
      Object.assign(R.hostInfo, info);
      if (S.settings.rain && info.canNotify === false && !R.askedNotify) { R.askedNotify = true; host.post({ a: 'notifyPermission' }); }
      if ('login' in info) S.settings.login = !!info.login;
      if (S.settings.autoUnits && 'metric' in info) { S.settings.unit = info.metric ? 'c' : 'f'; S.settings.speed = info.mph ? 'mph' : 'kmh'; save(); pushBar(); emit('settings'); }
      emit('hostInfo');
    },
    location(r) {
      if (!r || !r.ok) { R.loc = r && r.reason === 'denied' ? 'denied' : 'failed'; emit('loc'); return; }
      R.loc = 'ok';
      let p = S.places.find((x) => x.current);
      const sub = r.sub || '';
      if (!p) { p = { id: 'here', name: r.name || 'Current location', sub, lat: r.lat, lon: r.lon, current: true }; S.places.unshift(p); if (!S.selected) S.selected = p.id; }
      else { p.lat = r.lat; p.lon = r.lon; p.name = r.name || p.name; p.sub = sub || p.sub; }
      dedupe();
      S.selected = S.selected || p.id; save(); emit('places'); refresh('location');
    },
    update(u) { R.update = u; emit('update'); },
    state: () => JSON.parse(JSON.stringify({ S, R })),
  };
  window.WxEngine = api;

  window.addEventListener('online', () => window.wx.online());
  window.addEventListener('offline', () => { R.offline = true; emit('status'); });
  setInterval(() => api.tick(), 30000);
  if (S.places.length) { pushBar(); setTimeout(() => refresh('start'), 50); }
  host.post({ a: 'ready' });
})();
