# Restores Meta/Oculus as the active OpenXR runtime (undoes
# use-steamvr-runtime.ps1). Run from an admin terminal:
#   powershell -ExecutionPolicy Bypass -File use-meta-runtime.ps1

$ErrorActionPreference = "Stop"

$candidates = @(
  "G:\Meta Horizon\Support\oculus-runtime\oculus_openxr_64.json",
  "C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json"
)
# Also scan for it wherever Meta Horizon is installed.
foreach ($base in @("G:\Meta Horizon", "C:\Program Files\Oculus", "D:\Meta Horizon")) {
  $hit = Join-Path $base "Support\oculus-runtime\oculus_openxr_64.json"
  if (Test-Path $hit) { $candidates += $hit }
}

$runtime = $candidates | Select-Object -Unique | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $runtime) {
  Write-Host "[x] Could not find Meta's oculus_openxr_64.json." -ForegroundColor Red
  exit 1
}

$key = "HKLM:\SOFTWARE\Khronos\OpenXR\1"
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
Set-ItemProperty -Path $key -Name "ActiveRuntime" -Value $runtime
Write-Host "[✓] Active OpenXR runtime restored to Meta:" -ForegroundColor Green
Write-Host "    $runtime"
