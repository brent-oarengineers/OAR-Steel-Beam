# OAR Steel add-in: install / update for the current user.
#
# Copies the approved add-in from the office share to %LOCALAPPDATA%\OAR and, the first time only,
# registers it with Excel so it loads automatically. Safe to run at every login (via Install-OARSteel.cmd
# in the Startup folder): it only copies when the share has a newer file, and skips quietly if Excel
# has the add-in open.
#
# Settings: the first folder in $Shares that exists is used. The UNC path works even if a PC maps
# the NAS to a different drive letter. Change these if the release folder moves.

$Shares = @(
    '\\LOA-TrueNAS\Data\Programs\Spreadsheets\Add-Ins\OAR Steel',
    'X:\Programs\Spreadsheets\Add-Ins\OAR Steel'
)
$Share = $Shares | Where-Object { Test-Path $_ } | Select-Object -First 1
$Local   = Join-Path $env:LOCALAPPDATA 'OAR'
$LogFile = Join-Path $Local 'install.log'

New-Item -ItemType Directory -Force -Path $Local | Out-Null
function Log($msg) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm')  $msg" | Out-File -Append -FilePath $LogFile -Encoding utf8 }

# Excel bitness (Click-to-Run Office records it here); default to 64-bit
$bits = '64'
try {
    $platform = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Office\ClickToRun\Configuration' -ErrorAction Stop).Platform
    if ($platform -eq 'x86') { $bits = '32' }
} catch { }

if (-not $Share) { Log "release folder not reachable: $($Shares -join ' | ')"; exit 0 }

$fileName = "OAR.Steel-AddIn$bits.xll"
$source   = Join-Path $Share $fileName
$target   = Join-Path $Local $fileName

if (-not (Test-Path $source)) { Log "file missing: $source"; exit 0 }

# copy when missing or the share copy is newer
$needCopy = (-not (Test-Path $target)) -or ((Get-Item $source).LastWriteTimeUtc -gt (Get-Item $target).LastWriteTimeUtc)
if ($needCopy) {
    try {
        Copy-Item -Path $source -Destination $target -Force -ErrorAction Stop
        Unblock-File -Path $target -ErrorAction SilentlyContinue
        Log "updated $fileName from $source"
    } catch {
        Log "could not update $fileName (Excel probably has it open) - will retry next login"
    }
}

# register with Excel once (Excel 2016 / 2019 / 2021 / 365 all use version key 16.0)
$key = 'HKCU:\Software\Microsoft\Office\16.0\Excel\Options'
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
$props   = Get-ItemProperty -Path $key
$openVals = $props.PSObject.Properties | Where-Object { $_.Name -match '^OPEN\d*$' }
$already = $openVals | Where-Object { $_.Value -like "*OAR.Steel*AddIn*" }
if (-not $already) {
    $i = 0
    while ($true) {
        $name = if ($i -eq 0) { 'OPEN' } else { "OPEN$i" }
        if (-not ($openVals | Where-Object { $_.Name -eq $name })) { break }
        $i++
    }
    New-ItemProperty -Path $key -Name $name -Value "/R `"$target`"" -PropertyType String -Force | Out-Null
    Log "registered $target with Excel as $name"
}
