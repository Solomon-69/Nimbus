/* Nimbus screens: header, main scroll, Places and Settings sheets. Draws from WxEngine state only. */
(function () {
  const E = window.WxEngine, C = window.WxCore, S = E.S, R = E.R;
  const $ = (s, el) => (el || document).querySelector(s);
  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const reduceMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const IC = {
    pin: '<svg viewBox="0 0 24 24"><path d="M12 21s7-6.2 7-11.2A7 7 0 0 0 5 9.8C5 14.8 12 21 12 21z"/><circle cx="12" cy="10" r="2.4"/></svg>',
    gear: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>',
    back: '<svg viewBox="0 0 24 24"><path d="M15 5l-7 7 7 7"/></svg>',
    feel: '<svg viewBox="0 0 24 24"><path d="M14 14.8V5a2 2 0 0 0-4 0v9.8a4 4 0 1 0 4 0z"/></svg>',
    wind: '<svg viewBox="0 0 24 24"><path d="M3 8h10a2.5 2.5 0 1 0-2.5-2.5M3 12h15a2.5 2.5 0 1 1-2.5 2.5M3 16h7"/></svg>',
    uv: '<svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="4"/><path d="M12 3v2M12 19v2M3 12h2M19 12h2M5.6 5.6 7 7M17 17l1.4 1.4M5.6 18.4 7 17M17 7l1.4-1.4"/></svg>',
    rain: '<svg viewBox="0 0 24 24"><path d="M12 3s6 6.2 6 10.2A6 6 0 0 1 6 13.2C6 9.2 12 3 12 3z"/></svg>',
    sun: '<svg viewBox="0 0 24 24"><path d="M4 18h16M7 18a5 5 0 0 1 10 0M12 6v3M5.6 10.6l1.8 1.8M18.4 10.6l-1.8 1.8"/></svg>',
    air: '<svg viewBox="0 0 24 24"><path d="M4 9c3-3 6 3 9 0s5-1 7 0M4 15c3-3 6 3 9 0s5-1 7 0"/></svg>',
    x: '<svg viewBox="0 0 24 24"><path d="M6 6l12 12M18 6L6 18"/></svg>',
    trash: '<svg viewBox="0 0 24 24"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>',
    arrow: '<svg viewBox="0 0 24 24"><path d="M12 3l7 17-7-4-7 4z"/></svg>',
    search: '<svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="6.5"/><path d="M16 16l4 4"/></svg>',
    moon: '<svg viewBox="0 0 24 24"><path d="M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"/></svg>',
    temp: '<svg viewBox="0 0 24 24"><path d="M14 14.8V5a2 2 0 0 0-4 0v9.8a4 4 0 1 0 4 0z"/></svg>',
    bar: '<svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="6" rx="2"/><path d="M7 7h2M13 7h4"/></svg>',
    power: '<svg viewBox="0 0 24 24"><path d="M12 3v8M6.3 6.8a8 8 0 1 0 11.4 0"/></svg>',
    bell: '<svg viewBox="0 0 24 24"><path d="M6 9a6 6 0 0 1 12 0c0 6 2.5 7.5 2.5 7.5h-17S6 15 6 9zM10 20a2 2 0 0 0 4 0"/></svg>',
    sky: '<svg viewBox="0 0 24 24"><path d="M7 18a4 4 0 0 1-.6-7.96A6 6 0 0 1 18 9.5 3.8 3.8 0 0 1 17.5 18z"/></svg>',
    login: '<svg viewBox="0 0 24 24"><path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4M15 8l4 4-4 4M19 12H9"/></svg>',
  };

  /* ---------- theme + shell ---------- */
  function applyTheme() {
    const t = S.settings.theme;
    if (t === 'auto') document.documentElement.removeAttribute('data-theme'); else document.documentElement.setAttribute('data-theme', t);
    document.documentElement.classList.toggle('reduce', !!S.settings.reduce);
  }
  const app = $('#app');
  app.innerHTML = `
    <div class="hdr">
      <button class="icon-btn" id="b-places" aria-label="Places" title="Places">${IC.pin}</button>
      <div class="title" id="title"></div>
      <button class="icon-btn" id="b-settings" aria-label="Settings" title="Settings">${IC.gear}</button>
    </div>
    <div class="scroll" id="scroll"><div id="main"></div></div>
    <div class="ftr" id="ftr"></div>
    <section class="sheet" id="sh-places" aria-label="Places"></section>
    <section class="sheet" id="sh-settings" aria-label="Settings"></section>`;
  const main = $('#main'), scroll = $('#scroll');

  /* ---------- sky: a soft moving gradient that follows the weather and the time of day ---------- */
  const SKY_HUE = { blue: [212, 1], teal: [186, 1], warm: [18, 1], slate: [214, 0.3] };
  const SKY_BASE = { clear: [[75, 52], [70, 66]], partly: [[60, 54], [55, 68]], cloud: [[22, 55], [20, 68]], rain: [[25, 45], [22, 58]], drizzle: [[25, 48], [22, 60]], thunder: [[20, 34], [18, 46]], snow: [[30, 62], [28, 76]], fog: [[12, 58], [10, 70]] };
  const skyEl = document.createElement('div');
  skyEl.className = 'sky';
  skyEl.innerHTML = '<div class="layer"></div><div class="layer"></div><div class="scene"></div>';
  app.prepend(skyEl);
  const skyLayers = skyEl.querySelectorAll('.layer'), skyScene = $('.scene', skyEl);
  let skyFlip = 1, skyKey = '', sceneKey = '';
  const darkNow = () => S.settings.theme === 'dark' || (S.settings.theme === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  const hsl = (h, sat, l) => `hsl(${h} ${Math.round(Math.min(100, sat))}% ${Math.round(l)}%)`;
  const mins = (hm) => { const p = (hm || '').split(':'); return p.length > 1 ? +p[0] * 60 + +p[1] : null; };
  function skyColors(kind, day, golden) {
    const [hue, sat] = SKY_HUE[S.settings.sky] || SKY_HUE.blue;
    const k = kind.replace('-n', ''), clearish = k === 'clear' || k === 'partly';
    let [t, m] = SKY_BASE[k] || SKY_BASE.cloud;
    if (!day) { t = clearish ? [55, 16] : [30, 18]; m = clearish ? [45, 26] : [26, 28]; }
    const dim = day && darkNow() ? 0.72 : 1;
    const mid = golden && day ? hsl(S.settings.sky === 'warm' ? 30 : 24, 78 * Math.max(sat, 0.6), 66 * dim) : hsl(hue, m[0] * sat, m[1] * dim);
    return [hsl(hue, t[0] * sat, t[1] * dim), mid];
  }
  function sceneHTML(kind, day) {
    const k = kind.replace('-n', ''), clearish = k === 'clear' || k === 'partly';
    const clouds = (n, dark) => Array.from({ length: n }, (_, i) => `<i class="cl${dark ? ' d' : ''}" style="top:${8 + i * 32}px;left:${(i * 70) % 120}px;width:${150 + i * 40}px;height:${46 + i * 10}px;animation-duration:${70 + i * 28}s;animation-delay:-${i * 23}s"></i>`).join('');
    const rain = (n, ms) => Array.from({ length: n }, (_, i) => `<i class="rd" style="left:${(i * 37 + 13) % 100}%;height:${14 + (i % 3) * 5}px;animation-duration:${ms + (i % 5) * 160}ms;animation-delay:-${(i * 131) % 1500}ms"></i>`).join('');
    const snow = (n) => Array.from({ length: n }, (_, i) => `<i class="sf" style="left:${(i * 29 + 7) % 100}%;animation-duration:${4200 + (i % 4) * 700}ms;animation-delay:-${(i * 377) % 4000}ms"></i>`).join('');
    const stars = Array.from({ length: 16 }, (_, i) => `<i class="star" style="left:${(i * 53 + 9) % 96}%;top:${(i * 31 + 5) % 70}%;animation-duration:${1800 + (i % 5) * 600}ms;animation-delay:-${(i * 290) % 2000}ms"></i>`).join('');
    let h = '';
    if (clearish && day) h += '<i class="sun-glow"></i>';
    if (clearish && !day) h += '<i class="moon-glow"></i>' + stars;
    if (k === 'partly') h += clouds(2);
    if (k === 'cloud' || k === 'fog') h += clouds(4, k === 'cloud' && !day);
    if (k === 'rain' || k === 'drizzle') h += clouds(3, true) + rain(k === 'drizzle' ? 14 : 26, k === 'drizzle' ? 1700 : 1000);
    if (k === 'thunder') h += clouds(3, true) + rain(22, 950) + '<i class="flash"></i>';
    if (k === 'snow') h += clouds(3) + snow(22);
    return h;
  }
  function applySky() {
    app.classList.toggle('no-sky', S.settings.sky === 'none');
    const p = E.selectedPlace(), m = p && S.models[p.id];
    let kind = 'partly', day = true, golden = false;
    if (m) {
      const d = window.wxDescribe(m.now.code, m.now.isDay); kind = d.kind; day = !!m.now.isDay;
      const nowM = mins((m.now.time || '').slice(11)), rise = mins(m.now.sunrise), set = mins(m.now.sunset);
      golden = nowM != null && [rise, set].some((x) => x != null && Math.abs(nowM - x) <= 40);
    }
    const [top, mid] = skyColors(kind, day, golden), key = top + mid;
    if (key !== skyKey) {
      skyKey = key; skyFlip ^= 1;
      const next = skyLayers[skyFlip], prev = skyLayers[skyFlip ^ 1];
      next.style.setProperty('--s-top', top); next.style.setProperty('--s-mid', mid);
      next.classList.add('on'); prev.classList.remove('on');
    }
    const sk = kind + (day ? 'd' : 'n');
    if (sk !== sceneKey) { sceneKey = sk; skyScene.innerHTML = sceneHTML(kind, day); }
  }
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { skyKey = ''; applySky(); });

  /* ---------- number roll ---------- */
  let rollRaf = 0;
  function roll(el, from, to, ms) {
    cancelAnimationFrame(rollRaf);
    if (reduceMotion() || S.settings.reduce || from === to) { el.textContent = to + '°'; return; }
    const t0 = performance.now();
    const step = (t) => {
      const k = Math.min(1, (t - t0) / ms), e = 1 - Math.pow(1 - k, 3);
      el.textContent = Math.round(from + (to - from) * e) + '°';
      if (k < 1) rollRaf = requestAnimationFrame(step); else el.textContent = to + '°';
    };
    rollRaf = requestAnimationFrame(step);
  }

  /* ---------- main ---------- */
  let shown = { id: null, temp: null };
  function windText(m) { return C.speed(m.now.wind, S.settings.speed); }

  function welcomeHTML() {
    const denied = R.loc === 'denied', wait = R.loc === 'waiting';
    return `<div class="card welcome" data-i="0">
      <div class="tile">${IC.pin}</div>
      <h2>Weather where you are</h2>
      <p>${denied ? 'Location is switched off for Nimbus. You can turn it on in System Settings, or pick a city instead.' : 'Nimbus can use your location to show the forecast for where you are. It stays on this Mac and is only used to look up the weather.'}</p>
      <div class="stack">
        ${E.R.hostInfo.canLocate ? `<button class="btn" id="w-loc">${wait ? 'Finding you...' : 'Use my location'} <span class="arr">→</span></button>` : ''}
        <button class="btn soft" id="w-city">Pick a city instead</button>
      </div></div>`;
  }

  function spark(m) {
    const t = m.hours.slice(0, 13).map((h) => h.temp), lo = Math.min(...t), hi = Math.max(...t), span = hi - lo || 1;
    const pt = (v, i) => [(i * 100) / Math.max(1, t.length - 1), 6 + (1 - (v - lo) / span) * 24];
    const P = t.map(pt), d = P.map((p, i) => (i ? 'L' : 'M') + p[0].toFixed(1) + ' ' + p[1].toFixed(1)).join(' ');
    const hiI = t.indexOf(hi), loI = t.indexOf(lo);
    return `<svg class="spark" viewBox="-4 0 108 36"><defs><linearGradient id="sg" x1="0" x2="1"><stop offset="0" stop-color="var(--cool)"/><stop offset="1" stop-color="var(--warm)"/></linearGradient><linearGradient id="sf" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="var(--cool)" stop-opacity=".28"/><stop offset="1" stop-color="var(--cool)" stop-opacity="0"/></linearGradient></defs><path d="${d} L100 36 L0 36Z" fill="url(#sf)"/><path d="${d}" fill="none" stroke="url(#sg)" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" vector-effect="non-scaling-stroke"/><circle cx="${P[0][0]}" cy="${P[0][1].toFixed(1)}" r="3" class="sp-now"/></svg>`;
  }

  function mainHTML(p, m) {
    const s = S.settings, now = m.now, d = window.wxDescribe(now.code, now.isDay);
    const rise = mins(now.sunrise), set = mins(now.sunset), nowM = mins((now.time || '').slice(11));
    const dayPct = rise != null && set != null && nowM != null ? Math.round(Math.min(1, Math.max(0, (nowM - rise) / Math.max(1, set - rise))) * 100) : 0;
    const uv = Math.round(now.uv || 0), uvC = uv < 3 ? 'var(--c-ok)' : uv < 6 ? 'var(--c-uv)' : uv < 8 ? 'var(--c-sun)' : 'var(--c-hot)';
    const tiles = [
      ['rain', 'Rain chance', now.pop + '%', now.pop >= 50 ? 'Take an umbrella' : 'Next 3 hours', 'var(--c-rain)', `<div class="mini"><i style="width:${Math.max(4, now.pop)}%"></i></div>`],
      ['wind', 'Wind', windText(m), 'From ' + C.compass(now.windDir), 'var(--c-wind)', `<svg class="wdir" viewBox="0 0 24 24" style="transform:rotate(${Math.round(now.windDir + 180)}deg)"><path d="M12 4v16M6 10l6-6 6 6"/></svg>`],
      ['uv', 'UV index', uv + '', C.uvLabel(now.uv), uvC, `<div class="mini uv"><b style="left:${Math.min(100, (uv / 11) * 100)}%"></b></div>`],
      ['sun', 'Sunrise', now.sunrise, 'Sunset ' + now.sunset, 'var(--c-sun)', `<div class="mini day"><i style="width:${Math.max(4, dayPct)}%"></i></div>`],
    ];
    let i = 1;
    const aq = m.aqi;
    return `
    <div class="card now" data-i="0">
      <div class="now-l"><div class="temp num" id="big">${C.tempNum(now.temp, s.unit)}°</div><div class="small">Feels like ${C.temp(now.feels, s.unit)} · Humidity ${Math.round(now.humidity)}%</div></div>
      <div class="now-m">${spark(m)}<div class="small">Next 12 hours</div></div>
      <div class="now-r">${window.wxIcon(d.kind, 40)}<b>${esc(d.text)}</b><div class="small"><span class="hi-c">H ${C.temp(now.high, s.unit)}</span>  <span class="lo-c">L ${C.temp(now.low, s.unit)}</span></div></div>
    </div>
    <div class="grid">${tiles.map((t) => `<div class="card w" style="--c:${t[4]}" data-i="${i++}"><div class="tile">${IC[t[0]]}</div><div class="wt"><div class="small">${t[1]}</div><div class="v">${esc(t[2])}</div><div class="small">${esc(t[3])}</div>${t[0] === 'wind' ? '' : t[5]}</div>${t[0] === 'wind' ? t[5] : ''}</div>`).join('')}</div>
    ${aq ? `<div class="card aq" data-i="${i++}"><div class="tile ${aq.tone}">${IC.air}</div><div><div class="small">Air quality</div><div class="v num" style="font-size:15px">${esc(aq.label)} <span class="small">${aq.value}</span></div></div><div class="meter"><i class="${aq.tone}" style="width:${Math.min(100, Math.max(6, aq.value))}%"></i></div></div>` : ''}
    <div class="card hours" data-i="${i++}"><div class="hours-in" id="hours">${m.hours.map((h, k) => { const hd = window.wxDescribe(h.code, h.isDay); return `<div class="hr" style="--k:${k}"><div class="t">${C.hourLabel(h.hour, k)}</div>${window.wxIcon(hd.kind, 26)}<div class="d">${C.temp(h.temp, s.unit)}</div><div class="p">${h.pop >= 20 ? h.pop + '%' : ''}</div></div>`; }).join('')}</div></div>
    <div class="card days" data-i="${i++}">${(() => {
      const lo = Math.min(...m.days.map((x) => x.min)), hi = Math.max(...m.days.map((x) => x.max)), span = Math.max(1, hi - lo);
      return m.days.map((x, k) => { const dd = window.wxDescribe(x.code, 1); const a = ((x.min - lo) / span) * 100, b = ((x.max - lo) / span) * 100;
        return `<div class="day" style="--k:${k}"><div class="n">${C.dayName(x.date, k)}</div>${window.wxIcon(dd.kind, 22)}<div class="p">${x.pop >= 20 ? x.pop + '%' : ''}</div><div class="lo">${C.tempNum(x.min, s.unit)}°</div><div class="range"><i style="left:${a}%;right:${100 - b}%"></i></div><div class="hi">${C.tempNum(x.max, s.unit)}°</div></div>`; }).join('');
    })()}</div>`;
  }

  function renderTitle() {
    const p = E.selectedPlace();
    $('#title').innerHTML = p ? `${esc(p.name)}${p.current ? `<small>My location</small>` : p.sub ? `<small>${esc(p.sub)}</small>` : ''}` : 'Nimbus';
  }

  function renderMain(opts) {
    opts = opts || {};
    const p = E.selectedPlace(), m = p && S.models[p.id];
    renderTitle();
    if (!p || p.lat == null) { main.innerHTML = welcomeHTML(); bindWelcome(); shown = { id: null, temp: null }; return; }
    if (!m) { main.innerHTML = `<div class="hint" style="margin-top:80px">${R.offline ? 'No connection. Nimbus will load as soon as you are back online.' : 'Loading the forecast...'}</div>`; shown = { id: null, temp: null }; return; }
    const fresh = opts.resetScroll || shown.id !== p.id;   // first time this place is drawn: start at the top
    const keepScroll = scroll.scrollTop, keepHours = ($('#hours') || {}).scrollLeft || 0;
    main.innerHTML = mainHTML(p, m);
    scroll.scrollTop = fresh ? 0 : keepScroll;
    const hs = $('#hours'); if (hs && !fresh) hs.scrollLeft = keepHours;
    const big = $('#big'), t = C.tempNum(m.now.temp, S.settings.unit);
    if (opts.rollFrom != null) roll(big, opts.rollFrom, t, 700);
    else if (shown.id === p.id && shown.temp != null && shown.temp !== t) roll(big, shown.temp, t, 600);
    shown = { id: p.id, temp: t };
  }

  function replayEnter() {
    main.classList.remove('enter'); void main.offsetWidth; main.classList.add('enter');
    const p = E.selectedPlace(), m = p && S.models[p.id];
    if (m) { const t = C.tempNum(m.now.temp, S.settings.unit); const big = $('#big'); if (big) roll(big, t - (t >= 0 ? 8 : -8), t, 800); }
    const hs = $('#hours'); if (hs) hs.scrollLeft = 0; scroll.scrollTop = 0;
  }

  function bindWelcome() {
    const l = $('#w-loc'); if (l) l.onclick = () => { E.locate(); renderMain(); };
    const c = $('#w-city'); if (c) c.onclick = () => openSheet('places', true);
  }

  function renderFooter() {
    const f = $('#ftr'), p = E.selectedPlace(), m = p && S.models[p.id];
    if (R.loading) f.innerHTML = '<span class="dot spin"></span><span>Updating...</span>';
    else if (R.offline) f.innerHTML = `<span class="dot off"></span><span>Offline${m ? ', updated ' + C.ago(m.updated) : ''}</span>`;
    else if (m) f.innerHTML = `<span class="dot"></span><span>Updated ${C.ago(m.updated)} · Open-Meteo</span>`;
    else f.innerHTML = '<span>Data from Open-Meteo</span>';
  }

  /* ---------- sheets ---------- */
  const sheets = { places: $('#sh-places'), settings: $('#sh-settings') };
  let openName = null;
  function openSheet(name, focusSearch) {
    if (openName && openName !== name) closeSheet(true);
    openName = name; buildSheet(name);
    const sh = sheets[name]; sh.classList.add('open'); sh.classList.remove('closing'); app.classList.add('paged');
    if (focusSearch) setTimeout(() => { const t = $('input', sh); if (t) t.focus({ preventScroll: true }); }, 60);
  }
  function closeSheet(instant) {
    if (!openName) return; const sh = sheets[openName]; openName = null;
    if (!instant) app.classList.remove('paged');
    sh.classList.remove('open'); if (!instant) { sh.classList.add('closing'); setTimeout(() => sh.classList.remove('closing'), 480); }
    renderMain(); renderFooter();
  }
  $('#b-places').onclick = () => openSheet('places');
  $('#b-settings').onclick = () => openSheet('settings');
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && openName) { e.preventDefault(); closeSheet(); } });

  const sheetHead = (title) => `<div class="hdr"><button class="icon-btn" data-back aria-label="Back">${IC.back}</button><div class="title">${title}</div><span style="width:32px"></span></div>`;

  function seg(options, value, onPick) {
    const el = document.createElement('div'); el.className = 'seg'; el.setAttribute('role', 'radiogroup');
    el.innerHTML = '<span class="pill"></span>' + options.map(([v, label]) => `<button role="radio" aria-checked="${v === value}" data-v="${v}" class="${v === value ? 'on' : ''}">${label}</button>`).join('');
    const place = (animate) => { const b = $('button.on', el), pill = $('.pill', el); if (!b) return; if (!animate) pill.style.transition = 'none'; pill.style.width = b.offsetWidth + 'px'; pill.style.transform = `translateX(${b.offsetLeft - 2}px)`; if (!animate) { void pill.offsetWidth; pill.style.transition = ''; } };
    el.querySelectorAll('button').forEach((b) => b.addEventListener('click', () => { el.querySelectorAll('button').forEach((x) => { x.classList.toggle('on', x === b); x.setAttribute('aria-checked', x === b); }); place(true); onPick(b.dataset.v); }));
    requestAnimationFrame(() => place(false)); setTimeout(() => place(false), 90);
    return el;
  }
  function switchEl(on, onPick, label) {
    const b = document.createElement('button'); b.className = 'sw' + (on ? ' on' : ''); b.setAttribute('role', 'switch'); b.setAttribute('aria-checked', on); b.setAttribute('aria-label', label); b.innerHTML = '<i></i>';
    b.onclick = () => { const v = !b.classList.contains('on'); b.classList.toggle('on', v); b.setAttribute('aria-checked', v); onPick(v); };
    return b;
  }
  const swatchesEl = (value, onPick) => {
    const w = document.createElement('div'); w.className = 'swatches'; w.setAttribute('role', 'radiogroup');
    [['blue', '#5aa9e6', 'Blue'], ['teal', '#43b6b0', 'Teal'], ['warm', '#e8946f', 'Warm'], ['slate', '#8794a3', 'Slate'], ['none', null, 'None']].forEach(([v, c, label]) => {
      const b = document.createElement('button'); b.className = 'swatch' + (v === 'none' ? ' none' : '') + (v === value ? ' on' : '');
      if (c) b.style.background = `linear-gradient(160deg, ${c}, color-mix(in srgb, ${c} 55%, #fff))`;
      b.setAttribute('role', 'radio'); b.setAttribute('aria-label', label); b.setAttribute('aria-checked', v === value);
      b.onclick = () => { w.querySelectorAll('.swatch').forEach((x) => { x.classList.toggle('on', x === b); x.setAttribute('aria-checked', x === b); }); onPick(v); };
      w.appendChild(b);
    });
    return w;
  };
  const setRow = (icon, title, sub, control, tone) => { const r = document.createElement('div'); r.className = 'card row'; r.style.marginTop = '8px'; r.innerHTML = `<div class="tile ${tone || ''}">${IC[icon]}</div><div class="grow"><div class="ttl">${title}</div><div class="sub">${sub}</div></div>`; if (control) r.appendChild(control); return r; };

  function buildSheet(name) {
    const sh = sheets[name];
    if (name === 'settings') {
      const s = S.settings;
      sh.innerHTML = sheetHead('Settings') + '<div class="scroll"><div id="set-body"></div></div><div class="ftr">Nimbus 0.2 · Data from Open-Meteo</div>';
      const body = $('#set-body', sh);
      body.append(
        setRow('moon', 'Appearance', 'Light, dark or Auto', seg([['light', 'Light'], ['auto', 'Auto'], ['dark', 'Dark']], s.theme, (v) => { E.setSettings({ theme: v }); applyTheme(); })),
        setRow('temp', 'Temperature', 'Degrees on every screen', seg([['c', '°C'], ['f', '°F']], s.unit, (v) => E.setSettings({ unit: v }))),
        setRow('wind', 'Wind speed', 'How fast the wind blows', seg([['kmh', 'km/h'], ['mph', 'mph']], s.speed, (v) => E.setSettings({ speed: v }))),
        setRow('bar', 'Menu bar', 'What you see up top', seg([['temp', 'Temp'], ['icon', 'Icon'], ['both', 'Both']], s.bar, (v) => E.setSettings({ bar: v }))),
        setRow('sky', 'Sky colour', 'Top of the panel', swatchesEl(s.sky, (v) => E.setSettings({ sky: v }))),
        setRow('bell', 'Rain alerts', 'Tell me before it rains', switchEl(!!s.rain, (v) => E.setSettings({ rain: v }), 'Rain alerts')),
        setRow('pin', 'Places', S.places.length + (S.places.length === 1 ? ' place saved' : ' places saved'), (() => { const b = document.createElement('button'); b.className = 'btn soft'; b.style.height = '30px'; b.style.padding = '0 12px'; b.textContent = 'Edit'; b.onclick = () => openSheet('places'); return b; })()),
        setRow('login', 'Open at login', 'Start Nimbus when you sign in', switchEl(!!s.login, (v) => E.setSettings({ login: v }), 'Open at login')),
        setRow('power', 'Quit Nimbus', 'Close it completely', (() => { const b = document.createElement('button'); b.className = 'btn soft'; b.style.height = '30px'; b.style.padding = '0 12px'; b.textContent = 'Quit'; b.onclick = () => E.quit(); return b; })(), 'mute'));
    } else {
      sh.innerHTML = sheetHead('Places') + `<div class="scroll"><label class="search"><svg viewBox="0 0 24 24">${IC.search.slice(IC.search.indexOf('>') + 1, -6)}</svg><input id="q" placeholder="Search for a city" autocomplete="off" spellcheck="false"></label><div id="res"></div><div id="plist"></div></div>`;
      renderPlaces();
      let timer = 0, seq = 0;
      $('#q', sh).addEventListener('input', (e) => {
        const q = e.target.value.trim(); clearTimeout(timer); const my = ++seq;
        if (q.length < 2) { $('#res', sh).innerHTML = ''; return; }
        timer = setTimeout(async () => {
          try { const r = await E.search(q); if (my !== seq) return;
            $('#res', sh).innerHTML = r.length ? '<div class="card group">' + r.map((x, k) => `<button class="row" data-k="${k}"><div class="tile">${IC.pin}</div><div class="grow"><div class="ttl">${esc(x.name)}</div><div class="sub">${esc([x.admin1, x.country].filter(Boolean).join(', '))}</div></div></button>`).join('') + '</div>' : '<div class="hint">No places found. Try another spelling.</div>';
            $('#res', sh).querySelectorAll('button').forEach((b) => b.onclick = () => { if (!E.addPlace(r[+b.dataset.k])) { $('#res', sh).innerHTML = '<div class="hint">You can keep up to 8 places. Remove one first.</div>'; return; } $('#q', sh).value = ''; $('#res', sh).innerHTML = ''; $('#plist', sh).hidden = false; closeSheet(); });
          } catch (err) { if (my === seq) $('#res', sh).innerHTML = '<div class="hint">Could not search right now. Check your connection.</div>'; }
        }, 260);
      });
    }
    sh.querySelectorAll('[data-back]').forEach((b) => b.onclick = () => closeSheet());
  }

  function renderPlaces() {
    const sh = sheets.places, list = $('#plist', sh); if (!list) return;
    const s = S.settings;
    const hasHere = S.places.some((p) => p.current);
    let html = '';
    if (!hasHere && E.R.hostInfo.canLocate) html += `<button class="card row" id="p-here" style="margin-top:10px"><div class="tile">${IC.arrow}</div><div class="grow"><div class="ttl">${R.loc === 'waiting' ? 'Finding you...' : 'Use my location'}</div><div class="sub">${R.loc === 'denied' ? 'Location is off for Nimbus. Turn it on in System Settings.' : R.loc === 'failed' ? 'Could not find you. Tap to try again.' : 'Show the weather where you are'}</div></div></button>`;
    html += '<div class="section-label">Saved places</div><div class="card group">';
    if (!S.places.length) html += '<div class="hint">Nothing saved yet. Search for a city above.</div>';
    S.places.forEach((p) => {
      const m = S.models[p.id], d = m && window.wxDescribe(m.now.code, m.now.isDay);
      html += `<div class="row" style="padding:4px 6px 4px 8px"><button class="row" data-sel="${p.id}" style="padding:6px 4px"><div class="list-icon" style="width:30px;text-align:center">${m ? window.wxIcon(d.kind, 28) : `<div class="tile">${IC.pin}</div>`}</div><div class="grow"><div class="ttl">${esc(p.name)}${p.current ? ' <span class="small">· My location</span>' : ''}</div><div class="sub">${m ? esc(d.text) : esc(p.sub || 'Loading...')}</div></div><div class="val num">${m ? C.temp(m.now.temp, s.unit) : ''}</div></button>${p.current ? '<span class="x-btn ghost" aria-hidden="true"></span>' : `<button class="x-btn" data-del="${p.id}" aria-label="Remove ${esc(p.name)}" title="Remove">${IC.trash}</button>`}</div>`;
    });
    html += '</div>';
    if (R.loc === 'denied' && !hasHere) html += '<div class="hint warn">Location is switched off. Open System Settings, Privacy and Security, Location Services, and turn it on for Nimbus.</div>';
    list.innerHTML = html;
    list.querySelectorAll('[data-sel]').forEach((b) => b.onclick = () => { E.select(b.dataset.sel); closeSheet(); });
    list.querySelectorAll('[data-del]').forEach((b) => b.onclick = () => E.removePlace(b.dataset.del));
    const h = $('#p-here', list); if (h) h.onclick = () => { E.locate(); renderPlaces(); };
  }

  /* ---------- engine events ---------- */
  let lastIdx = 0;
  E.subscribe((what) => {
    if (what === 'data') { applySky(); renderMain(); if (openName === 'places') renderPlaces(); renderFooter(); }
    else if (what === 'status' || what === 'clock') renderFooter();
    else if (what === 'select') {
      applySky();
      const idx = S.places.findIndex((p) => p.id === S.selected), dir = idx >= lastIdx ? 'r' : 'l'; lastIdx = idx;
      main.className = 'slide-out-' + (dir === 'r' ? 'l' : 'r');
      setTimeout(() => { main.className = ''; renderMain({ resetScroll: true }); main.className = 'slide-in-' + dir; renderFooter(); setTimeout(() => (main.className = ''), 480); }, reduceMotion() ? 0 : 150);
    }
    else if (what === 'places') { applySky(); renderMain(); if (openName === 'places') renderPlaces(); renderFooter(); }
    else if (what === 'settings') { applySky(); renderMain(); if (openName === 'places') renderPlaces(); }
    else if (what === 'loc') { if (openName === 'places') renderPlaces(); else renderMain(); }
    else if (what === 'opened') { if (!openName) replayEnter(); }
    else if (what === 'hostInfo') { if (openName === 'settings') buildSheet('settings'); if (!S.places.length) renderMain(); }
  });

  applyTheme(); applySky(); renderMain(); renderFooter();
  main.classList.add('enter');
  /* Esc from the native shell: close a sheet first; tell the shell if nothing was open. */
  const escape = () => { if (openName) { closeSheet(); return true; } return false; };
  window.WxUI = { openSheet, closeSheet, renderMain, replayEnter, escape, applySky, state: () => ({ sheet: openName }) };
})();

/* Focus rings only appear once someone presses Tab, never when the panel opens. */
window.addEventListener('keydown', (e) => { if (e.key === 'Tab') document.body.classList.add('kbd'); });
window.addEventListener('mousedown', () => document.body.classList.remove('kbd'));
