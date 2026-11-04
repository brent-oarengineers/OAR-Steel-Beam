namespace OAR.Steel

/// Deflection limit for one SLS case: span/ratio, capped by an absolute maximum when given.
type DeflectionCriterion =
    { Ratio: float
      MaxMm: float option }

type DeflectionResult =
    { Name: string
      /// maximum deflection, mm (absolute value)
      Actual: float
      /// location of the maximum, mm from left support
      At: float
      /// governing limit, mm
      Limit: float
      /// span / actual (infinity when actual = 0)
      SpanRatio: float
      Utilisation: float }

module Serviceability =

    let limit (span: float) (c: DeflectionCriterion) =
        let byRatio = span / c.Ratio
        match c.MaxMm with
        | Some m when m > 0.0 -> min byRatio m
        | _ -> byRatio

    let private make name (at: float) (d: float) (length: float) (crit: DeflectionCriterion) : DeflectionResult =
        let actual = abs d
        let lim = limit length crit
        { Name = name
          Actual = actual
          At = at
          Limit = lim
          SpanRatio = (if actual > 0.0 then length / actual else infinity)
          Utilisation = actual / lim }

    /// Worst deflection within one span (a to b) over a set of load patterns; L = that span
    let private spanResult name (beam: Beam) (a: float, b: float) (patterns: BeamLoad list list) crit =
        let best =
            patterns
            |> List.map (fun acts -> Analysis.maxAbsOn a b (Analysis.breakpointsOn beam acts) (Analysis.deflection beam acts))
            |> List.maxBy (fun (_, d) -> abs d)
        make name (fst best) (snd best) (b - a) crit

    /// Worst tip deflection of a right-hand overhang over a set of load patterns; limit length = 2 × overhang
    let private tipResult name (beam: Beam) (patterns: BeamLoad list list) crit =
        let a = beam.Span - beam.SupportB
        let d = patterns |> List.map (fun acts -> Analysis.deflection beam acts beam.Span) |> List.maxBy abs
        make name beam.Span d (2.0 * a) crit

    /// The five standard checks, in sheet order:
    /// Δg, Δg+ψl.q, Δg+ψs.q, Δq, Δ1kN (1 kN midway between the supports).
    /// With an overhang each case is checked between the supports (L = backspan) and at the tip
    /// (L = 2 × overhang), with Q on the whole beam, the backspan only and the overhang only; the
    /// tip rows follow as "… tip" and Δ1kN tip has 1 kN at the tip. On a two-span continuous beam the
    /// five checks are made for span 1 (L = span 1) then span 2 ("… span 2", L = span 2), with Q on both
    /// spans, span 1 only and span 2 only, and Δ1kN at the middle of each span.
    /// `loads` must include self-weight if required.
    let check (beam: Beam) (loads: Load list) (criteria: DeflectionCriterion list) (psiL: float) (psiS: float) =
        if List.length criteria <> 5 then failwith "deflection criteria: expected 5 rows"
        let crit = List.toArray criteria
        let span = beam.Span
        let overhang = Patterns.hasOverhang beam
        let spans = Beam.spans beam
        // Q patterns (whole beam; with an overhang also backspan only and overhang only)
        let patterns (factors: (LoadCase * float) list) =
            let c = Combination.ofFactors "sls" factors
            let all = Analysis.actions span c loads
            if Combination.factor c Q = 0.0 then
                [ all ]
            elif spans.Length > 1 then
                let noQ = (Map.ofList factors).Remove Q
                all
                :: (spans |> List.map (fun (a, b) -> Analysis.actions span { c with Outside = Some { A = a; B = b; Factors = noQ } } loads))
            elif not overhang then
                [ all ]
            else
                let full = Map.ofList factors
                let noQ = full.Remove Q
                let o f : Outside = { A = beam.SupportA; B = beam.SupportB; Factors = f }
                [ all
                  Analysis.actions span { c with Outside = Some(o noQ) } loads
                  Analysis.actions span { c with Factors = noQ; Outside = Some(o full) } loads ]
        let cases =
            [ "Δg", patterns [ G, 1.0 ], crit.[0]
              "Δg+ψl·q", patterns [ G, 1.0; Q, psiL ], crit.[1]
              "Δg+ψs·q", patterns [ G, 1.0; Q, psiS ], crit.[2]
              "Δq", patterns [ Q, 1.0 ], crit.[3]
              "Δ1kN", [], crit.[4] ]
        let spanRows =
            spans
            |> List.mapi (fun k (a, b) ->
                let suffix = if k = 0 then "" else sprintf " span %d" (k + 1)
                cases
                |> List.map (fun (n, p, c) ->
                    let p' = if n = "Δ1kN" then [ [ Conc(Units.kN 1.0, (a + b) / 2.0) ] ] else p
                    spanResult (n + suffix) beam (a, b) p' c))
            |> List.concat
        if not overhang then
            spanRows
        else
            let tipRows =
                cases
                |> List.map (fun (n, p, c) ->
                    let p' = if n = "Δ1kN" then [ [ Conc(Units.kN 1.0, beam.Span) ] ] else p
                    tipResult (n + " tip") beam p' c)
            spanRows @ tipRows

    /// The five checks with span and tip combined: the row with the higher utilisation for each case
    let governing (results: DeflectionResult list) =
        let arr = List.toArray results
        if arr.Length <= 5 then results
        // the second row (tip, or span 2) only when it is clearly worse, so equal spans report span 1
        else [ for i in 0..4 -> if arr.[i + 5].Utilisation > arr.[i].Utilisation * (1.0 + 1e-9) + 1e-12 then arr.[i + 5] else arr.[i] ]

    /// Deflected shapes for plotting at n+1 stations: (x, Δg, Δg+ψl.q, Δg+ψs.q, Δq), mm, downward positive
    let curves (beam: Beam) (loads: Load list) (psiL: float) (psiS: float) (n: int) =
        let span = beam.Span
        let acts factors = Analysis.actions span (Combination.ofFactors "c" factors) loads
        let g = acts [ G, 1.0 ]
        let gl = acts [ G, 1.0; Q, psiL ]
        let gs = acts [ G, 1.0; Q, psiS ]
        let q = acts [ Q, 1.0 ]
        [ for i in 0..n do
              let x = span * float i / float n
              let d a = Analysis.deflection beam a x
              yield (x, d g, d gl, d gs, d q) ]
