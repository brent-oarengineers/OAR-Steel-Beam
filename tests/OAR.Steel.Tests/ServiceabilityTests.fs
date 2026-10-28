module ServiceabilityTests

open Expecto
open OAR.Steel
open Helpers

// Benchmark: OAR Blockpad steel beam calc, 310UB40.4, 8300 mm span, trib 5.65 m.
// G roof 0.1 kPa + self-weight (40.4 kg/m x 9.81), Q roof 0.25 kPa.
// Blockpad results (2 dp): Δg 3.44, Δg+0.4q 5.46, Δg+0.7q 6.97, Δq 5.05, Δ1kN 0.69 mm.
[<Tests>]
let tests =
    testList
        "Serviceability"
        [ test "Blockpad 310UB40.4 benchmark" {
              let s = Sections.find "310UB40.4"
              let span = 8300.0
              let beam = { Span = span; EI = 200000.0 * s.Ix }
              let loads =
                  [ Analysis.selfWeightLoad s
                    { Case = G; Shape = FullSpan; Magnitude = 5.65 * 0.10; Description = "roof"; Dir = "" }
                    { Case = Q; Shape = FullSpan; Magnitude = 5.65 * 0.25; Description = "roof live"; Dir = "" } ]
              let crit =
                  [ { Ratio = 500.0; MaxMm = Some 20.0 }
                    { Ratio = 360.0; MaxMm = Some 25.0 }
                    { Ratio = 300.0; MaxMm = Some 30.0 }
                    { Ratio = 250.0; MaxMm = Some 30.0 }
                    { Ratio = 1000.0; MaxMm = Some 1.5 } ]
              let res = Serviceability.check beam loads crit 0.4 0.7 |> List.toArray
              let expected = [| 3.44; 5.46; 6.97; 5.05; 0.69 |]
              for i in 0..4 do
                  near 0.006 expected.[i] res.[i].Actual res.[i].Name
              // limits: lesser of L/ratio and max
              near 1e-9 16.6 res.[0].Limit "Δg limit = 8300/500"
              near 1e-9 1.5 res.[4].Limit "Δ1kN limit = 1.5 cap"
              near 1e-6 (res.[0].Actual / 16.6) res.[0].Utilisation "utilisation"
          }

          test "limit uses ratio only when max is blank" {
              near 1e-9 (8300.0 / 250.0) (Serviceability.limit 8300.0 { Ratio = 250.0; MaxMm = None }) "limit"
          } ]
