# Restores Meta/Oculus as the active OpenXR runtime for the CURRENT USER
# (undoes use-steamvr-runtime.ps1). Run from a normal (non-admin) terminal:
#   powershell -ExecutionPolicy Bypass -File use-meta-runtime.ps1
#
# Writes to HKEY_CURRENT_USER rather than HKLM: the OpenXR loader checks a
# per-user override there BEFORE the machine-wide HKLM default, and it needs
# no admin elevation to write — unlike HKLM, which requires a UAC prompt an
# unattended/automated shell can't click through.

$ErrorActionPreference = "Stop"

$candidates = @(
  "G:\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json",
  "C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json"
)
foreach ($base in @("G:\Meta Horizon", "C:\Program Files\Oculus", "D:\Meta Horizon")) {
  $hit = Join-Path $base "Support\oculus-runtime\oculus_openxr_64.json"
  if (Test-Path $hit) { $candidates += $hit }
}

$runtime = $candidates | Select-Object -Unique | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $runtime) {
  Write-Host "[x] Could not find Meta's oculus_openxr_64.json." -ForegroundColor Red
  exit 1
}

$key = "HKCU:\SOFTWARE\Khronos\OpenXR\1"
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty -Path $key -Name "ActiveRuntime" -Value $runtime
Write-Host "[✓] Active OpenXR runtime (current user) restored to Meta:" -ForegroundColor Green
Write-Host "    $runtime"
