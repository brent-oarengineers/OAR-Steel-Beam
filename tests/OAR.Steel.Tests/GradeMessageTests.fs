module GradeMessageTests

open Expecto
open OAR.Steel

[<Tests>]
let tests =
    testList
        "Grade messages"
        [ test "SHS with Grade 300: says which grades it comes in" {
              let s = Sections.find "200x200x9 SHS"
              let msg =
                  try
                      Sections.fy s 300 |> ignore
                      ""
                  with e ->
                      e.Message
              Expect.stringContains msg "C350L0 or C450L0 only" "grades it comes in"
              Expect.stringContains msg "set the steel grade to 350 (C350L0) or 450 (C450L0)" "what to enter"
              Expect.equal (Sections.fy s 350) (350.0, 350.0) "Grade 350 works"
          }

          test "open section: message unchanged" {
              let s = Sections.find "310UB40.4"
              let msg =
                  try
                      Sections.fy s 450 |> ignore
                      ""
                  with e ->
                      e.Message
              Expect.stringContains msg "no yield stress data for Grade 450" "open section"
          } ]
