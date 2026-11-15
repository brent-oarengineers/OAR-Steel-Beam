# OAR Steel: kept for PCs and Startup shortcuts set up the old way.
#
# OAR Steel is now loaded and kept up to date by the shared OAR Loader (repo OAR-Excel-Loader), which
# updates every OAR add-in each time Excel starts. This script just runs the loader's installer, which
# registers the loader and removes the old OAR Steel registration. Running it again is harmless.

$Shares = @(
    '\\LOA-TrueNAS\Data\Programs\Spreadsheets\Add-Ins',
    'X:\Programs\Spreadsheets\Add-Ins'
)
$Share = $Shares | Where-Object { Test-Path $_ } | Select-Object -First 1
$Local = Join-Path $env:LOCALAPPDATA 'OAR'
New-Item -ItemType Directory -Force -Path $Local | Out-Null
$LogFile = Join-Path $Local 'install.log'
function Log($msg) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm')  $msg" | Out-File -Append -FilePath $LogFile -Encoding utf8 }

if (-not $Share) { Log "office Add-Ins folder not reachable: $($Shares -join ' | ')"; exit 0 }
$installer = Join-Path $Share 'OAR Loader\Install-OARAddins.ps1'
if (Test-Path $installer) {
    & $installer
} else {
    Log "OAR Loader not on the share yet ($installer) - extract the OAR-Loader release zip over X:\Programs\Spreadsheets\"
}
