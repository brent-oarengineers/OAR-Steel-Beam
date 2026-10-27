namespace OAR.Steel

open System
open System.Globalization

/// A worksheet cell value, independent of Excel. The Excel layer converts ranges into Cell grids.
type Cell =
    | Num of float
    | Str of string
    | Blank

/// Parse the input tables on the beam sheet into domain records.
/// Column orders match the sheet tables (see comments on each function).
module Inputs =

    let private inv = CultureInfo.InvariantCulture

    let private text =
        function
        | Str s -> s.Trim()
        | Num v -> v.ToString(inv)
        | Blank -> ""

    let private optNum (row: int) (col: string) =
        function
        | Num v -> Some v
        | Blank -> None
        | Str s when s.Trim() = "" || s.Trim() = "–" || s.Trim() = "-" -> None
        | Str s ->
            match Double.TryParse(s.Trim(), NumberStyles.Float, inv) with
            | true, v -> Some v
            | _ -> failwithf "row %d: %s '%s' is not a number" row col s

    let private isBlankRow (cells: Cell[]) =
        cells |> Array.forall (fun c -> text c = "")

    /// Loads table, 9 columns:
    /// Case | Type | Description | Trib (m or m2) | Pressure (kPa) | Direct (kN/m or kN) | Start (mm) | End (mm) | Load (ignored)
    /// Magnitude = trib x pressure + direct. Line loads end up in N/mm (= kN/m), point loads in N.
    /// Rows with a blank Case are skipped. Row numbers in errors count from 1 = first data row.
    let loads (span: float) (grid: Cell[,]) : Load list =
        if Array2D.length2 grid < 8 then failwith "loads: expected at least 8 columns (Case..End)"
        [ for i in 0 .. Array2D.length1 grid - 1 do
              let row = i + 1
              let c j = grid.[i, j]
              let cells = Array.init (min 8 (Array2D.length2 grid)) c
              let caseText = text (c 0)
              if not (isBlankRow cells) && caseText <> "" then
                  let lc =
                      match LoadCase.tryParse caseText with
                      | Some x -> x
                      | None -> failwithf "row %d: unknown case '%s' (use G, Q, Wd, Wu, Wid, Wiu)" row caseText
                  let trib = optNum row "Trib" (c 3)
                  let press = optNum row "Pressure" (c 4)
                  let direct = optNum row "Direct" (c 5) |> Option.defaultValue 0.0
                  let fromTrib =
                      match trib, press with
                      | Some t, Some p -> t * p
                      | None, None -> 0.0
                      | Some _, None -> 0.0 // trib with no pressure contributes nothing
                      | None, Some _ -> failwithf "row %d: pressure given without a trib width/area" row
                  let mag = fromTrib + direct
                  if mag < 0.0 then failwithf "row %d: load is negative; enter magnitudes positive and use the case for direction" row
                  let start = optNum row "Start" (c 6)
                  let finish = optNum row "End" (c 7)
                  let kind = (text (c 1)).ToUpperInvariant()
                  let shape, magnitude =
                      match kind with
                      | "UDL"
                      | "" -> FullSpan, mag // kN/m = N/mm
                      | "PUDL" ->
                          (match start, finish with
                           | Some a, Some b when a >= 0.0 && b > a && b <= span + 1e-6 -> Partial(a, min b span), mag
                           | Some a, Some b -> failwithf "row %d: PUDL needs 0 <= start < end <= span (got %g to %g, span %g)" row a b span
                           | _ -> failwithf "row %d: PUDL needs start and end positions" row)
                      | "POINT" ->
                          (match start with
                           | Some x when x >= 0.0 && x <= span + 1e-6 -> Point(min x span), Units.kN mag
                           | Some x -> failwithf "row %d: point load position %g is outside the span (0 to %g)" row x span
                           | None -> failwithf "row %d: point load needs a position in Start" row)
                      | k -> failwithf "row %d: unknown type '%s' (use UDL, PUDL or Point)" row k
                  let load: Load =
                      { Case = lc
                        Shape = shape
                        Magnitude = magnitude
                        Description = text (c 2) }
                  yield load ]

    /// Combinations table, 9 columns:
    /// ID | Crit. flange | Combination | G | Q | Wd | Wu | Wid | Wiu
    let combinations (grid: Cell[,]) : Combination list =
        // a single cell (e.g. FILTER's "No combinations selected") means no combinations
        if Array2D.length1 grid = 1 && Array2D.length2 grid = 1 then
            []
        else
            if Array2D.length2 grid < 9 then failwith "combinations: expected 9 columns (ID..Wiu)"
            [ for i in 0 .. Array2D.length1 grid - 1 do
                  let row = i + 1
                  let c j = grid.[i, j]
                  let id = text (c 0)
                  if id <> "" then
                      let flange =
                          match (text (c 1)).ToUpperInvariant() with
                          | "TOP" -> Top
                          | "BOTTOM" -> Bottom
                          | f -> failwithf "combination row %d: critical flange '%s' must be Top or Bottom" row f
                      let factors =
                          [ G; Q; Wd; Wu; Wid; Wiu ]
                          |> List.mapi (fun k lc -> lc, (optNum row (LoadCase.name lc) (c (3 + k)) |> Option.defaultValue 0.0))
                          |> List.filter (fun (_, f) -> f <> 0.0)
                      if factors |> List.exists (fun (_, f) -> f < 0.0) then
                          failwithf "combination row %d: factors must be positive (direction comes from the case)" row
                      let combo: Combination =
                          { Id = id
                            Description = text (c 2)
                            CriticalFlange = flange
                            Factors = Map.ofList factors }
                      yield combo ]

    /// Find one combination by ID
    let combination (id: string) (grid: Cell[,]) =
        let all = combinations grid
        match all |> List.tryFind (fun c -> String.Equals(c.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) with
        | Some c -> c
        | None -> failwithf "combination '%s' not found (have: %s)" id (String.Join(", ", all |> List.map (fun c -> c.Id)))

    /// Deflection criteria, 5 rows x 2 columns: Span ratio | Max (mm, blank = ratio only)
    let deflectionCriteria (grid: Cell[,]) : DeflectionCriterion list =
        if Array2D.length1 grid <> 5 || Array2D.length2 grid < 2 then
            failwith "deflection criteria: expected 5 rows x 2 columns (ratio, max mm)"
        [ for i in 0..4 do
              let ratio =
                  match optNum (i + 1) "ratio" grid.[i, 0] with
                  | Some r when r > 0.0 -> r
                  | _ -> failwithf "deflection criteria row %d: span ratio must be a positive number" (i + 1)
              yield
                  { Ratio = ratio
                    MaxMm = optNum (i + 1) "max" grid.[i, 1] } ]

    /// Restraints table, 5 to 7 columns:
    /// x (mm) | Ref | Description | Top flange | Bottom flange | [αm top] | [αm bottom]
    /// αm top / bottom = override for that flange's segment that starts at this row and runs to the next restraint.
    /// Restraint codes F, P, L, U; blank or "–" = no restraint to that flange at this point.
    let restraints (grid: Cell[,]) : RestraintPoint list =
        if Array2D.length2 grid < 5 then failwith "restraints: expected at least 5 columns (x, Ref, Description, Top, Bottom)"
        let code (row: int) (col: string) (c: Cell) =
            match (text c).Trim() with
            | ""
            | "–"
            | "-"
            | "—" -> None
            | t ->
                (match RestraintType.tryParse t with
                 | Some r -> Some r
                 | None -> failwithf "restraint row %d: %s flange code '%s' must be F, P, L, U or –" row col t)
        [ for i in 0 .. Array2D.length1 grid - 1 do
              let row = i + 1
              let xo = optNum row "x" grid.[i, 0]
              if xo.IsSome then
                  let amCol (j: int) name =
                      let v = if Array2D.length2 grid > j then optNum row name grid.[i, j] else None
                      match v with
                      | Some a when a <= 0.0 || a > 2.5 -> failwithf "restraint row %d: %s must be > 0 and <= 2.5" row name
                      | _ -> v
                  let amTop = amCol 5 "αm top"
                  let amBot = amCol 6 "αm bottom"
                  let p: RestraintPoint =
                      { X = xo.Value
                        Ref = text grid.[i, 1]
                        Top = code row "top" grid.[i, 3]
                        Bottom = code row "bottom" grid.[i, 4]
                        AlphaMTop = amTop
                        AlphaMBottom = amBot }
                  yield p ]

    /// k factors, 2 x 2: [kl top, kl bottom; kr top, kr bottom]
    let kFactors (grid: Cell[,]) : KFactors =
        let rows = Array2D.length1 grid
        if (rows <> 2 && rows <> 3) || Array2D.length2 grid <> 2 then
            failwith "k factors: expected 2 x 2 (kl, kr) or 3 x 2 (kl, kr, continuous restraint Yes/No) for top and bottom flanges"
        let v i j name =
            match optNum (i + 1) name grid.[i, j] with
            | Some x when x > 0.0 -> x
            | _ -> failwithf "k factors: %s must be a positive number" name
        let cont j =
            if rows = 3 then
                (match (text grid.[2, j]).ToUpperInvariant() with
                 | "YES" | "Y" | "TRUE" | "1" -> true
                 | "NO" | "N" | "FALSE" | "0" | "" -> false
                 | t -> failwithf "continuous restraint must be Yes or No (got '%s')" t)
            else false
        { KlTop = v 0 0 "kl top"
          KlBottom = v 0 1 "kl bottom"
          KrTop = v 1 0 "kr top"
          KrBottom = v 1 1 "kr bottom"
          ContinuousTop = cont 0
          ContinuousBottom = cont 1 }

    let yesNo (c: Cell) =
        match (text c).ToUpperInvariant() with
        | "YES"
        | "Y"
        | "TRUE"
        | "1" -> true
        | "NO"
        | "N"
        | "FALSE"
        | "0"
        | "" -> false
        | s -> failwithf "expected Yes or No, got '%s'" s
