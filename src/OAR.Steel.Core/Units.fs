namespace OAR.Steel

/// Internal unit system: N, mm, MPa (N/mm2).
/// Handy identities: 1 kN/m = 1 N/mm, 1 kN = 1000 N, 1 kN.m = 1e6 N.mm.
module Units =

    /// Steel shear modulus, AS 4100 Cl. 1.4 (MPa)
    [<Literal>]
    let G_MPa = 80000.0

    /// Default elastic modulus (MPa)
    [<Literal>]
    let E_MPa = 200000.0

    /// Gravity used to turn mass (kg/m) into self-weight (N/mm = kN/m)
    [<Literal>]
    let gravity = 9.81

    let kN (v: float) = v * 1000.0          // kN -> N
    let toKN (v: float) = v / 1000.0        // N -> kN
    let kNm (v: float) = v * 1.0e6          // kN.m -> N.mm
    let toKNm (v: float) = v / 1.0e6        // N.mm -> kN.m

    /// Self-weight line load (N/mm) from mass in kg/m
    let selfWeight (massKgPerM: float) = massKgPerM * gravity / 1000.0
