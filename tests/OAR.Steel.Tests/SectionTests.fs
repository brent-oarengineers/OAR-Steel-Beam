module SectionTests

open Expecto
open OAR.Steel
open Helpers

[<Tests>]
let tests =
    testList
        "Sections"
        [ test "data loads and common sections are present" {
              let all = Sections.all ()
              Expect.isGreaterThan all.Length 60 "number of complete sections"
              for n in [ "310UB40.4"; "310UB46.2"; "200UB29.8"; "250PFC"; "1200WB455" ] do
                  Expect.isOk (Sections.tryFind n) n
          }

          test "designation lookup ignores spaces and case" {
              let s = Sections.find "310 ub 40.4"
              Expect.equal s.Designation "310UB40.4" "designation"
          }

          // Source: OAR Section Data sheet (310UB40.4 row) and Blockpad beam calc properties
          test "310UB40.4 properties in base units" {
              let s = Sections.find "310UB40.4"
              near 1e-6 40.4 s.Mass "mass"
              near 1.0 86.4e6 s.Ix "Ix"
              near 1.0 7.65e6 s.Iy "Iy"
              near 1.0 157.0e3 s.J "J"
              near 1.0e3 165.0e9 s.Iw "Iw"
              Expect.isFalse s.IwCalculated "Iw tabulated"
              Expect.equal (Sections.fy s 300) (320.0, 320.0) "fy Gr300"
          }

          test "incomplete sections give a clear error" {
              match Sections.tryFind "310UC137" with
              | Ok _ -> failtest "310UC137 should be flagged incomplete in the current data"
              | Error e -> Expect.stringContains e "incomplete" "message"
          }

          test "unknown section gives a clear error" {
              match Sections.tryFind "999UB1" with
              | Ok _ -> failtest "should not exist"
              | Error e -> Expect.stringContains e "not found" "message"
          }

          // Check of the Iw = Iy (d - tf)^2 / 4 formula used for sections without tabulated Iw.
          test "Iw formula matches tabulated Iw for all UB/UC within 1%" {
              for s in Sections.all () do
                  if (s.Family = UB || s.Family = UC) && not s.IwCalculated then
                      within 0.01 s.Iw (Sections.iwDoublySymmetric s.Iy s.d s.tf) (s.Designation + " Iw")
          }

          // Check of the AS 4100 Cl. 5.2 Ze calculation against the tabulated φMsx (Grade 300)
          // for every section that has one. Tables are rounded to 3 significant figures.
          test "calculated φMsx matches tabulated Grade 300 values within 0.5%" {
              let checkedSections =
                  Sections.all ()
                  |> List.choose (fun s ->
                      match s.PhiMsxTab300, s.Fy.TryFind 300 with
                      | Some tab, Some _ -> Some(s, tab)
                      | _ -> None)
              for s, tab in checkedSections do
                  let calc = Units.toKNm (Capacity.sectionMomentX s 300).PhiMs
                  within 0.005 tab calc (s.Designation + " φMsx")
              let n = checkedSections.Length
              Expect.isGreaterThan n 30 "number of sections checked"
          } ]
