# OAR Steel add-in: install / update for the current user.
#
# Installs the OAR Steel Loader in %LOCALAPPDATA%\OAR and, the first time only, registers it with Excel.
# The loader then does the updating: every time Excel starts it fetches the approved OAR Steel version from
# the office share (when newer) and loads it - no logging out, no script to run after a release.
#
# Run once by double-clicking Install-OARSteel.cmd (Excel closed). Running it again is harmless; a shortcut
# in the Startup folder from the old set-up can stay (it just keeps the loader itself up to date).
#
# Settings: the first folder in $Shares that exists is used. The UNC path works even if a PC maps
# the NAS to a different drive letter. Change these (and in Loader.fs) if the release folder moves.

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

$fileName = "OAR.Steel-Loader$bits.xll"
$source   = Join-Path $Share $fileName
$target   = Join-Path $Local $fileName

if (-not (Test-Path $source)) { Log "file missing: $source (release older than v0.8.6?)"; exit 0 }

# copy the loader when missing or the share copy is newer
$needCopy = (-not (Test-Path $target)) -or ((Get-Item $source).LastWriteTimeUtc -gt (Get-Item $target).LastWriteTimeUtc)
if ($needCopy) {
    try {
        Copy-Item -Path $source -Destination $target -Force -ErrorAction Stop
        Unblock-File -Path $target -ErrorAction SilentlyContinue
        Log "updated $fileName from $source"
    } catch {
        Log "could not update $fileName (Excel probably has it open) - will retry next time"
    }
}

# register the loader with Excel (Excel 2016 / 2019 / 2021 / 365 all use version key 16.0):
# - already registered: nothing to do
# - the add-in itself registered by the old script (%LOCALAPPDATA%\OAR\OAR.Steel-AddIn..): swap it for the loader
# - an OAR Steel add-in registered from anywhere else (a development build): leave it alone
# - nothing yet: add the loader
$key = 'HKCU:\Software\Microsoft\Office\16.0\Excel\Options'
if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
$props    = Get-ItemProperty -Path $key
$openVals = @($props.PSObject.Properties | Where-Object { $_.Name -match '^OPEN\d*$' })
$oar      = @($openVals | Where-Object { $_.Value -like '*OAR.Steel*' })
$loader   = $oar | Where-Object { $_.Value -like '*OAR.Steel-Loader*' }
$oldLocal = $oar | Where-Object { $_.Value -like "*$Local\OAR.Steel-AddIn*" }

if ($loader) {
    # nothing to do
} elseif ($oldLocal) {
    foreach ($o in $oldLocal) {
        Set-ItemProperty -Path $key -Name $o.Name -Value "/R `"$target`""
        Log "replaced $($o.Value) with the loader ($($o.Name))"
    }
} elseif ($oar.Count -gt 0) {
    Log "OAR Steel registered from elsewhere ($($oar[0].Value)) - development PC, left alone"
} else {
    $i = 0
    while ($true) {
        $name = if ($i -eq 0) { 'OPEN' } else { "OPEN$i" }
        if (-not ($openVals | Where-Object { $_.Name -eq $name })) { break }
        $i++
    }
    New-ItemProperty -Path $key -Name $name -Value "/R `"$target`"" -PropertyType String -Force | Out-Null
    Log "registered $target with Excel as $name"
}
