module Helpers

open Expecto

/// Relative check: |actual/expected - 1| <= tol. Message shows both values and the difference.
let within (tol: float) (expected: float) (actual: float) (what: string) =
    let diff = if expected = 0.0 then abs actual else abs (actual / expected - 1.0)
    if diff > tol then
        failtestf "%s: expected %g, got %g (%.3f%%, tolerance %.3f%%)" what expected actual (diff * 100.0) (tol * 100.0)

/// Absolute check: |actual - expected| <= tol
let near (tol: float) (expected: float) (actual: float) (what: string) =
    if abs (actual - expected) > tol then
        failtestf "%s: expected %g ± %g, got %g" what expected tol actual
