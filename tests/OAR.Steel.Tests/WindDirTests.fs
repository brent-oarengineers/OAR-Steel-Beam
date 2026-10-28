module WindDirTests

open Expecto
open OAR.Steel
open Helpers

let private span = 8300.0
let private beam = { Span = span; EI = 200000.0 * 86.4e6 }

let private grid (rows: string list list) =
    let r = rows.Length
    let c = rows |> List.map List.length |> List.max
    Array2D.init r c (fun i j ->
        let v = if j < rows.[i].Length then rows.[i].[j] else ""
        match System.Double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture) with
        | true, x -> Num x
        | _ -> if v = "" then Blank else Str v)

let private ld case shape mag dir : Load =
    { Case = case; Shape = shape; Magnitude = mag; Description = ""; Dir = dir }

// G 1.0 kN/m; crosswind uplift in two zones (3.0 kN/m to 2 m, 1.5 kN/m beyond); longitudinal uplift
// uniform 2.0 kN/m; internal pressure 0.5 kN/m untagged (acts in both directions).
let private loads =
    [ ld G FullSpan 1.0 ""
      ld Wu (Partial(0.0, 2000.0)) 3.0 "Cross"
      ld Wu (Partial(2000.0, span)) 1.5 "cross" // tags are case-insensitive
      ld Wu FullSpan 2.0 "Long"
      ld Wiu FullSpan 0.5 "" ]

let private uplift (dir: string) : Combination =
    let c = Combination.ofFactors ("ULS5" + dir) [ G, 0.9; Wu, 1.0; Wiu, 1.0 ]
    { c with Dir = dir }

[<Tests>]
let tests =
    testList
        "Wind directions"
        [ test "crosswind combination picks up crosswind zones and untagged wind only" {
              let acts = Analysis.actions span (uplift "Cross") loads
              let ra, rb = Analysis.reactions beam acts
              // hand statics: 0.9 x 8300 - 3 x 2000 - 1.5 x 6300 - 0.5 x 8300 = -12130 N
              within 1e-12 (-12130.0) (ra + rb) "ΣR"
              within 1e-9 (-7203.554216867469) ra "RA by moments about B"
              within 1e-9 (-10972375.0) (Analysis.moment beam acts 4150.0) "M midspan (N.mm)"
          }

          test "longitudinal combination is uniform: wL²/8 with w = 0.9 - 2.0 - 0.5" {
              let acts = Analysis.actions span (uplift "Long") loads
              within 1e-12 (-1.6 * span * span / 8.0) (Analysis.moment beam acts (span / 2.0)) "M midspan"
          }

          test "wind combination without Dir is refused once wind loads are split by direction" {
              Expect.throws (fun () -> Analysis.actions span (uplift "") loads |> ignore) "would add directions together"
          }

          test "gravity combination is unaffected by wind tags" {
              let acts = Analysis.actions span (Combination.ofFactors "ULS1" [ G, 1.35 ]) loads
              within 1e-12 (1.35 * span * span / 8.0) (Analysis.moment beam acts (span / 2.0)) "1.35G"
          }

          test "no tags anywhere: behaves as before" {
              let plain = [ ld G FullSpan 1.0 ""; ld Wu FullSpan 2.0 "" ]
              let acts = Analysis.actions span (uplift "") plain
              within 1e-12 ((0.9 - 2.0) * span * span / 8.0) (Analysis.moment beam acts (span / 2.0)) "M"
          }

          test "single-case reaction per direction" {
              let ra, rb = Analysis.reactions beam (Analysis.actions span (Combination.singleDir Wu "Long") loads)
              within 1e-12 (-2.0 * span) (ra + rb) "Wu Long"
              let c = Combination.singleDir Wiu "Cross"
              Expect.equal c.Id "Wiu Cross" "id"
          }

          test "loads table Dir column (10 columns) and errors" {
              let g =
                  grid
                      [ [ "G"; "UDL"; "roof"; "5.65"; "0.10"; ""; ""; ""; "0.565"; "" ]
                        [ "Wu"; "PUDL"; "zone 1"; "5.65"; "0.9"; ""; "0"; "2000"; "5.085"; "Cross" ]
                        [ "Wu"; "UDL"; "long"; "5.65"; "0.5"; ""; ""; ""; "2.825"; "Long" ] ]
              let ls = Inputs.loads span g |> List.toArray
              Expect.equal ls.[0].Dir "" "G untagged"
              Expect.equal ls.[1].Dir "Cross" "cross"
              Expect.equal ls.[2].Dir "Long" "long"
              let bad = grid [ [ "G"; "UDL"; ""; ""; ""; "1"; ""; ""; "1"; "Cross" ] ]
              Expect.throws (fun () -> Inputs.loads span bad |> ignore) "Dir on a G load"
              // 9 columns (Case..Load, numeric Load) and 8 columns still read, untagged
              let nine = grid [ [ "Wu"; "UDL"; ""; ""; ""; "1"; ""; ""; "1" ] ]
              Expect.equal (Inputs.loads span nine).Head.Dir "" "9 columns"
          }

          test "combinations table Dir column" {
              let g =
                  grid
                      [ [ "ULS5C"; "Bottom"; "0.9G + Wu + Wi"; "0.9"; "0"; "0"; "1"; "0"; "1"; "Cross" ]
                        [ "ULS1"; "Top"; "1.35G"; "1.35"; "0"; "0"; "0"; "0"; "0"; "" ] ]
              let cs = Inputs.combinations g
              Expect.equal cs.[0].Dir "Cross" "dir"
              Expect.equal cs.[1].Dir "" "blank"
          }

          test "directions are checked separately, never added" {
              let m c = Analysis.moment beam (Analysis.actions span c loads) (span / 2.0)
              Expect.isLessThan (m (uplift "Long")) (m (uplift "Cross")) "longitudinal hogging is larger here"
              within 1e-12 (-1.6 * span * span / 8.0) (m (uplift "LONG")) "upper-case tag matches"
          } ]
