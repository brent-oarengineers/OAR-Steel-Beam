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
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMTop = None; AlphaMBottom = None }

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
