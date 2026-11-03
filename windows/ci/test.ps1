# Automated Windows test for Nimbus: start it in test mode (--test), wait for the live Open-Meteo forecast to load,
# open the panel above the tray, change settings through the real screens (dark, sky colour, F and mph), check Esc,
# restart to check the settings were kept, try Windows dark mode, then quit. Screenshots go to $Shots.
# The app is driven through its test hook: commands are appended to %APPDATA%\Nimbus\test-commands.txt and each one
# is answered in test-results.txt (see src/TestHook.cs). The script fails if the page never loads or the log shows errors.
param([string]$Exe = 'out/Nimbus/Nimbus.exe', [string]$Shots = 'shots')
. "$PSScriptRoot/winlib.ps1"
New-Item -ItemType Directory -Force $Shots | Out-Null
$Exe = (Resolve-Path $Exe).Path
$Shots = (Resolve-Path $Shots).Path
$dir = Join-Path $env:APPDATA 'Nimbus'
$cmdFile = Join-Path $dir 'test-commands.txt'
$resFile = Join-Path $dir 'test-results.txt'
$logFile = Join-Path $dir 'Nimbus.log'
$script:Seq = 0

# ---- Talking to the test hook ----
function Wait-Result([int]$n, [int]$seconds) {
  $until = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $until) {
    if (Test-Path $resFile) {
      foreach ($l in @(Get-Content $resFile -ErrorAction SilentlyContinue)) { if ($l -like "$n *") { return $l } }
    }
    Start-Sleep -Milliseconds 200
  }
  return $null
}
function Send([string]$line, [int]$seconds = 60) {
  $script:Seq++
  [IO.File]::WriteAllText($cmdFile, "$line`n")
  $l = Wait-Result $script:Seq $seconds
  if (-not $l) { throw "no answer to '$line' within $seconds s" }
  $p = $l -split ' ', 4
  return [pscustomobject]@{ Ok = ($p[2] -eq 'OK'); Name = $p[1]; Detail = $(if ($p.Count -gt 3) { $p[3] } else { '' }) }
}
function State { return (Send 'state').Detail | ConvertFrom-Json }
function Js([string]$code) { return (Send "js:$code").Detail }
function Click([string]$selector) { [void](Js "document.querySelector('$selector').click()") }
function Shot([string]$name) { $r = Send "shot:$(Join-Path $Shots $name)"; Check $r.Ok "screenshot $name" }

# ---- Setup helpers ----
function Seed-State([string]$theme = 'light', [string]$unit = 'c', [string]$speed = 'kmh') {
  $st = [ordered]@{
    settings = [ordered]@{ theme = $theme; unit = $unit; speed = $speed; bar = 'both'; login = $false; sky = 'blue'; rain = $true; autoUnits = $false }
    places = @(
      [ordered]@{ id = 'p2988507'; name = 'Paris'; sub = 'Ile-de-France, France'; lat = 48.8534; lon = 2.3488 },
      [ordered]@{ id = 'p2650225'; name = 'Edinburgh'; sub = 'Scotland, United Kingdom'; lat = 55.9521; lon = -3.1965 })
    selected = 'p2988507'; models = @{}; seenWelcome = $true; alerted = @{}
  }
  New-Item -ItemType Directory -Force $dir | Out-Null
  ($st | ConvertTo-Json -Depth 6 -Compress) | Set-Content -Encoding UTF8 (Join-Path $dir 'state.json')
}
function Start-Nimbus {
  $script:Seq = 0
  Remove-Item $cmdFile, $resFile -ErrorAction SilentlyContinue
  $p = Start-App $Exe @('--test')
  Check ($null -ne (Wait-Result 0 120)) 'Nimbus started and its page loaded (test hook ready)'
  return $p
}
function Wait-Loaded([int]$seconds = 90) {
  $until = (Get-Date).AddSeconds($seconds)
  $s = $null
  while ((Get-Date) -lt $until) {
    $s = State
    if ($s.page -and $s.page.places -ge 1 -and $s.page.loaded -ge $s.page.places -and $s.bar.Text) { return $s }
    Start-Sleep -Seconds 2
  }
  return $s   # never fully loaded: the caller's checks report it
}
function Lum([string]$png, [int]$x, [int]$y) {
  $b = [System.Drawing.Bitmap]::FromFile($png)
  try { $c = $b.GetPixel($x, $y) } finally { $b.Dispose() }
  return 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B
}
function Check-Log([string]$tag) {
  $bad = @()
  if (Test-Path $logFile) {
    $bad = @(Get-Content $logFile | Where-Object { $_ -match 'Unhandled|Fatal|Script failed|WebView2 (start|process) failed|Page failed to load|Test command|Could not write|could not be read|Bad message' })
  }
  Check ($bad.Count -eq 0) "${tag}: no errors in Nimbus.log $(if ($bad.Count) { '(' + ($bad -join ' | ') + ')' })"
}
function Press-Esc {
  [void](Send 'panel')   # makes sure the panel has the keyboard
  Start-Sleep -Milliseconds 600
  [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
  Start-Sleep -Milliseconds 900
}

Get-Process Nimbus -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
$p = $null
try {
  # ================= Run 1: light, live forecast, settings =================
  Set-Theme $false
  Seed-State 'light' 'c' 'kmh'
  $p = Start-Nimbus
  Check (Promote-TrayIcon 'Nimbus.exe') 'tray icon registered with Windows (NotifyIconSettings entry)'
  $s = Wait-Loaded 90
  Write-Host ($s | ConvertTo-Json -Depth 5)
  Check ($s.ready -and $s.page.loaded -ge 2) "live forecast loaded for both places (loaded $($s.page.loaded), offline $($s.page.offline))"
  Check ($s.page.hero -match '\d') "main screen shows a temperature (hero: $($s.page.hero -replace '\s+', ' '))"
  Check ($s.bar.Text -match '^-?\d+.*C$' -and -not $s.bar.Empty) "tray icon got the temperature from the page (bar: $($s.bar.Text), $($s.bar.Kind))"
  Check $s.trayVisible 'tray icon visible'

  [void](Send 'panel')
  Start-Sleep -Seconds 3   # the sky and the entrance animation settle
  $s = State
  $b = $s.bounds
  Check $s.visible 'panel opened'
  Check (($b[0] -ge 0) -and ($b[1] -ge 0) -and (($b[0] + $b[2]) -le $s.screen[0]) -and (($b[1] + $b[3]) -le $s.screen[1])) "panel fully on screen ($($b -join 'x') on $($s.screen -join 'x'))"
  Check (([math]::Abs($s.page.w - 380) -le 1) -and ([math]::Abs($s.page.h - 620) -le 1)) "page laid out at its designed 380x620 (got $($s.page.w)x$($s.page.h))"
  if ($s.tray) { Check (($b[1] + $b[3]) -le ($s.tray[1] + 4)) 'panel sits above the tray icon' } else { Write-Host 'INFO: tray icon is in the overflow area, anchor check skipped' }
  [void](Send "screen:$(Join-Path $Shots '01-desktop-light.png')")
  Shot '02-panel-light.png'
  [void](Send "icon:$(Join-Path $Shots '03-tray-icon.png')")
  Save-TrayCorner (Join-Path $Shots '04-tray-corner-light.png')

  # Settings through the real screens
  Click '#b-settings'; Start-Sleep -Milliseconds 1200
  Check ((State).page.sheet -eq 'settings') 'Settings sheet opens'
  Shot '05-settings-light.png'
  $lum = Lum (Join-Path $Shots '05-settings-light.png') 22 316
  Check ($lum -gt 180) "panel really light, and the screenshot is not black (brightness $([math]::Round($lum)))"
  Click '#sh-settings [data-v=dark]'; Start-Sleep -Milliseconds 1500
  $s = State
  Check ($s.page.theme -eq 'dark') "Appearance Dark applied (data-theme: $($s.page.theme))"
  Shot '06-settings-dark.png'
  $lum = Lum (Join-Path $Shots '06-settings-dark.png') 22 316
  Check ($lum -lt 90) "panel really dark (brightness $([math]::Round($lum)))"
  Click '#sh-settings [aria-label=Warm]'; Start-Sleep -Milliseconds 1000
  Check ((State).page.sky -eq 'warm') 'Sky colour Warm applied'
  Click '#sh-settings [data-v=f]'; Click '#sh-settings [data-v=mph]'; Start-Sleep -Milliseconds 1200
  $s = State
  Check (($s.page.unit -eq 'f') -and ($s.page.speed -eq 'mph')) "Fahrenheit and mph applied (unit $($s.page.unit), speed $($s.page.speed))"
  Check ($s.bar.Text -match 'F$') "tray icon number follows Fahrenheit (bar: $($s.bar.Text))"
  Shot '07-settings-dark-f-mph.png'
  Click '#sh-settings [data-back]'; Start-Sleep -Milliseconds 2200
  Check ((State).page.sheet -eq $null) 'back closes the Settings sheet'
  Check ((Js "/mph/.test(document.body.innerText)") -eq 'true') 'main screen now shows wind in mph'
  Shot '08-main-dark-warm-f-mph.png'
  [void](Send "icon:$(Join-Path $Shots '09-tray-icon-f.png')")

  # Places sheet
  Click '#b-places'; Start-Sleep -Milliseconds 1200
  Check ((Js "/Edinburgh/.test(document.body.innerText)") -eq 'true') 'Places sheet lists Edinburgh'
  Shot '10-places-dark.png'
  Click '#sh-places [data-back]'; Start-Sleep -Milliseconds 900

  # Esc: closes an open sheet first, then the panel
  Click '#b-settings'; Start-Sleep -Milliseconds 1000
  Press-Esc
  $s = State
  if ($s.page.sheet -ne $null) {
    Write-Host 'WARN: a real Esc key did not reach the page; trying the same key as a script event'
    [void](Js "document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))"); Start-Sleep -Milliseconds 800
    $s = State
  }
  Check (($s.page.sheet -eq $null) -and $s.visible) 'Esc closes the open sheet and keeps the panel'
  Press-Esc
  $s = State
  if ($s.visible) {
    Write-Host 'WARN: a real Esc key did not close the panel; trying the same key as a script event'
    [void](Js "document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))"); Start-Sleep -Milliseconds 800
    $s = State
  }
  Check (-not $s.visible) 'Esc closes the panel when nothing is open'
  [void](Send 'panel'); Start-Sleep -Seconds 1
  Check ((State).visible) 'panel opens again'

  # Rain alert notification path (the page's own alert logic needs real rain; here the message is sent directly)
  [void](Js "chrome.webview.postMessage({a:'notify',title:'Rain soon',body:'Rain is expected around 15:00 in Paris.'})")
  Start-Sleep -Seconds 2
  Check ((Test-Path $logFile) -and ((Get-Content $logFile -Raw) -match 'notice: Rain soon')) 'rain alert reached the notification code'

  # Use my location: browser location, else the IP address. Needs the internet; the result is reported, only a hang fails.
  [void](Js 'WxEngine.locate()')
  $loc = 'waiting'
  for ($i = 0; $i -lt 18 -and $loc -eq 'waiting'; $i++) { Start-Sleep -Seconds 2; $loc = (Js 'WxEngine.R.loc') -replace '"', '' }
  Check ($loc -ne 'waiting') "Use my location finished (result: $loc)"
  Write-Host "INFO: location result '$loc'"
  Start-Sleep -Seconds 3
  [void](Send "shot:$(Join-Path $Shots '11-after-location.png')")

  Check ((Test-Path (Join-Path $dir 'state.json')) -and ((Get-Content (Join-Path $dir 'state.json') -Raw) -match '"unit":"f"')) 'settings saved to state.json'
  Check-Log 'run 1'
  Check (Quit-App $Exe 'Nimbus') 'quit with --quit (clean exit)'

  # ================= Run 2: restart keeps the settings =================
  $p = Start-Nimbus
  [void](Send 'panel')
  $s = Wait-Loaded 90
  Check (($s.page.theme -eq 'dark') -and ($s.page.unit -eq 'f') -and ($s.page.sky -eq 'warm')) "settings kept after restart (theme $($s.page.theme), unit $($s.page.unit), sky $($s.page.sky))"
  Check ($s.page.loaded -ge 2) 'places and forecast back after restart'
  Start-Sleep -Seconds 3
  Shot '12-after-restart.png'
  Check-Log 'run 2'
  Check (Quit-App $Exe 'Nimbus') 'run 2: clean quit'

  # ================= Run 3: Windows dark mode, Appearance Auto =================
  Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
  Set-Theme $true
  Seed-State 'auto' 'c' 'kmh'
  $p = Start-Nimbus
  [void](Promote-TrayIcon 'Nimbus.exe' 10)
  $s = Wait-Loaded 90
  [void](Send 'panel'); Start-Sleep -Seconds 3
  Check ((Js "matchMedia('(prefers-color-scheme: dark)').matches") -eq 'true') 'Auto follows Windows dark mode'
  Shot '13-panel-windows-dark.png'
  [void](Send "icon:$(Join-Path $Shots '14-tray-icon-windows-dark.png')")
  Save-TrayCorner (Join-Path $Shots '15-tray-corner-dark.png')
  Check-Log 'run 3'
  Check (Quit-App $Exe 'Nimbus') 'run 3: clean quit'
}
catch {
  Fail "test stopped early: $($_.Exception.Message)"
  [void](Save-Screen (Join-Path $Shots '99-failure-desktop.png'))
}
finally {
  Get-Process Nimbus -ErrorAction SilentlyContinue | Stop-Process -Force
  Set-Theme $false
}
Finish
