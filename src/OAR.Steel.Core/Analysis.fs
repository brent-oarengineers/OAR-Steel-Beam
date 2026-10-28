namespace OAR.Steel

open System

/// A signed action on the beam after factoring. Positive = downward.
/// Dist: w (N/mm) from a to b (mm). Conc: P (N) at x (mm).
type BeamLoad =
    | Dist of w: float * a: float * b: float
    | Conc of p: float * x: float

/// Simply supported beam, supports at x = 0 and x = Span.
type Beam = { Span: float; EI: float }

/// Statics and elastic deflection of a simply supported beam by superposition.
/// Sign convention: loads and deflection positive downward; sagging moment positive;
/// shear V(x) = left reaction minus load to the left of x.
module Analysis =

    /// Turn input loads into signed actions for a combination (self-weight must already be in the list).
    /// Wind loads tagged with a direction only act in combinations with the same Dir; untagged wind
    /// loads act in every direction. A combination with wind factors but no Dir is an error once any
    /// wind load it would pick up is tagged, since it would add the directions together.
    let actions (span: float) (combo: Combination) (loads: Load list) : BeamLoad list =
        let tagged =
            loads
            |> List.filter (fun l -> LoadCase.isWind l.Case && WindDir.normalise l.Dir <> "" && Combination.factor combo l.Case <> 0.0)
        if WindDir.normalise combo.Dir = "" && not tagged.IsEmpty then
            failwithf
                "combination %s has wind factors but no Dir, and the wind loads are split by direction (%s): set Dir on the combination, or clear Dir on the loads"
                combo.Id
                (String.Join(", ", WindDir.distinct (tagged |> List.map (fun l -> l.Dir))))
        loads
        |> List.choose (fun ld ->
            let applies =
                not (LoadCase.isWind ld.Case)
                || WindDir.normalise ld.Dir = ""
                || WindDir.same ld.Dir combo.Dir
            let f = if applies then Combination.factor combo ld.Case else 0.0
            if f = 0.0 || ld.Magnitude = 0.0 then None
            else
                let v = f * LoadCase.direction ld.Case * ld.Magnitude
                match ld.Shape with
                | FullSpan -> Some(Dist(v, 0.0, span))
                | Partial(a, b) -> Some(Dist(v, a, b))
                | Point x -> Some(Conc(v, x)))

    /// Support reactions (left, right), positive upward, N
    let reactions (beam: Beam) (acts: BeamLoad list) =
        let span = beam.Span
        acts
        |> List.fold
            (fun (ra, rb) act ->
                match act with
                | Conc(p, x) -> ra + p * (span - x) / span, rb + p * x / span
                | Dist(w, a, b) ->
                    let total = w * (b - a)
                    let c = (a + b) / 2.0
                    ra + total * (span - c) / span, rb + total * c / span)
            (0.0, 0.0)

    /// Shear force at x (N), just to the right of any point load located exactly at x
    let shear (beam: Beam) (acts: BeamLoad list) (x: float) =
        let ra, _ = reactions beam acts
        acts
        |> List.fold
            (fun v act ->
                match act with
                | Conc(p, xp) -> if xp <= x then v - p else v
                | Dist(w, a, b) ->
                    let e = min x b
                    if e > a then v - w * (e - a) else v)
            ra

    /// Bending moment at x (N.mm), sagging positive
    let moment (beam: Beam) (acts: BeamLoad list) (x: float) =
        let ra, _ = reactions beam acts
        acts
        |> List.fold
            (fun m act ->
                match act with
                | Conc(p, xp) -> if x > xp then m - p * (x - xp) else m
                | Dist(w, a, b) ->
                    let e = min x b
                    if e > a then m - w * (e - a) * (x - (a + e) / 2.0) else m)
            (ra * x)

    /// Deflection at x due to a point load p at xp (standard closed form)
    let private pointDeflection (beam: Beam) (p: float) (xp: float) (x: float) =
        let l = beam.Span
        let a = xp
        let b = l - xp
        if x <= a then
            p * b * x * (l * l - b * b - x * x) / (6.0 * l * beam.EI)
        else
            let xr = l - x
            p * a * xr * (l * l - a * a - xr * xr) / (6.0 * l * beam.EI)

    // 3-point Gauss-Legendre: exact for polynomials up to degree 5. The point-load deflection is a
    // cubic in the load position on each side of x, so splitting the load at x makes this exact.
    let private gauss =
        [| -sqrt (3.0 / 5.0), 5.0 / 9.0
           0.0, 8.0 / 9.0
           sqrt (3.0 / 5.0), 5.0 / 9.0 |]

    let private integrate (f: float -> float) (lo: float) (hi: float) =
        if hi <= lo then 0.0
        else
            let m = (lo + hi) / 2.0
            let h = (hi - lo) / 2.0
            gauss |> Array.sumBy (fun (t, wt) -> wt * f (m + h * t)) |> (*) h

    /// Deflection at x (mm), positive downward
    let deflection (beam: Beam) (acts: BeamLoad list) (x: float) =
        acts
        |> List.sumBy (fun act ->
            match act with
            | Conc(p, xp) -> pointDeflection beam p xp x
            | Dist(w, a, b) ->
                let f s = pointDeflection beam w s x
                integrate f a (min b x) + integrate f (max a x) b)

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

    /// Maximum deflection (location mm, value mm) for a set of actions
    let maxDeflection (beam: Beam) (acts: BeamLoad list) =
        maxAbs beam.Span (breakpoints acts) (deflection beam acts)

    /// Maximum moment (location mm, value N.mm), by absolute value
    let maxMoment (beam: Beam) (acts: BeamLoad list) =
        maxAbs beam.Span (breakpoints acts) (moment beam acts)

    /// Largest |V| occurs at a support or either side of a point load
    let maxShear (beam: Beam) (acts: BeamLoad list) =
        let ra, rb = reactions beam acts
        let candidates =
            [ 0.0, ra; beam.Span, -rb ]
            @ (breakpoints acts |> List.collect (fun x -> [ x, shear beam acts (x - 1e-6); x, shear beam acts x ]))
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
