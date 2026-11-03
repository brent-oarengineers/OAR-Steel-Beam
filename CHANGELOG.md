# OAR Steel – changelog

Each version that reaches `main` is built, tested and published automatically as a GitHub Release.
The calc sheet shows the version it was calculated with (title block, under the green bar).

## v0.3.4
- **kl override per segment:** restraints table has kl top → and kl bot. → columns, like αm. A value applies to that flange's segment from the row to the next restraint on that flange and replaces both the factor-box kl and the moment-reversal rule. Flagged * in the ULS table; ULSDETAIL shows "override". Must be > 0 and ≤ 3.0; an override on a row where that flange is unrestrained, or at the right end, is an error.

## v0.3.3
- **kl in segments where the moment reverses:** the higher of the top- and bottom-flange kl is used (e.g. 1.4 for a bottom-flange segment that also sags under top-flange load), because the load is destabilising where the other flange is in compression. Such segments are marked † in the ULS table, and the note in `OAR.ULSDETAIL` states the kl used. Purely sagging or hogging segments are unchanged.
- Matches RFEM 6 (benchmark B2, C2, bottom flange 0–6 m): Le 8.40 m, Mo 66.49, αs 0.231. RFEM's αm is 1% higher (2.427 vs 2.404) because it interpolates the quarter-point moments.

## v0.3.2
- **Shear deformation option** for deflections: Geometry "Include shear deformation in deflections" (Yes/No, default No). Adds δs from V/(G·As) with As = clear web (d − 2tf)·tw, G = 80 000 MPa. Applies to the SLS checks and the deflected-shape chart; `OAR.SLS`, `OAR.DEFLECTIONS` and `OAR.DIAGRAM` take an optional last argument `shearDefl`.
- Checked against RFEM 6 (benchmark B2, backspan + overhang, which includes shear deformation by default): all tip and backspan deflections agree to RFEM's 0.1 mm; reactions and support moments agree to 0.01.

## v0.3.1
- **Compression flange found per segment from the moment sign.** Every combination now checks the top flange's segments wherever the moment sags and the bottom flange's segments wherever it hogs, each with its own restraints, kl and kr. The Crit. flange input is gone (the library and printed combinations show the Group instead). M* for a segment is the largest moment compressing that flange; αm still uses the whole segment's moment diagram, and a segment where the moment reverses is noted.
- Analysis generalised to two simple supports anywhere on the beam (overhangs), with exact deflections by double integration of M/EI (Macaulay). Simply supported results are unchanged – all existing benchmarks (DCT Ex 1 and 2, Blockpad, VBA PUDL) pass as before. The sheet itself is still simply supported; the overhang inputs and cantilever rules follow in the next stages.
- Summary rows read "Bending – top flange (sagging)" and "Bending – bottom flange (hogging)"; the bottom row greys out when nothing hogs the beam.
- ULS table: "Flange · ends" column (e.g. "Top FL", "Bottom FF").

## v0.3.0
- **New loads table layout:** one row per load position with a column per case – `Type | Trib | Description | G | Q | Wd | Wu | Start | End | Dir`. With a Trib the case values are pressures (kPa, load = trib × value); with a blank Trib they are direct kN/m (Point: kN). Cells show their unit (kPa / kN/m / kN).
- Internal wind pressure is entered as its own row: −cpi in Wd, +cpi in Wu. The Wid / Wiu cases are gone from new sheets; combinations are G | Q | Wd | Wu | Dir (ULS4 = 1.2G + ψcQ + Wd, ULS5 = 0.9G + Wu, plus the Cross / Long versions).
- Load library lists assemblies with a value per case column; picking a Description fills the row (type over to change).
- Support reactions table: G, Q, Wd, Wu (no Dir / Cross / Long). A Point row with blank Trib and `='B1'!RxnR_G` etc. in each case column transfers all cases in one row.
- A row with values but no Type is an error (it would otherwise be hidden by the print tidy-up).
- Older calc sheets (v0.2 one-case-per-row layout, Wid / Wiu combinations) still calculate unchanged – the add-in reads either layout.

## v0.2.8
- Wind directions: new **Dir** column (Cross / Long) on the loads table and the combination library. A wind load with a Dir acts only in combinations with the same Dir; a wind load without one acts in every direction. Crosswind zones are entered as PUDL rows tagged Cross.
- Library has ULS4C / ULS5C (Cross) and ULS4L / ULS5L (Long) alongside ULS4 / ULS5; the Dir rows switch on automatically once any wind load has a Dir, and the undivided ones switch off.
- A wind combination without a Dir is refused (#OAR!) when the wind loads it would pick up are split by direction, so directions can never be added together.
- Support reactions: wind cases per direction (No Dir / Cross / Long) with transfer names such as `RxnR_Wu_Cross`. `OAR.REACTIONS` lists e.g. "Wu Cross"; `OAR.DIAGRAM` accepts it as an ID.
- Sheet layout gains a narrow column K (Dir); Description column slightly narrower. Older calc sheets without Dir columns still calculate as before.
- No change to gravity checks.

## v0.2.7
- Release is a single zip laid out like `X:\Programs\Spreadsheets` (Add-Ins\OAR Steel, Releases archive, Calc Templates); template keeps its proper name.
- Print tidy-up: printing or saving as PDF hides the unused rows in the combinations, loads, restraints, ULS, shear and reactions blocks, so the printout has no gaps. The rows come back on your next edit to the sheet. New **OAR** ribbon tab: *Tidy for print* (preview) and *Show all rows*. Template marks the blocks with `OAR_Flex_…` names.
- No calculation changes.

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
