# OAR Steel – releasing and installing

## How a release happens (Brent)
1. Claude pushes changes to a branch and opens a pull request on GitHub.
2. GitHub builds the add-in on Windows and runs every benchmark test. The pull request shows a green tick
   (passed) or red cross (failed). The built add-in can be downloaded from the run's "Artifacts" for checking.
3. You review and click **Merge**. That is the sign-off.
4. GitHub publishes a Release named after the version (e.g. *OAR Steel v0.2.7*) with one file,
   `OAR-Steel-v0.2.7.zip`. Releases page: https://github.com/brent-oarengineers/OAR-Steel-Beam/releases
   (ignore the "Source code" downloads GitHub adds to every release).
5. Download the zip and extract it into `X:\Programs\Spreadsheets\`, choosing **Replace** when asked.
   Its folders match the server:
   - `Add-Ins\OAR Steel\` – add-ins (64/32-bit), loaders, `VERSION.txt`, install script, changelog, release notes
   - `Add-Ins\OAR Steel\Releases\v0.8.6\` – archive copy of this version's add-ins
   - `Calc Templates\OAR - Steel Beam Design.xlsx` – the calc template

That's the release. Each PC picks the new version up **the next time Excel starts** – nobody needs to log out.

Release folder: `X:\Programs\Spreadsheets\Add-Ins\OAR Steel\` (also found as
`\\LOA-TrueNAS\Data\Programs\Spreadsheets\Add-Ins\OAR Steel\`, so a different drive letter on a PC doesn't matter).

## How a PC stays up to date
OAR Steel is loaded by the shared **OAR Loader** (repo `OAR-Excel-Loader`, release folder
`Add-Ins\OAR Loader\`), which does the same for OAR Timber and OAR Concrete. Each time Excel starts it reads
`VERSION.txt` in `Add-Ins\OAR Steel\`, copies that version to `%LOCALAPPDATA%\OAR\addins\OAR Steel\v<version>\`
if it isn't on the PC yet, and loads it. Details, and the rules every OAR add-in follows, are in that repo's
`README.md` and `ADDIN-CONTRACT.md`. Log: `%LOCALAPPDATA%\OAR\loader.log`.

**Rolling back:** extract the older release zip over the share; its `VERSION.txt` makes it the approved version again.

## Installing on a PC (once per user)
1. Close Excel.
2. Double-click `X:\Programs\Spreadsheets\Add-Ins\OAR Loader\Install-OARAddins.cmd`. It registers the loader and
   removes the old OAR Steel registration. (`Add-Ins\OAR Steel\Install-OARSteel.cmd`, and any old Startup shortcut
   to it, now just runs that.)
3. Open Excel and check `=OAR.VERSION()`.

Brent's development PC: if Excel has an OAR Steel add-in registered from somewhere else (a build from the repo), the
installer leaves it alone and the loader skips OAR Steel, so it is never loaded twice.

## Calc records
The workbook recalculates with whatever add-in version is installed when it is opened. The issued PDF is the
record of a calc; the title block shows the add-in version it was calculated with.
