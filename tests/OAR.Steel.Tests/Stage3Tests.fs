module Stage3Tests

open Expecto
open OAR.Steel
open Helpers

let private pt x top bottom : RestraintPoint =
    { X = x; Ref = ""; Top = top; Bottom = bottom; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }

let private k14 =
    { KlDown = 1.4; KlUp = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false; KlTipDown = 2.0; KlTipUp = 1.0 }

let private ei = 200000.0 * 121e6
let private beam = Beam.supported 8000.0 ei 0.0 6000.0 // 6.0 m backspan + 2.0 m overhang

[<Tests>]
let tests =
    testList
        "Stage 3: overhang patterns, equilibrium, SLS, C restraint"
        [ test "pattern variants: Q on backspan only / overhang only" {
              let c = Combination.ofFactors "ULS2" [ G, 1.2; Q, 1.5 ]
              let cs = Patterns.expand beam [ c; Combination.ofFactors "ULS1" [ G, 1.35 ] ]
              Expect.equal (cs |> List.map (fun c -> c.Id)) [ "ULS2"; "ULS2-span"; "ULS2-cant"; "ULS1" ] "ids"
              let q : Load = { Case = Q; Shape = FullSpan; Magnitude = 4.0; Description = ""; Dir = "" }
              let mAtB c = Analysis.moment beam (Analysis.actions 8000.0 c [ q ]) 6000.0
              let span = cs.[1]
              let cant = cs.[2]
              within 1e-9 0.0 (mAtB span) "Q backspan only: no moment at B"
              within 1e-9 (-1.5 * 4.0 * 2000.0 ** 2.0 / 2.0) (mAtB cant) "Q overhang only: -1.5 q a²/2"
              let ra c = fst (Analysis.reactions beam (Analysis.actions 8000.0 c [ q ]))
              within 1e-9 (1.5 * 4.0 * 3000.0) (ra span) "RA, Q backspan only"
              within 1e-9 (-1.5 * 4.0 * 2000.0 * 1000.0 / 6000.0) (ra cant) "RA, Q overhang only (uplift)"
              Expect.equal (Patterns.expand (Beam.simple 8000.0 ei) [ c ]).Length 1 "no variants without an overhang"
          }

          // B2 C5 by construction: 0.9 x 3 kN/m backspan, 1.2 x (3 kN/m + 10 kN tip) + 1.5 x 4 kN/m overhang -> RA 0.90, RB 46.50 (RFEM)
          test "equilibrium combination reproduces RFEM B2 C5 reactions" {
              let ld c shape mag : Load = { Case = c; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld G FullSpan 3.0; ld G (Point 8000.0) 10000.0; ld Q (Partial(6000.0, 8000.0)) 4.0 ]
              let eq = Patterns.equilibrium beam |> List.exactlyOne
              Expect.isFalse eq.Strength "reactions only"
              let ra, rb = Analysis.reactions beam (Analysis.actions 8000.0 eq loads)
              near 0.005 0.90 (Units.toKN ra) "RA (RFEM 0.90)"
              near 0.005 46.50 (Units.toKN rb) "RB (RFEM 46.50)"
          }

          test "SLS with an overhang: span rows then tip rows, tip limit 2a/ratio, Q patterned" {
              let s = Sections.find "360UB44.7"
              let b = Beam.supported 8000.0 (200000.0 * s.Ix) 0.0 6000.0
              let ld c shape mag : Load = { Case = c; Shape = shape; Magnitude = mag; Description = ""; Dir = "" }
              let loads = [ ld G FullSpan 3.0; ld Q FullSpan 4.0 ]
              let crit = [ for r in [ 500.0; 360.0; 300.0; 250.0; 1000.0 ] -> { Ratio = r; MaxMm = None } ]
              let res = Serviceability.check b loads crit 0.4 0.7 |> List.toArray
              Expect.equal res.Length 10 "5 span + 5 tip"
              Expect.equal res.[8].Name "Δq tip" "names"
              within 1e-9 (2.0 * 2000.0 / 250.0) res.[8].Limit "tip limit = 2a / 250"
              within 1e-9 (6000.0 / 250.0) res.[3].Limit "span limit = backspan / 250"
              // Δq tip: worst of Q everywhere / backspan only / overhang only
              let dq (acts: BeamLoad list) = Analysis.deflection b acts 8000.0
              let qAll = dq [ Dist(4.0, 0.0, 8000.0) ]
              let qSpan = dq [ Dist(4.0, 0.0, 6000.0) ]
              let qCant = dq [ Dist(4.0, 6000.0, 8000.0) ]
              let worst = [ qAll; qSpan; qCant ] |> List.maxBy abs
              within 1e-9 (abs worst) res.[8].Actual "Δq tip = worst pattern"
              let gov = Serviceability.governing (List.ofArray res) |> List.toArray
              Expect.equal gov.Length 5 "governing: 5 rows"
              Expect.isTrue (gov.[3].Utilisation >= res.[3].Utilisation && gov.[3].Utilisation >= res.[8].Utilisation) "worse of span / tip"
              let simple = Serviceability.check (Beam.simple 8300.0 (200000.0 * s.Ix)) loads crit 0.4 0.7
              Expect.equal simple.Length 5 "simply supported: 5 rows"
          }

          test "C restraint: continuous length of flange gets φMb = φMsx; the rest is checked normally" {
              let s = Sections.find "250UB31.4"
              let b = Beam.supported 6500.0 (200000.0 * s.Ix) 0.0 5000.0
              let loads : Load list = [ { Case = G; Shape = FullSpan; Magnitude = 8.0; Description = ""; Dir = "" } ]
              // sheeting on the top flange over the backspan only (C at A), free tip
              let pts = [ pt 0.0 (Some RC) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let r = Uls.checkCombination s 300 200000.0 b loads pts k14 (Combination.single G)
              let top = r |> List.find (fun c -> c.Flange = Top && c.X1 = 0.0)
              Expect.equal top.Ends "CF" "C segment"
              within 1e-9 (Capacity.sectionMomentX s 300).PhiMs top.MemberCap.PhiMb "φMb = φMsx"
              let bot = r |> List.find (fun c -> c.Flange = Bottom && c.X1 = 0.0)
              Expect.equal bot.Ends "FF" "bottom flange unaffected by the top-flange C"
              Expect.isLessThan bot.MemberCap.PhiMb (Capacity.sectionMomentX s 300).PhiMs "bottom flange still buckles"
              // C partway: purlin row with C at 2.0 m – segment 0–2.0 ends at a lateral restraint (L), 2.0–5.0 is continuous
              let pts2 = [ pt 0.0 (Some RF) (Some RF); pt 2000.0 (Some RC) None; pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RU) (Some RU) ]
              let r2 = Uls.checkCombination s 300 200000.0 b loads pts2 k14 (Combination.single G)
              let first = r2 |> List.find (fun c -> c.Flange = Top && c.X1 = 0.0)
              Expect.equal first.Ends "FL" "C acts as L at the end of the preceding segment"
              let second = r2 |> List.tryFind (fun c -> c.Flange = Top && c.X1 = 2000.0)
              match second with
              | Some c -> Expect.equal c.Ends "CF" "continuous segment"
              | None -> ()
              Expect.throws
                  (fun () -> Uls.checkCombination s 300 200000.0 b loads [ pt 0.0 (Some RF) (Some RF); pt 5000.0 (Some RF) (Some RF); pt 6500.0 (Some RC) (Some RU) ] k14 (Combination.single G) |> ignore)
                  "C at the right end is an error"
          } ]
