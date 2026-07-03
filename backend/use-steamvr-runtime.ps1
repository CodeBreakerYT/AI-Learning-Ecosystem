# Switches the system OpenXR runtime to SteamVR so browser WebXR (Chrome/Edge)
# routes through SteamVR instead of Meta's runtime — which has no
# "unknown sources" gate. Run this AFTER installing Steam + SteamVR.
#
# Usage: right-click -> Run with PowerShell, OR from an admin terminal:
#   powershell -ExecutionPolicy Bypass -File use-steamvr-runtime.ps1
#
# The headset must still be connected via Quest Link (Meta app) — that's how
# the physical Quest is presented to SteamVR. This only changes which runtime
# the browser talks to.

$ErrorActionPreference = "Stop"

function Find-SteamVROpenXR {
  # Steam can live on any drive/library; check the registry then scan libraries.
  $steamRoot = $null
  foreach ($k in @("HKCU:\SOFTWARE\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam")) {
    if (Test-Path $k) {
      $p = (Get-ItemProperty $k -ErrorAction SilentlyContinue).SteamPath
      if (-not $p) { $p = (Get-ItemProperty $k -ErrorAction SilentlyContinue).InstallPath }
      if ($p) { $steamRoot = $p.Replace('/', '\'); break }
    }
  }

  $candidates = @()
  if ($steamRoot) {
    $candidates += Join-Path $steamRoot "steamapps\common\SteamVR\steamxr_win64.json"
    # Parse extra library folders from libraryfolders.vdf
    $vdf = Join-Path $steamRoot "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
      foreach ($line in Get-Content $vdf) {
        if ($line -match '"path"\s*"([^"]+)"') {
          $lib = $matches[1].Replace('\\', '\')
          $candidates += Join-Path $lib "steamapps\common\SteamVR\steamxr_win64.json"
        }
      }
    }
  }
  # Last resort: common drive letters
  foreach ($d in @("C", "D", "E", "F", "G")) {
    $candidates += "${d}:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json"
    $candidates += "${d}:\SteamLibrary\steamapps\common\SteamVR\steamxr_win64.json"
  }

  foreach ($c in $candidates | Select-Object -Unique) {
    if (Test-Path $c) { return $c }
  }
  return $null
}

$runtime = Find-SteamVROpenXR
if (-not $runtime) {
  Write-Host "[x] Could not find SteamVR's OpenXR runtime (steamxr_win64.json)." -ForegroundColor Red
  Write-Host "    Make sure SteamVR is installed via Steam (Library -> search 'SteamVR' -> Install)," -ForegroundColor Yellow
  Write-Host "    launch it once, then re-run this script." -ForegroundColor Yellow
  exit 1
}

Write-Host "[+] Found SteamVR OpenXR runtime:" -ForegroundColor Green
Write-Host "    $runtime"

$key = "HKLM:\SOFTWARE\Khronos\OpenXR\1"
$current = (Get-ItemProperty $key -ErrorAction SilentlyContinue).ActiveRuntime
Write-Host "[i] Current active runtime: $current"

if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty -Path $key -Name "ActiveRuntime" -Value $runtime

$new = (Get-ItemProperty $key).ActiveRuntime
Write-Host "[✓] Active OpenXR runtime is now:" -ForegroundColor Green
Write-Host "    $new"
Write-Host ""
Write-Host "Next:" -ForegroundColor Cyan
Write-Host "  1. Connect the Quest via Link (Meta app), so the headset is live."
Write-Host "  2. Launch SteamVR — it should detect your Quest through Link."
Write-Host "  3. In Chrome/Edge open your site and click Enter VR. No 'unknown sources' prompt."
Write-Host ""
Write-Host "To switch back to Meta's runtime later, run use-meta-runtime.ps1."
