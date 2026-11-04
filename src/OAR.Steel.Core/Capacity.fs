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
      /// f*vm / f*va, ratio of maximum to average web shear stress (Cl. 5.11.3); 1.0 = uniform (I-sections)
      StressRatio: float
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
        | RHS
        | SHS -> 30.0, 40.0 // CF: both edges supported (between the webs), uniform compression

    /// Table 5.2 limits for a web (both edges supported, compression at one edge, tension at the other)
    let private webLimits = 82.0, 115.0

    /// Section moment capacity about the major axis, AS 4100 Cl. 5.2
    let sectionMomentX (s: Section) (grade: int) : SectionMoment =
        let fyf, fyw = Sections.fy s grade
        // flange width: outstand from the web (I-section, PFC), or clear width between the webs (RHS / SHS)
        let outstand =
            match s.Family with
            | PFC -> s.bf - s.tw
            | RHS
            | SHS -> s.bf - 2.0 * s.tw
            | _ -> (s.bf - s.tw) / 2.0
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
            elif gov.Element = "flange" && Family.isHollow s.Family then
                // Cl. 5.2.5, effective section: the compression flange (supported by both webs) is effective over
                // be = b λey/λ; Ze = Ieff / ymax about the shifted neutral axis. Matches the tabulated C450L0 Zex.
                let t = s.tf
                let bc = s.bf - 2.0 * t
                let be = bc * gov.LambdaY / gov.Lambda
                let dA = (bc - be) * t
                let y = s.d / 2.0 - t / 2.0
                let a = s.Ag - dA
                let shift = dA * y / a
                let iEff = s.Ix - dA * y * y - (bc - be) * t ** 3.0 / 12.0 - a * shift * shift
                (Slender, iEff / (s.d / 2.0 + shift))
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

    /// Segment length with full lateral restraint for an RHS / SHS, AS 4100 Cl. 5.3.2.4(b):
    /// l / ry <= (1800 + 1500 βm)(b / d)(250 / fy), taken with βm = -1 (the most conservative case, as in the ATM DCT).
    /// None for open sections.
    let fullLateralRestraintLength (s: Section) (grade: int) : float option =
        if Family.isHollow s.Family then
            let fyf, _ = Sections.fy s grade
            let ry = sqrt (s.Iy / s.Ag)
            Some(ry * (1800.0 - 1500.0) * (s.bf / s.d) * (250.0 / fyf))
        else
            None

    /// Shear capacity of an unstiffened web, AS 4100 Cl. 5.11.2, 5.11.3 and 5.11.5.1.
    /// Aw = d tw for hot-rolled sections; web slenderness uses the clear depth dp = d - 2tf.
    let shear (s: Section) (grade: int) : ShearCapacity =
        let _, fyw = Sections.fy s grade
        let aw =
            match s.Family with
            | WB -> (s.d - 2.0 * s.tf) * s.tw // welded: web depth only
            | RHS
            | SHS -> 2.0 * (s.d - 2.0 * s.tf) * s.tw // two webs, clear depth
            | _ -> s.d * s.tw
        let dp = s.d - 2.0 * s.tf
        let lam = dp / s.tw * sqrt (fyw / 250.0)
        let vw = 0.6 * fyw * aw
        let av = if lam <= 82.0 then 1.0 else (82.0 / lam) ** 2.0
        let vu = av * vw
        // RHS / SHS webs carry a non-uniform shear stress (Cl. 5.11.3): Vv = 2 Vu / (0.9 + f*vm/f*va) <= Vu, with the
        // stress ratio from the elastic shear flow of the thin-walled (centreline) section, f*vm = V Q / (2 I t) at the
        // neutral axis and f*va = V / (2 dm t). Reproduces the ATM DCT φVv for every RHS / SHS within 0.6%.
        let ratio =
            if Family.isHollow s.Family then
                let t = s.tw
                let dm = s.d - t
                let bm = s.bf - t
                let i = t * dm ** 3.0 / 6.0 + bm * t * dm ** 2.0 / 2.0
                let q = bm * t * dm / 2.0 + t * dm ** 2.0 / 4.0
                q * dm / i
            else
                1.0
        let vv = if ratio > 1.0 then min vu (2.0 * vu / (0.9 + ratio)) else vu
        { Aw = aw
          Fyw = fyw
          Lambda = lam
          Vw = vw
          AlphaV = av
          StressRatio = ratio
          Vv = vv
          PhiVv = phi * vv }
