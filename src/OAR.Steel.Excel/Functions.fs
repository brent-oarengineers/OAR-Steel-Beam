/// Excel worksheet functions. This file only converts between Excel values and the core library;
/// all engineering is in OAR.Steel.Core so it can be tested without Excel.
module OAR.Steel.Excel.Functions

open System
open ExcelDna.Integration
open OAR.Steel

[<Literal>]
let Category = "OAR Steel"

let private version = "0.2.3"

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

// ---------- functions ----------

[<ExcelFunction(Name = "OAR.VERSION", Description = "OAR Steel add-in version", Category = Category)>]
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
              row "Iw" (s.Iw / 1e9) "×10⁹ mm⁶" (if s.IwCalculated then "calculated Iy(d-tf)²/4" else "")
              row "fyf" fyf "MPa" (sprintf "Grade %d" g)
              row "fyw" fyw "MPa" (sprintf "Grade %d" g)
              row "λe flange" sm.Flange.Lambda "" (sprintf "λep %g, λey %g" sm.Flange.LambdaP sm.Flange.LambdaY)
              row "λe web" sm.Web.Lambda "" (sprintf "λep %g, λey %g" sm.Web.LambdaP sm.Web.LambdaY)
              row "Ze" (sm.Ze / 1e3) "×10³ mm³" (sprintf "%A, governed by %s" sm.Class sm.Governing)
              row "φMsx" (Units.toKNm sm.PhiMs) "kN·m" "AS 4100 Cl. 5.2"
              row "φVv" (Units.toKN sv.PhiVv) "kN" (sprintf "Aw %.0f mm², λw %.1f, αv %.3f" sv.Aw sv.Lambda sv.AlphaV) ])

[<ExcelFunction(Name = "OAR.SLS",
                Description = "Spills deflection checks (5 rows): case | Δ max mm | at x mm | limit mm | L/Δ | utilisation",
                Category = Category)>]
let oarSLS
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa (default 200000)")>] e: obj)
    ([<ExcelArgument(Name = "includeSW", Description = "Yes/No (default Yes)")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads", Description = "Loads table data range (Case..End or Case..Load)")>] loads: obj)
    ([<ExcelArgument(Name = "criteria", Description = "5x2 range: span ratio | max mm")>] criteria: obj)
    ([<ExcelArgument(Name = "psiL")>] psiL: obj)
    ([<ExcelArgument(Name = "psiS")>] psiS: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let l = num "span" span
        let beam = { Span = l; EI = numOr "E" Units.E_MPa e * s.Ix }
        let ls = loadsWithSW s l includeSW loads
        let crit = Inputs.deflectionCriteria (grid criteria)
        let res = Serviceability.check beam ls crit (num "psiL" psiL) (num "psiS" psiS)
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
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa (default 200000)")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos", Description = "Combinations table data range (ID..Wiu)")>] combos: obj)
    ([<ExcelArgument(Name = "comboId", Description = "e.g. ULS2, or a single case G, Q, Wu...")>] comboId: obj)
    ([<ExcelArgument(Name = "[points]", Description = "number of intervals (default 20)")>] points: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let l = num "span" span
        let beam = { Span = l; EI = numOr "E" Units.E_MPa e * s.Ix }
        let ls = loadsWithSW s l includeSW loads
        let id = str comboId
        let combo =
            match LoadCase.tryParse id with
            | Some lc -> Combination.single lc
            | None -> Inputs.combination id (grid combos)
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
                Description = "Spills support reactions (kN, + up) for each case and each combination: ID | left | right",
                Category = Category)>]
let oarReactions
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let l = num "span" span
        let beam = { Span = l; EI = 1.0 }
        let ls = loadsWithSW s l includeSW loads
        let cs = (LoadCase.all |> List.map Combination.single) @ Inputs.combinations (grid combos)
        table
            [ for c in cs do
                  let ra, rb = Analysis.reactions beam (Analysis.actions l c ls)
                  yield [ box c.Id; box (Units.toKN ra); box (Units.toKN rb) ] ])

[<ExcelFunction(Name = "OAR.MAXACTIONS",
                Description = "Spills design actions for each combination: ID | M* max kN.m | at x mm | V* max kN",
                Category = Category)>]
let oarMaxActions
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos")>] combos: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let l = num "span" span
        let beam = { Span = l; EI = 1.0 }
        let ls = loadsWithSW s l includeSW loads
        table
            [ for c in Inputs.combinations (grid combos) do
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
                Description = "Echo of how the loads table was read: case | shape | magnitude (kN/m or kN) | a mm | b mm | direction",
                Category = Category)>]
let oarLoads
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    : obj =
    guard (fun () ->
        let l = num "span" span
        let ls = Inputs.loads l (grid loads)
        if ls.IsEmpty then box "no loads"
        else
            table
                [ for ld in ls do
                      let dir = if LoadCase.direction ld.Case > 0.0 then "down" else "up"
                      match ld.Shape with
                      | FullSpan -> yield [ box (LoadCase.name ld.Case); box "UDL"; box ld.Magnitude; box 0.0; box l; box dir ]
                      | Partial(a, b) -> yield [ box (LoadCase.name ld.Case); box "PUDL"; box ld.Magnitude; box a; box b; box dir ]
                      | Point x -> yield [ box (LoadCase.name ld.Case); box "Point"; box (Units.toKN ld.Magnitude); box x; box x; box dir ] ])

// ---------- ULS (v0.2) ----------

type private BeamCtx =
    { Sec: Section
      Grade: int
      E: float
      Beam: Beam
      Loads: Load list }

let private ctx designation gr span e includeSW loads : BeamCtx =
    let s = section designation
    let l = num "span" span
    let ee = numOr "E" Units.E_MPa e
    { Sec = s
      Grade = grade gr
      E = ee
      Beam = { Span = l; EI = ee * s.Ix }
      Loads = arg "loads" (fun () -> loadsWithSW s l includeSW loads) }

let private segmentChecks (c: BeamCtx) combos restraints kfactors =
    let cs = arg "combos" (fun () -> Inputs.combinations (grid combos))
    let pts = arg "restraints" (fun () -> Inputs.restraints (grid restraints))
    let k = arg "kfactors" (fun () -> Inputs.kFactors (grid kfactors))
    Uls.checkAll c.Sec c.Grade c.E c.Beam c.Loads pts k cs

let private m1 (v: float) = v / 1000.0 // mm -> m

let private segText (r: SegmentCheck) =
    sprintf "%.2f–%.2f%s" (m1 r.X1) (m1 r.X2) (if r.MemberCap.AlphaMOverridden then " *" else "")

[<ExcelFunction(Name = "OAR.ULS",
                Description = "Spills member bending checks for every segment of every combination: combo | segment m (* = αm override) | ends | Le m | αm | αs | φMb kN.m | M* kN.m | utilisation",
                Category = Category)>]
let oarUls
    ([<ExcelArgument(Name = "designation")>] designation: obj)
    ([<ExcelArgument(Name = "grade")>] gr: obj)
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "E", Description = "MPa")>] e: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "combos", Description = "active combinations (ID..Wiu)")>] combos: obj)
    ([<ExcelArgument(Name = "restraints", Description = "Restraints table: x | Ref | Description | Top | Bottom | αm override")>] restraints: obj)
    ([<ExcelArgument(Name = "kfactors", Description = "2x2: kl top, kl bottom / kr top, kr bottom")>] kfactors: obj)
    : obj =
    guard (fun () ->
        let c = ctx designation gr span e includeSW loads
        table
            [ for r in segmentChecks c combos restraints kfactors do
                  yield
                      [ box r.Combo
                        box (segText r)
                        box r.Ends
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
                  row "kl" (box r.Kl) "Table 5.6.3(2)"
                  row "kr" (box r.Kr) "Table 5.6.3(3)"
                  row "Le = kt kl kr l" (box mc.Le) "mm"
                  row "M2*" (box (Units.toKNm r.M2)) "kN·m"
                  row "M3*" (box (Units.toKNm r.M3)) "kN·m"
                  row "M4*" (box (Units.toKNm r.M4)) "kN·m"
                  row "Mm* (max in segment)" (box (Units.toKNm r.MmSigned)) (sprintf "kN·m at x = %.0f mm" r.XAtMm)
                  row "αm, Cl. 5.6.1.1(a)(iii)" (box r.AlphaMFormula) ""
                  row "αm used" (box mc.AlphaM) (if mc.AlphaMOverridden then "override" else "formula")
                  row "Msx" (box (Units.toKNm mc.Ms)) "kN·m"
                  row "Mo" (box (Units.toKNm mc.Mo)) "kN·m"
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
            let sc = Uls.shearChecks c.Sec c.Grade c.Beam c.Loads (Inputs.combinations (grid combos))
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
            [ for r in Uls.shearChecks c.Sec c.Grade c.Beam c.Loads (Inputs.combinations (grid combos)) do
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
            Uls.envelope c.Beam c.Loads (Inputs.combinations (grid combos)) n
            |> List.map (fun (x, mx, mn, vx, vn) ->
                [ box (m1 x); box (Units.toKNm mx); box (Units.toKNm mn); box (Units.toKN vx); box (Units.toKN vn) ])
        table ([ box "x (m)"; box "M* max"; box "M* min"; box "V* max"; box "V* min" ] :: rows))

[<ExcelFunction(Name = "OAR.DEFLECTIONS",
                Description = "Spills deflected shapes for charts: x m | Δg | Δg+ψl·q | Δg+ψs·q | Δq (mm, down +)",
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
    : obj =
    guard (fun () ->
        let c = ctx designation (box 300.0) span e includeSW loads
        let n = max 2 (int (numOr "points" 60.0 points))
        let rows =
            Serviceability.curves c.Beam c.Loads (num "psiL" psiL) (num "psiS" psiS) n
            |> List.map (fun (x, a, b, d, q) -> [ box (m1 x); box a; box b; box d; box q ])
        table ([ box "x (m)"; box "Δg"; box "Δg+ψl·q"; box "Δg+ψs·q"; box "Δq" ] :: rows))

[<ExcelFunction(Name = "OAR.SECTIONLIST",
                Description = "Spills the designations with complete data for a family (UB, UC, WB, PFC), largest first; blank = all",
                Category = Category)>]
let oarSectionList ([<ExcelArgument(Name = "[family]", Description = "UB, UC, WB or PFC")>] family: obj) : obj =
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
    ([<ExcelArgument(Name = "span", Description = "mm")>] span: obj)
    ([<ExcelArgument(Name = "includeSW")>] includeSW: obj)
    ([<ExcelArgument(Name = "loads")>] loads: obj)
    ([<ExcelArgument(Name = "[kind]", Description = "line (default) or point")>] kind: obj)
    : obj =
    guard (fun () ->
        let s = section designation
        let l = num "span" span
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
