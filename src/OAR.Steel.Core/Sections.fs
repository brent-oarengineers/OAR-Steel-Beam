namespace OAR.Steel

open System
open System.Globalization
open System.IO
open System.Reflection

type Family =
    | UB
    | UC
    | WB
    | PFC

/// Section properties in base units (mm, mm2, mm3, mm4, mm6, kg/m).
type Section =
    { Designation: string
      Family: Family
      Mass: float
      d: float
      bf: float
      tf: float
      tw: float
      Ag: float
      Ix: float
      Zx: float
      Sx: float
      Iy: float
      J: float
      Iw: float
      /// true when Iw was not tabulated and was calculated as Iy (d - tf)^2 / 4
      IwCalculated: bool
      /// grade -> (fy flange, fy web) in MPa
      Fy: Map<int, float * float>
      /// tabulated phiMsx for Grade 300 (kN.m), kept only so tests can check the Ze calculation
      PhiMsxTab300: float option }

module Sections =

    let private inv = CultureInfo.InvariantCulture

    let private num (s: string) =
        let t = s.Trim()
        if t = "" then None
        else
            match Double.TryParse(t, NumberStyles.Float, inv) with
            | true, v -> Some v
            | _ -> failwithf "sections.csv: cannot read number '%s'" t

    let private parseFamily =
        function
        | "UB" -> UB
        | "UC" -> UC
        | "WB" -> WB
        | "PFC" -> PFC
        | f -> failwithf "sections.csv: unknown family '%s'" f

    /// Normalise a designation so "310 UB 40.4" and "310ub40.4" both match "310UB40.4".
    let normalise (s: string) =
        if isNull s then "" else s.Replace(" ", "").Trim().ToUpperInvariant()

    /// Warping constant for a doubly symmetric I-section, Iw = Iy df^2 / 4 with df = d - tf
    let iwDoublySymmetric (iy: float) (d: float) (tf: float) = iy * (d - tf) ** 2.0 / 4.0

    /// Parse the CSV text. Returns (complete sections, incomplete designation -> missing fields).
    let parse (text: string) =
        let lines =
            text.Split([| '\n' |])
            |> Array.map (fun l -> l.TrimEnd('\r'))
            |> Array.filter (fun l -> l.Trim() <> "" && not (l.StartsWith "#"))

        let header = lines.[0].Split(',') |> Array.map (fun h -> h.Trim())

        let rows =
            lines.[1..]
            |> Array.map (fun l ->
                let cells = l.Split(',')
                header
                |> Array.mapi (fun i h -> h, (if i < cells.Length then cells.[i] else ""))
                |> Map.ofArray)

        let complete = Collections.Generic.List<Section>()
        let incomplete = Collections.Generic.Dictionary<string, string list>()

        for r in rows do
            let get k = num r.[k]
            let name = r.["Designation"].Trim()
            let fam = parseFamily (r.["Family"].Trim())

            let required =
                [ "Mass"; "d"; "bf"; "tf"; "tw"; "Ag"; "Ix_e6"; "Zx_e3"; "Sx_e3"; "Iy_e6"; "J_e3" ]

            let missing = required |> List.filter (fun k -> (get k).IsNone)

            let iwTab = get "Iw_e9" |> Option.map (fun v -> v * 1.0e9)

            let missing =
                match iwTab, fam with
                | None, PFC -> missing @ [ "Iw_e9" ]
                | _ -> missing

            let grades =
                [ 250; 300; 350; 400 ]
                |> List.choose (fun g ->
                    match get (sprintf "fyf%d" g), get (sprintf "fyw%d" g) with
                    | Some f, Some w -> Some(g, (f, w))
                    | _ -> None)
                |> Map.ofList

            let missing = if grades.IsEmpty then missing @ [ "fy (all grades)" ] else missing

            if not missing.IsEmpty then
                incomplete.[normalise name] <- missing
            else
                let v k = (get k).Value
                let iy = v "Iy_e6" * 1.0e6

                let iw, calc =
                    match iwTab with
                    | Some x -> x, false
                    | None -> iwDoublySymmetric iy (v "d") (v "tf"), true

                complete.Add(
                    { Designation = name
                      Family = fam
                      Mass = v "Mass"
                      d = v "d"
                      bf = v "bf"
                      tf = v "tf"
                      tw = v "tw"
                      Ag = v "Ag"
                      Ix = v "Ix_e6" * 1.0e6
                      Zx = v "Zx_e3" * 1.0e3
                      Sx = v "Sx_e3" * 1.0e3
                      Iy = iy
                      J = v "J_e3" * 1.0e3
                      Iw = iw
                      IwCalculated = calc
                      Fy = grades
                      PhiMsxTab300 = get "phiMsx300_tab" }
                )

        List.ofSeq complete, (incomplete |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq)

    let private loadEmbedded () =
        let asm = Assembly.GetExecutingAssembly()
        use stream = asm.GetManifestResourceStream("sections.csv")
        if isNull stream then failwith "sections.csv resource not found in OAR.Steel.Core"
        use reader = new StreamReader(stream)
        parse (reader.ReadToEnd())

    let private loaded = lazy (loadEmbedded ())

    let all () = fst loaded.Value

    let private byName =
        lazy (all () |> List.map (fun s -> normalise s.Designation, s) |> Map.ofList)

    /// Find a section by designation. Error text explains incomplete or unknown sections.
    let tryFind (designation: string) : Result<Section, string> =
        let key = normalise designation
        match byName.Value.TryFind key with
        | Some s -> Ok s
        | None ->
            match (snd loaded.Value).TryFind key with
            | Some miss -> Error(sprintf "%s has incomplete data in sections.csv (missing %s)" designation (String.Join(", ", miss)))
            | None -> Error(sprintf "section '%s' not found" designation)

    let find designation =
        match tryFind designation with
        | Ok s -> s
        | Error e -> failwith e

    /// Yield stresses (flange, web) in MPa for a grade
    let fy (s: Section) (grade: int) =
        match s.Fy.TryFind grade with
        | Some v -> v
        | None ->
            let have = s.Fy |> Map.toList |> List.map (fst >> string)
            failwithf "%s: no yield stress data for Grade %d (available: %s)" s.Designation grade (String.Join(", ", have))
