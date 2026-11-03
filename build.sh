#!/usr/bin/env bash
# Builds Weather.app with swiftc only (no Xcode project) and installs it to ~/Applications.
# Usage: ./build.sh [--no-install]     RELEASE=1 leaves out the test hook.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT="${WEATHER_OUT:-/private/tmp/weather-build}"
APP="$OUT/Weather.app"
[ -f "$REPO/.signing" ] && . "$REPO/.signing"
SIGN_ID="${SIGN_ID:--}"
rm -rf "$OUT"; mkdir -p "$OUT"

# Icon: delete assets/Weather.icns to redraw it from make-icon.swift.
if [ ! -f "$REPO/assets/Weather.icns" ]; then
  mkdir -p "$REPO/assets"
  swiftc -O -framework AppKit "$REPO/make-icon.swift" -o "$OUT/mkicon"
  "$OUT/mkicon" "$OUT"
  iconutil -c icns "$OUT/Weather.iconset" -o "$REPO/assets/Weather.icns"
fi

TEST_FLAG="-DNIMBUS_TEST -DUPDATER_TEST"; [ "${RELEASE:-0}" = "1" ] && TEST_FLAG=""
# shellcheck disable=SC2086
swiftc -O -parse-as-library -swift-version 5 $TEST_FLAG -target arm64-apple-macos14.0 \
  -framework AppKit -framework WebKit -framework CoreLocation -framework ServiceManagement -framework Network -framework UserNotifications \
  "$REPO"/Sources/*.swift -o "$OUT/WeatherBar"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$REPO/Info.plist" "$APP/Contents/"
cp "$OUT/WeatherBar" "$APP/Contents/MacOS/"
cp "$REPO/assets/Weather.icns" "$APP/Contents/Resources/"
cp -R "$REPO/web" "$APP/Contents/Resources/web"
xattr -cr "$APP"
codesign --force --deep --sign "$SIGN_ID" "$APP" 2>/dev/null || codesign --force --deep --sign - "$APP"
echo "Built $APP"
if [ "${1:-}" != "--no-install" ]; then
  mkdir -p "$HOME/Applications"
  # Never "pkill -x Weather": that is Apple's own Weather app. Ours runs as WeatherBar (Nimbus before the rename).
  pkill -x WeatherBar 2>/dev/null || true; pkill -x Nimbus 2>/dev/null || true
  rm -rf "$HOME/Applications/Nimbus.app" "$HOME/Applications/Weather.app"
  ditto "$APP" "$HOME/Applications/Weather.app"
  open -g "$HOME/Applications/Weather.app"
  echo "Installed to ~/Applications/Weather.app"
fi
