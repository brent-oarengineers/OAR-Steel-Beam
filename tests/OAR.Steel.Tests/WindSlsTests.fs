module WindSlsTests

open Expecto
open OAR.Steel
open Helpers

let private L = 6000.0
let private ei = 200000.0 * 86.4e6
let private beam = Beam.simple L ei
let private ld case shape mag dir : Load = { Case = case; Shape = shape; Magnitude = mag; Description = ""; Dir = dir }
let private crit5 = [ for r, m in [ 500.0, 20.0; 360.0, 25.0; 300.0, 30.0; 250.0, 30.0; 1000.0, 1.5 ] -> { Ratio = r; MaxMm = Some m } ]
let private crit6 = crit5 @ [ { Ratio = 150.0; MaxMm = None } ]
let private udl w = 5.0 * w * L ** 4.0 / (384.0 * ei)

[<Tests>]
let tests =
    testList
        "Wind serviceability deflection"
        [ test "Δws = factor x deflection under the wind loads alone (self-weight and G excluded)" {
              let loads = [ ld G FullSpan 1.0 ""; ld Q FullSpan 2.0 ""; ld Wu FullSpan 4.0 "" ]
              let rows = Serviceability.checkWith beam loads crit6 0.4 0.7 0.47
              Expect.equal (rows |> List.map (fun r -> r.Name)) [ "Δg"; "Δg+ψl·q"; "Δg+ψs·q"; "Δq"; "Δ1kN"; "Δws" ] "six rows"
              let w = rows |> List.last
              within 1e-9 (0.47 * udl 4.0) w.Actual "0.47 x 5wL⁴/384EI (uplift, shown as a magnitude)"
              within 1e-12 (L / 150.0) w.Limit "L/150"
              within 1e-6 (L / 2.0) w.At "at midspan"
          }

          test "worse of down (Wd) and up (Wu)" {
              let loads = [ ld Wd FullSpan 1.0 ""; ld Wu FullSpan 3.0 "" ]
              let w = Serviceability.checkWith beam loads crit6 0.4 0.7 0.5 |> List.last
              within 1e-9 (0.5 * udl 3.0) w.Actual "uplift governs"
          }

          test "wind directions are checked separately, untagged wind acts in each" {
              let loads =
                  [ ld Wu (Partial(0.0, 2000.0)) 6.0 "Cross"; ld Wu (Partial(2000.0, L)) 2.0 "Cross"
                    ld Wu FullSpan 2.5 "Long"; ld Wu FullSpan 0.5 "" ]
              let w = Serviceability.checkWith beam loads crit6 0.4 0.7 0.47 |> List.last
              let defl dir =
                  let c = { Combination.ofFactors "x" [ Wu, 0.47 ] with Dir = dir }
                  let a = Analysis.actions L c loads
                  Analysis.maxAbsOn 0.0 L (Analysis.breakpointsOn beam a) (Analysis.deflection beam a) |> snd |> abs
              within 1e-9 (max (defl "Cross") (defl "Long")) w.Actual "worst direction, never summed"
              Expect.isLessThan w.Actual (defl "Cross" + defl "Long") "not added together"
          }

          test "five criteria: unchanged; overhang and two spans: span and tip / span 2 rows; governing six" {
              let loads = [ ld G FullSpan 1.0 ""; ld Wu FullSpan 4.0 "" ]
              Expect.equal (Serviceability.check beam loads crit5 0.4 0.7).Length 5 "no wind row with 5 criteria"
              let ov = Beam.supported 8000.0 ei 0.0 6000.0
              let rows = Serviceability.checkWith ov loads crit6 0.4 0.7 0.47
              Expect.equal rows.Length 12 "6 span + 6 tip"
              Expect.equal rows.[11].Name "Δws tip" "tip wind row"
              within 1e-12 (2.0 * 2000.0 / 150.0) rows.[11].Limit "tip: L = 2 x overhang"
              Expect.equal (Serviceability.governing rows).Length 6 "governing: six"
              let two = Serviceability.checkWith (Beam.continuous 12000.0 ei [ 6000.0 ]) loads crit6 0.4 0.7 0.47
              Expect.equal two.[11].Name "Δws span 2" "two spans"
          }

          test "six criteria need a positive wind factor" {
              Expect.throws (fun () -> Serviceability.checkWith beam [] crit6 0.4 0.7 0.0 |> ignore) "factor required"
          } ]
