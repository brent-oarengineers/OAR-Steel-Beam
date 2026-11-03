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
      /// αm for the top-flange segment that starts at this point (overrides Cl. 5.6.1.1(a)(iii))
      AlphaMTop: float option
      /// αm for the bottom-flange segment that starts at this point
      AlphaMBottom: float option
      /// kl for the top-flange segment that starts at this point (overrides the kl from the factor box)
      KlOverrideTop: float option
      /// kl for the bottom-flange segment that starts at this point
      KlOverrideBottom: float option }

/// Load height factor kl by load direction (Table 5.6.3(2), loads applied at the top flange) and kr per flange.
/// A downward load on the top flange acts towards the shear centre and is destabilising (kl 1.4, free tip 2.0);
/// an upward load (wind uplift) acts away from it and is not (kl 1.0).
type KFactors =
    { /// kl for a segment whose own load acts downward (restrained at both ends)
      KlDown: float
      /// kl for a segment whose own load acts upward
      KlUp: float
      KrTop: float
      KrBottom: float
      /// flange continuously restrained along the span (e.g. sheeting / closely spaced purlins): φMb = φMsx
      ContinuousTop: bool
      ContinuousBottom: bool
      /// kl for a cantilever segment with a free (U) tip, downward / upward load
      KlTipDown: float
      KlTipUp: float }

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
      /// where the αm used came from: "Cl. 5.6.1.1(a)(iii)", "Table 5.6.2 …" or "override"
      AlphaMBasis: string
      MemberCap: MemberMoment
      Utilisation: float
      /// basis of kl: "load down", "load up", "no load in segment" or "override"
      KlBasis: string
      /// kl taken from the restraints table for this segment
      KlOverridden: bool
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

    let alphaMFor (flange: Flange) (r: RestraintPoint) =
        match flange with
        | Top -> r.AlphaMTop
        | Bottom -> r.AlphaMBottom

    let klFor (flange: Flange) (r: RestraintPoint) =
        match flange with
        | Top -> r.KlOverrideTop
        | Bottom -> r.KlOverrideBottom

    let flangeName =
        function
        | Top -> "top"
        | Bottom -> "bottom"

    /// Segments for one flange: consecutive points that restrain that flange.
    /// Both ends of the beam must be listed with a restraint to that flange, and the supports must be F or P.
    let segments (beam: Beam) (flange: Flange) (pts: RestraintPoint list) =
        let span = beam.Span
        let restrained =
            pts
            |> List.choose (fun p -> restraintFor flange p |> Option.map (fun t -> p, t))
            |> List.sortBy (fun (p, _) -> p.X)

        let tol = 1.0 // mm
        let at x =
            restrained |> List.tryFind (fun (p, _) -> abs (p.X - x) <= tol)

        let isSupport x = abs (x - beam.SupportA) <= tol || abs (x - beam.SupportB) <= tol
        let describe x =
            if abs (x - beam.SupportA) <= tol then "support A"
            elif abs (x - beam.SupportB) <= tol then "support B"
            elif x <= tol then "left end"
            else "right end (tip)"

        for x in List.distinct [ 0.0; beam.SupportA; beam.SupportB; span ] do
            match at x with
            | None ->
                failwithf "restraints: the %s (x = %g) must be listed with a restraint to the %s flange" (describe x) x (flangeName flange)
            | Some(_, t) when isSupport x && t <> RF && t <> RP ->
                failwithf "restraints: the %s must be F or P for the %s flange (got %s)" (describe x) (flangeName flange) (RestraintType.code t)
            | _ -> ()

        // at a free end (cantilever tip) only U, F or P make sense: an L restraint holds one flange laterally but
        // nothing prevents twist there, so it does not form a segment end (Space Gass refuses it too)
        for p, t in restrained do
            let freeEnd = (p.X <= tol || abs (p.X - span) <= tol) && not (isSupport p.X)
            if t = RL && freeEnd then
                failwithf "restraints: L for the %s flange at the free end (x = %g) is not a valid segment end – use U, or F / P if the tip is also restrained against twist" (flangeName flange) p.X
        // U (unrestrained) is only valid at a free end, i.e. the tip of a cantilever beyond a support
        for p, t in restrained do
            if t = RU then
                let atFreeEnd = (p.X <= tol || abs (p.X - span) <= tol) && not (isSupport p.X)
                if not atFreeEnd then
                    failwithf "restraints: U for the %s flange at x = %g is only allowed at the free end of a cantilever" (flangeName flange) p.X

        if restrained |> List.exists (fun (p, _) -> p.X < -tol || p.X > span + tol) then
            failwith "restraints: a restraint position is outside the beam"

        // an αm or kl override must sit on a row that starts a segment for that flange, otherwise it would be ignored
        for p in pts do
            for what, v in [ "αm", alphaMFor flange p; "kl", klFor flange p ] do
                match v with
                | None -> ()
                | Some _ ->
                    if (restraintFor flange p).IsNone then
                        failwithf "restraints: %s %s is given at x = %g but the %s flange is not restrained there (no segment starts at that row)"
                            what (flangeName flange) p.X (flangeName flange)
                    if abs (p.X - span) <= tol then
                        failwithf "restraints: %s %s is given at the right end (x = %g); it applies to the segment starting at that row, so leave it blank"
                            what (flangeName flange) p.X

        restrained
        |> List.pairwise
        |> List.filter (fun ((p1, _), (p2, _)) -> p2.X - p1.X > tol)

    /// Moment that puts this flange in compression (sagging for top, hogging for bottom), >= 0
    let private compressive (flange: Flange) (m: float) =
        match flange with
        | Top -> max 0.0 m
        | Bottom -> max 0.0 (-m)

    /// Check every segment for one combination. Both flanges are considered: each flange's segments
    /// (from its own restraints, kl and kr) are checked wherever the moment puts that flange in compression.
    /// M* for a segment is the largest moment compressing that flange; αm uses the whole segment's
    /// moment diagram (Cl. 5.6.1.1(a)(iii), Mm = max |M*| in the segment). Moments below 1% of the
    /// combination's peak are ignored so that sign noise does not create checks.
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
        let bps = Analysis.breakpointsOn beam acts
        let m x = Analysis.moment beam acts x
        let span = beam.Span
        let _, peak = Analysis.maxAbsOn 0.0 span bps m
        let tolM = 0.01 * abs peak
        let sm = Capacity.sectionMomentX s grade

        [ for flange in [ Top; Bottom ] do
              let kl, kr, continuous =
                  match flange with
                  | Top -> 1.0, k.KrTop, k.ContinuousTop
                  | Bottom -> 1.0, k.KrBottom, k.ContinuousBottom
              let mc x = compressive flange (m x)
              let _, mcBeam = Analysis.maxAbsOn 0.0 span bps mc
              if mcBeam > tolM && mcBeam > 0.0 then
                  if continuous then
                      // flange continuously restrained: no member buckling, φMb = φMsx over the whole beam
                      let xm, mcv = Analysis.maxAbsOn 0.0 span bps mc
                      let cap: MemberMoment =
                          { Le = 0.0
                            Mo = infinity
                            AlphaS = 1.0
                            AlphaM = 1.0
                            AlphaMOverridden = false
                            Ms = sm.Ms
                            PhiMb = sm.PhiMs
                            PhiMs = sm.PhiMs }
                      let r: SegmentCheck =
                          { Combo = combo.Id
                            Flange = flange
                            X1 = 0.0
                            X2 = span
                            Ends = "cont."
                            Length = span
                            Kt = 1.0
                            Kl = 1.0
                            Kr = 1.0
                            M2 = m (span / 4.0)
                            M3 = m (span / 2.0)
                            M4 = m (3.0 * span / 4.0)
                            Mm = mcv
                            XAtMm = xm
                            MmSigned = m xm
                            AlphaMFormula = 1.0
                            AlphaMBasis = "continuous restraint"
                            MemberCap = cap
                            Utilisation = mcv / cap.PhiMb
                            KlBasis = "continuous restraint"
                            KlOverridden = false
                            Note = "" }
                      yield r
                  else
                      for (p1, t1), (p2, t2) in segments beam flange pts do
                          let xc, mcs = Analysis.maxAbsOn p1.X p2.X bps mc
                          if mcs > tolM then
                              let len = p2.X - p1.X
                              let q (f: float) = p1.X + len * f / 4.0
                              let m2 = m (q 1.0)
                              let m3 = m (q 2.0)
                              let m4 = m (q 3.0)
                              let _, mmAll = Analysis.maxAbsOn p1.X p2.X bps m
                              let amFormula = Capacity.alphaMFormula mmAll m2 m3 m4
                              // a segment unrestrained at one end (cantilever, free tip U): αm from Table 5.6.2 –
                              // a point load at the tip gives 1.25; otherwise (distributed load, or point loads along the
                              // overhang, which concentrate the moment towards the support) 2.25. Mixed: the lower value,
                              // with a moment-weighted estimate in the note to guide an override.
                              let cantilever = (t1 = RU || t2 = RU)
                              let tipX, supX = (if t2 = RU then p2.X, p1.X else p1.X, p2.X)
                              let near5 x = abs (x - tipX) <= 0.05 * len + 1.0
                              let inSeg (x: float) = x >= p1.X - 1.0 && x <= p2.X + 1.0
                              // moment about the supported end from loads on the segment: tip point loads and the rest
                              let mTip, mRest =
                                  acts
                                  |> List.fold
                                      (fun (mt, mr) act ->
                                          match act with
                                          | Conc(p, x) when inSeg x ->
                                              if near5 x then mt + p * abs (x - supX), mr else mt, mr + p * abs (x - supX)
                                          | Dist(w, a0, b0) ->
                                              let lo, hi = max a0 p1.X, min b0 p2.X
                                              if hi > lo then mt, mr + w * (hi - lo) * abs ((lo + hi) / 2.0 - supX) else mt, mr
                                          | _ -> mt, mr)
                                      (0.0, 0.0)
                              let amAuto, amBasis, mixedNote =
                                  if cantilever then
                                      if abs mTip > 0.0 then
                                          let mixed = abs mRest > 0.0
                                          let est =
                                              if mixed && sign mTip = sign mRest then
                                                  Some((1.25 * mTip + 2.25 * mRest) / (mTip + mRest))
                                              else None
                                          let note =
                                              if not mixed then ""
                                              else
                                                  match est with
                                                  | Some v ->
                                                      sprintf "mixed loading on the cantilever: Table 5.6.2 lower value 1.25 adopted; moment-weighted estimate %.2f – override αm if you judge it appropriate" v
                                                  | None -> "mixed loading on the cantilever: Table 5.6.2 lower value 1.25 adopted"
                                          1.25, "Table 5.6.2, point load at tip", note
                                      else 2.25, "Table 5.6.2, distributed / along-span load", ""
                                  else amFormula, "Cl. 5.6.1.1(a)(iii)", ""
                              let am, overridden, amBasisUsed =
                                  match alphaMFor flange p1 with
                                  | Some v -> (v, true, "override")
                                  | None -> (amAuto, false, amBasis)
                              let ktv = kt s len t1 t2
                              let opposite = Analysis.maxAbsOn p1.X p2.X bps (fun x -> compressive (if flange = Top then Bottom else Top) (m x)) |> snd
                              let reverses = opposite > tolM
                              // kl from the direction of the load acting on the segment (Table 5.6.3(2), loads at the top flange):
                              // net downward = destabilising; net upward = not; no load within the segment = 1.0.
                              // For a restrained segment, loads exactly at its ends are not "within" it; at a free tip they count.
                              let netLoad =
                                  acts
                                  |> List.sumBy (fun act ->
                                      match act with
                                      | Conc(p, x) ->
                                          let within = if cantilever then inSeg x else x > p1.X + 1.0 && x < p2.X - 1.0
                                          if within then p else 0.0
                                      | Dist(w, a0, b0) ->
                                          let lo, hi = max a0 p1.X, min b0 p2.X
                                          if hi > lo then w * (hi - lo) else 0.0)
                              let klAuto, klBasisAuto =
                                  if netLoad > 0.0 then (if cantilever then k.KlTipDown else k.KlDown), "load down (destabilising)"
                                  elif netLoad < 0.0 then (if cantilever then k.KlTipUp else k.KlUp), "load up"
                                  else 1.0, "no load within the segment"
                              let klUsed, klOverridden, klBasis =
                                  match klFor flange p1 with
                                  | Some v -> v, true, "override"
                                  | None -> klAuto, false, klBasisAuto
                              let le = Capacity.effectiveLength ktv klUsed kr len
                              let cap = Capacity.memberMoment s grade e le am overridden
                              let join (a: string) (b: string) = if a = "" then b elif b = "" then a else a + "; " + b
                              let note =
                                  [ (if cantilever && not overridden then sprintf "free tip: αm = %.2f from %s" am amBasis else "")
                                    (if overridden then "" else mixedNote)
                                    (if klOverridden then sprintf "kl = %.2f from the restraints table" klUsed else "")
                                    (if reverses then "moment reverses in this segment (αm from the whole segment)" else "") ]
                                  |> List.fold join ""
                              let r: SegmentCheck =
                                  { Combo = combo.Id
                                    Flange = flange
                                    X1 = p1.X
                                    X2 = p2.X
                                    Ends = RestraintType.code t1 + RestraintType.code t2
                                    Length = len
                                    Kt = ktv
                                    Kl = klUsed
                                    Kr = kr
                                    M2 = m2
                                    M3 = m3
                                    M4 = m4
                                    Mm = mcs
                                    XAtMm = xc
                                    MmSigned = m xc
                                    AlphaMFormula = amFormula
                                    AlphaMBasis = amBasisUsed
                                    MemberCap = cap
                                    Utilisation = mcs / cap.PhiMb
                                    KlBasis = klBasis
                                    KlOverridden = klOverridden
                                    Note = note }
                              yield r ]

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
