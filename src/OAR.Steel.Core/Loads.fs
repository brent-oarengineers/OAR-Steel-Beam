namespace OAR.Steel

/// Load cases as used on the input sheet.
/// Direction is fixed by the case: G, Q, Wd, Wid act down; Wu, Wiu act up.
type LoadCase =
    | G
    | Q
    | Wd
    | Wu
    | Wid
    | Wiu

module LoadCase =
    let all = [ G; Q; Wd; Wu; Wid; Wiu ]

    let tryParse (s: string) =
        match (if isNull s then "" else s.Trim().ToUpperInvariant()) with
        | "G" -> Some G
        | "Q" -> Some Q
        | "WD" -> Some Wd
        | "WU" -> Some Wu
        | "WID" -> Some Wid
        | "WIU" -> Some Wiu
        | _ -> None

    /// +1 = downward (gravity direction), -1 = upward
    let direction =
        function
        | G
        | Q
        | Wd
        | Wid -> 1.0
        | Wu
        | Wiu -> -1.0

    let name =
        function
        | G -> "G"
        | Q -> "Q"
        | Wd -> "Wd"
        | Wu -> "Wu"
        | Wid -> "Wid"
        | Wiu -> "Wiu"

/// Where a load sits on the span (positions in mm from the left support)
type Shape =
    | FullSpan
    | Partial of a: float * b: float
    | Point of x: float

/// An input load. Magnitude is positive and in N/mm (line loads) or N (point loads);
/// the case sets its direction.
type Load =
    { Case: LoadCase
      Shape: Shape
      Magnitude: float
      Description: string }

type Flange =
    | Top
    | Bottom

/// A load combination: factors on each case (all positive) and the critical flange
/// that selects the restraint set for member bending checks.
type Combination =
    { Id: string
      Description: string
      CriticalFlange: Flange
      Factors: Map<LoadCase, float> }

module Combination =
    let factor (c: Combination) (lc: LoadCase) =
        c.Factors.TryFind lc |> Option.defaultValue 0.0

    /// A combination with factor 1.0 on one case only (for unfactored case results)
    let single (lc: LoadCase) : Combination =
        { Id = LoadCase.name lc
          Description = LoadCase.name lc
          CriticalFlange = Top
          Factors = Map.ofList [ lc, 1.0 ] }

    let ofFactors id (factors: (LoadCase * float) list) : Combination =
        { Id = id
          Description = id
          CriticalFlange = Top
          Factors = Map.ofList factors }
