#!/usr/bin/env bash
# Arrow-join seam check (light + dark). Needs the dev build (./build.sh) running.
# Silent: uses the app's own theme setting, never the Mac's dark mode; restores theme "auto".
# Output: QA/proof/arrow-join-{light,dark}.png (zoomed) and PASS/FAIL per theme.
set -euo pipefail
cd "$(dirname "$0")"; mkdir -p proof
WX=/private/tmp/wxtest
WINID='ObjC.import("CoreGraphics");ObjC.import("Foundation");const a=ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1,0)));const w=a.filter(x=>x.kCGWindowOwnerName==="WeatherBar"&&x.kCGWindowBounds.Height>300);w.length?w[0].kCGWindowNumber:"none"'
for theme in light dark; do
  $WX "js:WxEngine.setSettings({theme:'$theme'})"; /bin/sleep 0.4
  id=none
  for i in 1 2 3 4 5 6; do $WX close; /bin/sleep 0.3; $WX panel; /bin/sleep 3; id=$(osascript -l JavaScript -e "$WINID"); [ "$id" != none ] && break; done
  [ "$id" = none ] && { echo "$theme: panel not on screen"; exit 2; }
  screencapture -x -o -l "$id" "/private/tmp/arrow-$theme.png"
  python3 - "$theme" <<'PY'
import sys
from PIL import Image
t=sys.argv[1]; im=Image.open(f'/private/tmp/arrow-{t}.png').convert('RGB'); w,h=im.size; cx=w//2
lum=lambda p:(p[0]*299+p[1]*587+p[2]*114)/1000
# arrow tip: first row (from top) where centre column is opaque
col=[im.getpixel((cx,y)) for y in range(0,60)]
tip=next(y for y,p in enumerate(col) if p!=(0,0,0))
# body top at x=cx-80: first opaque row
bx=cx-80; body=next(y for y in range(0,60) if im.getpixel((bx,y))!=(0,0,0))
# column through arrow base: biggest luminance step between neighbouring rows inside the shape (skip rim: start 3px below tip)
steps=[abs(lum(col[y+1])-lum(col[y])) for y in range(tip+3,body+14)]
worst=max(steps)
if len({im.getpixel((cx,y)) for y in range(h//2,h//2+200)})<20: print(t+": blank capture (page not rendered yet)"); sys.exit(3)
im.crop((cx-100,0,cx+100,60)).resize((800,240),Image.LANCZOS).save(f'proof/arrow-join-{t}.png')
print(f'{t}: tip y={tip} body y={body} worst step through join={worst:.1f}', 'PASS' if worst<6 else 'FAIL')
sys.exit(0 if worst<6 else 1)
PY
done
$WX "js:WxEngine.setSettings({theme:'auto'})"; $WX close
