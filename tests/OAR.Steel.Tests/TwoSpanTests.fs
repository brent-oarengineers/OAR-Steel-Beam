module TwoSpanTests

open Expecto
open OAR.Steel
open Helpers

// Two equal spans L = 6.0 m (supports A at 0, B at 6000, C at 12000)
let private L = 6000.0
let private ei = 200000.0 * 86.4e6
let private beam = Beam.continuous (2.0 * L) ei [ L ]

let private ld case shape mag : Load = { Case = case; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }

let private pt x top bottom : RestraintPoint =
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }

let private k14 =
    { KlDown = 1.4; KlUp = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false; KlTipDown = 2.0; KlTipUp = 1.0 }

let private sagging (b: Beam) acts lo hi =
    Analysis.maxAbsOn lo hi (Analysis.breakpointsOn b acts) (fun x -> max 0.0 (Analysis.moment b acts x))

[<Tests>]
let tests =
    testList
        "Two-span continuous"
        [ test "equal spans, UDL: 3wL/8, 10wL/8, M at B = -wL²/8, sagging 9wL²/128 at 3L/8" {
              let w = 10.0
              let acts = Analysis.actions (2.0 * L) (Combination.single G) [ ld G FullSpan w ]
              let rs = Analysis.supportReactions beam acts
              Expect.equal (rs |> List.map fst) [ 0.0; L; 2.0 * L ] "support positions"
              within 1e-12 (3.0 * w * L / 8.0) (snd rs.[0]) "RA"
              within 1e-12 (10.0 * w * L / 8.0) (snd rs.[1]) "RB"
              within 1e-12 (3.0 * w * L / 8.0) (snd rs.[2]) "RC"
              let ra, rc = Analysis.reactions beam acts
              within 1e-12 (snd rs.[0]) ra "reactions = end supports"
              within 1e-12 (snd rs.[2]) rc "reactions = end supports (C)"
              within 1e-12 (-w * L * L / 8.0) (Analysis.moment beam acts L) "M at B"
              let x, m = sagging beam acts 0.0 L
              within 1e-9 (9.0 * w * L * L / 128.0) m "peak sagging"
              within 1e-6 (3.0 * L / 8.0) x "at 3L/8"
              within 1e-12 0.0 (Analysis.deflection beam acts L) "no deflection at B"
              let _, d = Analysis.maxAbsOn 0.0 L (Analysis.breakpointsOn beam acts) (Analysis.deflection beam acts)
              // propped cantilever: δmax = 0.0054161 wL⁴/EI at 0.4215 L
              within 1e-4 (0.0054161 * w * L ** 4.0 / ei) d "δmax"
              let _, v = Analysis.maxShear beam acts
              within 1e-12 (5.0 * w * L / 8.0) (abs v) "V max 5wL/8 either side of B"
          }

          test "point load at the middle of span 1: 13P/32, 11P/16, -3P/32; M at B = -3PL/32; δ under the load 23PL³/1536EI" {
              let p = 10000.0
              let acts = [ Conc(p, L / 2.0) ]
              let rs = Analysis.supportReactions beam acts |> List.map snd
              within 1e-12 (13.0 * p / 32.0) rs.[0] "RA"
              within 1e-12 (11.0 * p / 16.0) rs.[1] "RB"
              within 1e-12 (-3.0 * p / 32.0) rs.[2] "RC (uplift)"
              within 1e-12 (-3.0 * p * L / 32.0) (Analysis.moment beam acts L) "M at B"
              within 1e-12 (23.0 * p * L ** 3.0 / (1536.0 * ei)) (Analysis.deflection beam acts (L / 2.0)) "δ under the load"
          }

          test "unequal spans, UDL: M at B by the three-moment equation" {
              let w = 10.0
              let b = Beam.continuous 13000.0 ei [ 5000.0 ]
              let acts = Analysis.actions 13000.0 (Combination.single G) [ ld G FullSpan w ]
              within 1e-12 (-(w * 5000.0 ** 3.0 + w * 8000.0 ** 3.0) / (8.0 * 13000.0)) (Analysis.moment b acts 5000.0) "MB"
              let total = Analysis.supportReactions b acts |> List.sumBy snd
              within 1e-12 (w * 13000.0) total "ΣR"
          }

          test "shear deformation: deflection still zero at B, interior reaction slightly lower" {
              let s = Sections.find "310UB40.4"
              let b = Beam.continuous (2.0 * L) (200000.0 * s.Ix) [ L ]
              let bs = Beam.withShear (Units.G_MPa * Beam.shearArea s) b
              let acts = Analysis.actions (2.0 * L) (Combination.single G) [ ld G FullSpan 10.0 ]
              within 1e-9 0.0 (Analysis.deflection bs acts L) "δ at B with shear"
              let rb0 = (Analysis.supportReactions b acts).[1] |> snd
              let rb1 = (Analysis.supportReactions bs acts).[1] |> snd
              Expect.isLessThan rb1 rb0 "shear flexibility moves load to the end supports"
              Expect.isGreaterThan rb1 (0.995 * rb0) "by a small amount"
              within 1e-12 (10.0 * 2.0 * L) (Analysis.supportReactions bs acts |> List.sumBy snd) "ΣR"
          }

          test "pattern variants: Q on span 1 only / span 2 only" {
              let c = Combination.ofFactors "ULS2" [ G, 1.2; Q, 1.5 ]
              let cs = Patterns.expand beam [ c; Combination.ofFactors "ULS1" [ G, 1.35 ] ]
              Expect.equal (cs |> List.map (fun c -> c.Id)) [ "ULS2"; "ULS2-span1"; "ULS2-span2"; "ULS1" ] "ids"
              let loads = [ ld G FullSpan 2.0; ld Q FullSpan 3.0 ]
              let rs c = Analysis.supportReactions beam (Analysis.actions (2.0 * L) c loads) |> List.map snd
              // G 2.4 kN/m on both spans + Q 4.5 kN/m on span 1 only: (3/8, 10/8, 3/8) gL + (7/16, 10/16, -1/16) qL
              let g, q = 2.4, 4.5
              let r1 = rs cs.[1]
              within 1e-12 (3.0 / 8.0 * g * L + 7.0 / 16.0 * q * L) r1.[0] "RA, Q span 1"
              within 1e-12 (10.0 / 8.0 * g * L + 10.0 / 16.0 * q * L) r1.[1] "RB, Q span 1"
              within 1e-12 (3.0 / 8.0 * g * L - 1.0 / 16.0 * q * L) r1.[2] "RC, Q span 1"
              let r2 = rs cs.[2]
              within 1e-12 r1.[0] r2.[2] "mirror image"
              let mb c = Analysis.moment beam (Analysis.actions (2.0 * L) c loads) L
              within 1e-12 (-(g + q) * L * L / 8.0) (mb cs.[0]) "full Q gives the largest hogging at B"
              within 1e-12 (-g * L * L / 8.0 - q * L * L / 16.0) (mb cs.[1]) "one span loaded: -qL²/16"
              Expect.equal (Patterns.equilibrium beam) [] "no EQ combination without an overhang"
          }

          test "SLS: five checks per span, L = each span, Q patterned, Δ1kN at each midspan" {
              let crit = [ for r, m in [ 500.0, 20.0; 360.0, 25.0; 300.0, 30.0; 250.0, 30.0; 1000.0, 1.5 ] -> { Ratio = r; MaxMm = Some m } ]
              let b = Beam.continuous 11000.0 ei [ 6000.0 ] // 6.0 m + 5.0 m
              let rows = Serviceability.check b [ ld G FullSpan 2.0; ld Q FullSpan 3.0 ] crit 0.4 0.7
              Expect.equal (rows |> List.map (fun r -> r.Name))
                  [ "Δg"; "Δg+ψl·q"; "Δg+ψs·q"; "Δq"; "Δ1kN"; "Δg span 2"; "Δg+ψl·q span 2"; "Δg+ψs·q span 2"; "Δq span 2"; "Δ1kN span 2" ] "names"
              within 1e-12 12.0 rows.[0].Limit "span 1: 6000/500"
              within 1e-12 10.0 rows.[5].Limit "span 2: 5000/500"
              for r in rows.[0..4] do Expect.isLessThanOrEqual r.At 6000.0 (r.Name + " in span 1")
              for r in rows.[5..9] do Expect.isGreaterThanOrEqual r.At 6000.0 (r.Name + " in span 2")
              // Δq span 1 with Q on span 1 only is larger than with Q on both spans
              let qAll = Analysis.actions 11000.0 (Combination.single Q) [ ld Q FullSpan 3.0 ]
              let _, dAll = Analysis.maxAbsOn 0.0 6000.0 (Analysis.breakpointsOn b qAll) (Analysis.deflection b qAll)
              Expect.isGreaterThan rows.[3].Actual (abs dAll) "pattern governs Δq"
              Expect.equal (Serviceability.governing rows).Length 5 "governing: five rows"
          }

          test "ULS: segments end at the interior support; hogging over B checks the bottom flange" {
              let s = Sections.find "310UB40.4"
              let b = Beam.continuous (2.0 * L) (200000.0 * s.Ix) [ L ]
              let pts = [ pt 0.0 (Some RF) (Some RF); pt L (Some RF) (Some RF); pt (2.0 * L) (Some RF) (Some RF) ]
              let loads = [ ld G FullSpan 2.0; ld Q FullSpan 3.0 ]
              let c = Combination.ofFactors "ULS2" [ G, 1.2; Q, 1.5 ]
              let r = Uls.checkCombination s 300 200000.0 b loads pts k14 c
              Expect.equal (r |> List.map (fun c -> c.Flange, c.X1, c.X2) |> List.sort)
                  ([ Top, 0.0, L; Top, L, 2.0 * L; Bottom, 0.0, L; Bottom, L, 2.0 * L ] |> List.sort) "four segments"
              let bot = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              within 1e-9 (1.2 * 2.0 * L * L / 8.0 + 1.5 * 3.0 * L * L / 8.0) bot.Mm "M* = wL²/8 at B"
              within 1e-9 L bot.XAtMm "at B"
              Expect.stringContains bot.Note "moment reverses" "note"
          }

          test "ULS: the interior support must be listed and be F, P or C" {
              let s = Sections.find "310UB40.4"
              let b = Beam.continuous (2.0 * L) (200000.0 * s.Ix) [ L ]
              let c = Combination.ofFactors "ULS1" [ G, 1.35 ]
              let loads = [ ld G FullSpan 2.0 ]
              let run pts = Uls.checkCombination s 300 200000.0 b loads pts k14 c |> ignore
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt (2.0 * L) (Some RF) (Some RF) ]) "support B missing"
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt L (Some RL) (Some RF); pt (2.0 * L) (Some RF) (Some RF) ]) "L at support B"
              Expect.throws (fun () -> run [ pt 0.0 (Some RF) (Some RF); pt L (Some RF) (Some RF); pt (2.0 * L) (Some RU) (Some RU) ]) "U at an end support"
          }

          test "continuous beam input checks" {
              Expect.throws (fun () -> Beam.continuous 12000.0 ei [] |> ignore) "no interior support"
              Expect.throws (fun () -> Beam.continuous 12000.0 ei [ 12000.0 ] |> ignore) "interior support at the end"
              let three = Beam.continuous 15000.0 ei [ 5000.0; 10000.0 ]
              Expect.throws (fun () -> Patterns.expand three [ Combination.ofFactors "ULS2" [ G, 1.2; Q, 1.5 ] ] |> ignore) "patterns: two spans only"
              // three spans still analyse (equal spans, UDL: interior reactions 1.1 wL)
              let acts = Analysis.actions 15000.0 (Combination.single G) [ ld G FullSpan 10.0 ]
              let rs = Analysis.supportReactions three acts |> List.map snd
              within 1e-12 (1.1 * 10.0 * 5000.0) rs.[1] "three spans: RB = 1.1 wL"
              within 1e-12 (0.4 * 10.0 * 5000.0) rs.[0] "three spans: RA = 0.4 wL"
          }

          test "simply supported and overhang beams are unchanged (B8 reactions)" {
              let s = Sections.find "310UB40.4"
              let b = Beam.supported 8500.0 (200000.0 * s.Ix) 0.0 7000.0
              let loads = [ ld G FullSpan 1.5; ld Wu (Partial(0.0, 2000.0)) 6.0; ld Wu (Partial(2000.0, 8500.0)) 3.5 ]
              let acts = Analysis.actions 8500.0 (Combination.ofFactors "ULS5" [ G, 0.9; Wu, 1.0 ]) loads
              let rs = Analysis.supportReactions b acts
              Expect.equal rs.Length 2 "two supports"
              within 1e-12 (-(9300.0 * 1000.0 + 13975.0 * 5250.0) / 7000.0) (snd rs.[1]) "RB"
          } ]
