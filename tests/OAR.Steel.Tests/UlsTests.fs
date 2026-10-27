module UlsTests

open Expecto
open OAR.Steel
open Helpers

let private pt x top bottom am : RestraintPoint =
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMOverride = am }

let private k1 = { KlTop = 1.0; KlBottom = 1.0; KrTop = 1.0; KrBottom = 1.0 }

let private gravity = Combination.ofFactors "G" [ G, 1.0 ]

/// DCT Example 1/2 loading: two 20 kN design loads at 2 m and 6 m on an 8 m span (self-weight included in the 20 kN)
let private dctLoads =
    [ { Case = G; Shape = Point 2000.0; Magnitude = 20000.0; Description = "P1" }
      { Case = G; Shape = Point 6000.0; Magnitude = 20000.0; Description = "P2" } ]

let private dctBeam (s: Section) = { Span = 8000.0; EI = 200000.0 * s.Ix }

[<Tests>]
let tests =
    testList
        "ULS segments"
        [ // Expected values from tools/reference_check.py (same formula, separate code)
          test "kt, Table 5.6.3(1), 310UB40.4, l = 4.0 m" {
              let s = Sections.find "310UB40.4"
              near 1e-12 1.0 (Uls.kt s 4000.0 RF RF) "FF"
              near 1e-12 1.0 (Uls.kt s 4000.0 RL RF) "LF"
              near 1e-12 1.0 (Uls.kt s 4000.0 RL RL) "LL"
              within 1e-9 1.041434991915623 (Uls.kt s 4000.0 RP RF) "FP"
              within 1e-9 1.041434991915623 (Uls.kt s 4000.0 RL RP) "PL"
              within 1e-9 1.0828699838312459 (Uls.kt s 4000.0 RP RP) "PP"
              Expect.throws (fun () -> Uls.kt s 4000.0 RL RU |> ignore) "LU is not a valid segment"
          }

          // DCT Vol 1 (1999) 5.3.6 Example 1: full restraint at load points and supports.
          test "DCT Example 1 through the segment builder" {
              let s = Sections.find "200UB29.8"
              let pts =
                  [ pt 0.0 (Some RF) (Some RF) None
                    pt 2000.0 (Some RF) (Some RF) (Some 1.0) // DCT uses αm = 1.0 for uniform moment
                    pt 6000.0 (Some RF) (Some RF) None
                    pt 8000.0 (Some RF) (Some RF) None ]
              let r = Uls.checkCombination s 300 200000.0 (dctBeam s) dctLoads pts k1 gravity |> List.toArray
              Expect.equal r.Length 3 "three segments"
              within 1e-9 40.0 (Units.toKNm r.[1].Mm) "M* in segment 2"
              within 1e-9 4000.0 r.[1].MemberCap.Le "Le segment 2"
              within 0.005 47.6 (Units.toKNm r.[1].MemberCap.PhiMb) "φMb segment 2 (DCT 47.6)"
              Expect.isTrue r.[1].MemberCap.AlphaMOverridden "override used"
              within 1e-6 (1.7 / sqrt 3.0) r.[1].AlphaMFormula "αm (iii) still reported for segment 2"
              // end segment: linear 0 -> 40 kN.m, quarter points 10, 20, 30
              within 1e-6 1.8173764450044858 r.[0].AlphaMFormula "αm end segment"
              within 1e-4 91.008 (Units.toKNm r.[0].MemberCap.PhiMb) "end segment capped at φMsx"
              within 1e-9 (r.[1].Mm / r.[1].MemberCap.PhiMb) r.[1].Utilisation "utilisation = M*/φMb"
          }

          // DCT Example 2: restraint at the supports only, top-flange load within the segment, kl = 1.4.
          test "DCT Example 2 through the segment builder" {
              let s = Sections.find "310UB46.2"
              let k = { k1 with KlTop = 1.4 }
              let pts = [ pt 0.0 (Some RF) (Some RF) None; pt 8000.0 (Some RF) (Some RF) None ]
              let r = Uls.checkCombination s 300 200000.0 (dctBeam s) dctLoads pts k gravity |> List.exactlyOne
              within 1e-9 11200.0 r.MemberCap.Le "Le = 1.0 x 1.4 x 1.0 x 8.0 m"
              within 1e-9 40.0 (Units.toKNm r.Mm) "M*"
              within 1e-6 (1.7 / sqrt 3.0) r.MemberCap.AlphaM "αm (iii) = 0.98 (DCT Table 5.6.1 gives 1.088)"
              within 1e-4 42.18889278186632 (Units.toKNm r.MemberCap.PhiMb) "φMb with αm (iii)"
              let ptsOv = [ pt 0.0 (Some RF) (Some RF) (Some 1.088); pt 8000.0 (Some RF) (Some RF) None ]
              let ro = Uls.checkCombination s 300 200000.0 (dctBeam s) dctLoads ptsOv k gravity |> List.exactlyOne
              within 1e-4 46.76691572049174 (Units.toKNm ro.MemberCap.PhiMb) "φMb with DCT αm = 1.088"
          }

          // Blockpad 310UB40.4, 8.3 m, full-span UDL, restraint at supports only, kl = 1.4:
          // Le = 11.62 m and αm = 1.166 (Blockpad segment 1).
          test "UDL on a single segment: αm = 1.166 and Le = 11.62 m" {
              let s = Sections.find "310UB40.4"
              let beam = { Span = 8300.0; EI = 200000.0 * s.Ix }
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 3.272; Description = "" } ]
              let pts = [ pt 0.0 (Some RF) (Some RF) None; pt 8300.0 (Some RF) (Some RF) None ]
              let r = Uls.checkCombination s 300 200000.0 beam loads pts { k1 with KlTop = 1.4 } gravity |> List.exactlyOne
              within 1e-6 1.16619037896906 r.AlphaMFormula "αm"
              within 1e-9 11620.0 r.MemberCap.Le "Le"
              within 1e-7 (3.272 * 8300.0 ** 2.0 / 8.0) r.Mm "M*"
          }

          test "uplift combination uses the bottom flange restraints and kl" {
              let s = Sections.find "310UB40.4"
              let beam = { Span = 8300.0; EI = 200000.0 * s.Ix }
              let loads =
                  [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = "" }
                    { Case = Wu; Shape = FullSpan; Magnitude = 5.0; Description = "" } ]
              let pts =
                  [ pt 0.0 (Some RF) (Some RF) None
                    pt 2767.0 (Some RL) None None // purlin: top flange only
                    pt 5533.0 (Some RL) None None
                    pt 8300.0 (Some RF) (Some RF) None ]
              let k = { KlTop = 1.4; KlBottom = 1.0; KrTop = 1.0; KrBottom = 1.0 }
              let upBase = Combination.ofFactors "0.9G+Wu" [ G, 0.9; Wu, 1.0 ]
              let up = { upBase with CriticalFlange = Bottom }
              let r = Uls.checkCombination s 300 200000.0 beam loads pts k up |> List.exactlyOne
              within 1e-9 8300.0 r.MemberCap.Le "one bottom-flange segment, kl = 1.0"
              Expect.equal r.Note "" "hogging with bottom flange critical: no warning"
              let down = Uls.checkCombination s 300 200000.0 beam loads pts k gravity
              Expect.equal down.Length 3 "top flange: three segments at the purlins"
          }

          test "warning when the critical flange does not match the moment sign" {
              let s = Sections.find "310UB40.4"
              let beam = { Span = 8300.0; EI = 200000.0 * s.Ix }
              let loads = [ { Case = Wu; Shape = FullSpan; Magnitude = 5.0; Description = "" } ]
              let pts = [ pt 0.0 (Some RF) (Some RF) None; pt 8300.0 (Some RF) (Some RF) None ]
              let r = Uls.checkCombination s 300 200000.0 beam loads pts k1 (Combination.single Wu) |> List.exactlyOne
              Expect.stringContains r.Note "hogging" "note"
          }

          test "restraint table must include both supports, restrained F or P" {
              let s = Sections.find "310UB40.4"
              let beam = { Span = 8300.0; EI = 200000.0 * s.Ix }
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = "" } ]
              let run pts = Uls.checkCombination s 300 200000.0 beam loads pts k1 gravity |> ignore
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF) None ]) "missing right support"
              Expect.throws (fun () -> run [ pt 0.0 (Some RL) (Some RF) None; pt 8300.0 (Some RF) (Some RF) None ]) "L at support"
          }

          test "shear check and envelope" {
              let s = Sections.find "310UB40.4"
              let beam = { Span = 8300.0; EI = 200000.0 * s.Ix }
              let loads =
                  [ { Case = G; Shape = FullSpan; Magnitude = 2.0; Description = "" }
                    { Case = Q; Shape = FullSpan; Magnitude = 1.0; Description = "" } ]
              let c1 = Combination.ofFactors "1.35G" [ G, 1.35 ]
              let c2 = Combination.ofFactors "1.2G+1.5Q" [ G, 1.2; Q, 1.5 ]
              let sh = Uls.shearChecks s 300 beam loads [ c1; c2 ] |> List.toArray
              within 1e-9 (3.9 * 8300.0 / 2.0) sh.[1].VStar "V* = wL/2 for 1.2G + 1.5Q (3.9 kN/m)"
              let env = Uls.envelope beam loads [ c1; c2 ] 2 |> List.toArray
              let _, mmax, mmin, _, _ = env.[1]
              within 1e-9 (3.9 * 8300.0 ** 2.0 / 8.0) mmax "envelope max at midspan"
              within 1e-9 (2.7 * 8300.0 ** 2.0 / 8.0) mmin "envelope min at midspan"
          } ]
