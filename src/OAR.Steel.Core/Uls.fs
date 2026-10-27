namespace OAR.Steel

open System

/// Cross-section restraint types, AS 4100 Cl. 5.4 (F full, P partial, L lateral, U unrestrained)
type RestraintType =
    | RF
    | RP
    | RL
    | RU

module RestraintType =
    let tryParse (s: string) =
        match (if isNull s then "" else s.Trim().ToUpperInvariant()) with
        | "F" -> Some RF
        | "P" -> Some RP
        | "L" -> Some RL
        | "U" -> Some RU
        | _ -> None

    let code =
        function
        | RF -> "F"
        | RP -> "P"
        | RL -> "L"
        | RU -> "U"

    let rank =
        function
        | RF -> 0
        | RP -> 1
        | RL -> 2
        | RU -> 3

/// One row of the restraints table. None = no restraint to that flange at this point.
type RestraintPoint =
    { X: float
      Ref: string
      Top: RestraintType option
      Bottom: RestraintType option
      /// αm for the segment that starts at this point (overrides Cl. 5.6.1.1(a)(iii))
      AlphaMOverride: float option }

/// kl and kr for each critical flange (sheet inputs)
type KFactors =
    { KlTop: float
      KlBottom: float
      KrTop: float
      KrBottom: float }

/// Member bending check of one segment for one combination, with all working kept.
type SegmentCheck =
    { Combo: string
      Flange: Flange
      X1: float
      X2: float
      /// end restraint codes, e.g. "FL"
      Ends: string
      Length: float
      Kt: float
      Kl: float
      Kr: float
      M2: float
      M3: float
      M4: float
      /// max |M*| in the segment (N.mm) and its location
      Mm: float
      XAtMm: float
      /// signed moment at XAtMm (sagging +)
      MmSigned: float
      AlphaMFormula: float
      MemberCap: MemberMoment
      Utilisation: float
      Note: string }

type ShearCheck =
    { Combo: string
      /// max |V*| (N) and location (mm)
      VStar: float
      At: float
      ShearCap: ShearCapacity
      Utilisation: float
      Note: string }

module Uls =

    /// Twist restraint factor kt, AS 4100 Table 5.6.3(1), single web (nw = 1)
    let kt (s: Section) (segmentLength: float) (a: RestraintType) (b: RestraintType) =
        let d1 = s.d - 2.0 * s.tf
        let term = (d1 / segmentLength) * (s.tf / (2.0 * s.tw)) ** 3.0
        let lo, hi = (if RestraintType.rank a <= RestraintType.rank b then (a, b) else (b, a))
        match lo, hi with
        | RF, RF
        | RF, RL
        | RL, RL
        | RF, RU -> 1.0
        | RF, RP
        | RP, RL
        | RP, RU -> 1.0 + term
        | RP, RP -> 1.0 + 2.0 * term
        | _ ->
            failwithf "segment with end restraints %s%s is not covered by Table 5.6.3(1)" (RestraintType.code lo) (RestraintType.code hi)

    let private restraintFor (flange: Flange) (r: RestraintPoint) =
        match flange with
        | Top -> r.Top
        | Bottom -> r.Bottom

    let flangeName =
        function
        | Top -> "top"
        | Bottom -> "bottom"

    /// Segments for one critical flange: consecutive points that restrain that flange.
    /// Both supports must be listed and restrained F or P.
    let segments (span: float) (flange: Flange) (pts: RestraintPoint list) =
        let restrained =
            pts
            |> List.choose (fun p -> restraintFor flange p |> Option.map (fun t -> p, t))
            |> List.sortBy (fun (p, _) -> p.X)

        let tol = 1.0 // mm
        let atSupport x =
            restrained |> List.tryFind (fun (p, _) -> abs (p.X - x) <= tol)

        for x, name in [ 0.0, "left"; span, "right" ] do
            match atSupport x with
            | None ->
                failwithf "restraints: the %s support (x = %g) must be listed with a restraint to the %s flange" name x (flangeName flange)
            | Some(_, t) when t <> RF && t <> RP ->
                failwithf "restraints: the %s support must be F or P for the %s flange (got %s)" name (flangeName flange) (RestraintType.code t)
            | _ -> ()

        if restrained |> List.exists (fun (p, _) -> p.X < -tol || p.X > span + tol) then
            failwith "restraints: a restraint position is outside the span"

        restrained
        |> List.pairwise
        |> List.filter (fun ((p1, _), (p2, _)) -> p2.X - p1.X > tol)

    /// Check every segment for one combination
    let checkCombination
        (s: Section)
        (grade: int)
        (e: float)
        (beam: Beam)
        (loads: Load list)
        (pts: RestraintPoint list)
        (k: KFactors)
        (combo: Combination)
        : SegmentCheck list =
        let acts = Analysis.actions beam.Span combo loads
        let flange = combo.CriticalFlange
        let kl, kr =
            match flange with
            | Top -> k.KlTop, k.KrTop
            | Bottom -> k.KlBottom, k.KrBottom
        let bps = Analysis.breakpoints acts
        let m x = Analysis.moment beam acts x

        segments beam.Span flange pts
        |> List.map (fun ((p1, t1), (p2, t2)) ->
            let len = p2.X - p1.X
            let q (f: float) = p1.X + len * f / 4.0
            let m2 = m (q 1.0)
            let m3 = m (q 2.0)
            let m4 = m (q 3.0)
            let xm, mm = Analysis.maxAbsOn p1.X p2.X bps m
            let amFormula = Capacity.alphaMFormula mm m2 m3 m4
            let am, overridden =
                match p1.AlphaMOverride with
                | Some v -> (v, true)
                | None -> (amFormula, false)
            let ktv = kt s len t1 t2
            let le = Capacity.effectiveLength ktv kl kr len
            let cap = Capacity.memberMoment s grade e le am overridden
            let mabs = abs mm
            let util = if mabs = 0.0 then 0.0 else mabs / cap.PhiMb
            let note =
                if mabs = 0.0 then ""
                elif flange = Top && mm < 0.0 then "hogging moment: top flange is in tension, check the critical flange"
                elif flange = Bottom && mm > 0.0 then "sagging moment: bottom flange is in tension, check the critical flange"
                else ""
            let r: SegmentCheck =
                { Combo = combo.Id
                  Flange = flange
                  X1 = p1.X
                  X2 = p2.X
                  Ends = RestraintType.code t1 + RestraintType.code t2
                  Length = len
                  Kt = ktv
                  Kl = kl
                  Kr = kr
                  M2 = m2
                  M3 = m3
                  M4 = m4
                  Mm = mabs
                  XAtMm = xm
                  MmSigned = mm
                  AlphaMFormula = amFormula
                  MemberCap = cap
                  Utilisation = util
                  Note = note }
            r)

    /// All segment checks for all combinations, in combination order
    let checkAll s grade e beam loads pts k (combos: Combination list) =
        combos |> List.collect (checkCombination s grade e beam loads pts k)

    /// Governing (highest utilisation) segment check, optionally for one flange only
    let governing (flange: Flange option) (checks: SegmentCheck list) =
        checks
        |> List.filter (fun c ->
            match flange with
            | Some f -> c.Flange = f
            | None -> true)
        |> List.sortByDescending (fun c -> c.Utilisation)
        |> List.tryHead

    /// Shear check for each combination, AS 4100 Cl. 5.11
    let shearChecks (s: Section) (grade: int) (beam: Beam) (loads: Load list) (combos: Combination list) =
        let cap = Capacity.shear s grade
        combos
        |> List.map (fun c ->
            let acts = Analysis.actions beam.Span c loads
            let x, v = Analysis.maxShear beam acts
            let va = abs v
            let note =
                if va > 0.6 * cap.PhiVv then "V* > 0.6φVv: check shear–bending interaction (Cl. 5.12.3)" else ""
            let r: ShearCheck =
                { Combo = c.Id
                  VStar = va
                  At = x
                  ShearCap = cap
                  Utilisation = va / cap.PhiVv
                  Note = note }
            r)

    /// Moment and shear envelopes over all combinations at n+1 stations:
    /// (x, Mmax, Mmin, Vmax, Vmin) in N.mm / N
    let envelope (beam: Beam) (loads: Load list) (combos: Combination list) (n: int) =
        let actsAll = combos |> List.map (fun c -> Analysis.actions beam.Span c loads)
        [ for i in 0..n do
              let x = beam.Span * float i / float n
              let ms = actsAll |> List.map (fun a -> Analysis.moment beam a x)
              let vs = actsAll |> List.map (fun a -> Analysis.shear beam a x)
              let mx = if ms.IsEmpty then 0.0 else List.max ms
              let mn = if ms.IsEmpty then 0.0 else List.min ms
              let vx = if vs.IsEmpty then 0.0 else List.max vs
              let vn = if vs.IsEmpty then 0.0 else List.min vs
              yield (x, mx, mn, vx, vn) ]
