/* Nimbus icons, drawn in code. One function: wxIcon(kind, size) -> SVG string.
   kinds: clear, partly, cloud, rain, drizzle, snow, thunder, fog, wind
   night: pass kind + "-n" for moon variants (clear-n, partly-n). */
(function () {
  const sun = (cx, cy, r, rays) => `
    <g class="wx-sun"><g class="wx-rays" style="transform-origin:${cx}px ${cy}px">
      ${[0,45,90,135,180,225,270,315].map(a => `<rect x="${cx-1.2}" y="${cy-r-5.2}" width="2.4" height="4" rx="1.2" transform="rotate(${a} ${cx} ${cy})"/>`).join('')}
    </g><circle cx="${cx}" cy="${cy}" r="${r}"/></g>`;
  const moon = (cx, cy, r) => `<path class="wx-moon" d="M${cx+r*0.55} ${cy-r*0.95}a${r} ${r} 0 1 0 ${r*0.9} ${r*1.45}a${r*0.78} ${r*0.78} 0 0 1 ${-r*0.9} ${-r*1.45}z"/>`;
  const cloud = (dx = 0, dy = 0, cls = 'wx-cloud') => `
    <g class="${cls}" transform="translate(${dx} ${dy})">
      <circle cx="17" cy="29" r="7.5"/><circle cx="26" cy="23.5" r="9.5"/><circle cx="35" cy="29.5" r="6.5"/>
      <rect x="14" y="29" width="25" height="7.5" rx="3.7"/>
    </g>`;
  const drops = (n, long) => Array.from({ length: n }, (_, i) =>
    `<line class="wx-drop" style="animation-delay:${i * 0.28}s" x1="${19 + i * 7}" y1="${39 + (i % 2) * 2}" x2="${17.2 + i * 7}" y2="${(long ? 45.5 : 43.5) + (i % 2) * 2}"/>`).join('');
  const flakes = n => Array.from({ length: n }, (_, i) =>
    `<circle class="wx-flake" style="animation-delay:${i * 0.45}s" cx="${19 + i * 7}" cy="${41 + (i % 2) * 2}" r="1.7"/>`).join('');
  const parts = {
    clear: () => sun(24, 24, 8.5),
    'clear-n': () => moon(23, 24, 11),
    partly: () => sun(18, 18, 6.5) + cloud(0, 3),
    'partly-n': () => moon(17, 17, 8) + cloud(0, 3),
    cloud: () => cloud(-1, 0, 'wx-cloud wx-cloud-dark') + `<g class="wx-cloud wx-back" transform="translate(7 -6) scale(.7)"><circle cx="17" cy="29" r="7.5"/><circle cx="26" cy="23.5" r="9.5"/><circle cx="35" cy="29.5" r="6.5"/><rect x="14" y="29" width="25" height="7.5" rx="3.7"/></g>`,
    rain: () => cloud(0, -3, 'wx-cloud wx-cloud-dark') + `<g class="wx-drops">${drops(3, true)}</g>`,
    drizzle: () => cloud(0, -3, 'wx-cloud wx-cloud-dark') + `<g class="wx-drops wx-fine">${drops(4, false)}</g>`,
    snow: () => cloud(0, -3, 'wx-cloud') + `<g class="wx-flakes">${flakes(3)}</g>`,
    thunder: () => cloud(0, -4, 'wx-cloud wx-cloud-dark') + `<path class="wx-bolt" d="M26 30l-6 10h5l-2.5 8 9-12h-5.5l3-6z"/>`,
    fog: () => cloud(0, -5, 'wx-cloud') + `<g class="wx-fog"><rect x="11" y="35" width="26" height="2.6" rx="1.3"/><rect x="15" y="40.5" width="22" height="2.6" rx="1.3"/></g>`,
    wind: () => `<g class="wx-wind"><path d="M10 20h19a4.5 4.5 0 1 0-4.5-4.5"/><path d="M10 27h26a4.5 4.5 0 1 1-4.5 4.5"/><path d="M10 34h13"/></g>`,
  };
  window.wxIcon = function (kind, size) {
    const f = parts[kind] || parts.cloud;
    return `<svg class="wx wx-${kind}" viewBox="0 0 48 48" width="${size || 28}" height="${size || 28}" aria-hidden="true">${f()}</svg>`;
  };
  /* WMO weather code -> { kind, text } */
  window.wxDescribe = function (code, isDay) {
    const n = isDay === 0 || isDay === false;
    const t = (kind, text) => ({ kind: (kind === 'clear' || kind === 'partly') && n ? kind + '-n' : kind, text });
    if (code === 0) return t('clear', n ? 'Clear night' : 'Sunny');
    if (code === 1) return t('clear', n ? 'Mostly clear' : 'Mostly sunny');
    if (code === 2) return t('partly', 'Partly cloudy');
    if (code === 3) return t('cloud', 'Cloudy');
    if (code === 45 || code === 48) return t('fog', 'Foggy');
    if (code >= 51 && code <= 57) return t('drizzle', 'Drizzle');
    if (code === 61 || code === 80) return t('rain', 'Light rain');
    if (code === 63 || code === 81) return t('rain', 'Rain');
    if (code === 65 || code === 82 || code === 66 || code === 67) return t('rain', 'Heavy rain');
    if (code >= 71 && code <= 77 || code === 85 || code === 86) return t('snow', 'Snow');
    if (code >= 95) return t('thunder', 'Thunderstorm');
    return t('cloud', 'Cloudy');
  };
})();
