/// Excel worksheet functions. This file only converts between Excel values and the core library;
/// all engineering is in OAR.Steel.Core so it can be tested without Excel.
module OAR.Steel.Excel.Functions

open System
open ExcelDna.Integration
open OAR.Steel

[<Literal>]
let Category = "OAR Steel"

let private version = "0.8.4"

// ---------- conversion helpers ----------

let private toCell (o: obj) : Cell =
    match o with
    | null -> Blank
    | :? float as v -> Num v
    | :? string as s -> if s.Trim() = "" then Blank else Str s
    | :? bool as b -> Str(if b then "TRUE" else "FALSE")
    | :? ExcelEmpty
    | :? ExcelMissing -> Blank
    | :? ExcelError -> failwith "an input cell contains an Excel error"
    | v -> Str(string v)

let private grid (o: obj) : Cell[,] =
    match o with
    | :? (obj[,]) as a ->
        a
        |> Array2D.mapi (fun i j v ->
            match v with
            | :? ExcelError as err ->
                failwithf "input range has an Excel error (%A) at row %d, column %d of the range" err (i + 1) (j + 1)
            | _ -> toCell v)
    | v -> Array2D.create 1 1 (toCell v)

/// Name the argument in any error raised while reading it
let private arg (name: string) (f: unit -> 'T) : 'T =
    try
        f ()
    with ex ->
        failwithf "%s: %s" name ex.Message

let private isMissing (o: obj) =
    match o with
    | null -> true
    | :? ExcelMissing
    | :? ExcelEmpty -> true
    | :? string as s -> s.Trim() = ""
    | _ -> false

let private num (name: string) (o: obj) =
    match toCell o with
    | Num v -> v
    | Str s ->
        (match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
         | true, v -> v
         | _ -> failwithf "%s: '%s' is not a number" name s)
    | Blank -> failwithf "%s is blank" name

let private numOr (name: string) (dflt: float) (o: obj) = if isMissing o then dflt else num name o

let private str (o: obj) =
    match toCell o with
    | Str s -> s.Trim()
    | Num v -> v.ToString(Globalization.CultureInfo.InvariantCulture)
    | Blank -> ""

let private grade (o: obj) = int (Math.Round(numOr "grade" 300.0 o))

/// Run a function and turn any failure into readable text in the cell
let private guard (f: unit -> obj) : obj =
    try
        f ()
    with ex ->
        box ("#OAR! " + ex.Message)

let private table (rows: obj list list) : obj =
    if rows.IsEmpty then
        box "none"
    else
        let r = List.length rows
        let c = rows |> List.map List.length |> List.max
        let a = Array2D.create r c (box "")
        rows |> List.iteri (fun i row -> row |> List.iteri (fun j v -> a.[i, j] <- v))
        box a

let private section (o: obj) = Sections.find (str o)

/// Loads list with self-weight added when required
let private loadsWithSW (sec: Section) (span: float) (includeSW: obj) (loads: obj) =
    let ls = Inputs.loads span (grid loads)
    let sw = if isMissing includeSW then true else Inputs.yesNo (toCell includeSW)
    if sw then Analysis.selfWeightLoad sec :: ls else ls

/// Beam geometry from the sheet: span (or backspan, or span 1), overhang beyond B, and span 2 (B–C) of a
/// two-span continuous beam. Overhang and span 2 are zero when not used, and never both non-zero.
type private Geom =
    { Span1: float
      Overhang: float
      Span2: float }

/// The span argument: one cell (simply supported span), two cells {backspan, overhang}, or three cells
/// {span 1, overhang, span 2}. With an overhang the supports are at 0 and the backspan, and the beam runs on
/// to the tip; with span 2 the beam is continuous over B and runs on to support C.
let private geometry (o: obj) : Geom =
    let one (v: obj) = num "span" v
    let opt name (v: obj) = if isMissing v then 0.0 else num name v
    let g =
        match o with
        | :? (obj[,]) as a ->
            (match a |> Seq.cast<obj> |> Seq.toList with
             | [ v ] -> { Span1 = one v; Overhang = 0.0; Span2 = 0.0 }
             | [ v1; v2 ] -> { Span1 = one v1; Overhang = opt "overhang" v2; Span2 = 0.0 }
             | [ v1; v2; v3 ] -> { Span1 = one v1; Overhang = opt "overhang" v2; Span2 = opt "span 2" v3 }
             | _ -> failwith "span: give one cell (span), two cells (backspan, overhang) or three (span 1, overhang, span 2)")
        | v -> { Span1 = one v; Overhang = 0.0; Span2 = 0.0 }
    if g.Span1 <= 0.0 then failwith "span must be positive"
    if g.Overhang < 0.0 then failwith "overhang must be zero or positive"
    if g.Span2 < 0.0 then failwith "span 2 must be zero or positive"
    if g.Overhang > 0.0 && g.Span2 > 0.0 then
        failwith "two spans with an overhang aren't supported yet: set the overhang or span 2 to zero"
    g

let private totalLength (g: Geom) = g.Span1 + g.Overhang + g.Span2

let private beamOf (g: Geom) (ei: float) =
    if g.Span2 > 0.0 then Beam.continuous (g.Span1 + g.Span2) ei [ g.Span1 ]
    elif g.Overhang > 0.0 then Beam.supported (g.Span1 + g.Overhang) ei 0.0 g.Span1
    else Beam.simple g.Span1 ei

/// Strength combinations from the combos range, with pattern variants when the beam has an overhang
let private strengthCombos (beam: Beam) (combos: obj) =
    arg "combos" (fun () -> Patterns.expand beam (Inputs.combinations (grid combos)))

/// Shear deformation in deflections when the [shearDefl] argument is Yes (G·As, As = clear web)
let private withShearOpt (sec: Section) (o: obj) (b: Beam) =
    if not (isMissing o) && Inputs.yesNo (toCell o) then Beam.withShear (Units.G_MPa * Beam.shearArea sec) b else b

// ---------- functions ----------

// volatile so a saved sheet opened with a different add-in shows the add-in actually in use
[<ExcelFunction(Name = "OAR.VERSION", Description = "OAR Steel add-in version", Category = Category, IsVolatile = true)>]
let oarVersion () : obj = box ("OAR Steel " + version)

[<ExcelFunction(Name = "OAR.SECTION",
                Description = "Section property in table units: Mass kg/m; d bf tf tw mm; Ag mm2; Ix Iy x10^6 mm4; Zx Sx Ze x10^3 mm3; J x10^3 mm4; Iw x10^9 mm6; fyf fyw MPa; phiMsx kN.m; phiVv kN; Class",
                Category = Category)>]
let oarSection
    ([<ExcelArgument(Name = "designation", Description = "e.g. 310UB40.4")>] designation: obj)
    ([<ExcelArgument(Name = "property", Description = "Mass, d, bf, tf, tw, Ag, Ix, Zx, Sx, Iy, J, Iw, fyf, fyw, Ze, phiMsx, phiVv, Class")>] prop: obj)
    ([<ExcelArgument(Name = "[grade]", Description = "250, 300, 350 or 400 (default 300)")>] gr: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let p = (str prop).ToUpperInvariant()
        let g = grade gr
        match p with
        | "MASS" -> box s.Mass
        | "D" -> box s.d
        | "BF" -> box s.bf
        | "TF" -> box s.tf
        | "TW" -> box s.tw
        | "AG" -> box s.Ag
        | "IX" -> box (s.Ix / 1e6)
        | "ZX" -> box (s.Zx / 1e3)
        | "SX" -> box (s.Sx / 1e3)
        | "IY" -> box (s.Iy / 1e6)
        | "J" -> box (s.J / 1e3)
        | "IW" -> box (s.Iw / 1e9)
        | "FYF" -> box (fst (Sections.fy s g))
        | "FYW" -> box (snd (Sections.fy s g))
        | "ZE" -> box ((Capacity.sectionMomentX s g).Ze / 1e3)
        | "PHIMSX" -> box (Units.toKNm (Capacity.sectionMomentX s g).PhiMs)
        | "PHIVV" -> box (Units.toKN (Capacity.shear s g).PhiVv)
        | "CLASS" -> box (sprintf "%A" (Capacity.sectionMomentX s g).Class)
        | other -> failwithf "unknown property '%s'" other)

[<ExcelFunction(Name = "OAR.SECTIONPROPS", Description = "Spills a property list for a section: name | value | unit | note", Category = Category)>]
let oarSectionProps
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "[grade]", Description = "default 300")>] gr: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = grade gr
        let sm = Capacity.sectionMomentX s g
        let sv = Capacity.shear s g
        let fyf, fyw = Sections.fy s g
        let row (n: string) (v: float) (u: string) (note: string) = [ box n; box v; box u; box note ]
        table
            [ row "Mass" s.Mass "kg/m" ""
              row "Self-weight" (Units.selfWeight s.Mass) "kN/m" "g = 9.81"
              row "d" s.d "mm" ""
              row "bf" s.bf "mm" ""
              row "tf" s.tf "mm" ""
              row "tw" s.tw "mm" ""
              row "Ag" s.Ag "mm²" ""
              row "Ix" (s.Ix / 1e6) "×10⁶ mm⁴" ""
              row "Zx" (s.Zx / 1e3) "×10³ mm³" ""
              row "Sx" (s.Sx / 1e3) "×10³ mm³" ""
              row "Iy" (s.Iy / 1e6) "×10⁶ mm⁴" ""
              row "J" (s.J / 1e3) "×10³ mm⁴" ""
              row "Iw" (s.Iw / 1e9) "×10⁹ mm⁶" (if s.IwCalculated then "calculated Iy(d-tf)²/4" elif Family.isHollow s.Family then "closed section: 0" else "")
              row "fyf" fyf "MPa" (if Family.isHollow s.Family then sprintf "C%dL0" g else sprintf "Grade %d" g)
              row "fyw" fyw "MPa" (if Family.isHollow s.Family then sprintf "C%dL0" g else sprintf "Grade %d" g)
              row "λe flange" sm.Flange.Lambda "" (sprintf "λep %g, λey %g" sm.Flange.LambdaP sm.Flange.LambdaY)
              row "λe web" sm.Web.Lambda "" (sprintf "λep %g, λey %g" sm.Web.LambdaP sm.Web.LambdaY)
              row "Ze" (sm.Ze / 1e3) "×10³ mm³" (sprintf "%A, governed by %s%s" sm.Class sm.Governing (if Family.isHollow s.Family && sm.Class = Slender && sm.Governing = "flange" then " (effective width)" else ""))
              row "φMsx" (Units.toKNm sm.PhiMs) "kN·m" "AS 4100 Cl. 5.2"
              row "φVv" (Units.toKN sv.PhiVv) "kN" (sprintf "Aw %.0f mm², λw %.1f, αv %.3f%s" sv.Aw sv.Lambda sv.AlphaV (if sv.StressRatio > 1.0 then sprintf ", f*vm/f*va %.3f (Cl. 5.11.3)" sv.StressRatio else "")) ])

[<ExcelFunction(Name = "OAR.SLS",
                Description = "Spills deflection checks: case | Δ max mm | at x mm | limit mm | L/Δ | utilisation (5 rows; 10 with an overhang: span then tip)",
                Category = Category)>]
let oarSLS
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa (default 200000)")>] e: obj)
    ([<ExcelArgument(Name = "includeSW", Description = "Yes/No (default Yes)")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads", Description = "Loads table data range (Type..Dir; older Case..Dir / Case..End layouts also accepted)")>] loads: obj)
    ([<ExcelArgument(Name = "criteria", Description = "5x2 range: span ratio | max mm (6 rows adds the wind serviceability check)")>] criteria: obj)
    ([<ExcelArgument(Name = "psiL")>] psiL: obj)
    ([<ExcelArgument(Name = "psiS")>] psiS: obj)
    ([<ExcelArgument(Name = "[shearDefl]", Description = "Yes = include shear deformation (default No)")>] shearDefl: obj)
    ([<ExcelArgument(Name = "[mode]", Description = "blank = every row (with an overhang: the span rows then the tip rows); governing = one row per check, the worse of span and tip")>] mode: obj)
    ([<ExcelArgument(Name = "[windFactor]", Description = "SLS / ULS wind factor for the 6th (wind) check, e.g. 0.47")>] windFactor: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = geometry span
        let l = totalLength g
        let beam = beamOf g (numOr "E" Units.E_MPa e * s.Ix) |> withShearOpt s shearDefl
        let ls = loadsWithSW s l includeSW loads
        let crit = Inputs.deflectionCriteria (grid criteria)
        let kw = if isMissing windFactor then 0.0 else num "windFactor" windFactor
        let all = Serviceability.checkWith beam ls crit (num "psiL" psiL) (num "psiS" psiS) kw
        let res = if (str mode).ToUpperInvariant().StartsWith "GOV" then Serviceability.governing all else all
        table
            [ for r in res ->
                  [ box r.Name
                    box r.Actual
                    box r.At
                    box r.Limit
                    (if Double.IsInfinity r.SpanRatio then box "–" else box r.SpanRatio)
                    box r.Utilisation ] ])

[<ExcelFunction(Name = "OAR.DIAGRAM",
                Description = "Spills x | V* kN | M* kN.m | δ mm along the span for one combination (unfactored case if comboId is G, Q, Wd...)",
                Category = Category)>]
let oarDiagram
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa (default 200000)")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos", Description = "Combinations table data range (ID..Dir: G Q Wd Wu, or older G..Wiu)")>] combos: obj)
    ([<ExcelArgument(Name = "comboId", Description = "e.g. ULS2, or a single case G, Q, Wu...")>] comboId: obj)
    ([<ExcelArgument(Name = "[points]", Description = "number of intervals (default 20)")>] points: obj)
    ([<ExcelArgument(Name = "[shearDefl]", Description = "Yes = include shear deformation (default No)")>] shearDefl: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = geometry span
        let l = totalLength g
        let beam = beamOf g (numOr "E" Units.E_MPa e * s.Ix) |> withShearOpt s shearDefl
        let ls = loadsWithSW s l includeSW loads
        let id = str comboId
        // a single case, optionally with a wind direction ("Wu", "Wu Cross"), or a combination ID
        let parts = id.Split([| ' ' |], 2, StringSplitOptions.RemoveEmptyEntries)
        let combo =
            match (if parts.Length > 0 then LoadCase.tryParse parts.[0] else None) with
            | Some lc -> Combination.singleDir lc (if parts.Length > 1 then parts.[1] else "")
            | None ->
                let all = strengthCombos beam combos @ Patterns.equilibrium beam
                match all |> List.tryFind (fun c -> String.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) with
                | Some c -> c
                | None -> failwithf "combination '%s' not found (have: %s)" id (String.Join(", ", all |> List.map (fun c -> c.Id)))
        let acts = Analysis.actions l combo ls
        let n = max 2 (int (numOr "points" 20.0 points))
        let header = [ box "x (mm)"; box "V* (kN)"; box "M* (kN·m)"; box "δ (mm)" ]
        let rows =
            [ for i in 0..n do
                  let x = l * float i / float n
                  yield
                      [ box x
                        box (Units.toKN (Analysis.shear beam acts x))
                        box (Units.toKNm (Analysis.moment beam acts x))
                        box (Analysis.deflection beam acts x) ] ]
        table (header :: rows))

[<ExcelFunction(Name = "OAR.REACTIONS",
                Description = "Spills support reactions (kN, + up) for each case (wind per direction, e.g. Wu Cross), each combination (with pattern variants) and EQ (equilibrium): ID | A | B, plus C for two spans",
                Category = Category)>]
let oarReactions
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = geometry span
        let l = totalLength g
        let beam = beamOf g (1.0)
        let ls = loadsWithSW s l includeSW loads
        let combosIn = strengthCombos beam combos @ Patterns.equilibrium beam
        // unfactored cases: G, Q, then each wind case per direction (or undivided if no directions are used)
        // Wid / Wiu only when used (v0.3 sheets put internal pressure in Wd / Wu)
        let used lc =
            ls |> List.exists (fun ld -> ld.Case = lc)
            || combosIn |> List.exists (fun c -> Combination.factor c lc <> 0.0)
        let wind = [ Wd; Wu ] @ ([ Wid; Wiu ] |> List.filter used)
        let dirs =
            WindDir.distinct (
                (ls |> List.filter (fun ld -> LoadCase.isWind ld.Case) |> List.map (fun ld -> ld.Dir))
                @ (combosIn |> List.map (fun c -> c.Dir))
            )
        let caseRows =
            [ Combination.single G; Combination.single Q ]
            @ (if dirs.IsEmpty then
                   wind |> List.map Combination.single
               else
                   [ for d in dirs do
                         for lc in wind -> Combination.singleDir lc d ])
        let cs = caseRows @ combosIn
        table
            [ for c in cs do
                  let rs = Analysis.supportReactions beam (Analysis.actions l c ls)
                  yield box c.Id :: (rs |> List.map (fun (_, r) -> box (Units.toKN r))) ])

[<ExcelFunction(Name = "OAR.MAXACTIONS",
                Description = "Spills design actions for each combination: ID | M* max kN.m | at x mm | V* max kN",
                Category = Category)>]
let oarMaxActions
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = geometry span
        let l = totalLength g
        let beam = beamOf g (1.0)
        let ls = loadsWithSW s l includeSW loads
        table
            [ for c in strengthCombos beam combos do
                  let acts = Analysis.actions l c ls
                  let xm, m = Analysis.maxMoment beam acts
                  let _, v = Analysis.maxShear beam acts
                  yield [ box c.Id; box (Units.toKNm m); box xm; box (Units.toKN v) ] ])

[<ExcelFunction(Name = "OAR.PHIMB",
                Description = "Member moment capacity φMb (kN.m) for effective length Le, AS 4100 Cl. 5.6.1",
                Category = Category)>]
let oarPhiMb
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "Le", Description = "effective length, mm")>] le: obj)
    ([<ExcelArgument(Name = "[alpha_m]", Description = "default 1.0")>] am: obj)
    ([<ExcelArgument(Name = "[E]", Description = "MPa (default 200000)")>] e: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let r =
            Capacity.memberMoment s (grade gr) (numOr "E" Units.E_MPa e) (num "Le" le) (numOr "alpha_m" 1.0 am) (not (isMissing am))
        box (Units.toKNm r.PhiMb))

[<ExcelFunction(Name = "OAR.MBDETAIL",
                Description = "Spills φMb working: Le | Ms | Mo | αs | αm | φMb | φMs (moments kN.m)",
                Category = Category)>]
let oarMbDetail
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "Le", Description = "mm")>] le: obj)
    ([<ExcelArgument(Name = "[alpha_m]")>] am: obj)
    ([<ExcelArgument(Name = "[E]")>] e: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let r =
            Capacity.memberMoment s (grade gr) (numOr "E" Units.E_MPa e) (num "Le" le) (numOr "alpha_m" 1.0 am) (not (isMissing am))
        table
            [ [ box "Le (mm)"; box "Ms (kN·m)"; box "Mo (kN·m)"; box "αs"; box "αm"; box "φMb (kN·m)"; box "φMs (kN·m)" ]
              [ box r.Le
                box (Units.toKNm r.Ms)
                box (Units.toKNm r.Mo)
                box r.AlphaS
                box r.AlphaM
                box (Units.toKNm r.PhiMb)
                box (Units.toKNm r.PhiMs) ] ])

[<ExcelFunction(Name = "OAR.LOADS",
                Description = "Echo of how the loads table was read: case | shape | magnitude (kN/m or kN) | a mm | b mm | up/down | wind Dir",
                Category = Category)>]
let oarLoads
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    : obj =
    guard (fun () ->
        let g = geometry span
        let l = totalLength g
        let ls = Inputs.loads l (grid loads)
        if ls.IsEmpty then box "no loads"
        else
            table
                [ for ld in ls do
                      let dir = if LoadCase.direction ld.Case > 0.0 then "down" else "up"
                      let wd = box (if ld.Dir = "" then "–" else ld.Dir)
                      match ld.Shape with
                      | FullSpan -> yield [ box (LoadCase.name ld.Case); box "UDL"; box ld.Magnitude; box 0.0; box l; box dir; wd ]
                      | Partial(a, b) -> yield [ box (LoadCase.name ld.Case); box "PUDL"; box ld.Magnitude; box a; box b; box dir; wd ]
                      | Point x -> yield [ box (LoadCase.name ld.Case); box "Point"; box (Units.toKN ld.Magnitude); box x; box x; box dir; wd ] ])

// ---------- ULS (v0.2) ----------

type private BeamCtx =
    { Sec: Section
      Grade: int
      E: float
      Beam: Beam
      Loads: Load list }

let private ctx designation gr span e includeSW loads : BeamCtx =
    let s = section designation
    let g = geometry span
    let l = totalLength g
    let ee = numOr "E" Units.E_MPa e
    { Sec = s
      Grade = grade gr
      E = ee
      Beam = beamOf g (ee * s.Ix)
      Loads = arg "loads" (fun () -> loadsWithSW s l includeSW loads) }

let private segmentChecks (c: BeamCtx) combos restraints kfactors =
    let cs = strengthCombos c.Beam combos
    let pts = arg "restraints" (fun () -> Inputs.restraints (grid restraints))
    let k = arg "kfactors" (fun () -> Inputs.kFactors (grid kfactors))
    Uls.checkAll c.Sec c.Grade c.E c.Beam c.Loads pts k cs

let private m1 (v: float) = v / 1000.0 // mm -> m

let private segText (r: SegmentCheck) =
    sprintf "%.2f–%.2f%s" (m1 r.X1) (m1 r.X2) (if r.MemberCap.AlphaMOverridden || r.KlOverridden then " *" else "")

[<ExcelFunction(Name = "OAR.ULS",
                Description = "Spills member bending checks for every segment of every combination, both flanges where the moment compresses them: combo | segment m (* = αm or kl override) | flange and ends | Le m | αm | αs | φMb kN.m | M* kN.m | utilisation",
                Category = Category)>]
let oarUls
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos", Description = "active combinations (ID..Dir: G Q Wd Wu, or older G..Wiu)")>] combos: obj)
    ([<ExcelArgument(Name = "restraints", Description = "Restraints table: x | Ref | Description | Top | Bottom | αm top | αm bottom | kl top | kl bottom")>] restraints: obj)
    ([<ExcelArgument(Name = "kfactors", Description = "kl load down, kl load up / kr top, kr bottom / [continuous Yes/No top, bottom] / [kl free tip down, up]")>] kfactors: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation gr span e includeSW loads
        table
            [ for r in segmentChecks c combos restraints kfactors do
                  yield
                      [ box r.Combo
                        box (segText r)
                        box ((if r.Flange = Top then "Top " else "Bottom ") + r.Ends)
                        box (m1 r.MemberCap.Le)
                        box r.MemberCap.AlphaM
                        box r.MemberCap.AlphaS
                        box (Units.toKNm r.MemberCap.PhiMb)
                        box (Units.toKNm r.Mm)
                        box r.Utilisation ] ])

[<ExcelFunction(Name = "OAR.ULSDETAIL",
                Description = "Spills full working for one segment (default: the governing segment): item | value | unit",
                Category = Category)>]
let oarUlsDetail
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "span")>] span: obj)
    ([<ExcelArgument(Name = "E")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    ([<ExcelArgument(Name = "restraints")>] restraints: obj)
    ([<ExcelArgument(Name = "kfactors")>] kfactors: obj)
    ([<ExcelArgument(Name = "[comboId]", Description = "blank = governing")>] comboId: obj)
    ([<ExcelArgument(Name = "[segment]", Description = "segment number within the combination, from 1")>] segNo: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation gr span e includeSW loads
        let checks = segmentChecks c combos restraints kfactors
        let pick =
            if isMissing comboId then
                Uls.governing None checks
            else
                let id = str comboId
                let forCombo = checks |> List.filter (fun r -> String.Equals(r.Combo, id, StringComparison.OrdinalIgnoreCase))
                let n = int (numOr "segment" 1.0 segNo)
                if n < 1 || n > forCombo.Length then None else Some(forCombo.[n - 1])
        match pick with
        | None -> box "no segment found"
        | Some r ->
            let mc = r.MemberCap
            let row (n: string) (v: obj) (u: string) = [ box n; v; box u ]
            table
                [ row "Combination" (box r.Combo) ""
                  row "Critical flange" (box (Uls.flangeName r.Flange)) ""
                  row "Segment" (box (sprintf "%.0f – %.0f" r.X1 r.X2)) "mm"
                  row "End restraints" (box r.Ends) "Cl. 5.4"
                  row "Segment length l" (box r.Length) "mm"
                  row "kt" (box r.Kt) "Table 5.6.3(1)"
                  row "kl" (box r.Kl) ("Table 5.6.3(2): " + r.KlBasis)
                  row "kr" (box r.Kr) "Table 5.6.3(3)"
                  row "Le = kt kl kr l" (box mc.Le) "mm"
                  row "M2*" (box (Units.toKNm r.M2)) "kN·m"
                  row "M3*" (box (Units.toKNm r.M3)) "kN·m"
                  row "M4*" (box (Units.toKNm r.M4)) "kN·m"
                  row "M* (max compressing this flange)" (box (Units.toKNm r.MmSigned)) (sprintf "kN·m at x = %.0f mm" r.XAtMm)
                  row "αm, Cl. 5.6.1.1(a)(iii)" (box r.AlphaMFormula) ""
                  row "αm used" (box mc.AlphaM) r.AlphaMBasis
                  row "Msx" (box (Units.toKNm mc.Ms)) "kN·m"
                  row "Mo" (if Double.IsInfinity mc.Mo then box "–" else box (Units.toKNm mc.Mo)) (if Double.IsInfinity mc.Mo then "continuous restraint" else "kN·m")
                  row "αs" (box mc.AlphaS) ""
                  row "φMb = φ αm αs Msx ≤ φMsx" (box (Units.toKNm mc.PhiMb)) "kN·m"
                  row "φMsx" (box (Units.toKNm mc.PhiMs)) "kN·m"
                  row "M* / φMb" (box r.Utilisation) ""
                  row "Note" (box r.Note) "" ])

[<ExcelFunction(Name = "OAR.GOVERNING",
                Description = "3 rows (bending top flange, bending bottom flange, shear): label | M* or V* | φMb or φVv | where. Blank when no combination applies.",
                Category = Category)>]
let oarGoverning
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "span")>] span: obj)
    ([<ExcelArgument(Name = "E")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    ([<ExcelArgument(Name = "restraints")>] restraints: obj)
    ([<ExcelArgument(Name = "kfactors")>] kfactors: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation gr span e includeSW loads
        let checks = segmentChecks c combos restraints kfactors
        let bend label flange =
            match Uls.governing (Some flange) checks with
            | Some r ->
                [ box label
                  box (Units.toKNm r.Mm)
                  box (Units.toKNm r.MemberCap.PhiMb)
                  box (sprintf "%s, %s m%s" r.Combo (segText r) (if r.Note = "" then "" else " – " + r.Note)) ]
            | None -> [ box label; box ""; box ""; box "no combination" ]
        let shear =
            let sc = Uls.shearChecks c.Sec c.Grade c.Beam c.Loads (strengthCombos c.Beam combos)
            match sc |> List.sortByDescending (fun r -> r.Utilisation) |> List.tryHead with
            | Some r ->
                [ box "Shear"
                  box (Units.toKN r.VStar)
                  box (Units.toKN r.ShearCap.PhiVv)
                  box (sprintf "%s at x = %.2f m%s" r.Combo (m1 r.At) (if r.Note = "" then "" else " – " + r.Note)) ]
            | None -> [ box "Shear"; box ""; box ""; box "no combination" ]
        table [ bend "Bending, top flange critical" Top; bend "Bending, bottom flange critical" Bottom; shear ])

[<ExcelFunction(Name = "OAR.SHEAR",
                Description = "Spills shear checks per combination: combo | V* kN | at x m | φVv kN | utilisation | note",
                Category = Category)>]
let oarShear
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "span")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation gr span (box ExcelMissing.Value) includeSW loads
        table
            [ for r in Uls.shearChecks c.Sec c.Grade c.Beam c.Loads (strengthCombos c.Beam combos) do
                  yield
                      [ box r.Combo
                        box (Units.toKN r.VStar)
                        box (m1 r.At)
                        box (Units.toKN r.ShearCap.PhiVv)
                        box r.Utilisation
                        box r.Note ] ])

[<ExcelFunction(Name = "OAR.ENVELOPE",
                Description = "Spills moment and shear envelopes over all combinations for charts: x m | M* max | M* min (kN.m) | V* max | V* min (kN)",
                Category = Category)>]
let oarEnvelope
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    ([<ExcelArgument(Name = "[points]", Description = "intervals (default 60)")>] points: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation (box 300.0) span (box ExcelMissing.Value) includeSW loads
        let n = max 2 (int (numOr "points" 60.0 points))
        let rows =
            Uls.envelope c.Beam c.Loads (strengthCombos c.Beam combos) n
            |> List.map (fun (x, mx, mn, vx, vn) ->
                [ box (m1 x); box (Units.toKNm mx); box (Units.toKNm mn); box (Units.toKN vx); box (Units.toKN vn) ])
        table ([ box "x (m)"; box "M* max"; box "M* min"; box "V* max"; box "V* min" ] :: rows))

[<ExcelFunction(Name = "OAR.DEFLECTIONS",
                Description = "Spills deflected shapes for charts: x m | Δg | Δg+ψl·q | Δg+ψs·q | Δq [| Δws] (mm, down +)",
                Category = Category)>]
let oarDeflections
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span")>] span: obj)
    ([<ExcelArgument(Name = "E")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "psiL")>] psiL: obj)
    ([<ExcelArgument(Name = "psiS")>] psiS: obj)
    ([<ExcelArgument(Name = "[points]", Description = "intervals (default 60)")>] points: obj)
    ([<ExcelArgument(Name = "[shearDefl]", Description = "Yes = include shear deformation (default No)")>] shearDefl: obj)
    ([<ExcelArgument(Name = "[windFactor]", Description = "SLS / ULS wind factor: adds a Δws column (wind alone × factor, governing pattern)")>] windFactor: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation (box 300.0) span e includeSW loads
        let n = max 2 (int (numOr "points" 60.0 points))
        let beam = withShearOpt c.Sec shearDefl c.Beam
        let rows =
            Serviceability.curves beam c.Loads (num "psiL" psiL) (num "psiS" psiS) n
            |> List.map (fun (x, a, b, d, q) -> [ box (m1 x); box a; box b; box d; box q ])
        let head = [ box "x (m)"; box "Δg"; box "Δg+ψl·q"; box "Δg+ψs·q"; box "Δq" ]
        if isMissing windFactor then
            table (head :: rows)
        else
            let kw = num "windFactor" windFactor
            if not (kw > 0.0) then failwith "windFactor must be a positive number (e.g. 0.47)"
            let w = Serviceability.windCurve beam c.Loads kw n
            table ((head @ [ box "Δws" ]) :: List.map2 (fun r (_, d: float) -> r @ [ box d ]) rows w))

[<ExcelFunction(Name = "OAR.SECTIONLIST",
                Description = "Spills the designations with complete data for a family (UB, UC, WB, PFC, RHS, SHS), largest first; blank = all",
                Category = Category)>]
let oarSectionList ([<ExcelArgument(Name = "[family]", Description = "UB, UC, WB, PFC, RHS or SHS")>] family: obj) : obj =
    guard (fun () ->
        let f = (str family).ToUpperInvariant()
        let list =
            Sections.all ()
            |> List.filter (fun s -> f = "" || (sprintf "%A" s.Family) = f)
            |> List.map (fun s -> [ box s.Designation ])
        if list.IsEmpty then box (sprintf "no complete sections for '%s'" f) else table list)

[<ExcelFunction(Name = "OAR.LOADDIAGRAM",
                Description = "Applied load diagram data for charts. kind \"line\" (default): x m | G | Q | Wd | Wu | Wid | Wiu (kN/m, + down) padded with #N/A to 80 rows. kind \"point\": x m | P kN (+ down) | label, padded to 20 rows.",
                Category = Category)>]
let oarLoadDiagram
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm: one cell (span), two cells (backspan, overhang), or three (span 1, overhang, span 2)")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "[kind]", Description = "line (default) or point")>] kind: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let g = geometry span
        let l = totalLength g
        let ls = arg "loads" (fun () -> loadsWithSW s l includeSW loads)
        let na = box ExcelError.ExcelErrorNA
        let pad (n: int) (width: int) (rows: obj list list) =
            if rows.Length > n then failwithf "too many load steps to plot (%d, max %d)" rows.Length n
            rows @ List.replicate (n - rows.Length) (List.replicate width na)
        match (str kind).ToUpperInvariant() with
        | "POINT" ->
            let rows =
                Analysis.pointLoads ls
                |> List.map (fun (x, p, c) ->
                    [ box (m1 x); box (Units.toKN p); box (sprintf "%s %.1f kN" (LoadCase.name c) (abs (Units.toKN p))) ])
            table ([ box "x (m)"; box "P (kN)"; box "label" ] :: pad 20 3 rows)
        | _ ->
            let rows =
                Analysis.lineLoadProfile l ls
                |> List.map (fun (x, ws) -> box (m1 x) :: (ws |> List.map box))
            let header = box "x (m)" :: (LoadCase.all |> List.map (LoadCase.name >> box))
            table (header :: pad 80 7 rows))

// ---------- load review window ----------

/// Everything the OAR > Review loads window needs, from the same sheet values the worksheet functions take
/// (so the window shows exactly what the sheet calculates). Raises a readable error if an input is bad.
let reviewData
    (beamRef: string)
    (designation: obj)
    (span: obj)
    (e: obj)
    (includeSW: obj)
    (loads: obj)
    (combos: obj)
    (psiL: obj)
    (psiS: obj)
    (shearDefl: obj)
    (windFactor: obj)
    (restraints: obj)
    (continuousTop: obj)
    (continuousBottom: obj)
    (criteria: obj)
    : ReviewData =
    let c = ctx designation (box 300.0) span e includeSW loads
    let g = geometry span
    let beam = withShearOpt c.Sec shearDefl c.Beam
    let cs = strengthCombos beam combos @ Patterns.equilibrium beam
    let mm (v: float) = v.ToString("#,0", Globalization.CultureInfo.InvariantCulture)
    let geomText =
        if g.Span2 > 0.0 then sprintf "spans %s + %s mm" (mm g.Span1) (mm g.Span2)
        elif g.Overhang > 0.0 then sprintf "backspan %s + overhang %s mm" (mm g.Span1) (mm g.Overhang)
        else sprintf "span %s mm" (mm g.Span1)
    let kw = if isMissing windFactor then 0.0 else numOr "windFactor" 0.0 windFactor
    // the restraints are only drawn, so a mistake in that table is reported in the picture, not as a failure
    let points, note =
        if isMissing restraints then
            [], ""
        else
            try
                Inputs.restraints (grid restraints), ""
            with ex ->
                [], ex.Message
    // likewise the deflection limits: without them the limit line is simply not drawn
    let limits =
        if isMissing criteria then
            []
        else
            try
                Inputs.deflectionCriteria (grid criteria)
            with _ ->
                []
    let yes (o: obj) =
        match (str o).ToUpperInvariant() with
        | "YES" | "Y" | "TRUE" | "1" -> true
        | _ -> false
    { Title = String.Join(" · ", [ beamRef.Trim(); c.Sec.Designation; geomText ] |> List.filter (fun s -> s <> ""))
      Beam = beam
      Loads = c.Loads
      Items = Review.items c.Loads cs (numOr "psiL" 0.4 psiL) (numOr "psiS" 0.7 psiS) kw
      Restraints = points
      ContinuousTop = yes continuousTop
      ContinuousBottom = yes continuousBottom
      RestraintsNote = note
      Criteria = limits }
