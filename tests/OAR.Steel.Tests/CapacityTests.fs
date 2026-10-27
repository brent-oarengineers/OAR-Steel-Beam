module CapacityTests

open Expecto
open OAR.Steel
open Helpers

// Benchmarks: AISC Design Capacity Tables for Structural Steel, Vol 1: Open Sections (DCT/V1/03-1999),
// Section 5.3.6, pages 5-15 to 5-17. Published values are 3 significant figures, so tolerance is 0.5%.
// Note: 1999 tables; current section properties may differ slightly from those used in the DCT.
[<Tests>]
let tests =
    testList
        "Capacity"
        [ test "DCT Example 1: 200UB29.8, segment FF, Le = 4.0 m, αm = 1.0" {
              let s = Sections.find "200UB29.8"
              let le = Capacity.effectiveLength 1.0 1.0 1.0 4000.0
              near 1e-9 4000.0 le "Le = kt kl kr L"
              let r = Capacity.memberMoment s 300 200000.0 le 1.0 false
              within 0.005 47.6 (Units.toKNm r.PhiMb) "φMb"
              within 0.005 225.0 (Units.toKN (Capacity.shear s 300).PhiVv) "φVv"
          }

          test "DCT Example 2: 310UB46.2, φMsx, φVv and φMb at Le = 12 m" {
              let s = Sections.find "310UB46.2"
              within 0.005 197.0 (Units.toKNm (Capacity.sectionMomentX s 300).PhiMs) "φMsx"
              within 0.005 356.0 (Units.toKN (Capacity.shear s 300).PhiVv) "φVv"
              let r = Capacity.memberMoment s 300 200000.0 12000.0 1.0 false
              within 0.005 40.0 (Units.toKNm r.PhiMb) "φMb (Le = 12 m, αm = 1.0)"
          }

          test "DCT Example 2: Le = 1.0 x 1.4 x 1.0 x 8.0 m = 11.2 m" {
              near 1e-9 11200.0 (Capacity.effectiveLength 1.0 1.4 1.0 8000.0) "Le"
          }

          // Equal moments at the quarter points (constant 40 kN.m over the middle half):
          // Cl. 5.6.1.1(a)(iii) gives 1.7/sqrt(3) = 0.9815. DCT uses 1.088 from Table 5.6.1,
          // which the add-in only applies through the αm override.
          test "αm formula for DCT Example 2 moment diagram" {
              near 1e-9 (1.7 / sqrt 3.0) (Capacity.alphaMFormula 40.0 40.0 40.0 40.0) "αm (iii)"
          }

          test "αm formula: uniform moment gives 0.98, capped at 2.5" {
              near 1e-9 (1.7 / sqrt 3.0) (Capacity.alphaMFormula 10.0 10.0 10.0 10.0) "uniform"
              near 1e-9 2.5 (Capacity.alphaMFormula 100.0 1.0 1.0 1.0) "cap"
          }

          test "φMb never exceeds φMs and override flag is kept" {
              let s = Sections.find "310UB40.4"
              let r = Capacity.memberMoment s 300 200000.0 500.0 2.5 true
              near 1e-6 r.PhiMs r.PhiMb "short segment capped at φMs"
              Expect.isTrue r.AlphaMOverridden "override flag"
          }

          // Independent cross-check: same formulas computed in a separate Python script.
          test "310UB40.4 at Le = 11.62 m (Blockpad segment 1 length)" {
              let s = Sections.find "310UB40.4"
              let r = Capacity.memberMoment s 300 200000.0 11620.0 1.0 false
              within 0.001 40.92 (Units.toKNm r.Mo) "Mo"
              within 0.001 0.176563 r.AlphaS "αs"
          } ]
