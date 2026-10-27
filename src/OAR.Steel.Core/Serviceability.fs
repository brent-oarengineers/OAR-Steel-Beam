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

    let private result name (beam: Beam) acts crit : DeflectionResult =
        let at, d = Analysis.maxDeflection beam acts
        let actual = abs d
        let lim = limit beam.Span crit
        { Name = name
          Actual = actual
          At = at
          Limit = lim
          SpanRatio = (if actual > 0.0 then beam.Span / actual else infinity)
          Utilisation = actual / lim }

    /// The five standard checks, in sheet order:
    /// Δg, Δg+ψl.q, Δg+ψs.q, Δq, Δ1kN (1 kN at midspan).
    /// `loads` must already include self-weight if required.
    let check (beam: Beam) (loads: Load list) (criteria: DeflectionCriterion list) (psiL: float) (psiS: float) =
        if List.length criteria <> 5 then failwith "deflection criteria: expected 5 rows"
        let crit = List.toArray criteria
        let span = beam.Span
        let combo name factors = Analysis.actions span (Combination.ofFactors name factors) loads
        [ result "Δg" beam (combo "G" [ G, 1.0 ]) crit.[0]
          result "Δg+ψl·q" beam (combo "G+psiL.Q" [ G, 1.0; Q, psiL ]) crit.[1]
          result "Δg+ψs·q" beam (combo "G+psiS.Q" [ G, 1.0; Q, psiS ]) crit.[2]
          result "Δq" beam (combo "Q" [ Q, 1.0 ]) crit.[3]
          result "Δ1kN" beam [ Conc(Units.kN 1.0, span / 2.0) ] crit.[4] ]

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
