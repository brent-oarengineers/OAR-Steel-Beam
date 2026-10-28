module AnalysisTests

open Expecto
open OAR.Steel
open Helpers

let private span = 8300.0
let private ei = 200000.0 * 86.4e6 // 310UB40.4
let private beam = { Span = span; EI = ei }

[<Tests>]
let tests =
    testList
        "Analysis"
        [ test "UDL: reactions, midspan moment and deflection match closed forms" {
              let w = 3.272 // N/mm = kN/m
              let acts = [ Dist(w, 0.0, span) ]
              let ra, rb = Analysis.reactions beam acts
              within 1e-12 (w * span / 2.0) ra "RA"
              within 1e-12 (w * span / 2.0) rb "RB"
              within 1e-12 (w * span * span / 8.0) (Analysis.moment beam acts (span / 2.0)) "M midspan = wL²/8"
              within 1e-12 (5.0 * w * span ** 4.0 / (384.0 * ei)) (Analysis.deflection beam acts (span / 2.0)) "δ = 5wL⁴/384EI"
              let xm, mmax = Analysis.maxMoment beam acts
              near 1.0 (span / 2.0) xm "location of M max"
              within 1e-7 (w * span * span / 8.0) mmax "M max"
          }

          test "point load at midspan: PL/4 and PL³/48EI" {
              let p = 1000.0
              let acts = [ Conc(p, span / 2.0) ]
              within 1e-12 (p * span / 4.0) (Analysis.moment beam acts (span / 2.0)) "M"
              within 1e-12 (p * span ** 3.0 / (48.0 * ei)) (Analysis.deflection beam acts (span / 2.0)) "δ"
          }

          // Expected values from OAR's existing VBA function PUDL_Deflection (Module1),
          // w = 2 N/mm from 1500 to 5000 mm on an 8300 mm span.
          test "partial UDL deflection matches existing VBA closed form" {
              let acts = [ Dist(2.0, 1500.0, 5000.0) ]
              for x, expected in [ 1000.0, 1.6154185608173435
                                   2500.0, 3.5042114348598097
                                   4150.0, 4.176258107880016
                                   6000.0, 3.0387079277759184 ] do
                  within 1e-9 expected (Analysis.deflection beam acts x) (sprintf "δ at x = %g" x)
          }

          test "equilibrium for mixed loads" {
              let acts = [ Dist(2.0, 1500.0, 5000.0); Conc(5000.0, 6500.0); Dist(-1.0, 0.0, span) ]
              let ra, rb = Analysis.reactions beam acts
              let total = 2.0 * 3500.0 + 5000.0 - 1.0 * span
              within 1e-12 total (ra + rb) "ΣR = Σloads"
              near 1e-3 0.0 (Analysis.moment beam acts span) "M at right support (N.mm)"
              near 1e-3 (-rb) (Analysis.shear beam acts (span - 1e-9)) "V just left of right support = -RB"
          }

          // Dense-sampling reference computed independently (0.5 mm steps) in a Python check script.
          test "maximum deflection search finds the peak of an asymmetric load" {
              let acts = [ Dist(2.0, 1500.0, 5000.0); Conc(5000.0, 6500.0) ]
              let x, d = Analysis.maxDeflection beam acts
              within 1e-5 6.279026011417015 d "δmax"
              near 2.0 4195.0 x "location"
          }

          test "uplift case gives negative (upward) actions" {
              let loads =
                  [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = ""; Dir = "" }
                    { Case = Wu; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" } ]
              let combo = Combination.ofFactors "0.9G+Wu" [ G, 0.9; Wu, 1.0 ]
              let acts = Analysis.actions span combo loads
              let m = Analysis.moment beam acts (span / 2.0)
              within 1e-12 ((0.9 - 3.0) * span * span / 8.0) m "net uplift moment (hogging, negative)"
          } 
          test "line-load profile for the load diagram" {
              let loads =
                  [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = ""; Dir = "" }
                    { Case = Q; Shape = Partial(1000.0, 3000.0); Magnitude = 2.0; Description = ""; Dir = "" }
                    { Case = Wu; Shape = FullSpan; Magnitude = 3.0; Description = ""; Dir = "" }
                    { Case = G; Shape = Point 4150.0; Magnitude = 2750.0; Description = ""; Dir = "" } ]
              let rows = Analysis.lineLoadProfile span loads |> List.toArray
              Expect.equal rows.Length 8 "start + 2 rows x 3 intervals + end"
              let x, ws = rows.[3] // second interval (1000 to 3000), left end
              near 1e-9 1000.0 x "x"
              Expect.equal ws [ 1.0; 2.0; 0.0; -3.0; 0.0; 0.0 ] "G, Q, Wd, Wu (up = negative), Wid, Wiu"
              Expect.equal (Analysis.pointLoads loads) [ 4150.0, 2750.0, G ] "point loads"
          } ]
