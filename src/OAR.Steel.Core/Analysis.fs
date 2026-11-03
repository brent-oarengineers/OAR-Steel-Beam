namespace OAR.Steel

open System

/// A signed action on the beam after factoring. Positive = downward.
/// Dist: w (N/mm) from a to b (mm). Conc: P (N) at x (mm).
type BeamLoad =
    | Dist of w: float * a: float * b: float
    | Conc of p: float * x: float

/// Beam of total length Span on two simple supports at SupportA < SupportB (anywhere from 0 to Span,
/// so either end may overhang). A plain simply supported beam has the supports at 0 and Span.
/// Interior = further simple supports between A and B (continuous beam, e.g. two spans); empty for a
/// statically determinate beam. GAs = shear stiffness G·As (N) for shear deformation in deflections; 0 = bending only.
type Beam =
    { Span: float
      EI: float
      SupportA: float
      SupportB: float
      Interior: float list
      GAs: float }

module Beam =
    /// Simply supported: supports at both ends
    let simple (span: float) (ei: float) : Beam =
        { Span = span
          EI = ei
          SupportA = 0.0
          SupportB = span
          Interior = []
          GAs = 0.0 }

    /// Supports at xa and xb (overhangs beyond them)
    let supported (span: float) (ei: float) (xa: float) (xb: float) : Beam =
        if not (xa >= 0.0 && xb > xa && xb <= span) then
            failwithf "supports must satisfy 0 <= A < B <= length (got A = %g, B = %g, length %g)" xa xb span
        { Span = span
          EI = ei
          SupportA = xa
          SupportB = xb
          Interior = []
          GAs = 0.0 }

    /// Continuous beam with end supports at 0 and span and interior supports at the given positions
    /// (one interior support = two spans). No overhangs.
    let continuous (span: float) (ei: float) (interior: float list) : Beam =
        let xs = List.sort interior
        if xs.IsEmpty then failwith "continuous beam: give at least one interior support"
        for x in xs do
            if not (x > 1.0 && x < span - 1.0) then
                failwithf "continuous beam: interior support at x = %g must lie between the end supports (0 and %g)" x span
        for x1, x2 in List.pairwise xs do
            if x2 - x1 <= 1.0 then failwithf "continuous beam: interior supports at %g and %g coincide" x1 x2
        { Span = span
          EI = ei
          SupportA = 0.0
          SupportB = span
          Interior = xs
          GAs = 0.0 }

    /// Include shear deformation in deflections, with shear stiffness G·As (N)
    let withShear (gas: float) (b: Beam) : Beam = { b with GAs = gas }

    /// Shear area used for I-sections: clear web (d - 2tf)·tw (mm²)
    let shearArea (s: Section) = (s.d - 2.0 * s.tf) * s.tw

    /// Distance between the end supports (the whole length between A and B on a continuous beam)
    let backspan (b: Beam) = b.SupportB - b.SupportA

    /// Every support position, left to right
    let supports (b: Beam) = b.SupportA :: b.Interior @ [ b.SupportB ]

    /// The spans between consecutive supports, (start, end) in mm
    let spans (b: Beam) = supports b |> List.pairwise

    let isContinuous (b: Beam) = not b.Interior.IsEmpty

/// Statics and elastic deflection of a beam on two simple supports (with or without overhangs).
/// Sign convention: loads and deflection positive downward; sagging moment positive;
/// shear V(x) = reactions minus load to the left of x. Reactions positive upward.
module Analysis =

    /// Turn input loads into signed actions for a combination (self-weight must already be in the list).
    /// Wind loads tagged with a direction only act in combinations with the same Dir; untagged wind
    /// loads act in every direction. A combination with wind factors but no Dir is an error once any
    /// wind load it would pick up is tagged, since it would add the directions together.
    let actions (span: float) (combo: Combination) (loads: Load list) : BeamLoad list =
        let tagged =
            loads
            |> List.filter (fun l -> LoadCase.isWind l.Case && WindDir.normalise l.Dir <> "" && Combination.factorAny combo l.Case <> 0.0)
        if WindDir.normalise combo.Dir = "" && not tagged.IsEmpty then
            failwithf
                "combination %s has wind factors but no Dir, and the wind loads are split by direction (%s): set Dir on the combination, or clear Dir on the loads"
                combo.Id
                (String.Join(", ", WindDir.distinct (tagged |> List.map (fun l -> l.Dir))))
        // distributed loads are split at the supports when the combination factors loads beyond them differently
        let cuts =
            match combo.Outside with
            | Some o -> [ o.A; o.B ]
            | None -> []
        let pieces (a: float) (b: float) =
            let inner = cuts |> List.filter (fun c -> c > a + 1e-6 && c < b - 1e-6)
            (a :: inner) @ [ b ] |> List.pairwise
        loads
        |> List.collect (fun ld ->
            let applies =
                not (LoadCase.isWind ld.Case)
                || WindDir.normalise ld.Dir = ""
                || WindDir.same ld.Dir combo.Dir
            if not applies || ld.Magnitude = 0.0 then []
            else
                let v x = Combination.factorAt combo ld.Case x * LoadCase.direction ld.Case * ld.Magnitude
                let dist a b =
                    pieces a b
                    |> List.choose (fun (p, q) ->
                        let w = v ((p + q) / 2.0)
                        if w = 0.0 then None else Some(Dist(w, p, q)))
                match ld.Shape with
                | FullSpan -> dist 0.0 span
                | Partial(a, b) -> dist a b
                | Point x ->
                    let p = v x
                    if p = 0.0 then [] else [ Conc(p, x) ])

    /// End-support reactions (A, B) of the beam treated as statically determinate on A and B only
    let private detReactions (beam: Beam) (acts: BeamLoad list) =
        let xa = beam.SupportA
        let lab = Beam.backspan beam
        let total, momA =
            acts
            |> List.fold
                (fun (t, ma) act ->
                    match act with
                    | Conc(p, x) -> t + p, ma + p * (x - xa)
                    | Dist(w, a, b) ->
                        let f = w * (b - a)
                        t + f, ma + f * ((a + b) / 2.0 - xa))
                (0.0, 0.0)
        let rb = momA / lab
        total - rb, rb

    let private detShear (beam: Beam) (acts: BeamLoad list) (x: float) =
        let ra, rb = detReactions beam acts
        let fromSupports = (if beam.SupportA <= x then ra else 0.0) + (if beam.SupportB <= x then rb else 0.0)
        acts
        |> List.fold
            (fun v act ->
                match act with
                | Conc(p, xp) -> if xp <= x then v - p else v
                | Dist(w, a, b) ->
                    let e = min x b
                    if e > a then v - w * (e - a) else v)
            fromSupports

    let private detMoment (beam: Beam) (acts: BeamLoad list) (x: float) =
        let ra, rb = detReactions beam acts
        let fromSupports =
            (if x > beam.SupportA then ra * (x - beam.SupportA) else 0.0)
            + (if x > beam.SupportB then rb * (x - beam.SupportB) else 0.0)
        acts
        |> List.fold
            (fun m act ->
                match act with
                | Conc(p, xp) -> if x > xp then m - p * (x - xp) else m
                | Dist(w, a, b) ->
                    let e = min x b
                    if e > a then m - w * (e - a) * (x - (a + e) / 2.0) else m)
            fromSupports

    /// F(x) = double integral of M from 0 (Macaulay brackets), N.mm³. Exact for point loads and UDLs.
    let private doubleIntegralM (beam: Beam) (ra: float) (rb: float) (acts: BeamLoad list) (x: float) =
        let mac (c: float) (n: int) = if x > c then (x - c) ** float n else 0.0
        let fromSupports = ra * mac beam.SupportA 3 / 6.0 + rb * mac beam.SupportB 3 / 6.0
        acts
        |> List.fold
            (fun f act ->
                match act with
                | Conc(p, xp) -> f - p * mac xp 3 / 6.0
                | Dist(w, a, b) -> f - w * (mac a 4 - mac b 4) / 24.0)
            fromSupports

    let private detDeflection (beam: Beam) (acts: BeamLoad list) (x: float) =
        let ra, rb = detReactions beam acts
        let f = doubleIntegralM beam ra rb acts
        let xa, xb = beam.SupportA, beam.SupportB
        let fa, fb = f xa, f xb
        // δ(x) = (-F(x) + C1 x + C0) / EI, chosen so δ(xa) = δ(xb) = 0
        let c1 = (fb - fa) / (xb - xa)
        let c0 = fa - c1 * xa
        let bending = (-(f x) + c1 * x + c0) / beam.EI
        if beam.GAs > 0.0 then
            // ∫V dx = M, so δs = (M(x) - the straight line through M at the two supports) / GAs
            let m = detMoment beam acts
            let ma, mb = m xa, m xb
            let mLine = ma + (mb - ma) * (x - xa) / (xb - xa)
            bending + (m x - mLine) / beam.GAs
        else
            bending

    /// Solve the small dense system a·x = b (Gaussian elimination, partial pivoting)
    let private solve (a: float[,]) (b: float[]) =
        let n = b.Length
        let m = Array2D.copy a
        let v = Array.copy b
        for k in 0 .. n - 1 do
            let piv = seq { k .. n - 1 } |> Seq.maxBy (fun i -> abs m.[i, k])
            if abs m.[piv, k] < 1e-300 then failwith "continuous beam: singular support system"
            if piv <> k then
                for j in 0 .. n - 1 do
                    let t = m.[k, j]
                    m.[k, j] <- m.[piv, j]
                    m.[piv, j] <- t
                let t = v.[k]
                v.[k] <- v.[piv]
                v.[piv] <- t
            for i in k + 1 .. n - 1 do
                let f = m.[i, k] / m.[k, k]
                for j in k .. n - 1 do
                    m.[i, j] <- m.[i, j] - f * m.[k, j]
                v.[i] <- v.[i] - f * v.[k]
        let x = Array.zeroCreate n
        for i in n - 1 .. -1 .. 0 do
            let mutable sum = v.[i]
            for j in i + 1 .. n - 1 do
                sum <- sum - m.[i, j] * x.[j]
            x.[i] <- sum / m.[i, i]
        x

    /// Interior support reactions (N, positive upward), by the flexibility method: release the interior
    /// supports, then choose the reactions that bring the deflection back to zero at each one. The same
    /// deflection function (with shear deformation when GAs > 0) gives both the release deflections and
    /// the flexibilities, so the result is exact for the beam theory used.
    let interiorReactions (beam: Beam) (acts: BeamLoad list) : float list =
        match beam.Interior with
        | [] -> []
        | xs ->
            let xa = List.toArray xs
            let n = xa.Length
            let d0 = xa |> Array.map (detDeflection beam acts)
            let f = Array2D.init n n (fun i j -> detDeflection beam [ Conc(1.0, xa.[j]) ] xa.[i])
            solve f d0 |> Array.toList

    /// The actions plus the interior support reactions as upward point loads, so that the beam can be
    /// treated as determinate on its end supports
    let private withInterior (beam: Beam) (acts: BeamLoad list) =
        match beam.Interior with
        | [] -> acts
        | xs -> acts @ List.map2 (fun x r -> Conc(-r, x)) xs (interiorReactions beam acts)

    /// End-support reactions (A, B), positive upward, N. On a continuous beam the interior supports carry
    /// the rest (interiorReactions; supportReactions gives them all in order).
    let reactions (beam: Beam) (acts: BeamLoad list) = detReactions beam (withInterior beam acts)

    /// Every support reaction, left to right: (x mm, R N positive upward)
    let supportReactions (beam: Beam) (acts: BeamLoad list) =
        let ra, rb = reactions beam acts
        (beam.SupportA, ra) :: List.zip beam.Interior (interiorReactions beam acts) @ [ beam.SupportB, rb ]

    /// Shear force at x (N), just to the right of any point load or support located exactly at x
    let shear (beam: Beam) (acts: BeamLoad list) (x: float) = detShear beam (withInterior beam acts) x

    /// Bending moment at x (N.mm), sagging positive
    let moment (beam: Beam) (acts: BeamLoad list) (x: float) = detMoment beam (withInterior beam acts) x

    /// Deflection at x (mm), positive downward: δ'' = -M/EI with δ = 0 at every support,
    /// plus shear deformation δs' = V/GAs when the beam has a shear stiffness.
    let deflection (beam: Beam) (acts: BeamLoad list) (x: float) = detDeflection beam (withInterior beam acts) x

    /// Location and value of the largest |f(x)| between lo and hi.
    /// Samples the interval, adds load boundary points, then refines with golden-section search.
    let maxAbsOn (lo: float) (hi: float) (breakpoints: float list) (f: float -> float) =
        let n = 400
        let len = hi - lo
        let xs =
            [ for i in 0..n -> lo + len * float i / float n ] @ (breakpoints |> List.filter (fun x -> x >= lo && x <= hi))
            |> List.sort

        let x0 = xs |> List.maxBy (fun x -> abs (f x))
        // refine within one sample step either side
        let step = len / float n
        let mutable a = max lo (x0 - step)
        let mutable b = min hi (x0 + step)
        let g x = -(abs (f x))
        let r = (sqrt 5.0 - 1.0) / 2.0
        let mutable c = b - r * (b - a)
        let mutable d = a + r * (b - a)
        for _ in 1..60 do
            if g c < g d then b <- d else a <- c
            c <- b - r * (b - a)
            d <- a + r * (b - a)
        let xr = (a + b) / 2.0
        let best = if abs (f xr) >= abs (f x0) then xr else x0
        best, f best

    /// Location and value of the largest |f(x)| over the whole span
    let maxAbs (span: float) (breakpoints: float list) (f: float -> float) = maxAbsOn 0.0 span breakpoints f

    let breakpoints (acts: BeamLoad list) =
        acts
        |> List.collect (fun act ->
            match act with
            | Conc(_, x) -> [ x ]
            | Dist(_, a, b) -> [ a; b ])

    /// Load breakpoints plus the support positions (where M and V change form)
    let breakpointsOn (beam: Beam) (acts: BeamLoad list) =
        beam.SupportA :: beam.SupportB :: beam.Interior @ breakpoints acts

    /// Maximum deflection (location mm, value mm) for a set of actions
    let maxDeflection (beam: Beam) (acts: BeamLoad list) =
        maxAbs beam.Span (breakpointsOn beam acts) (deflection beam acts)

    /// Maximum moment (location mm, value N.mm), by absolute value
    let maxMoment (beam: Beam) (acts: BeamLoad list) =
        maxAbs beam.Span (breakpointsOn beam acts) (moment beam acts)

    /// Largest |V|: V is linear between load points and supports, so it peaks either side of one
    let maxShear (beam: Beam) (acts: BeamLoad list) =
        let xs =
            (0.0 :: beam.Span :: breakpointsOn beam acts)
            |> List.filter (fun x -> x >= 0.0 && x <= beam.Span)
            |> List.distinct
            |> List.sort
        let candidates =
            xs
            |> List.collect (fun x ->
                [ if x > 0.0 then yield x, shear beam acts (x - 1e-6)
                  if x < beam.Span then yield x, shear beam acts x ])
        candidates |> List.maxBy (fun (_, v) -> abs v)

    /// Self-weight load (G, full span) for a section
    let selfWeightLoad (s: Section) : Load =
        { Case = G
          Shape = FullSpan
          Magnitude = Units.selfWeight s.Mass
          Description = sprintf "Self-weight %s" s.Designation
          Dir = "" }

    /// Line-load step profile per case for plotting, rows of (x, [w for G; Q; Wd; Wu; Wid; Wiu]).
    /// w in N/mm (= kN/m), positive = downward (gravity direction). Includes zero-height start and end rows.
    let lineLoadProfile (span: float) (loads: Load list) =
        let dist =
            loads
            |> List.choose (fun l ->
                let w = l.Magnitude * LoadCase.direction l.Case
                match l.Shape with
                | FullSpan -> Some(l.Case, w, 0.0, span)
                | Partial(a, b) -> Some(l.Case, w, a, b)
                | Point _ -> None)
        let xs =
            (0.0 :: span :: (dist |> List.collect (fun (_, _, a, b) -> [ a; b ])))
            |> List.distinct
            |> List.sort
        let zero = LoadCase.all |> List.map (fun _ -> 0.0)
        let wAt (c: LoadCase) (xm: float) =
            dist |> List.sumBy (fun (lc, w, a, b) -> if lc = c && xm > a && xm < b then w else 0.0)
        [ yield (0.0, zero)
          for x1, x2 in List.pairwise xs do
              let xm = (x1 + x2) / 2.0
              let ws = LoadCase.all |> List.map (fun c -> wAt c xm)
              yield (x1, ws)
              yield (x2, ws)
          yield (span, zero) ]

    /// Point loads for plotting: (x mm, P N signed +down, case)
    let pointLoads (loads: Load list) =
        loads
        |> List.choose (fun l ->
            match l.Shape with
            | Point x -> Some(x, l.Magnitude * LoadCase.direction l.Case, l.Case)
            | _ -> None)

/// Pattern loading for beams with an overhang (AS/NZS 1170.0 Cl. 4.2): imposed action is applied to
/// the parts of the beam where it is unfavourable, and an equilibrium combination (Cl. 4.2.1) is added
/// for the back-support reaction (hold-down).
module Patterns =

    let hasOverhang (beam: Beam) = beam.SupportA > 1e-6 || beam.SupportB < beam.Span - 1e-6

    let private outside (beam: Beam) (factors: Map<LoadCase, float>) : Outside =
        { A = beam.SupportA
          B = beam.SupportB
          Factors = factors }

    /// Every strength combination that includes Q, plus the same with Q on the backspan only ("-span")
    /// and on the overhang only ("-cant"). On a two-span continuous beam: Q on span 1 only ("-span1") and on
    /// span 2 only ("-span2"), which give the largest span moments and deflections (the full pattern gives the
    /// largest hogging at the interior support). Unchanged for a simply supported beam.
    let expand (beam: Beam) (combos: Combination list) : Combination list =
        if Beam.isContinuous beam then
            let spans = Beam.spans beam
            if spans.Length > 2 then
                failwith "pattern loading is only set up for two spans (one interior support)"
            combos
            |> List.collect (fun c ->
                let q = Combination.factor c Q
                if q = 0.0 || c.Outside.IsSome then
                    [ c ]
                else
                    let noQ = c.Factors.Remove Q
                    c
                    :: (spans
                        |> List.mapi (fun i (a, b) ->
                            { c with
                                Id = sprintf "%s-span%d" c.Id (i + 1)
                                Description = sprintf "%s (Q on span %d only)" c.Description (i + 1)
                                Outside = Some { A = a; B = b; Factors = noQ } })))
        elif not (hasOverhang beam) then
            combos
        else
            combos
            |> List.collect (fun c ->
                let q = Combination.factor c Q
                if q = 0.0 || c.Outside.IsSome then
                    [ c ]
                else
                    let noQ = c.Factors.Remove Q
                    [ c
                      { c with
                          Id = c.Id + "-span"
                          Description = c.Description + " (Q backspan only)"
                          Outside = Some(outside beam noQ) }
                      { c with
                          Id = c.Id + "-cant"
                          Description = c.Description + " (Q overhang only)"
                          Factors = noQ
                          Outside = Some(outside beam c.Factors) } ])

    /// Equilibrium combination for the back-support reaction (Cl. 4.2.1): stabilising G on the backspan
    /// at 0.9, destabilising G at 1.2 and Q at 1.5 on the overhang. Reactions only (Strength = false).
    let equilibrium (beam: Beam) : Combination list =
        if not (hasOverhang beam) then
            []
        else
            [ { Id = "EQ"
                Description = "Equilibrium (Cl. 4.2.1): 0.9G backspan, 1.2G + 1.5Q overhang"
                CriticalFlange = Top
                Factors = Map.ofList [ G, 0.9 ]
                Dir = ""
                Outside = Some(outside beam (Map.ofList [ G, 1.2; Q, 1.5 ]))
                Strength = false } ]
