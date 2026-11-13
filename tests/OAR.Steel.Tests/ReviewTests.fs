module ReviewTests

open Expecto
open OAR.Steel
open Helpers

let private ld case shape mag desc : Load = { Case = case; Shape = shape; Magnitude = mag; Description = desc; Dir = "" }
let private all = { Loads = true; Reactions = true; Shear = true; Moment = true; Deflection = true; Restraints = true }
let private data title beam loads items : ReviewData =
    { Title = title; Beam = beam; Loads = loads; Items = items; Restraints = []; ContinuousTop = false; ContinuousBottom = false; RestraintsNote = "" }
let private rp x top bottom : RestraintPoint =
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }
let private texts (s: Scene) =
    s.Prims
    |> List.choose (function
        | PText(_, _, t, _, _, _, _) -> Some t
        | _ -> None)
let private hasText (s: Scene) (part: string) = texts s |> List.exists (fun t -> t.Contains part)

// the waler: 200UC46.2, spans 2.5 + 3.0 m, 70 kN/m working + self-weight, 1.4G
let private waler () =
    let sec = Sections.find "200UC46.2"
    let beam = Beam.continuous 5500.0 (200000.0 * sec.Ix) [ 2500.0 ]
    let loads = [ Analysis.selfWeightLoad sec; ld G FullSpan 70.0 "70kN/m working" ]
    let combos = Patterns.expand beam [ { Combination.ofFactors "ULS6" [ G, 1.4 ] with Description = "1.4xWorking" } ]
    data "B1" beam loads (Review.items loads combos 0.4 0.7 0.47)

[<Tests>]
let tests =
    testList
        "Load review"
        [ test "items: only the cases with loads, then the combinations; no envelope for a single combination" {
              let d = waler ()
              Expect.equal (d.Items |> List.map (fun i -> i.Group, i.Id)) [ "Load cases", "G"; "Combinations", "ULS6" ] "G and ULS6"
          }

          test "two-span combination: factored load label, reactions and peaks match the sheet" {
              let d = waler ()
              let sol = Review.solve d (d.Items |> List.find (fun i -> i.Id = "ULS6"))
              let c = sol.Cases.Head
              let w = 1.4 * (70.0 + Units.selfWeight 46.2)
              let mB = w * (2500.0 ** 3.0 + 3000.0 ** 3.0) / (8.0 * 5500.0)
              within 1e-9 (Units.toKNm mB) (-Units.toKNm (Analysis.moment c.Det c.All 2500.0)) "hogging at B = w(L1³+L2³)/8(L1+L2)"
              within 1e-9 (w * 5500.0) (c.Reactions |> List.sumBy snd) "reactions sum to the load"
              let s = Review.scene d sol all 1000.0 760.0
              Expect.isTrue (hasText s "1.4 × 70.0 = 98.0 kN/m") "factored UDL label"
              Expect.isTrue (hasText s "B = 341 kN") "reaction at B"
              Expect.isTrue (hasText s "-95.6  @ 2.50 m") "hogging peak"
              Expect.isTrue (hasText s "68.3  @ 4.32 m") "span 2 sagging peak at the point of zero shear"
              Expect.isTrue (hasText s "not a serviceability value") "ULS deflection is flagged"
              Expect.stringContains (Review.readout sol 2500.0) "M = -95.6 kN·m" "hover readout"
          }

          test "unfactored case: no factor in the label; tick boxes drop their parts" {
              let d = waler ()
              let sol = Review.solve d d.Items.Head
              let s = Review.scene d sol { all with Shear = false; Moment = false; Deflection = false } 1000.0 760.0
              Expect.isTrue (hasText s "70kN/m working:  70.0 kN/m") "plain value"
              Expect.isFalse (hasText s "Shear") "no shear panel"
              let none = Review.scene d sol { all with Loads = false; Reactions = false } 1000.0 760.0
              Expect.isFalse (hasText none "kN/m") "no load labels"
              Expect.isFalse (hasText none "A = ") "no reaction values"
              Expect.isTrue (hasText none "Deflection (mm, plotted downward)") "G deflection is a serviceability value"
          }

          test "overhang: patterns, envelope, hold-down, point loads in kN, upward loads" {
              let sec = Sections.find "310UB40.4"
              let beam = Beam.supported 8000.0 (200000.0 * sec.Ix) 0.0 6000.0
              let loads =
                  [ ld G FullSpan 3.2 "Roof"; ld Q FullSpan 1.5 "Live"; ld G (Point 2000.0) 12000.0 "B7 reaction"
                    ld G (Point 8000.0) -4000.0 "Tie-down"; ld Wu FullSpan 4.6 "Uplift" ]
              let combos =
                  Patterns.expand beam
                      [ Combination.ofFactors "ULS2" [ G, 1.2; Q, 1.5 ]; Combination.ofFactors "ULS4" [ G, 0.9; Wu, 1.0 ] ]
                  @ Patterns.equilibrium beam
              let d = data "B1" beam loads (Review.items loads combos 0.4 0.7 0.47)
              let ids = d.Items |> List.map (fun i -> i.Id)
              Expect.equal
                  ids
                  [ "G"; "Q"; "Wu"; "G+ψl·Q"; "G+ψs·Q"; "Ws up"; "Envelope"; "ULS2"; "ULS2-span"; "ULS2-cant"; "ULS4"; "EQ" ]
                  "list"
              let scene id = let sol = Review.solve d (d.Items |> List.find (fun i -> i.Id = id)) in sol, Review.scene d sol all 1000.0 760.0
              let _, s2 = scene "ULS2"
              Expect.isTrue (hasText s2 "B7 reaction:  1.2 × 12.0 = 14.4 kN") "point load in kN with its factor"
              Expect.isTrue (hasText s2 "Tie-down:  1.2 × 4.00 = 4.80 kN") "upward point load"
              // Q on the overhang only: the live load block covers 6 to 8 m only
              let solC, _ = scene "ULS2-cant"
              let q = solC.Cases.Head.Applied |> List.choose (function Dist(w, a, b) when abs (w - 2.25) < 1e-9 -> Some(a, b) | _ -> None)
              Expect.equal q [ 6000.0, 8000.0 ] "patterned Q"
              let sol4, s4 = scene "ULS4"
              Expect.isLessThan (snd sol4.Cases.Head.Reactions.[1]) 0.0 "B is in tension under 0.9G + Wu"
              Expect.isTrue (hasText s4 "(hold-down)") "flagged"
              let solE, sE = scene "Envelope"
              Expect.equal solE.Cases.Length 4 "strength combinations only (not EQ)"
              Expect.isTrue (hasText sE " to ") "reaction ranges"
              Expect.stringContains (Review.readout solE 3000.0) " to " "envelope readout gives ranges"
              // the envelope covers every single combination at every station
              for id in [ "ULS2"; "ULS2-span"; "ULS2-cant"; "ULS4" ] do
                  let sol, _ = scene id
                  let c = sol.Cases.Head
                  for x in [ 500.0; 2300.0; 6000.0; 7500.0 ] do
                      let m = Analysis.moment c.Det c.All x
                      let ms = solE.Cases |> List.map (fun e -> Analysis.moment e.Det e.All x)
                      Expect.isTrue (m >= List.min ms - 1e-6 && m <= List.max ms + 1e-6) "inside the envelope"
          }

          test "flange restraints strip: codes above (top) and below (bottom) the line at their positions" {
              // F both flanges at the supports, purlins (L, top only) at 1,250 crs in span 1, C on the bottom flange of span 2
              let d =
                  { waler () with
                      Restraints =
                          [ rp 0.0 (Some RF) (Some RF); rp 1250.0 (Some RL) None; rp 2500.0 (Some RF) (Some RC)
                            rp 4000.0 (Some RL) None; rp 5500.0 (Some RF) (Some RF) ] }
              let sol = Review.solve d d.Items.Head
              let s = Review.scene d sol all 1000.0 760.0
              let codes =
                  s.Prims
                  |> List.choose (function
                      | PText(x, y, t, _, _, Centre, true) when t.Length = 1 && "FPLUC".Contains t -> Some(t, x, y)
                      | _ -> None)
              let lineY =
                  s.Prims
                  |> List.pick (function
                      | PLine(_, y1, _, y2, _, w, false) when w = 2.5 && y1 = y2 -> Some y1
                      | _ -> None)
              let top = codes |> List.filter (fun (_, _, y) -> y < lineY) |> List.sortBy (fun (_, x, _) -> x)
              let bottom = codes |> List.filter (fun (_, _, y) -> y > lineY) |> List.sortBy (fun (_, x, _) -> x)
              Expect.equal (top |> List.map (fun (t, _, _) -> t)) [ "F"; "L"; "F"; "L"; "F" ] "top flange"
              Expect.equal (bottom |> List.map (fun (t, _, _) -> t)) [ "F"; "C"; "F" ] "bottom flange: no L"
              let px x = s.X0 + (s.X1 - s.X0) * x / 5500.0
              within 1e-9 (px 1250.0) (top |> List.item 1 |> fun (_, x, _) -> x) "purlin at 1,250"
              // the C runs as a band from B to C on the bottom side
              Expect.isTrue
                  (s.Prims |> List.exists (function
                      | PLine(xa, ya, xb, _, _, w, false) when w = 3.0 && ya > lineY -> abs (xa - px 2500.0) < 1e-6 && abs (xb - px 5500.0) < 1e-6
                      | _ -> false))
                  "continuous band"
              Expect.isFalse (hasText (Review.scene d sol { all with Restraints = false } 1000.0 760.0) "Flange restraints") "tick box off"
              Expect.isFalse (hasText (Review.scene (waler ()) sol all 1000.0 760.0) "Flange restraints") "no table, no strip"
              let bad = Review.scene { waler () with RestraintsNote = "row 3: top flange code 'X'" } sol all 1000.0 760.0
              Expect.isTrue (hasText bad "Restraints table: row 3") "a table error is shown, not thrown"
          }

          test "the picture is valid SVG text and stays inside the window" {
              let d = waler ()
              let sol = Review.solve d (d.Items |> List.last)
              let s = Review.scene d sol all 1000.0 760.0
              let svg = Review.svg s
              Expect.isTrue (svg.StartsWith "<svg") "svg"
              Expect.stringContains svg "kN·m" "units"
              for p in s.Prims do
                  match p with
                  | PPolyline(pts, _, _) ->
                      for x, y in pts do
                          Expect.isTrue (x >= 0.0 && x <= 1000.0 && y >= 0.0 && y <= 760.0) "curve inside the window"
                  | _ -> ()
          } ]
