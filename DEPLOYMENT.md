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
Excel registers the **OAR Steel Loader** (`OAR.Steel-Loader64.xll`), not the add-in itself. Every time Excel starts the
loader:
1. reads `VERSION.txt` on the share – the approved version;
2. copies that version's add-in to `%LOCALAPPDATA%\OAR\versions\v<version>\` if it isn't there yet;
3. loads it. The status bar says "OAR Steel updated to v…" the first time a new version loads.

Each version has its own folder, so a second Excel window still open on the old version doesn't block anything.
The last three versions are kept. If the share can't be reached within a few seconds (laptop out of the office), the
version used last time loads. Log: `%LOCALAPPDATA%\OAR\loader.log`.

**Rolling back:** extract the older release zip over the share. Its `VERSION.txt` makes it the approved version again,
and PCs load it at their next Excel start.

## Installing on a PC (once per user)
1. Close Excel.
2. Double-click `X:\Programs\Spreadsheets\Add-Ins\OAR Steel\Install-OARSteel.cmd`. It copies the loader to
   `%LOCALAPPDATA%\OAR\` and registers it with Excel. On a PC set up the old way it swaps the old registration for
   the loader.
3. Open Excel and check `=OAR.VERSION()`.

A Startup-folder shortcut to the script from the old set-up can stay; it just keeps the loader itself current. It is no
longer needed for add-in updates. Install log: `%LOCALAPPDATA%\OAR\install.log`.

Brent's development PC: if Excel has an OAR Steel add-in registered from somewhere else (a build from the repo), the
script leaves it alone and doesn't add the loader.

## Calc records
The workbook recalculates with whatever add-in version is installed when it is opened. The issued PDF is the
record of a calc; the title block shows the add-in version it was calculated with.
