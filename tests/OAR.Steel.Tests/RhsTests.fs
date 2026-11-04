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

          test "shear: both webs, Aw = 2(d - 2t)t, non-uniform shear stress (Cl. 5.11.3) as in the ATM DCT" {
              let s = Sections.find "75x75x5 SHS"
              let v = Capacity.shear s 350
              within 1e-12 650.0 v.Aw "Aw"
              let dm, bm = 70.0, 70.0
              let ratio = (bm * 5.0 * dm / 2.0 + 5.0 * dm ** 2.0 / 4.0) * dm / (5.0 * dm ** 3.0 / 6.0 + bm * 5.0 * dm ** 2.0 / 2.0)
              within 1e-12 ratio v.StressRatio "f*vm/f*va"
              within 1e-9 (0.9 * 2.0 * 0.6 * 350.0 * 650.0 / (0.9 + ratio) / 1000.0) (Units.toKN v.PhiVv) "φVv = 2Vu/(0.9 + ratio)"
              within 1e-12 650.0 (Beam.shearArea s) "shear area for deflection"
              within 1e-12 1.0 (Capacity.shear (Sections.find "310UB40.4") 300).StressRatio "I-sections unchanged"
          }

          // ATM Design Capacity Tables for Structural Steel Hollow Sections (Aug 2013): Tables 5.2-x (φMsx, φVv) and
          // 5.3-x (φMb, αm = 1, kt = kl = kr = 1, so Le = segment length; FLR with βm = -1). Engine checked against all
          // 203 x-axis section rows (φMsx, φVv within 1%) and 969 φMb values (within 1.5%); these are spot checks.
          test "ATM DCT: section and member moment capacities, shear, FLR" {
              let mb (n: string) g (le: float) =
                  let s = Sections.find n
                  let flr = (Capacity.fullLateralRestraintLength s g).Value
                  let c = Capacity.memberMoment s g 200000.0 le 1.0 false
                  Units.toKNm (if le <= flr then c.PhiMs else c.PhiMb)
              let msx (n: string) g = Units.toKNm (Capacity.sectionMomentX (Sections.find n) g).PhiMs
              let vv (n: string) g = Units.toKN (Capacity.shear (Sections.find n) g).PhiVv
              let flr (n: string) g = (Capacity.fullLateralRestraintLength (Sections.find n) g).Value / 1000.0
              // 200x100x6 RHS C450L0, Table 5.3-2(1): φMsx 85.1, φVv 522; φMb at 4 / 6 / 10 / 14 m; FLR 3.44
              within 0.005 85.1 (msx "200x100x6 RHS" 450) "200x100x6 φMsx"
              within 0.005 522.0 (vv "200x100x6 RHS" 450) "200x100x6 φVv"
              for le, v in [ 4.0, 83.2; 6.0, 80.7; 10.0, 76.0; 14.0, 71.5 ] do
                  within 0.006 v (mb "200x100x6 RHS" 450 (le * 1000.0)) (sprintf "200x100x6 φMb at %g m" le)
              within 0.005 3.44 (flr "200x100x6 RHS" 450) "200x100x6 FLR"
              // 150x50x6 RHS C450L0: φMsx 36.9, φVv 374; φMb 35.4 at 2 m, 30.3 at 6 m, 22.6 at 14 m; FLR 1.12
              within 0.005 36.9 (msx "150x50x6 RHS" 450) "150x50x6 φMsx"
              within 0.005 374.0 (vv "150x50x6 RHS" 450) "150x50x6 φVv"
              for le, v in [ 2.0, 35.4; 6.0, 30.3; 14.0, 22.6 ] do
                  within 0.006 v (mb "150x50x6 RHS" 450 (le * 1000.0)) (sprintf "150x50x6 φMb at %g m" le)
              within 0.005 1.12 (flr "150x50x6 RHS" 450) "150x50x6 FLR"
              // 75x25x2.5 RHS C350L0, Table 5.3-1: φMsx 3.17, φVv 61.5, φMb 2.74 at 3 m, 2.30 at 6 m, FLR 0.736
              within 0.005 3.17 (msx "75x25x2.5 RHS" 350) "75x25x2.5 φMsx"
              within 0.006 61.5 (vv "75x25x2.5 RHS" 350) "75x25x2.5 φVv"
              within 0.006 2.74 (mb "75x25x2.5 RHS" 350 3000.0) "75x25x2.5 φMb at 3 m"
              within 0.006 2.30 (mb "75x25x2.5 RHS" 350 6000.0) "75x25x2.5 φMb at 6 m"
              within 0.002 0.736 (flr "75x25x2.5 RHS" 350) "75x25x2.5 FLR"
              // slender: 400x300x8 RHS C450L0 φMsx 463, φVv 1450; 90x90x2.5 SHS C450L0 φMsx 9.03
              within 0.005 463.0 (msx "400x300x8 RHS" 450) "400x300x8 φMsx (effective width)"
              within 0.005 1450.0 (vv "400x300x8 RHS" 450) "400x300x8 φVv"
              within 0.005 9.03 (msx "90x90x2.5 SHS" 450) "90x90x2.5 SHS φMsx (effective width)"
          }

          test "ULS: an RHS segment within the FLR takes φMb = φMsx" {
              let s = Sections.find "150x50x6 RHS"
              let b = Beam.simple 1000.0 (200000.0 * s.Ix)
              let loads: Load list = [ { Case = G; Shape = FullSpan; Magnitude = 1.0; Description = ""; Dir = "" } ]
              let pt x : RestraintPoint = { X = x; Ref = ""; Top = Some RF; Bottom = Some RF; AlphaMTop = None; AlphaMBottom = None; KlOverrideTop = None; KlOverrideBottom = None }
              let k = { KlDown = 1.4; KlUp = 1.0; KrTop = 1.0; KrBottom = 1.0; ContinuousTop = false; ContinuousBottom = false; KlTipDown = 2.0; KlTipUp = 1.0 }
              let r = Uls.checkCombination s 450 200000.0 b loads [ pt 0.0; pt 1000.0 ] k (Combination.single G)
              let top = r |> List.find (fun c -> c.Flange = Top)
              within 1e-12 top.MemberCap.PhiMs top.MemberCap.PhiMb "1.0 m ≤ FLR 1.12 m"
              Expect.stringContains top.Note "Cl. 5.3.2.4" "note"
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
