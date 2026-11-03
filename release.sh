#!/usr/bin/env bash
# Builds Weather for release: Developer ID signed, notarized by Apple, stapled, then a zip and a dmg with checksums.
# Usage: ./release.sh      Output goes to ./dist (ignored by git).
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT=/private/tmp/weather-release
ID="Developer ID Application: Yael Michal Dredzen (QJ3359CB4R)"
VER=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" "$REPO/Info.plist")
rm -rf "$OUT" "$REPO/dist"; mkdir -p "$REPO/dist"
RELEASE=1 WEATHER_OUT="$OUT" "$REPO/build.sh" --no-install
APP="$OUT/Weather.app"
codesign --force --deep --options runtime --timestamp --sign "$ID" "$APP"
codesign --verify --deep --strict "$APP"
ditto -c -k --keepParent "$APP" "$OUT/submit.zip"
xcrun notarytool submit "$OUT/submit.zip" --keychain-profile notary --wait
xcrun stapler staple "$APP"
ZIP="$REPO/dist/Weather-mac.zip"
ditto -c -k --keepParent "$APP" "$ZIP"
# Bridge for Nimbus 1.0.x installs: their updater looks for Nimbus-mac.zip holding Nimbus.app. Same signed app, old
# folder name; on first launch it renames itself to Weather.app.
mkdir -p "$OUT/bridge"; rm -rf "$OUT/bridge/Nimbus.app"; ditto "$APP" "$OUT/bridge/Nimbus.app"
ditto -c -k --keepParent "$OUT/bridge/Nimbus.app" "$REPO/dist/Nimbus-mac.zip"
mkdir -p "$OUT/dmg"; cp -R "$APP" "$OUT/dmg/"; ln -s /Applications "$OUT/dmg/Applications"
DMG="$REPO/dist/Weather.dmg"
hdiutil create -volname "Weather" -srcfolder "$OUT/dmg" -ov -format UDZO "$DMG" >/dev/null
codesign --force --timestamp --sign "$ID" "$DMG"
xcrun notarytool submit "$DMG" --keychain-profile notary --wait
xcrun stapler staple "$DMG"
( cd "$REPO/dist" && shasum -a 256 Weather.dmg Weather-mac.zip Nimbus-mac.zip > SHA256SUMS.txt )
spctl -a -vvv -t install "$DMG" 2>&1 | head -3 || true
spctl -a -vvv "$APP" 2>&1 | head -3 || true
echo "Release files in $REPO/dist"
