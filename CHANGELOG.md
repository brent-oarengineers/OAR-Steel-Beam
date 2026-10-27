# OAR Steel – changelog

Each version that reaches `main` is built, tested and published automatically as a GitHub Release.
The calc sheet shows the version it was calculated with (title block, under the green bar).

## v0.2.6
- Install script reads the release folder `X:\Programs\Spreadsheets\Add-Ins\OAR Steel` (or `\\LOA-TrueNAS\Data\...`, whichever is reachable). No calculation changes.

## v0.2.5
- Continuous restraint option per flange (factor box, "Continuous" Yes/No): critical flange continuously restrained gives φMb = φMsx over the full span.
- Restraints table starts with the two supports and five blank rows for internal restraints.
- Add-in version printed on the calc sheet.
- Automatic build, test and release on GitHub; staff install/update script (`Install-OARSteel.cmd`).

## v0.2.4
- αm override split into αm top and αm bottom columns; each applies to that flange's segment from the row to the next restraint.
- An override that would be ignored (right support, or flange unrestrained at that row) is now an error.

## v0.2.3
- Bending moment envelope full width; shear diagram removed. `OAR.LOADDIAGRAM` added (not used on the sheet).
- Charts read fixed data blocks so Excel cannot regroup the series.
- Load library with case-filtered descriptions; pressure and load columns auto-fill on new rows.

## v0.2.2
- `OAR.SECTIONLIST`; Section size dropdown filtered by type (falls back to the full list if the add-in is not loaded); section properties wired.

## v0.2.1
- Input errors name the argument and the cell within the range.

## v0.2.0
- Segment builder from the restraints table (kt Table 5.6.3(1), kl/kr per flange, αm Cl. 5.6.1.1(a)(iii) with override), `OAR.ULS`, `OAR.ULSDETAIL`, `OAR.GOVERNING`, `OAR.SHEAR`, `OAR.ENVELOPE`, `OAR.DEFLECTIONS`.
- Benchmarks: DCT Vol 1 Examples 1 and 2 through the segment builder, Blockpad UDL αm 1.166.

## v0.1.x
- Section data, Cl. 5.2 section capacity, φMb, φVv, beam analysis (UDL / PUDL / point), deflection checks, loads / combinations input parsing.
- Benchmarks: DCT φMb/φMsx/φVv, tabulated φMsx for all UB/PFC, Blockpad deflections, VBA PUDL deflection.
