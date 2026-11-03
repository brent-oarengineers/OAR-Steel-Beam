module LoadRowTests

open Expecto
open OAR.Steel
open Helpers

let private span = 8300.0
let private beam = Beam.simple (span) (200000.0 * 86.4e6)

let private grid (rows: string list list) =
    let r = rows.Length
    let c = rows |> List.map List.length |> List.max
    Array2D.init r c (fun i j ->
        let v = if j < rows.[i].Length then rows.[i].[j] else ""
        match System.Double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
        | true, x -> Num x
        | _ -> if v = "" then Blank else Str v)

let private mid (combo: Combination) loads =
    Analysis.moment beam (Analysis.actions span combo loads) (span / 2.0)

// v0.3 layout: Type | Trib | Description | G | Q | Wd | Wu | Start | End | Dir
[<Tests>]
let tests =
    testList
        "Load rows (v0.3 layout)"
        [ test "one row, several cases: trib x pressure per case" {
              let ls = Inputs.loadRows span (grid [ [ "UDL"; "5.65"; "roof"; "0.10"; "0.25"; ""; "1.72"; ""; ""; "" ] ])
              Expect.equal ls.Length 3 "G, Q and Wu"
              let byCase lc = ls |> List.find (fun l -> l.Case = lc)
              near 1e-12 0.565 (byCase G).Magnitude "G 5.65 x 0.10"
              near 1e-12 1.4125 (byCase Q).Magnitude "Q 5.65 x 0.25"
              near 1e-12 9.718 (byCase Wu).Magnitude "Wu 5.65 x 1.72"
              Expect.equal (byCase G).Shape FullSpan "UDL"
          }

          test "no trib: values are direct kN/m, or kN for a point load" {
              let ls =
                  Inputs.loadRows
                      span
                      (grid
                          [ [ "UDL"; ""; "bracket"; "0.05"; ""; ""; ""; ""; ""; "" ]
                            [ "Point"; ""; "unit"; "2.75"; "1.0"; ""; ""; "4150"; ""; "" ]
                            [ "Point"; "1.5"; "platform"; "0.5"; ""; ""; ""; "2000"; ""; "" ] ])
                  |> List.toArray
              near 1e-12 0.05 ls.[0].Magnitude "direct kN/m"
              near 1e-9 2750.0 ls.[1].Magnitude "2.75 kN = 2750 N"
              Expect.equal ls.[1].Shape (Point 4150.0) "position"
              near 1e-9 1000.0 ls.[2].Magnitude "Q 1.0 kN"
              near 1e-9 750.0 ls.[3].Magnitude "1.5 m² x 0.5 kPa = 0.75 kN"
          }

          test "blank and zero rows are skipped; Dir tags wind values only" {
              let ls =
                  Inputs.loadRows
                      span
                      (grid
                          [ [ ""; ""; ""; ""; ""; ""; ""; ""; ""; "" ]
                            [ "UDL"; "5.65"; "wind zero"; ""; ""; "0"; "0"; ""; ""; "" ]
                            [ "PUDL"; "5.65"; "zone 1"; "0.1"; ""; ""; "0.9"; "0"; "2000"; "Cross" ] ])
              Expect.equal ls.Length 2 "G and Wu from the zone row"
              let g = ls |> List.find (fun l -> l.Case = G)
              let w = ls |> List.find (fun l -> l.Case = Wu)
              Expect.equal g.Dir "" "G ignores Dir"
              Expect.equal w.Dir "Cross" "Wu tagged"
              Expect.equal w.Shape (Partial(0.0, 2000.0)) "PUDL"
          }

          test "errors name the row" {
              let bad rows = Expect.throws (fun () -> Inputs.loadRows span (grid rows) |> ignore) "should fail"
              bad [ [ "UDL"; ""; ""; "-1"; ""; ""; ""; ""; ""; "" ] ]
              bad [ [ "PUDL"; ""; ""; "1"; ""; ""; ""; "5000"; "1000"; "" ] ]
              bad [ [ "Point"; ""; ""; "1"; ""; ""; ""; ""; ""; "" ] ]
              bad [ [ "Strip"; ""; ""; "1"; ""; ""; ""; ""; ""; "" ] ]
              bad [ [ ""; "5.65"; "no type"; "1"; ""; ""; ""; ""; ""; "" ] ]
          }

          test "Inputs.loads reads either layout" {
              let v3 = grid [ [ "UDL"; "5.65"; "roof"; "0.10"; "0.25"; ""; ""; ""; ""; "" ] ]
              let v2 =
                  grid
                      [ [ "G"; "UDL"; "roof"; "5.65"; "0.10"; ""; ""; ""; ""; "" ]
                        [ "Q"; "UDL"; "roof"; "5.65"; "0.25"; ""; ""; ""; ""; "" ] ]
              let a = Inputs.loads span v3
              let b = Inputs.loads span v2
              Expect.equal (a |> List.map (fun l -> l.Case, l.Magnitude)) (b |> List.map (fun l -> l.Case, l.Magnitude)) "same loads"
          }

          // Internal pressure entered as a Wu / Wd row must give the same actions as the v0.2 Wiu / Wid cases.
          test "internal pressure as a Wu / Wd row matches the separate Wi cases" {
              let v3 =
                  Inputs.loads
                      span
                      (grid
                          [ [ "UDL"; "5.65"; "roof"; "0.10"; "0.25"; ""; ""; ""; ""; "" ]
                            [ "UDL"; "5.65"; "external"; ""; ""; "0.40"; "1.72"; ""; ""; "" ]
                            [ "UDL"; "5.65"; "+cpi"; ""; ""; ""; "0.20"; ""; ""; "" ]
                            [ "UDL"; "5.65"; "-cpi"; ""; ""; "0.30"; ""; ""; ""; "" ] ])
              let v2 =
                  Inputs.loads
                      span
                      (grid
                          [ [ "G"; "UDL"; ""; "5.65"; "0.10"; ""; ""; "" ]
                            [ "Q"; "UDL"; ""; "5.65"; "0.25"; ""; ""; "" ]
                            [ "Wd"; "UDL"; ""; "5.65"; "0.40"; ""; ""; "" ]
                            [ "Wu"; "UDL"; ""; "5.65"; "1.72"; ""; ""; "" ]
                            [ "Wiu"; "UDL"; ""; "5.65"; "0.20"; ""; ""; "" ]
                            [ "Wid"; "UDL"; ""; "5.65"; "0.30"; ""; ""; "" ] ])
              let up3 = Combination.ofFactors "ULS5" [ G, 0.9; Wu, 1.0 ]
              let up2 = Combination.ofFactors "ULS5" [ G, 0.9; Wu, 1.0; Wiu, 1.0 ]
              let dn3 = Combination.ofFactors "ULS4" [ G, 1.2; Q, 0.4; Wd, 1.0 ]
              let dn2 = Combination.ofFactors "ULS4" [ G, 1.2; Q, 0.4; Wd, 1.0; Wid, 1.0 ]
              within 1e-12 (mid up2 v2) (mid up3 v3) "uplift"
              within 1e-12 (mid dn2 v2) (mid dn3 v3) "downward"
              within 1e-12 ((0.9 * 0.565 - 5.65 * 1.92) * span * span / 8.0) (mid up3 v3) "wL²/8"
          }

          test "stepped crosswind suction plus uniform longitudinal, checked separately" {
              let ls =
                  Inputs.loads
                      span
                      (grid
                          [ [ "UDL"; "5.65"; "roof"; "0.10"; ""; ""; ""; ""; ""; "" ]
                            [ "PUDL"; "5.65"; "0 to h/2"; ""; ""; ""; "0.90"; "0"; "2000"; "Cross" ]
                            [ "PUDL"; "5.65"; "h/2 to h"; ""; ""; ""; "0.50"; "2000"; "4000"; "Cross" ]
                            [ "PUDL"; "5.65"; "h to 2h"; ""; ""; ""; "0.30"; "4000"; "8300"; "Cross" ]
                            [ "UDL"; "5.65"; "longitudinal"; ""; ""; ""; "0.60"; ""; ""; "Long" ]
                            [ "UDL"; "5.65"; "+cpi"; ""; ""; ""; "0.20"; ""; ""; "" ] ])
              let combo dir =
                  let c = Combination.ofFactors ("ULS5" + dir) [ G, 0.9; Wu, 1.0 ]
                  { c with Dir = dir }
              let ra, rb = Analysis.reactions beam (Analysis.actions span (combo "Cross") ls)
              // 0.9 x 0.565 x 8.3 - 5.65 x (0.9 x 2 + 0.5 x 2 + 0.3 x 4.3 + 0.2 x 8.3) kN
              let expected = 0.9 * 0.565 * 8300.0 - 5.65 * (0.9 * 2000.0 + 0.5 * 2000.0 + 0.3 * 4300.0 + 0.2 * 8300.0)
              within 1e-12 expected (ra + rb) "ΣR cross (N)"
              within 1e-12 ((0.9 * 0.565 - 5.65 * 0.8) * span * span / 8.0) (mid (combo "Long") ls) "long wL²/8"
          }

          test "combinations table, 8 columns (G Q Wd Wu Dir)" {
              let cs =
                  Inputs.combinations
                      (grid
                          [ [ "ULS4C"; "Top"; "1.2G + ψcQ + Wd"; "1.2"; "0.4"; "1"; "0"; "Cross" ]
                            [ "ULS5"; "Bottom"; "0.9G + Wu"; "0.9"; "0"; "0"; "1"; "" ] ])
              Expect.equal cs.Length 2 "two"
              Expect.equal cs.[0].Dir "Cross" "dir"
              near 1e-12 0.4 (Combination.factor cs.[0] Q) "ψc"
              near 1e-12 1.0 (Combination.factor cs.[1] Wu) "Wu"
              Expect.equal cs.[1].CriticalFlange Bottom "flange"
          } ]
