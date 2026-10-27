module InputTests

open Expecto
open OAR.Steel
open Helpers

/// Build a Cell grid from rows written as strings ("" = blank, numbers parsed)
let private grid (rows: string list list) =
    let r = rows.Length
    let c = rows |> List.map List.length |> List.max
    Array2D.init r c (fun i j ->
        let v = if j < rows.[i].Length then rows.[i].[j] else ""
        match System.Double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
        | true, x -> Num x
        | _ -> if v = "" then Blank else Str v)

let private span = 8300.0

[<Tests>]
let tests =
    testList
        "Inputs"
        [ test "loads table: trib x pressure + direct, units and shapes" {
              let g =
                  grid
                      [ [ "G"; "UDL"; "roof"; "5.65"; "0.10"; ""; ""; ""; "" ]
                        [ "G"; "UDL"; "bracket"; ""; ""; "0.05"; ""; ""; "" ]
                        [ "Q"; "PUDL"; "store"; "2"; "5"; "1"; "1000"; "3000"; "" ]
                        [ "G"; "Point"; "unit"; "1.5"; "0.5"; "2.0"; "4150"; ""; "" ]
                        [ ""; ""; ""; ""; ""; ""; ""; ""; "" ]
                        [ "Wu"; "UDL"; "uplift"; "5.65"; "1.72"; ""; ""; ""; "" ] ]
              let ls = Inputs.loads span g |> List.toArray
              Expect.equal ls.Length 5 "blank row skipped"
              near 1e-12 0.565 ls.[0].Magnitude "5.65 m x 0.10 kPa = 0.565 kN/m"
              near 1e-12 0.05 ls.[1].Magnitude "direct only"
              near 1e-12 11.0 ls.[2].Magnitude "2 x 5 + 1 = 11 kN/m"
              Expect.equal ls.[2].Shape (Partial(1000.0, 3000.0)) "PUDL extent"
              near 1e-9 2750.0 ls.[3].Magnitude "1.5 m² x 0.5 kPa + 2.0 kN = 2.75 kN = 2750 N"
              Expect.equal ls.[3].Shape (Point 4150.0) "point position"
              Expect.equal ls.[4].Case Wu "case"
          }

          test "loads table: bad rows give row-numbered errors" {
              let bad rows = Expect.throws (fun () -> Inputs.loads span (grid rows) |> ignore) "should fail"
              bad [ [ "X"; "UDL"; ""; "1"; "1"; ""; ""; ""; "" ] ]
              bad [ [ "G"; "PUDL"; ""; "1"; "1"; ""; "5000"; "1000"; "" ] ]
              bad [ [ "G"; "Point"; ""; ""; ""; "2"; "9000"; ""; "" ] ]
              bad [ [ "G"; "UDL"; ""; ""; "1.0"; ""; ""; ""; "" ] ]
              bad [ [ "G"; "UDL"; ""; ""; ""; "-1"; ""; ""; "" ] ]
          }

          test "combinations table" {
              let g =
                  grid
                      [ [ "ULS1"; "Top"; "1.35G"; "1.35"; "0"; "0"; "0"; "0"; "0" ]
                        [ "ULS4"; "Bottom"; "0.9G + Wu + Wiu"; "0.9"; "0"; "0"; "1"; "0"; "1" ]
                        [ ""; ""; ""; ""; ""; ""; ""; ""; "" ] ]
              let cs = Inputs.combinations g
              Expect.equal cs.Length 2 "blank row skipped"
              let u4 = Inputs.combination "uls4" g
              Expect.equal u4.CriticalFlange Bottom "flange"
              near 1e-12 0.9 (Combination.factor u4 G) "G factor"
              near 1e-12 1.0 (Combination.factor u4 Wiu) "Wiu factor"
              near 1e-12 0.0 (Combination.factor u4 Q) "Q factor"
          }

          test "combinations: single text cell (FILTER found nothing) means no combinations" {
              Expect.isEmpty (Inputs.combinations (grid [ [ "No combinations selected" ] ])) "empty"
          }

          test "restraints table: codes, blanks and αm override" {
              let g =
                  grid
                      [ [ "0"; "A"; "support"; "F"; "F"; ""; "" ]
                        [ "2767"; ""; "purlin"; "L"; "–"; "1.2"; "" ]
                        [ ""; ""; ""; ""; ""; ""; "" ]
                        [ "8300"; "B"; "support"; "P"; "F"; ""; "" ] ]
              let r = Inputs.restraints g |> List.toArray
              Expect.equal r.Length 3 "blank row skipped"
              Expect.equal r.[1].Top (Some RL) "L"
              Expect.equal r.[1].Bottom None "– = none"
              Expect.equal r.[1].AlphaMTop (Some 1.2) "αm top"
              Expect.equal r.[1].AlphaMBottom None "αm bottom blank"
              Expect.equal r.[2].Top (Some RP) "P"
              Expect.throws (fun () -> Inputs.restraints (grid [ [ "0"; ""; ""; "X"; "F"; "" ] ]) |> ignore) "bad code"
          }

          test "k factors 2 x 2" {
              let k = Inputs.kFactors (grid [ [ "1.4"; "1.0" ]; [ "0.85"; "1.0" ] ])
              near 1e-12 1.4 k.KlTop "kl top"
              near 1e-12 0.85 k.KrTop "kr top"
          }

          test "deflection criteria: blank max means ratio only" {
              let g = grid [ [ "500"; "20" ]; [ "360"; "25" ]; [ "300"; "" ]; [ "250"; "30" ]; [ "1000"; "1.5" ] ]
              let c = Inputs.deflectionCriteria g |> List.toArray
              Expect.equal c.[2].MaxMm None "blank max"
              Expect.equal c.[4].MaxMm (Some 1.5) "1kN max"
          } ]
