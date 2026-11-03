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

let private k1 = { KlTop = 1.0; KlBottom = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false }

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

          // RFEM 6 steel design, B2 C2, purlins removed (run B), x = 6.0: bottom flange 0–6 m F–F with the moment
          // reversing at 4.5 m. RFEM uses kl = 1.4 (top-flange load, top flange compressed over 0–4.5 m):
          // Le 8.40 m, Mo 66.49, αs 0.231, αm 2.427 (from interpolated quarter-point moments; exact 2.404), η 0.348.
          test "RFEM B2 C2 run B: reversing segment takes the higher kl" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads =
                  [ ld (Partial(0.0, 6000.0)) (1.2 * 3.0 + 1.5 * 4.0)
                    ld (Partial(6000.0, 8000.0)) (1.2 * 3.0 + 1.5 * 4.0)
                    ld (Point 8000.0) (1.2 * 10000.0) ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RF) (Some RF) ]
              let k = { k1 with KlTop = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let seg = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 1e-12 1.4 seg.Kl "kl raised to the top-flange value"
              Expect.isTrue seg.KlRaised "flagged"
              Expect.stringContains seg.Note "kl = 1.40" "note"
              within 1e-9 8400.0 seg.MemberCap.Le "Le = 1.4 x 6.0 m"
              within 0.0005 66.49 (Units.toKNm seg.MemberCap.Mo) "Mo (RFEM 66.49)"
              within 0.002 0.231 seg.MemberCap.AlphaS "αs (RFEM 0.231)"
              within 0.002 2.404 seg.MemberCap.AlphaM "αm exact (RFEM 2.427 from interpolated M2)"
              within 0.002 136.76 (Units.toKNm (seg.MemberCap.PhiMb / 0.9)) "Mb (RFEM 138.04 with its αm)"
              within 1e-9 43.2 (Units.toKNm seg.Mm) "M* at B"
              let over = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = lb)
              Expect.isFalse over.KlRaised "overhang is all hogging: kl stays at the bottom-flange value"
              within 1e-12 1.0 over.Kl "overhang kl"
          }

          test "kl override in the restraints table replaces the reversal rule" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld shape mag : Load = { Case = G; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld (Partial(0.0, 8000.0)) 9.6; ld (Point 8000.0) 12000.0 ]
              let p0Base = pt 0.0 (Some RF) (Some RF)
              let p0 = { p0Base with KlOverrideBottom = Some 1.0 }
              let pts = [ p0; pt lb (Some RF) (Some RF); pt (lb + a) (Some RF) (Some RF) ]
              let k = { k1 with KlTop = 1.4 }
              let r = Uls.checkCombination s 300 200000.0 b loads pts k (Combination.single G)
              let seg = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              Expect.isTrue seg.KlOverridden "override used"
              Expect.isFalse seg.KlRaised "not raised"
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

          test "restraints: the tip must be listed; an unrestrained tip is not covered yet" {
              let s = Sections.find "310UB40.4"
              let b = Beam.supported (lb + a) (200000.0 * s.Ix) 0.0 lb
              let loads = [ { Case = G; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" } ]
              let run pts = Uls.checkCombination s 300 200000.0 b loads pts k1 (Combination.single G) |> ignore
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF) ]) "tip missing"
              Expect.throws
                  (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RF) (Some RF); pt (lb + a) (Some RU) (Some RU) ])
                  "U tip"
              Expect.throws
                  (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt lb (Some RL) (Some RF); pt (lb + a) (Some RF) (Some RF) ])
                  "support B must be F or P"
          } ]
