"""Independent Python reference for OAR.Steel (not used by the add-in).
Re-implements the section capacity, member capacity and beam deflection formulas so the F# results
can be cross-checked by a second implementation. Run:  python tools/reference_check.py
"""
import csv, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
CSV = os.path.join(HERE, '..', 'src', 'OAR.Steel.Core', 'Data', 'sections.csv')

def _load():
    rows = [l for l in open(CSV, encoding='utf-8') if l.strip() and not l.startswith('#')]
    out = {}
    for r in csv.DictReader(rows):
        f = lambda k: float(r[k]) if r[k] else None
        out[r['Designation']] = dict(Designation=r['Designation'], Family=r['Family'], Mass=f('Mass'), d=f('d'), bf=f('bf'),
            tf=f('tf'), tw=f('tw'), Ag=f('Ag'), Ix=f('Ix_e6'), Zx=f('Zx_e3'), Sx=f('Sx_e3'), Iy=f('Iy_e6'), J=f('J_e3'),
            Iw=f('Iw_e9'), phiMsx300_tab=f('phiMsx300_tab'),
            **{k: f(k) for k in ['fyf250','fyw250','fyf300','fyw300','fyf350','fyw350','fyf400','fyw400']})
    return out
S = _load()
E, G = 200000.0, 80000.0

def fy(sec, grade):
    return sec[f'fyf{grade}'], sec[f'fyw{grade}']

def Ze_x(sec, grade):
    fyf, fyw = fy(sec, grade)
    fam = sec['Family']
    resid = 'HW' if fam == 'WB' else 'HR'
    lep_f, ley_f = {'HR': (9, 16), 'HW': (8, 14)}[resid]
    lep_w, ley_w = 82, 115
    bf, tf, tw, d = sec['bf'], sec['tf'], sec['tw'], sec['d']
    b_out = (bf - tw) / 2 if fam != 'PFC' else (bf - tw)
    lf = b_out / tf * math.sqrt(fyf / 250)
    d1 = d - 2 * tf
    lw = d1 / tw * math.sqrt(fyw / 250)
    rf, rw = lf / ley_f, lw / ley_w
    if rf >= rw: ls, lsp, lsy, elem = lf, lep_f, ley_f, 'flange'
    else: ls, lsp, lsy, elem = lw, lep_w, ley_w, 'web'
    Z, Sx = sec['Zx'] * 1e3, sec['Sx'] * 1e3
    Zc = min(Sx, 1.5 * Z)
    if ls <= lsp: Ze = Zc; cls = 'C'
    elif ls <= lsy: Ze = Z + (lsy - ls) / (lsy - lsp) * (Zc - Z); cls = 'N'
    else:
        Ze = Z * (lsy / ls) if elem == 'flange' else Z * (lsy / ls) ** 2; cls = 'S'
    return Ze, cls, lf, lw

def Iw_calc(sec):
    return sec['Iy'] * 1e6 * (sec['d'] - sec['tf']) ** 2 / 4

def phiMb(sec, grade, Le, am=1.0):
    fyf, _ = fy(sec, grade)
    Ze, *_ = Ze_x(sec, grade)
    Ms = fyf * Ze
    Iy, J, Iw = sec['Iy'] * 1e6, sec['J'] * 1e3, (sec['Iw'] or Iw_calc(sec) / 1e9) * 1e9
    Mo = math.sqrt(math.pi**2 * E * Iy / Le**2 * (G * J + math.pi**2 * E * Iw / Le**2))
    a_s = 0.6 * (math.sqrt((Ms / Mo) ** 2 + 3) - Ms / Mo)
    return 0.9 * min(am * a_s * Ms, Ms) / 1e6, 0.9 * Ms / 1e6, Mo / 1e6, a_s

def phiVv(sec, grade):
    fyw = fy(sec, grade)[1]
    d, tw = sec['d'], sec['tw']
    dp = d - 2 * sec['tf']   # clear web depth for slenderness
    Aw = (d - 2 * sec['tf']) * tw if sec['Family'] == 'WB' else d * tw  # 5.11.2: web depth only for welded
    Vw = 0.6 * fyw * Aw
    lam = dp / tw * math.sqrt(fyw / 250)
    if lam > 82: Vw = Vw * (82 / lam) ** 2  # Vb approx (alpha_v) for unstiffened web
    return 0.9 * Vw / 1e3, lam


# ---- beam analysis (superposition, Gauss-integrated partial UDL) ----
# loads: ('dist', w N/mm, a, b) or ('point', P N, x) ; downward positive
GX = [(-math.sqrt(3/5), 5/9), (0.0, 8/9), (math.sqrt(3/5), 5/9)]
def defl_point(P, xp, x, L, EI):
    a, b = xp, L - xp
    if x <= a: return P * b * x * (L*L - b*b - x*x) / (6*L*EI)
    xr = L - x
    return P * a * xr * (L*L - a*a - xr*xr) / (6*L*EI)
def integ(f, lo, hi):
    if hi <= lo: return 0.0
    m, h = (lo+hi)/2, (hi-lo)/2
    return sum(wt * f(m + h*t) for t, wt in GX) * h
def defl(loads, x, L, EI):
    d = 0.0
    for ld in loads:
        if ld[0] == 'point': d += defl_point(ld[1], ld[2], x, L, EI)
        else:
            w, a, b = ld[1:]
            f = lambda s: defl_point(w, s, x, L, EI)
            d += integ(f, a, min(b, x)) + integ(f, max(a, x), b)
    return d
def moment(loads, x, L):
    RA = 0.0; m = 0.0
    for ld in loads:
        if ld[0] == 'point':
            P, xp = ld[1:]; RA += P*(L-xp)/L
            if x > xp: m -= P*(x-xp)
        else:
            w, a, b = ld[1:]; W = w*(b-a); c = (a+b)/2; RA += W*(L-c)/L
            e = min(x, b)
            if e > a: m -= w*(e-a)*(x-(a+e)/2)
    return RA*x + m
# his VBA PUDL deflection for comparison
def vba_pudl(w,E,I,a,b,c,x):
    L=a+b+c
    if x<=a: return w/(24*E*I)*b*(b+2*c)*x/L*(-2*x**2+2*a*(2*L-a)+b*(b+2*c))
    elif x<=a+b: return w/(24*E*I)*((x-a)**4 + b*(b+2*c)*x/L*(-2*x**2+2*a*(2*L-a)+b*(b+2*c)))
    else:
        xn=L-x; an,bn,cn=c,b,a
        return w/(24*E*I)*bn*(bn+2*cn)*xn/L*(-2*xn**2+2*an*(2*L-an)+bn*(bn+2*cn))

if __name__ == '__main__':
    print('--- Ze check vs tabulated phiMsx Gr300 ---')
    worst = 0
    for n, s in S.items():
        if s['phiMsx300_tab'] and s['fyf300'] and s['Sx']:
            Ze, cls, lf, lw = Ze_x(s, 300)
            calc = 0.9 * s['fyf300'] * Ze / 1e6
            err = calc / s['phiMsx300_tab'] - 1
            worst = max(worst, abs(err))
            if abs(err) > 0.005: print(f'{n:10s} tab {s["phiMsx300_tab"]:7.1f} calc {calc:7.1f} {err:+.2%} {cls} lf={lf:.1f} lw={lw:.1f}')
    print('worst', f'{worst:.2%}')
    print('--- Iw formula vs tabulated ---')
    w = 0
    for n, s in S.items():
        if s['Iw'] and s['Family'] in ('UB', 'UC'):
            e = Iw_calc(s) / 1e9 / s['Iw'] - 1; w = max(w, abs(e))
            if abs(e) > 0.01: print(n, s['Iw'], round(Iw_calc(s) / 1e9, 1), f'{e:+.2%}')
    print('worst Iw', f'{w:.2%}')
    print('--- DCT benchmarks ---')
    print('Ex1 200UB29.8 Le=4.0', [round(v, 2) for v in phiMb(S['200UB29.8'], 300, 4000)], 'phiVv', phiVv(S['200UB29.8'], 300))
    print('Ex2 310UB46.2 Le=12', [round(v, 2) for v in phiMb(S['310UB46.2'], 300, 12000)], 'phiVv', phiVv(S['310UB46.2'], 300))
    print('Blockpad 310UB40.4 Le=11.62', [round(v, 3) for v in phiMb(S['310UB40.4'], 300, 11620)])
    print('Blockpad 310UB40.4 Le=9.24', [round(v, 3) for v in phiMb(S['310UB40.4'], 300, 9240)])
