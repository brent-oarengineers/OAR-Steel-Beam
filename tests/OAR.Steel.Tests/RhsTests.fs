module RhsTests

open Expecto
open OAR.Steel
open Helpers

[<Tests>]
let tests =
    testList
        "RHS / SHS"
        [ // OAR "Closed Section Bending-Compression Capacity.xlsm", RHS & SHS sheet: 75x75x5 SHS, fy 350, l = 2.7 m,
          // kt = kl = kr = 1, αm = 1: Zex 33.6 (compact), φMsx 10.584, Mo 198.72, αs 1.004, φMbx 10.584 kN.m
          test "75x75x5 SHS C350L0 matches the OAR closed-section sheet" {
              let s = Sections.find "75x75x5 SHS"
              Expect.equal s.Family SHS "family"
              within 1e-12 0.0 s.Iw "closed section: Iw = 0"
              near 0.01 10.28 s.Mass "mass = Ag x 7850 kg/m3 (catalogue 10.3)"
              let sm = Capacity.sectionMomentX s 350
              Expect.equal sm.Class Compact "compact"
              within 1e-9 33.6e3 sm.Ze "Ze = S"
              within 1e-9 10.584 (Units.toKNm sm.PhiMs) "φMsx"
              within 1e-6 198.72446908790153 (Units.toKNm (Capacity.mo s 200000.0 2700.0)) "Mo, Iw = 0"
              let mb = Capacity.memberMoment s 350 200000.0 2700.0 1.0 false
              within 1e-9 10.584 (Units.toKNm mb.PhiMb) "φMbx capped at φMsx"
          }

          test "flange slenderness uses the clear width between the webs, CF limits 30 / 40" {
              let s = Sections.find "75x75x5 SHS"
              let sm = Capacity.sectionMomentX s 350
              within 1e-12 (65.0 / 5.0 * sqrt (350.0 / 250.0)) sm.Flange.Lambda "λ = (b - 2t)/t √(fy/250)"
              Expect.equal (sm.Flange.LambdaP, sm.Flange.LambdaY) (30.0, 40.0) "CF, both edges supported"
          }

          test "slender sections: effective-width Ze reproduces the tabulated C450L0 Zex" {
              let tabulated = Sections.all () |> List.filter (fun s -> s.ZexTab450.IsSome)
              Expect.isGreaterThan tabulated.Length 15 "tabulated sizes"
              for s in tabulated do
                  let sm = Capacity.sectionMomentX s 450
                  within 0.006 s.ZexTab450.Value sm.Ze (s.Designation + " Ze (C450L0)")
          }

          test "non-compact: Ze interpolated between Z and min(S, 1.5Z)" {
              let s = Sections.find "300x200x6 RHS"
              let sm = Capacity.sectionMomentX s 350
              Expect.equal sm.Class NonCompact "C350: non-compact"
              let lam = (200.0 - 12.0) / 6.0 * sqrt (350.0 / 250.0)
              let zc = min s.Sx (1.5 * s.Zx)
              within 1e-12 (s.Zx + (40.0 - lam) / (40.0 - 30.0) * (zc - s.Zx)) sm.Ze "Cl. 5.2.4"
              Expect.equal (Capacity.sectionMomentX s 450).Class Slender "C450: slender"
          }

          test "shear: both webs, Aw = 2(d - 2t)t" {
              let s = Sections.find "75x75x5 SHS"
              let v = Capacity.shear s 350
              within 1e-12 650.0 v.Aw "Aw"
              within 1e-9 (0.9 * 0.6 * 350.0 * 650.0 / 1000.0) (Units.toKN v.PhiVv) "φVv"
              within 1e-12 650.0 (Beam.shearArea s) "shear area for deflection"
          }

          test "RHS grades are C350L0 and C450L0 only" {
              let s = Sections.find "150x50x3 RHS"
              Expect.throws (fun () -> Sections.fy s 300 |> ignore) "Grade 300 refused"
              Expect.equal (Sections.fy s 450) (450.0, 450.0) "C450L0"
              Expect.equal (Sections.find "150X50X3RHS").Designation "150x50x3 RHS" "designation matching ignores case and spaces"
          }

          test "an RHS beam runs through the ULS check (Iw = 0, LTB from Iy and J)" {
              let s = Sections.find "150x50x3 RHS"
              let b = Beam.simple 4000.0 (200000.0 * s.Ix)
              let loads: Load list = [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = ""; Dir = "" } ]
              let pts: RestraintPoint list =
                  [ { X = 0.0; Ref = ""; Top = Some RF; Bottom = Some RF; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }
                    { X = 4000.0; Ref = ""; Top = Some RF; Bottom = Some RF; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None } ]
              let k = { KlDown = 1.4; KlUp = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false; KlTipDown = 2.0; KlTipUp = 1.0 }
              let r = Uls.checkCombination s 450 200000.0 b loads pts k (Combination.single G)
              let top = r |> List.find (fun c -> c.Flange = Top)
              within 1e-9 5600.0 top.MemberCap.Le "Le = 1.4 x 4.0 m"
              Expect.isLessThanOrEqual top.MemberCap.PhiMb top.MemberCap.PhiMs "φMb ≤ φMs"
              Expect.isGreaterThan top.MemberCap.PhiMb (0.5 * top.MemberCap.PhiMs) "a closed section keeps most of φMs"
          } ]
