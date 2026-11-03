# OAR Steel – Excel add-in (v0.4.0)

F# / Excel-DNA add-in for the OAR steel beam calc sheet. Engineering lives in `OAR.Steel.Core`
(no Excel references, fully testable); `OAR.Steel.Excel` only converts worksheet values.

```
OAR.Steel.sln
src/OAR.Steel.Core      sections, loads, beam analysis, SLS, AS 4100 capacities   (netstandard2.0)
src/OAR.Steel.Excel     worksheet functions -> OAR.Steel.Excel-AddIn64-packed.xll   (net48)
tests/OAR.Steel.Tests   Expecto benchmark tests (console app)                       (net10.0)
tools/reference_check.py independent Python re-implementation for cross-checking
```

See **DEPLOYMENT.md** for releases and staff installs, **CHANGELOG.md** for what changed.

## 1. One-off setup (Windows, development only)

1. Check Excel bitness: File > Account > About Excel (64-bit is normal).
2. Install **one** of:
   - Visual Studio Community, workload **".NET desktop development"** (includes F# and the
     .NET Framework 4.8 targeting pack). Check the Community licence terms suit OAR.
   - or VS Code + Ionide-fsharp extension + .NET SDK (latest) + .NET Framework 4.8 Developer Pack.
3. Install Git (optional for now, recommended later).

## 2. Build and test

From the folder containing `OAR.Steel.sln` (Developer PowerShell or any terminal):

```
dotnet run --project tests/OAR.Steel.Tests     # runs all benchmark tests, prints pass/fail
dotnet build -c Release                        # builds the add-in
```

The add-in is at
`src/OAR.Steel.Excel/bin/Release/net48/` (Excel-DNA 1.7+ puts the packed file in the `publish` subfolder):
`OAR.Steel.Excel-AddIn64-packed.xll`
(the `-packed` file is the single-file version with Core and FSharp.Core inside).

Load it: Excel > File > Options > Add-ins > Manage: Excel Add-ins > Go > Browse > select the .xll.
Type `=OAR.VERSION()` in a cell to confirm. For office use later, see DEPLOYMENT.md.

If the build complains about a package version, update `ExcelDna.AddIn` / `Expecto` in the
.fsproj files to the latest stable version.

## 3. Functions (v0.1)

All lengths mm. `span` is one cell (simply supported) or two cells {backspan, overhang}: supports at 0 and the backspan,
tip at backspan + overhang. With an overhang, combinations with Q get -span / -cant pattern variants and EQ
(equilibrium, reactions only) is added. Section properties use catalogue units (×10⁶ mm⁴ etc.). Errors appear as `#OAR! message`.

| Function | Returns |
|---|---|
| `OAR.VERSION()` | version text – put it in the sheet header |
| `OAR.SECTION(size, prop, [grade])` | one property: Mass, d, bf, tf, tw, Ag, Ix, Zx, Sx, Iy, J, Iw, fyf, fyw, Ze, phiMsx, phiVv, Class |
| `OAR.SECTIONLIST([family])` | spills the designations with complete data for UB, UC, WB or PFC (feeds the Section size dropdown) |
| `OAR.SECTIONPROPS(size, [grade])` | spills name / value / unit / note, incl. λe flange & web, Ze and class |
| `OAR.LOADS(loads, span)` | spills how the loads table was read (case, shape, magnitude, a, b, up/down, wind Dir) |
| `OAR.SLS(size, span, E, includeSW, loads, criteria, psiL, psiS, [shearDefl])` | spills 5 rows: case, Δmax, x at max, limit, L/Δ, utilisation |
| `OAR.DIAGRAM(size, span, E, includeSW, loads, combos, id, [n])` | spills x, V*, M*, δ along the span for a combination ID or single case (G, Q, Wu, Wu Cross…) |
| `OAR.MAXACTIONS(size, span, includeSW, loads, combos)` | spills per combination: M* max, x, V* max |
| `OAR.REACTIONS(size, span, includeSW, loads, combos)` | spills reactions for each case (wind per direction, e.g. Wu Cross) and combination |
| `OAR.PHIMB(size, grade, Le, [αm], [E])` | φMb, kN·m |
| `OAR.MBDETAIL(size, grade, Le, [αm], [E])` | spills Le, Ms, Mo, αs, αm, φMb, φMs |
| `OAR.ULS(size, grade, span, E, SW, loads, combos, restraints, k)` | spills every segment of every combination: combo, segment, ends, Le, αm, αs, φMb, M*, utilisation (* = αm override) |
| `OAR.ULSDETAIL(…same…, [comboId], [segment])` | full working for one segment (default governing): kt, kl, kr, Le, M2–M4, Mm, αm, Ms, Mo, αs, φMb |
| `OAR.GOVERNING(…same…)` | 3 rows: bending top flange, bending bottom flange, shear – action, capacity, where |
| `OAR.SHEAR(size, grade, span, SW, loads, combos)` | per combination: V*, x, φVv, utilisation, interaction note |
| `OAR.ENVELOPE(size, span, SW, loads, combos, [n])` | x, M* max/min, V* max/min for charts |
| `OAR.LOADDIAGRAM(size, span, SW, loads, ["line"\|"point"])` | applied load diagram data: line-load steps per case (kN/m) or point loads (kN), padded with #N/A for charts |
| `OAR.DEFLECTIONS(size, span, E, SW, loads, ψl, ψs, [n], [shearDefl])` | x and the four deflected shapes for charts |

`restraints` = Restraints table (x | Ref | Description | Top | Bottom | αm top | αm bottom | kl top | kl bottom; each αm / kl override applies to that flange's segment from the row to the next restraint on that flange); `k` = 2 × 2 range
(kl top, kl bottom / kr top, kr bottom). For every combination both flanges are checked: each flange's
segments (from its own restraints) are checked wherever the moment puts that flange in compression
(top: sagging, bottom: hogging; moments under 1% of the combination's peak are ignored). M* = the largest
compressing moment in the segment, αm from the whole segment's diagram. kl comes from the direction of the load
on each segment: downward on the top flange = destabilising (kl ↓, default 1.4), upward = kl ↑ (default 1.0),
no load within the segment = 1.0. Both ends of the beam must be
listed for a checked flange, and the supports must be F or P. A free tip may be U: that segment takes αm from
Table 5.6.2 (1.25 for a point load at the tip, otherwise 2.25; mixed loading adopts 1.25 and notes a
moment-weighted estimate) and kl from the factor box's "kl free tip" row (default 2.0 ↓ / 1.0 ↑). kt from Table 5.6.3(1) (nw = 1); kl, kr as entered.

Ranges on the base sheet: `loads` = `Loads_B1[[Type]:[Dir]]` (v0.3: Type | Trib | Description | G | Q | Wd | Wu | Start | End | Dir;
the v0.2 one-case-per-row layout `Case..Dir` / `Case..End` is still read), `combos` = the active-combinations
spill (ID | flange | description | G | Q | Wd | Wu | Dir; the v0.2 G..Wiu layout is still read), `criteria` = the 5 × 2 block of Span / and Max (mm).
The sheet's Support Reactions table already calls `OAR.REACTIONS` and names each cell (`RxnL_G`, `RxnR_Wu`…)
so other beam sheets can pick them up, e.g. Direct = `='B1'!RxnR_G` on a Point row with case G.

**Loads rows (v0.3).** With a Trib, the case values are kPa and each becomes trib × value; with a blank Trib they
are kN/m (Point: kN). Internal pressure is its own row: −cpi in Wd, +cpi in Wu.

**Wind directions.** `Dir` (last column of the loads table and the combinations) tags wind loads
and combinations, e.g. Cross / Long. A tagged wind load acts only in combinations with the same Dir
(case-insensitive); untagged wind loads act in all. A combination with wind factors and no Dir is an
error once any wind load it picks up is tagged. In the v0.3 layout Dir on a row applies to its wind values only.

Example (summary Δg actual): `=INDEX(OAR.SLS(SectionSize,Span,E_mod,IncludeSW,Loads_B1[[Case]:[End (mm)]],<criteria range>,PsiL,PsiS),1,2)`

## 3a. Print tidy-up

Variable-length blocks are marked on the template with sheet-scoped names starting `OAR_Flex`, each
referring to one key column (e.g. `OAR_Flex_Combos` = the ID column of the combinations spill,
`OAR_Flex_Loads` = `Loads_B1[Case]`). When a workbook is printed or saved as PDF the add-in hides every
row whose key cell shows nothing; the next edit on that sheet unhides them. The **OAR** ribbon tab has
*Tidy for print* (to preview) and *Show all rows*. To make another block collapse, add a name
`OAR_Flex_<anything>` (scope: the sheet) over its key column.

## 4. What is checked, and against what

`dotnet run --project tests/OAR.Steel.Tests` checks:

| Area | Benchmark | Tolerance |
|---|---|---|
| φMsx (Cl. 5.2 Ze) | tabulated Grade 300 φMsx for every UB and PFC in the data | 0.5% |
| Iw = Iy(d−tf)²/4 | tabulated Iw for every UB/UC | 1% |
| φMb, φVv | DCT Vol 1 (1999) §5.3.6 Ex 1: 200UB29.8, Le 4.0 m → 47.6 kN·m, 225 kN | 0.5% |
| φMb, φMsx, φVv | DCT Ex 2: 310UB46.2, Le 12 m → 40.0, 197 kN·m, 356 kN | 0.5% |
| αm (iii) | DCT Ex 2 moment diagram → 1.7/√3 = 0.98 (DCT uses 1.088 from Table 5.6.1 – override) | exact |
| Deflection | Blockpad 310UB40.4 / 8.3 m: 3.44, 5.46, 6.97, 5.05, 0.69 mm | ±0.006 mm |
| Partial UDL | existing VBA `PUDL_Deflection` at 4 points | 1e-9 |
| Statics | wL²/8, 5wL⁴/384EI, PL/4, PL³/48EI, equilibrium, uplift sign | 1e-12 |
| Inputs | trib × pressure + direct, m vs m², bad-row errors, combos, criteria | exact |
| Load rows | trib × kPa per case, direct values, points, Dir on wind only, both layouts agree, internal pressure as Wu/Wd = Wiu/Wid, stepped crosswind statics | 1e-12 |
| Wind Dir | two-zone crosswind vs uniform longitudinal: hand statics, filtering, undivided-combo error, Dir columns | 1e-9 |

| kt | Table 5.6.3(1) FF/FL/LL/FP/PL/PP, 310UB40.4 | 1e-9 |
| Segments | DCT Ex 1 through the builder (segment 2 φMb 47.6 with αm 1.0; end segment αm 1.817) | 0.5% |
| Segments | DCT Ex 2 through the builder (Le 11.2 m; αm 0.98 vs override 1.088) | 1e-4 |
| Segments | Blockpad UDL: αm 1.166, Le 11.62 m | 1e-6 |
| Segments | uplift uses bottom-flange restraints and kl; compression flange from moment sign; support validation | exact |
| Stage 3 | pattern variants (M at B, RA), EQ = RFEM B2 C5 reactions, SLS tip rows / limits / patterns, C restraint segments | 1e-9 / 0.01 kN |
| Space Gass B3–B5 | UDL-only overhang (αm 2.25), mid-overhang point load (2.25), uplift (kl 1.0); 250UB31.4 backspan failing at 1.04 | 0.1% |
| Space Gass B2 | C2 overhang FU (kl 2.0, Le 4.0, αm 1.25, φMb 150.78) and backspan FF (φMb 123.09) | 0.05% |
| RFEM B2 LTB | C1 4–6 m, C2 2–4 m (Run A) and C2 0–6 m bottom flange (Run B): kl, Le, Mo, αs, Mb | 0.1% |
| RFEM B2 | backspan + overhang, C1–C5: reactions and M at B to 0.01; tip and backspan δ (with shear deformation, Az 22.51 cm²) to 0.05 mm | 0.01 kN / 0.05 mm |
| Shear defl. | wL²/8GAs midspan; tip load Pa(L+a)/(L·GAs) | 1e-9 |
| Overhang | tip point load and full-length UDL closed forms (reactions, M at B, tip and backspan deflection), mirror case, per-flange segments with reversal | 1e-9 |

`python tools/reference_check.py` re-computes the capacity benchmarks with separate code, and
`python tools/example_b1.py` independently calculates the base-sheet example beam (B1) so the sheet
output can be compared line by line.

## 5. Section data – needs attention

`src/OAR.Steel.Core/Data/sections.csv` was extracted from the old calc workbook. Edit it in Excel or
a text editor and rebuild; units are as printed in the catalogue.

- **UCs:** only 310UC158 has a full row, and it has no yield stresses, so **no UCs work yet**. Fill
  tw, Ag, Zx, Sx, J and fy from the current catalogue.
- **760UB / 690UB:** yield stress only given for Grade 250. Add Grade 300 values if you use them.
- **WB, 760UB, 690UB:** no tabulated Iw – calculated as Iy(d−tf)²/4 (flagged in `OAR.SECTIONPROPS`).
- RHS / SHS / CHS are not loaded yet.

## 6. Assumptions in v0.1 (check these)

- Simply supported, or backspan + one overhang (right). Deflection limit for the tip uses L = 2 × overhang. E default 200 000 MPa, G = 80 000 MPa, g = 9.81 for self-weight.
- Section capacity: Table 5.2 limits – flange outstand λep/λey 9/16 (HR) or 8/14 (WB, HW);
  web 82/115. Slender: Ze = Z(λsy/λs) for flange, Z(λsy/λs)² for web.
- Shear: Aw = d·tw (hot-rolled) or (d−2tf)·tw (WB); αv = (82/λw)² when λw > 82; no shear–bending
  interaction or bearing yet.
- Deflection is bending only unless "Include shear deformation" is Yes (As = (d − 2tf)·tw, G = 80 000 MPa).
- SLS: Δg, Δg+ψl·Q, Δg+ψs·Q, ΔQ from G and Q cases only (wind not in SLS); 1 kN at midspan.

## Not yet covered

Shear–bending interaction (flagged only), bearing / web crippling, kl automation from load position,
cantilevers and continuous spans, hollow sections, UC data.
