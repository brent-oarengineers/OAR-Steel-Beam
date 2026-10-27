# OAR Steel – releasing and installing

## How a release happens (Brent)
1. Claude pushes changes to a branch and opens a pull request on GitHub.
2. GitHub builds the add-in on Windows and runs every benchmark test. The pull request shows a green tick
   (passed) or red cross (failed). The built add-in can be downloaded from the run's "Artifacts" for checking.
3. You review and click **Merge**. That is the sign-off.
4. GitHub publishes a Release named after the version (e.g. *OAR Steel v0.2.5*) with:
   `OAR.Steel-AddIn64.xll`, `OAR.Steel-AddIn32.xll`, `Steel Beam Design - Base.xlsx`,
   `Install-OARSteel.cmd`, `Install-OARSteel.ps1`, `CHANGELOG.md`, `RELEASE_NOTES.md`.
   Releases page: https://github.com/brent-oarengineers/OAR-Steel-Beam/releases
5. Download the files and copy them into `Z:\Programs\OAR Steel\` (overwrite).
   Also copy the `.xll` files into `Z:\Programs\OAR Steel\Releases\v0.2.5\` so old calcs can be reproduced.
   Copy the template into `Z:\Programs\Spreadsheets\Calc Templates\` if it changed.

Staff pick up the new add-in at their next login.

## Installing on a PC (once per user)
1. Press Win+R, type `shell:startup`, Enter. Create a shortcut there to
   `Z:\Programs\OAR Steel\Install-OARSteel.cmd`.
2. Double-click the shortcut once (Excel closed). It copies the add-in to `%LOCALAPPDATA%\OAR\` and registers it
   with Excel, so it loads automatically from then on.
3. Open Excel and check `=OAR.VERSION()`.

At every login the script copies a newer add-in from Z: if there is one. It never loads the add-in from Z:
directly, because Excel locks a loaded add-in and Z: could then not be updated. A log is kept in
`%LOCALAPPDATA%\OAR\install.log`.

Brent's development PC: the script sees the existing OAR registration and leaves it alone.

## Calc records
The workbook recalculates with whatever add-in version is installed when it is opened. The issued PDF is the
record of a calc; the title block shows the add-in version it was calculated with.
