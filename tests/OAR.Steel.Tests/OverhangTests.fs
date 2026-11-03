module OverhangTests

open Expecto
open OAR.Steel
open Helpers

// Backspan L = 6.0 m (supports A at 0, B at 6000), overhang a = 2.0 m to the tip at 8000.
let private lb = 6000.0
let private a = 2000.0
let private ei = 200000.0 * 121e6
let private beam = Beam.supported (lb + a) ei 0.0 lb

let private pt x top bottom : RestraintPoint =
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }

/// numeric k-factor grid (row 3 of a 4-row grid is the continuous Yes/No row: 0 = No)
let private grid4 (rows: float list list) =
    Array2D.init rows.Length 2 (fun i j ->
        if i = 2 then Str(if rows.[i].[j] = 0.0 then "No" else "Yes") else Num rows.[i].[j])

let private k1 = { KlDown = 1.0; KlUp = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false; KlTipDown = 2.0; KlTipUp = 2.0 }

[<Tests>]
let tests =
    testList
        "Supports anywhere (overhang)"
        [ // Closed forms for a beam overhanging one support with a point load P at the free end
          test "tip point load: reactions, moment at B, tip and backspan deflections" {
              let p = 10000.0
              let acts = [ Conc(p, lb + a) ]
              let ra, rb = Analysis.reactions beam acts
              within 1e-12 (-p * a / lb) ra "RA = -Pa/L (hold-down)"
              within 1e-12 (p * (lb + a) / lb) rb "RB = P(L+a)/L"
              within 1e-12 (-p * a) (Analysis.moment beam acts lb) "M at B = -Pa"
              within 1e-12 (p * a * a * (lb + a) / (3.0 * ei)) (Analysis.deflection beam acts (lb + a)) "tip δ = Pa²(L+a)/3EI"
              let xUp = lb / sqrt 3.0
              within 1e-9 (-p * a * lb * lb / (9.0 * sqrt 3.0 * ei)) (Analysis.deflection beam acts xUp) "backspan uplift at L/√3"
              near 1e-9 0.0 (Analysis.deflection beam acts 0.0) "δ = 0 at A"
              near 1e-9 0.0 (Analysis.deflection beam acts lb) "δ = 0 at B"
              let x, v = Analysis.maxShear beam acts
              within 1e-9 p (abs v) "max |V| = P on the overhang"
              near 1.0 lb x "at B"
          }

          // AISC beam diagram "overhanging one support, uniform load over the whole length"
          test "UDL over the whole length: reactions, M at B, tip deflection" {
              let w = 3.0
              let acts = [ Dist(w, 0.0, lb + a) ]
              let ra, rb = Analysis.reactions beam acts
              within 1e-12 (w * (lb + a) ** 2.0 / (2.0 * lb)) rb "RB = w(L+a)²/2L"
              within 1e-12 (w * (lb + a) - rb) ra "RA"
              within 1e-12 (-w * a * a / 2.0) (Analysis.moment beam acts lb) "M at B = -wa²/2"
              within 1e-12 (w * a * (4.0 * a * a * lb - lb ** 3.0 + 3.0 * a ** 3.0) / (24.0 * ei))
                  (Analysis.deflection beam acts (lb + a)) "tip δ = wa(4a²L - L³ + 3a³)/24EI"
              let xm, mm = Analysis.maxMoment beam acts
              within 1e-9 (ra * ra / (2.0 * w)) mm "max sagging = RA²/2w"
              near 1.0 (ra / w) xm "at x = RA/w"
          }

          test "mirror image: overhang at the left end" {
              let left = Beam.supported (lb + a) ei a (lb + a)
              let p = 10000.0
              let d = Analysis.deflection left [ Conc(p, 0.0) ] 0.0
              within 1e-12 (p * a * a * (lb + a) / (3.0 * ei)) d "tip δ"
              let ra, rb = Analysis.reactions left [ Conc(p, 0.0) ]
              within 1e-12 (p * (lb + a) / lb) ra "RA (next to the overhang)"
              within 1e-12 (-p * a / lb) rb "RB hold-down"
          }

          test "supports at the ends reproduce the simply supported results" {
              let s = Beam.simple 8300.0 ei
              let g = Beam.supported 8300.0 ei 0.0 8300.0
              let acts = [ Dist(2.0, 1500.0, 5000.0); Conc(5000.0, 6500.0) ]
              for x in [ 1000.0; 4150.0; 7000.0 ] do
                  within 1e-12 (Analysis.deflection s acts x) (Analysis.deflection g acts x) "same deflection"
          }

          test "supports must lie on the beam in order" {
              Expect.throws (fun () -> Beam.supported 8000.0 ei 6000.0 2000.0 |> ignore) "A after B"
              Expect.throws (fun () -> Beam.supported 8000.0 ei 0.0 9000.0 |> ignore) "B beyond the end"
          }

          // Gravity UDL: sagging in the backspan, hogging from the contraflexure point (x = 2RA/w) to the tip.
          test "segments: each flange checked where the moment compresses it" {
              let s = Sections.find "310UB40.4"
              let b = Beam.supported (lb + a) (200000.0 * s.Ix) 0.0 lb
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" } ]
              let pts =
                  [ pt 0.0 (Some RF) (Some RF)
                    pt 2000.0 (Some RL) None
                    pt 4000.0 (Some RL) None
                    pt lb (Some RF) (Some RF)
                    pt (lb + a) (Some RF) (Some RF) ]
              let r = Uls.checkCombination s 300 200000.0 b loads pts k1 (Combination.single G) |> List.toArray
              let top = r |> Array.filter (fun c -> c.Flange = Top)
              let bot = r |> Array.filter (fun c -> c.Flange = Bottom)
              Expect.equal top.Length 3 "top flange: three backspan segments (no sagging on the overhang)"
              Expect.equal bot.Length 2 "bottom flange: backspan (hogging near B) and overhang"
              let over = bot |> Array.find (fun c -> c.X1 = lb)
              within 1e-9 (3.0 * a * a / 2.0) over.Mm "overhang M* = wa²/2"
              within 1e-9 a over.MemberCap.Le "Le = 2.0 m (FF, kl = kr = 1)"
              Expect.equal over.Ends "FF" "ends"
              let back = bot |> Array.find (fun c -> c.X1 = 0.0)
              within 1e-9 (3.0 * a * a / 2.0) back.Mm "backspan bottom flange M* = hogging at B"
              Expect.stringContains back.Note "reverses" "reversal flagged"
              let ra = 3.0 * (lb + a) - 3.0 * (lb + a) ** 2.0 / (2.0 * lb)
              let t3 = top |> Array.find (fun c -> c.X1 = 4000.0)
              Expect.stringContains t3.Note "reverses" "contraflexure inside 4000–6000"
              let t2 = top |> Array.find (fun c -> c.X1 = 2000.0)
              within 1e-9 (ra * ra / 6.0) t2.Mm "peak sagging RA²/2w in the middle segment"
          }

          // RFEM 6 benchmark B2 (Brent, 3 Nov 2026): 360UB44.7, supports at 0 and 6.0 m, tip at 8.0 m.
          // G: 3 kN/m backspan (LC1), 3 kN/m overhang (LC2), 10 kN at tip (LC3); Q: 4 kN/m backspan (LC4), overhang (LC5).
          // RFEM reactions and moments at B to 0.01. (RFEM's sagging peaks are at 0.6 m result points, so they
          // are not compared; its deflections include shear deformation, which the engine does not.)
          test "RFEM benchmark B2: reactions and moment at B for C1 to C5" {
              let b = Beam.supported 8000.0 (200000.0 * 121e6) 0.0 6000.0
              let lc1 f = Dist(f * 3.0, 0.0, 6000.0)
              let lc2 f = Dist(f * 3.0, 6000.0, 8000.0)
              let lc3 f = Conc(f * 10000.0, 8000.0)
              let lc4 f = Dist(f * 4.0, 0.0, 6000.0)
              let lc5 f = Dist(f * 4.0, 6000.0, 8000.0)
              let cases =
                  [ "C1", [ lc1 1.35; lc2 1.35; lc3 1.35 ], 6.30, 39.60, -35.10
                    "C2", [ lc1 1.2; lc2 1.2; lc3 1.2; lc4 1.5; lc5 1.5 ], 21.60, 67.20, -43.20
                    "C3", [ lc1 1.2; lc2 1.2; lc3 1.2; lc5 1.5 ], 3.60, 49.20, -43.20
                    "C4", [ lc1 1.2; lc2 1.2; lc3 1.2; lc4 1.5 ], 23.60, 53.20, -31.20
                    "C5", [ lc1 0.9; lc2 1.2; lc3 1.2; lc5 1.5 ], 0.90, 46.50, -43.20 ]
              for name, acts, ra, rb, mb in cases do
                  let a1, b1 = Analysis.reactions b acts
                  near 0.005 ra (Units.toKN a1) (name + " RA")
                  near 0.005 rb (Units.toKN b1) (name + " RB")
                  near 0.005 mb (Units.toKNm (Analysis.moment b acts 6000.0)) (name + " M at B")
              // sagging peak C5: RA²/2w at x = RA/w (w = 0.9 x 3 = 2.7 kN/m)
              let acts5 = [ lc1 0.9; lc2 1.2; lc3 1.2; lc5 1.5 ]
              near 1e-6 (0.9 * 0.9 / (2.0 * 2.7)) (Units.toKNm (Analysis.moment b acts5 (900.0 / 2.7))) "C5 sagging peak"
          }

          test "shear deformation: closed forms" {
              let gas = 80000.0 * 2251.0
              let ss = Beam.simple 6000.0 ei |> Beam.withShear gas
              let w = 3.0
              let udl = [ Dist(w, 0.0, 6000.0) ]
              let bendingOnly = Analysis.deflection (Beam.simple 6000.0 ei) udl 3000.0
              within 1e-9 (w * 6000.0 ** 2.0 / (8.0 * gas)) (Analysis.deflection ss udl 3000.0 - bendingOnly) "UDL: wL²/8GAs at midspan"
              let ov = beam |> Beam.withShear gas
              let p = 10000.0
              let tip = [ Conc(p, lb + a) ]
              let extra = Analysis.deflection ov tip (lb + a) - Analysis.deflection beam tip (lb + a)
              within 1e-9 (p * a * (lb + a) / (lb * gas)) extra "tip load: Pa(L+a)/(L GAs)"
              near 1e-9 0.0 (Analysis.deflection ov tip lb) "still zero at B"
          }

          // RFEM includes shear deformation (Az = 22.51 cm² for 360UB44.7): tip deflections to its 0.1 mm display.
          test "RFEM benchmark B2: tip deflections with shear deformation" {
              let b = Beam.supported 8000.0 (200000.0 * 121e6) 0.0 6000.0 |> Beam.withShear (80000.0 * 2251.0)
              let lc1 f = Dist(f * 3.0, 0.0, 6000.0)
              let lc2 f = Dist(f * 3.0, 6000.0, 8000.0)
              let lc3 f = Conc(f * 10000.0, 8000.0)
              let lc4 f = Dist(f * 4.0, 0.0, 6000.0)
              let lc5 f = Dist(f * 4.0, 6000.0, 8000.0)
              let tips =
                  [ "C1", [ lc1 1.35; lc2 1.35; lc3 1.35 ], 4.9
                    "C2", [ lc1 1.2; lc2 1.2; lc3 1.2; lc4 1.5; lc5 1.5 ], 2.4
                    "C3", [ lc1 1.2; lc2 1.2; lc3 1.2; lc5 1.5 ], 6.9
                    "C4", [ lc1 1.2; lc2 1.2; lc3 1.2; lc4 1.5 ], -0.1
                    "C5", [ lc1 0.9; lc2 1.2; lc3 1.2; lc5 1.5 ], 7.6
                    "LC3", [ lc3 1.0 ], 4.6 ]
              for name, acts, d in tips do
                  near 0.05 d (Analysis.deflection b acts 8000.0) (name + " tip")
              near 0.05 2.2 (Analysis.deflection b [ lc1 1.0 ] 3000.0) "LC1 backspan midspan"
          }

          // RFEM 6 steel design, B2 C2, purlins removed (run B), x = 6.0: bottom flange 0–6 m F–F, hogging governs.
          // RFEM and Space Gass both use kl = 1.4 (downward load on the top flange):
          // Le 8.40 m, Mo 66.49, αs 0.231, αm 2.427 (RFEM interpolates quarter-point moments; exact 2.404), η 0.348.
          test "RFEM B2 C2 run B: downward top-flange load gives kl 1.4 for the hogging segment" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads =
                  [ ld (Partial(0.0, 6000.0)) (1.2 * 3.0 + 1.5 * 4.0)
                    ld (Partial(6000.0, 8000.0)) (1.2 * 3.0 + 1.5 * 4.0)
                    ld (Point 8000.0) (1.2 * 10000.0) ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RF) (Some RF) ]
              let k = { k1 with KlDown = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let seg = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 1e-12 1.4 seg.Kl "kl for downward load"
              Expect.stringContains seg.KlBasis "load down" "basis"
              within 1e-9 8400.0 seg.MemberCap.Le "Le = 1.4 x 6.0 m"
              within 0.0005 66.49 (Units.toKNm seg.MemberCap.Mo) "Mo (RFEM 66.49)"
              within 0.002 0.231 seg.MemberCap.AlphaS "αs (RFEM 0.231)"
              within 0.002 2.404 seg.MemberCap.AlphaM "αm exact (RFEM 2.427 from interpolated M2)"
              within 0.002 136.76 (Units.toKNm (seg.MemberCap.PhiMb / 0.9)) "Mb (RFEM 138.04 with its αm)"
              within 1e-9 43.2 (Units.toKNm seg.Mm) "M* at B"
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              within 1e-12 1.4 over.Kl "overhang (tip restrained): downward load, kl 1.4"
          }

          test "kl override in the restraints table replaces the factor-box kl" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 8000.0)) 9.6; ld (Point 8000.0) 12000.0 ]
              let p0Base = pt 0.0 (Some RF) (Some RF)
              let p0 = { p0Base with KlOverrideBottom = Some 1.0 }
              let pts = [ p0; pt lb (Some RF) (Some RF); pt (lb + a) (Some RF) (Some RF) ]
              let k = { k1 with KlDown = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let seg = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              Expect.isTrue seg.KlOverridden "override used"
              within 1e-9 6000.0 seg.MemberCap.Le "Le = 1.0 x 6.0 m"
              Expect.stringContains seg.Note "restraints table" "note"
              let topSeg = r |> List.find (fun c -> c.Flange = Top && c.X1 = 0.0)
              within 1e-12 1.4 topSeg.Kl "top flange keeps its own kl"
              let tipBase = pt (lb + a) (Some RF) (Some RF)
              let atTip = { tipBase with KlOverrideBottom = Some 1.0 }
              Expect.throws
                  (fun () -> Uls.checkCombination s 300 200000.0 b loads [ p0; pt lb (Some RF) (Some RF); atTip ] k (Combination.single G) |> ignore)
                  "kl override at the right end is an error"
          }

          test "restraints: the tip must be listed; U only at a free end" {
              let s = Sections.find "310UB40.4"
              let b = Beam.supported (lb + a) (200000.0 * s.Ix) 0.0 lb
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" } ]
              let run pts = Uls.checkCombination s 300 200000.0 b loads pts k1 (Combination.single G) |> ignore
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF) ]) "tip missing"
              run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RU) (Some RU) ] // U tip is fine
              Expect.throws
                  (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt 3000.0 (Some RU) (Some RU); pt lb (Some RF) (Some RF); pt (lb + a) (Some RF) (Some RF) ])
                  "U inside the backspan"
              Expect.throws
                  (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RL) (Some RF); pt (lb + a) (Some RF) (Some RF) ])
                  "support B must be F or P"
          }

          // Space Gass 12.28 (AS 4100-1998), B2 C2, overhang 6–8 m with the tip unrestrained (FU):
          // kt 1.00, kl 2.00, Le 4.000 m, αm 1.25, αs 0.54, φMo 184.64 (Mo 205.16), φMb 150.78, M* 43.20, load factor 3.49.
          // (RFEM 6 reported the same Le, Mo and αs but took αm = 0.25 from Table 5.6.2.)
          test "Space Gass B2 C2: cantilever with a free tip, αm 1.25 from Table 5.6.2" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 8000.0)) 9.6; ld (Point 8000.0) 12000.0 ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RU) (Some RU) ]
              let k = { k1 with KlDown = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              Expect.equal over.Ends "FU" "ends"
              within 1e-12 1.0 over.Kt "kt FU"
              within 1e-12 2.0 over.Kl "kl free tip"
              within 1e-9 4000.0 over.MemberCap.Le "Le 4.000 m"
              within 1e-12 1.25 over.MemberCap.AlphaM "αm 1.25"
              Expect.stringContains over.AlphaMBasis "Table 5.6.2" "basis"
              within 0.0005 205.16 (Units.toKNm over.MemberCap.Mo) "Mo"
              within 0.002 0.544 over.MemberCap.AlphaS "αs"
              within 0.0005 150.78 (Units.toKNm over.MemberCap.PhiMb) "φMb (Space Gass 150.78)"
              within 0.005 (1.0 / 3.49) over.Utilisation "load factor 3.49"
              // the backspan segment in the same run: Space Gass Group 1, φMb 123.09, load factor 2.85
              let back = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 0.0005 123.09 (Units.toKNm back.MemberCap.PhiMb) "backspan φMb (Space Gass 123.09)"
              within 0.005 (1.0 / 2.85) back.Utilisation "backspan load factor 2.85"
          }

          // Space Gass 12.28, benchmark B3: 360UB44.7, supports 0 and 5.0 m, tip 6.5 m (U), one ULS case:
          // 8 kN/m full length + 20 kN at 2.0 m, top flange. Group 1 (0–5 m FF): kl 1.40, Le 7.000, αm 1.35, αs 0.29,
          // φMo 76.99, Mbx 86.47, M* 44.40, load factor 1.95. Group 2 (overhang FU): kl 2.00, Le 3.000, αm 2.25,
          // Mb = Ms (section governs), M* -9.00, load factor 24.63. V* 30.20 at A, 12.00 at B (overhang).
          test "Space Gass B3: UDL-only overhang (αm 2.25) and backspan point load" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 6500.0 (200000.0 * s.Ix) 0.0 5000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 6500.0)) 8.0; ld (Point 2000.0) 20000.0 ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let k = { k1 with KlDown = 1.4 }
              let acts = Analysis.actions 6500.0 (Combination.single G) loads
              let ra, _ = Analysis.reactions b acts
              near 0.005 30.20 (Units.toKN ra) "RA = V* at A"
              near 0.005 -9.00 (Units.toKNm (Analysis.moment b acts 5000.0)) "M at B"
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let top = r |> List.find (fun c -> c.Flange = Top && c.X1 = 0.0)
              within 1e-12 1.4 top.Kl "kl"
              within 1e-9 7000.0 top.MemberCap.Le "Le 7.000"
              near 0.005 1.35 top.MemberCap.AlphaM "αm 1.35"
              near 0.005 0.29 top.MemberCap.AlphaS "αs 0.29"
              within 0.0005 76.99 (0.9 * Units.toKNm top.MemberCap.Mo) "φMo 76.99"
              within 0.0005 86.47 (Units.toKNm top.MemberCap.PhiMb) "Mbx 86.47"
              within 1e-6 44.40 (Units.toKNm top.Mm) "M* 44.40"
              within 0.005 (1.0 / 1.95) top.Utilisation "load factor 1.95"
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 5000.0)
              Expect.equal over.Ends "FU" "FU"
              within 1e-12 2.0 over.Kl "kl free tip"
              within 1e-9 3000.0 over.MemberCap.Le "Le 3.000"
              within 1e-12 2.25 over.MemberCap.AlphaM "αm 2.25"
              within 0.0005 221.68 (Units.toKNm over.MemberCap.PhiMb) "φMb = φMsx (section governs)"
              within 0.005 (1.0 / 24.63) over.Utilisation "load factor 24.63"
          }

          // Space Gass 12.28, benchmark B3 with 250UB31.4 (same geometry and load): Group 1 (0–5 m FF): kl 1.40,
          // Le 7.000, αm 1.35, αs 0.28, φMo 37.80, Mbx 42.73, M* 44.40, ratio 1.04 – FAIL (load factor 0.96).
          // Group 2 (overhang FU): kl 2.00, Le 3.000, αm 2.25, αs 0.64, φMo 130.96, Mbx 113.97 (= φMsx), load factor 12.66.
          // Section φMsx 113.97 (load factor 2.57), shear load factor 8.80 at A (V* 30.20).
          test "Space Gass B3 (250UB31.4): backspan fails at 1.04, free-tip overhang αm 2.25" {
              let s = Sections.find "250UB31.4"
              let b = Beam.supported 6500.0 (200000.0 * s.Ix) 0.0 5000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 6500.0)) 8.0; ld (Point 2000.0) 20000.0 ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let k = { k1 with KlDown = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let top = r |> List.find (fun c -> c.Flange = Top && c.X1 = 0.0)
              within 1e-9 7000.0 top.MemberCap.Le "Le 7.000"
              near 0.005 1.35 top.MemberCap.AlphaM "αm 1.35"
              near 0.005 0.28 top.MemberCap.AlphaS "αs 0.28"
              within 0.0005 37.80 (0.9 * Units.toKNm top.MemberCap.Mo) "φMo 37.80"
              within 0.0005 42.73 (Units.toKNm top.MemberCap.PhiMb) "Mbx 42.73"
              within 0.005 1.04 top.Utilisation "ratio 1.04 (fails)"
              Expect.isGreaterThan top.Utilisation 1.0 "fails"
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 5000.0)
              within 1e-12 2.25 over.MemberCap.AlphaM "αm 2.25"
              within 1e-9 3000.0 over.MemberCap.Le "Le 3.000"
              within 0.0005 130.96 (0.9 * Units.toKNm over.MemberCap.Mo) "φMo 130.96"
              near 0.005 0.64 over.MemberCap.AlphaS "αs 0.64"
              within 0.0005 113.97 (Units.toKNm over.MemberCap.PhiMb) "Mbx 113.97"
              within 0.005 (1.0 / 12.66) over.Utilisation "load factor 12.66"
              let sh = Uls.shearChecks s 300 b loads [ Combination.single G ] |> List.exactlyOne
              within 0.005 (1.0 / 8.80) sh.Utilisation "shear load factor 8.80"
              within 0.0005 113.97 (Units.toKNm (Capacity.sectionMomentX s 300).PhiMs) "φMsx 113.97"
          }

          // Space Gass B4: 250UB31.4, supports 0 / 5.0 m, tip 6.5 m (U); 8 kN/m full length + 15 kN at 5.75 m (mid-overhang).
          // Group 1 (0–5 m FF, hogging governs): kl 1.40, Le 7.000, αm 1.68, φMo 37.80, Mbx 53.00, M* -20.25, load factor 2.62.
          // Group 2 (overhang FU): kl 2.00, Le 3.000, αm 2.25 (point load not at the tip), Mbx = φMsx 113.97, load factor 5.63.
          test "Space Gass B4: point load along the overhang (not at the tip) takes αm 2.25" {
              let s = Sections.find "250UB31.4"
              let b = Beam.supported 6500.0 (200000.0 * s.Ix) 0.0 5000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 6500.0)) 8.0; ld (Point 5750.0) 15000.0 ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let k = { k1 with KlDown = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let back = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 1e-12 1.4 back.Kl "kl 1.40"
              near 0.005 1.68 back.MemberCap.AlphaM "αm 1.68"
              within 0.0005 53.00 (Units.toKNm back.MemberCap.PhiMb) "Mbx 53.00"
              within 0.005 (1.0 / 2.62) back.Utilisation "load factor 2.62"
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 5000.0)
              within 1e-12 2.25 over.MemberCap.AlphaM "αm 2.25"
              within 0.0005 113.97 (Units.toKNm over.MemberCap.PhiMb) "Mbx = φMsx"
              within 0.005 (1.0 / 5.63) over.Utilisation "load factor 5.63"
          }

          // Space Gass B5: same beam, 6 kN/m uplift (Wu) full length. Upward load on the top flange is not destabilising: kl 1.00.
          // Group 1 (0–5 m FF, hogging): Le 5.000, αm 1.22, αs 0.40, φMo 59.41, Mbx 55.43, M* -15.52, load factor 3.57.
          // Group 2 (overhang FU, sagging – top flange compressed): kl 1.00, Le 1.500, αm 2.25, section governs, load factor 16.88.
          test "Space Gass B5: uplift gives kl 1.0 (load acts away from the shear centre)" {
              let s = Sections.find "250UB31.4"
              let b = Beam.supported 6500.0 (200000.0 * s.Ix) 0.0 5000.0
              let loads = [ { Case = Wu; Shape = FullSpan; Magnitude = 6.0; Description = ""; Dir = "" } ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let k = { k1 with KlDown = 1.4; KlUp = 1.0; KlTipDown = 2.0; KlTipUp = 1.0 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single Wu)
              let back = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 1e-12 1.0 back.Kl "kl 1.00"
              within 1e-9 5000.0 back.MemberCap.Le "Le 5.000"
              near 0.005 1.22 back.MemberCap.AlphaM "αm 1.22"
              within 0.0005 59.41 (0.9 * Units.toKNm back.MemberCap.Mo) "φMo 59.41"
              within 0.0005 55.43 (Units.toKNm back.MemberCap.PhiMb) "Mbx 55.43"
              within 0.005 (1.0 / 3.57) back.Utilisation "load factor 3.57"
              let over = r |> List.find (fun c -> c.Flange = Top && c.X1 = 5000.0)
              within 1e-12 1.0 over.Kl "free tip under uplift: kl 1.00"
              within 1e-9 1500.0 over.MemberCap.Le "Le 1.500"
              within 1e-12 2.25 over.MemberCap.AlphaM "αm 2.25"
              within 0.005 (1.0 / 16.88) over.Utilisation "load factor 16.88"
          }

          test "mixed loading on a free-tip cantilever: 1.25 adopted, moment-weighted estimate noted" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 8000.0)) 9.6; ld (Point 8000.0) 12000.0 ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RU) (Some RU) ]
              let r = Uls.checkCombination s 300 200000.0 b loads pts k1 (Combination.single G)
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              within 1e-12 1.25 over.MemberCap.AlphaM "adopted 1.25"
              // tip 12 kN x 2 m = 24 kN.m, UDL 9.6 x 2² / 2 = 19.2 kN.m -> (24 x 1.25 + 19.2 x 2.25) / 43.2 = 1.69
              Expect.stringContains over.Note "moment-weighted estimate 1.69" "estimate in the note"
          }

          test "free tip: distributed load only gives αm 2.25; override still wins" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" } ]
              let tip = pt (lb + a) (Some RU) (Some RU)
              let pts = [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); tip ]
              let r = Uls.checkCombination s 300 200000.0 b loads pts k1 (Combination.single G)
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              within 1e-12 2.25 over.MemberCap.AlphaM "UDL: 2.25"
              Expect.stringContains over.Note "free tip" "noted"
              let bBase = pt lb (Some RF) (Some RF)
              let bOv = { bBase with AlphaMBottom = Some 1.0 }
              let ro = Uls.checkCombination s 300 200000.0 b loads [ pt 0.0 (Some RF) (Some RF); bOv; tip ] k1 (Combination.single G)
              let o2 = ro |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              within 1e-12 1.0 o2.MemberCap.AlphaM "override"
              Expect.equal o2.AlphaMBasis "override" "basis"
          }

          test "free-tip kl comes from its own row in the factor box" {
              let k = Inputs.kFactors (grid4 [ [ 1.4; 1.0 ]; [ 1.0; 1.0 ]; [ 0.0; 0.0 ]; [ 2.0; 1.5 ] ])
              within 1e-12 2.0 k.KlTipDown "tip top"
              within 1e-12 1.5 k.KlTipUp "tip bottom"
              let k3 = Inputs.kFactors (grid4 [ [ 1.4; 1.0 ]; [ 1.0; 1.0 ] ])
              within 1e-12 2.0 k3.KlTipDown "default 2.0 for load down"
              within 1e-12 1.0 k3.KlTipUp "default 1.0 for load up"
          } ]
