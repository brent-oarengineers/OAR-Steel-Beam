namespace OAR.Steel

open System

type ElementSlenderness =
    { Element: string
      /// λe = (b/t) sqrt(fy/250)
      Lambda: float
      LambdaP: float
      LambdaY: float }

type SectionClass =
    | Compact
    | NonCompact
    | Slender

/// AS 4100 Cl. 5.2 section moment capacity about x
type SectionMoment =
    { Flange: ElementSlenderness
      Web: ElementSlenderness
      /// element with the highest λe/λey
      Governing: string
      Class: SectionClass
      /// effective section modulus, mm3
      Ze: float
      /// fy used (flange), MPa
      Fy: float
      /// Msx = fy Ze, N.mm
      Ms: float
      /// φMsx, N.mm
      PhiMs: float }

/// AS 4100 Cl. 5.6.1 member moment capacity for one segment, all intermediate values kept.
type MemberMoment =
    { Le: float
      /// reference buckling moment Mo, N.mm
      Mo: float
      AlphaS: float
      AlphaM: float
      /// true when αm came from a user override rather than Cl. 5.6.1.1(a)(iii)
      AlphaMOverridden: bool
      Ms: float
      /// φMb = φ αm αs Ms <= φMs, N.mm
      PhiMb: float
      PhiMs: float }

/// AS 4100 Cl. 5.11 shear capacity (unstiffened web, uniform shear stress distribution)
type ShearCapacity =
    { Aw: float
      Fyw: float
      /// web slenderness (dp/tw) sqrt(fyw/250)
      Lambda: float
      /// Vw = 0.6 fyw Aw, N
      Vw: float
      /// αv = (82/λ)^2 when λ > 82, else 1.0
      AlphaV: float
      Vv: float
      PhiVv: float }

module Capacity =

    [<Literal>]
    let phi = 0.9

    /// Table 5.2 limits (λep, λey) for a flange outstand (one edge supported, uniform compression)
    let private flangeLimits =
        function
        | WB -> 8.0, 14.0 // HW: heavily welded
        | UB
        | UC
        | PFC -> 9.0, 16.0 // HR: hot-rolled

    /// Table 5.2 limits for a web (both edges supported, compression at one edge, tension at the other)
    let private webLimits = 82.0, 115.0

    /// Section moment capacity about the major axis, AS 4100 Cl. 5.2
    let sectionMomentX (s: Section) (grade: int) : SectionMoment =
        let fyf, fyw = Sections.fy s grade
        let outstand = if s.Family = PFC then s.bf - s.tw else (s.bf - s.tw) / 2.0
        let lf = outstand / s.tf * sqrt (fyf / 250.0)
        let d1 = s.d - 2.0 * s.tf
        let lw = d1 / s.tw * sqrt (fyw / 250.0)
        let fp, fy' = flangeLimits s.Family
        let wp, wy = webLimits
        let flange: ElementSlenderness = { Element = "flange"; Lambda = lf; LambdaP = fp; LambdaY = fy' }
        let web: ElementSlenderness = { Element = "web"; Lambda = lw; LambdaP = wp; LambdaY = wy }
        let gov = if lf / fy' >= lw / wy then flange else web
        let zc = min s.Sx (1.5 * s.Zx)

        let cls, ze =
            if gov.Lambda <= gov.LambdaP then
                (Compact, zc)
            elif gov.Lambda <= gov.LambdaY then
                (NonCompact, s.Zx + (gov.LambdaY - gov.Lambda) / (gov.LambdaY - gov.LambdaP) * (zc - s.Zx))
            elif gov.Element = "flange" then
                (Slender, s.Zx * (gov.LambdaY / gov.Lambda)) // Cl. 5.2.5(a)
            else
                (Slender, s.Zx * (gov.LambdaY / gov.Lambda) ** 2.0) // Cl. 5.2.5(b)

        let ms = fyf * ze
        { Flange = flange
          Web = web
          Governing = gov.Element
          Class = cls
          Ze = ze
          Fy = fyf
          Ms = ms
          PhiMs = phi * ms }

    /// Reference buckling moment Mo, AS 4100 Cl. 5.6.1.1 (N.mm)
    let mo (s: Section) (e: float) (le: float) =
        let pi2 = Math.PI * Math.PI
        sqrt ((pi2 * e * s.Iy / (le * le)) * (Units.G_MPa * s.J + pi2 * e * s.Iw / (le * le)))

    /// Slenderness reduction factor αs, AS 4100 Cl. 5.6.1.1
    let alphaS (ms: float) (mo: float) =
        let r = ms / mo
        0.6 * (sqrt (r * r + 3.0) - r)

    /// Moment modification factor, AS 4100 Cl. 5.6.1.1(a)(iii):
    /// αm = 1.7 Mm* / sqrt(M2*^2 + M3*^2 + M4*^2) <= 2.5
    let alphaMFormula (mm: float) (m2: float) (m3: float) (m4: float) =
        let denom = sqrt (m2 * m2 + m3 * m3 + m4 * m4)
        if denom = 0.0 then 1.0 else min 2.5 (1.7 * abs mm / denom)

    /// Effective length Le = kt kl kr L (Cl. 5.6.3)
    let effectiveLength kt kl kr (segmentLength: float) = kt * kl * kr * segmentLength

    /// Member moment capacity for a segment of effective length le, AS 4100 Cl. 5.6.1
    let memberMoment (s: Section) (grade: int) (e: float) (le: float) (alphaM: float) (overridden: bool) : MemberMoment =
        let sm = sectionMomentX s grade
        let m0 = mo s e le
        let a_s = alphaS sm.Ms m0
        let mb = min (alphaM * a_s * sm.Ms) sm.Ms
        { Le = le
          Mo = m0
          AlphaS = a_s
          AlphaM = alphaM
          AlphaMOverridden = overridden
          Ms = sm.Ms
          PhiMb = phi * mb
          PhiMs = sm.PhiMs }

    /// Shear capacity of an unstiffened web, AS 4100 Cl. 5.11.2, 5.11.3 and 5.11.5.1.
    /// Aw = d tw for hot-rolled sections; web slenderness uses the clear depth dp = d - 2tf.
    let shear (s: Section) (grade: int) : ShearCapacity =
        let _, fyw = Sections.fy s grade
        let aw =
            match s.Family with
            | WB -> (s.d - 2.0 * s.tf) * s.tw // welded: web depth only
            | _ -> s.d * s.tw
        let dp = s.d - 2.0 * s.tf
        let lam = dp / s.tw * sqrt (fyw / 250.0)
        let vw = 0.6 * fyw * aw
        let av = if lam <= 82.0 then 1.0 else (82.0 / lam) ** 2.0
        let vv = av * vw
        { Aw = aw
          Fyw = fyw
          Lambda = lam
          Vw = vw
          AlphaV = av
          Vv = vv
          PhiVv = phi * vv }
