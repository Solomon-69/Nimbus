/* Nimbus core: pure functions shared by the Mac app and the tests.
   Data source: Open-Meteo (free, no key). All stored values are metric; units are applied only when drawing. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.WxCore = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  const FORECAST = 'https://api.open-meteo.com/v1/forecast';
  const AIR = 'https://air-quality-api.open-meteo.com/v1/air-quality';
  const GEO = 'https://geocoding-api.open-meteo.com/v1/search';

  function forecastUrl(lat, lon) {
    const q = new URLSearchParams({
      latitude: lat.toFixed(4), longitude: lon.toFixed(4), timezone: 'auto', forecast_days: '10',
      current: 'temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,wind_speed_10m,wind_direction_10m,precipitation',
      hourly: 'temperature_2m,weather_code,precipitation_probability,is_day',
      daily: 'weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset,uv_index_max',
      wind_speed_unit: 'kmh', temperature_unit: 'celsius',
    });
    return FORECAST + '?' + q.toString();
  }
  const airUrl = (lat, lon) => AIR + '?' + new URLSearchParams({ latitude: lat.toFixed(4), longitude: lon.toFixed(4), current: 'european_aqi,pm2_5', timezone: 'auto' });
  const geoUrl = (name) => GEO + '?' + new URLSearchParams({ name, count: '6', language: 'en', format: 'json' });

  /* Open-Meteo hands out local times without a zone ("2026-10-27T14:00"); keep them as plain strings. */
  const hourOf = (s) => parseInt(s.slice(11, 13), 10);
  const hhmm = (s) => (s ? s.slice(11, 16) : '');
  const dateOf = (s) => s.slice(0, 10);

  function aqiInfo(v) {
    if (v == null || isNaN(v)) return null;
    const bands = [[20, 'Good', 'good'], [40, 'Fair', 'good'], [60, 'Moderate', 'mid'], [80, 'Poor', 'bad'], [100, 'Very poor', 'bad'], [1e9, 'Extremely poor', 'bad']];
    const b = bands.find((x) => v <= x[0]);
    return { value: Math.round(v), label: b[1], tone: b[2] };
  }
  const uvLabel = (v) => (v == null ? '' : v < 3 ? 'Low' : v < 6 ? 'Moderate' : v < 8 ? 'High' : v < 11 ? 'Very high' : 'Extreme');
  const compass = (deg) => ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'][Math.round((((deg % 360) + 360) % 360) / 45) % 8];

  /* raw forecast + optional air quality -> the one model the screens draw from */
  function normalize(raw, air, place, now) {
    const c = raw.current, h = raw.hourly, d = raw.daily;
    const nowKey = c.time.slice(0, 13); // "2026-10-27T14"
    let start = h.time.findIndex((t) => t.slice(0, 13) === nowKey);
    if (start < 0) start = 0;
    const hours = [];
    for (let i = start; i < Math.min(start + 25, h.time.length); i++) {
      hours.push({ time: h.time[i], hour: hourOf(h.time[i]), temp: h.temperature_2m[i], code: h.weather_code[i], isDay: h.is_day[i], pop: h.precipitation_probability[i] });
    }
    const days = d.time.map((t, i) => ({
      date: t, code: d.weather_code[i], min: d.temperature_2m_min[i], max: d.temperature_2m_max[i],
      pop: d.precipitation_probability_max[i], sunrise: hhmm(d.sunrise[i]), sunset: hhmm(d.sunset[i]), uv: d.uv_index_max[i],
    }));
    if (hours.length) hours[0] = Object.assign({}, hours[0], { temp: c.temperature_2m, code: c.weather_code, isDay: c.is_day });
    const t0 = days[0] || {};
    return {
      place: place ? { id: place.id, name: place.name, lat: place.lat, lon: place.lon } : null,
      updated: now || Date.now(),
      timezone: raw.timezone,
      now: {
        temp: c.temperature_2m, feels: c.apparent_temperature, code: c.weather_code, isDay: c.is_day,
        wind: c.wind_speed_10m, windDir: c.wind_direction_10m, humidity: c.relative_humidity_2m, precip: c.precipitation,
        high: t0.max, low: t0.min, pop: hours.length ? Math.max(...hours.slice(0, 3).map((x) => x.pop || 0)) : 0,
        sunrise: t0.sunrise, sunset: t0.sunset, uv: t0.uv, time: c.time,
      },
      hours, days,
      aqi: air && air.current ? aqiInfo(air.current.european_aqi) : null,
    };
  }

  /* ---- units ---- */
  const toF = (c) => c * 9 / 5 + 32;
  const temp = (c, unit) => (c == null ? '–' : Math.round(unit === 'f' ? toF(c) : c) + '°');
  const tempNum = (c, unit) => (c == null ? 0 : Math.round(unit === 'f' ? toF(c) : c));
  const speed = (kmh, unit) => (kmh == null ? '–' : unit === 'mph' ? Math.round(kmh * 0.621371) + ' mph' : Math.round(kmh) + ' km/h');
  const ago = (ms, now) => {
    const s = Math.max(0, Math.round(((now || Date.now()) - ms) / 1000));
    if (s < 45) return 'just now';
    const m = Math.round(s / 60);
    if (m < 60) return m + ' min ago';
    const hr = Math.round(m / 60);
    return hr < 24 ? hr + ' h ago' : Math.round(hr / 24) + ' d ago';
  };
  const dayName = (dateStr, i) => {
    if (i === 0) return 'Today';
    const [y, m, dd] = dateStr.split('-').map(Number);
    return ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][new Date(Date.UTC(y, m - 1, dd)).getUTCDay()];
  };
  const hourLabel = (hr, i) => (i === 0 ? 'Now' : String(hr).padStart(2, '0'));

  return { forecastUrl, airUrl, geoUrl, normalize, aqiInfo, uvLabel, compass, temp, tempNum, speed, ago, dayName, hourLabel, dateOf };
});
