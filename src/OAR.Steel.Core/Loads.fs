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

    /// External or internal wind case (the only cases a wind direction applies to)
    let isWind =
        function
        | Wd
        | Wu
        | Wid
        | Wiu -> true
        | G
        | Q -> false

    let name =
        function
        | G -> "G"
        | Q -> "Q"
        | Wd -> "Wd"
        | Wu -> "Wu"
        | Wid -> "Wid"
        | Wiu -> "Wiu"

/// Wind direction tags ("Cross", "Long", ...). Blank = applies in every direction.
/// Compared trimmed and case-insensitive.
module WindDir =
    open System

    let normalise (s: string) = if isNull s then "" else s.Trim()

    let same (a: string) (b: string) =
        String.Equals(normalise a, normalise b, StringComparison.OrdinalIgnoreCase)

    /// Distinct non-blank tags in order of first appearance
    let distinct (tags: string seq) =
        tags
        |> Seq.map normalise
        |> Seq.filter (fun t -> t <> "")
        |> Seq.fold (fun (acc: string list) t -> if acc |> List.exists (same t) then acc else acc @ [ t ]) []

/// Where a load sits on the span (positions in mm from the left support)
type Shape =
    | FullSpan
    | Partial of a: float * b: float
    | Point of x: float

/// An input load. Magnitude is positive and in N/mm (line loads) or N (point loads);
/// the case sets its direction. Dir = wind direction tag for wind cases ("" = all directions).
type Load =
    { Case: LoadCase
      Shape: Shape
      Magnitude: float
      Description: string
      Dir: string }

type Flange =
    | Top
    | Bottom

/// A load combination: factors on each case (all positive) and the critical flange
/// that selects the restraint set for member bending checks. Dir = wind direction the combination
/// applies to: its wind factors pick up loads tagged with that direction plus untagged wind loads.
type Combination =
    { Id: string
      Description: string
      CriticalFlange: Flange
      Factors: Map<LoadCase, float>
      Dir: string }

module Combination =
    let factor (c: Combination) (lc: LoadCase) =
        c.Factors.TryFind lc |> Option.defaultValue 0.0

    /// A combination with factor 1.0 on one case only (for unfactored case results)
    let single (lc: LoadCase) : Combination =
        { Id = LoadCase.name lc
          Description = LoadCase.name lc
          CriticalFlange = Top
          Factors = Map.ofList [ lc, 1.0 ]
          Dir = "" }

    /// One wind case in one direction, factor 1.0, id e.g. "Wu Cross"
    let singleDir (lc: LoadCase) (dir: string) : Combination =
        let d = WindDir.normalise dir
        if d = "" then
            single lc
        else
            { Id = LoadCase.name lc + " " + d
              Description = LoadCase.name lc + " " + d
              CriticalFlange = Top
              Factors = Map.ofList [ lc, 1.0 ]
              Dir = d }

    let ofFactors id (factors: (LoadCase * float) list) : Combination =
        { Id = id
          Description = id
          CriticalFlange = Top
          Factors = Map.ofList factors
          Dir = "" }
