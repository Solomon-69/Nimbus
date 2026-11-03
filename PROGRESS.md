---
type: progress
status: in-progress
tags: [weather, mac-apps]
---
# Weather app: progress (updated 3 Nov 2026)

Brief: `Apps/WEATHER-APP-PROMPT.md`. Folder: `Apps/Weather`.

## Done
- Read brief, rules and the reference video (Apple Weather menu bar: icon + "10°C", popover with hero, 5 day strip, place list).
- Design options page: `design-options.html` (3 layouts, light/dark).
- Council (quick, 3 advisors) picked **B hybrid**. Notes in `DESIGN-DECISION.md`.
- Web UI built in `web/` (shared by Mac and later Windows):
  - `icons.js` animated weather icons + WMO code names
  - `core.js` Open-Meteo URLs, data cleanup, units (tested live against the API)
  - `engine.js` state, saving, polling (5 min open, 15 min closed, retry 1 min, refresh on open/wake/online), search, location bridge, menu bar text bridge
  - `style.css` Ludus tokens, light/dark, springs, entrance animations
  - `app.js` main screen, Places sheet, Settings sheet
  - `index.html`
- Checked in browser at 380x620: welcome card, main screen with live Paris data, Settings sheet. Fixed: back-button focus ring, cut-off subtitle, "Now" hour now equals current temp.

## Done 3 Nov (Mac shell, installed 0.1.0)
- `Sources/App.swift` (status item with SF Symbol + temp, NSPopover 380x620, WKWebView, location, login item, wake/network refresh, test hook `com.solomon.Weather.test`), `Info.plist`, `make-icon.swift`, `build.sh` (swiftc, signed, installs to ~/Applications), `QA/wxtest.swift` (test helper).
- Checked live in the installed app: menu bar shows rain icon + "10°C" from real Open-Meteo; panel opens with arrow under the icon; Paris + Edinburgh saved and kept after a reinstall; scroll starts at top (fixed).
- Not yet checked live: dark mode inside the app, offline, F/mph, remove place, location Allow prompt, open at login, wake refresh, icon bounce frames, Esc, reduce motion.

## Done 3 Nov (Nimbus 0.2.0, installed)
- Renamed to Nimbus (app, bundle id com.solomon.Nimbus, support folder Nimbus with copy of old Weather state, log ~/Library/Logs/Nimbus.log, test hook `com.solomon.Nimbus.test`, build flag NIMBUS_TEST).
- Your answers applied: moving sky gradient (blue default, natural day/night/golden-hour colours, rain/snow/cloud/star/lightning scenes), compact hero with today's weather near the top, Sky colour setting (blue, teal, warm, slate, none), rain alerts (one alert per 3 hours, macOS notification), units follow the Mac's region (UK: C + mph), blue app icon.
- Checked live in the installed app: panel with sky + compact hero, dark + warm sky inside the app, offline then back online, Esc closes sheet then panel, remove and re-add a place, open at login on/off, rain alert fires once and dedupes, units by region, focus ring only after Tab.
- Waiting on Solomon's click: macOS "Allow" pop-ups for notifications and location. Still to check: Reduce Motion, wake refresh, icon bounce frames, location result.

## Next, in this order
1. Finish the live test pass (above), fix, re-run until zero problems.
2. Windows shell (copy `ClaudeGames/windows` pattern, WebView2) + GitHub Actions Windows test with screenshots.
3. Public GitHub repo (MIT) + page per `Apps/PAGES-UPDATE-LOG.md` + Mac-Apps card: WAIT FOR SOLOMON'S GO. Final animated dashboard artifact.

## Notes
- Preview server: `cd Weather && python3 -m http.server 8791`, open `http://127.0.0.1:8791/web/index.html`. Seed test places with localStorage key `weather.state.v1` (see engine.js).
- Playwright can't open file://; port 8765 is used by another process.
- Disk: 8 GB free; build in /private/tmp.

## 3 Nov, evening
- Fixes from Solomon's recording: Places temps now line up (hidden slot where the cross would be), search results show above the saved list, Settings and Places slide in like Ludus pages (24 px spring, home page slides back), no overshoot so the back button is never cut off, panel outline thinner, "Now" card has a 12-hour temperature line instead of empty space.
- Windows: private repo Solomon-69/Nimbus, GitHub Windows runner run passes every check (live data, tray icon, light/dark, F/mph, places, Esc, location, restart, clean quit). Screenshots in QA/proof/windows.
- Open: Windows location falls back to an IP lookup (ipapi.co) and a name lookup (bigdatacloud), which differs from the "stays on this device" wording. Publishing (public repo, page, Mac-Apps card, release) waits for Solomon's go.

- 3 Nov: Mac only (Windows removed). Release v1.0.0 public: Nimbus.dmg, Nimbus-mac.zip, SHA256SUMS.txt. In-app update check added (shared AppUpdater, 5 cases tested). Panel reveal is done by the page (a native alpha of 0 made the panel paint blank). Mac-Apps page live.
