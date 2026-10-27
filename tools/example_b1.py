"""Independent Python calculation of the base-sheet example beam (B1) for cross-checking the add-in output."""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reference_check as R

L = 8300.0
sec = R.S['310UB40.4']
EI = 2e5 * sec['Ix'] * 1e6
sw = sec['Mass'] * 9.81 / 1000
G_udl = 5.65 * 0.10 + 5.65 * 0.15 + 0.05 + sw
loads = {'G': [('dist', G_udl, 0, L), ('point', 2750.0, 4150.0)], 'Q': [('dist', 1.4125, 0, L)],
         'Wd': [], 'Wu': [('dist', -9.718, 0, L)], 'Wid': [], 'Wiu': []}
combos = [('ULS1', 'Top', {'G': 1.35}), ('ULS2', 'Top', {'G': 1.2, 'Q': 1.5}),
          ('ULS4', 'Top', {'G': 1.2, 'Q': 0.4, 'Wd': 1, 'Wid': 1}), ('ULS5', 'Bottom', {'G': 0.9, 'Wu': 1, 'Wiu': 1})]
restr = {'Top': [(0, 'F'), (2767, 'L'), (5533, 'L'), (L, 'F')], 'Bottom': [(0, 'F'), (2767, 'F'), (5533, 'F'), (L, 'F')]}
kl = {'Top': 1.4, 'Bottom': 1.0}

def acts(f):
    out = []
    for c, fac in f.items():
        for ld in loads[c]:
            out.append((ld[0], ld[1] * fac) + ld[2:])
    return out

def M(a, x): return R.moment(a, x, L)

def kt(t1, t2, l):
    d1 = sec['d'] - 2 * sec['tf']; term = d1 / l * (sec['tf'] / (2 * sec['tw'])) ** 3
    p = ''.join(sorted(t1 + t2, key='FPLU'.index))
    return {'FF': 1, 'FL': 1, 'LL': 1, 'FU': 1, 'FP': 1 + term, 'PL': 1 + term, 'PU': 1 + term, 'PP': 1 + 2 * term}[p]

def mmax(a, x1, x2, n=4000):
    best = max((abs(M(a, x1 + (x2 - x1) * i / n)), x1 + (x2 - x1) * i / n) for i in range(n + 1))
    return best

if __name__ == '__main__':
    print('Self-weight', round(sw, 4), 'G UDL', round(G_udl, 4))
    for nm, f in [('dg', {'G': 1}), ('dl', {'G': 1, 'Q': .4}), ('ds', {'G': 1, 'Q': .7}), ('dq', {'Q': 1})]:
        a = acts(f); d = max(R.defl(a, L * i / 2000, L, EI) for i in range(2001)); print(nm, round(d, 3))
    print('d1kN', round(1000 * L ** 3 / (48 * EI), 3))
    for cid, fl, f in combos:
        a = acts(f); pts = restr[fl]
        for (x1, t1), (x2, t2) in zip(pts, pts[1:]):
            l = x2 - x1; q = lambda k: M(a, x1 + l * k / 4)
            m2, m3, m4 = q(1), q(2), q(3); mm, xm = mmax(a, x1, x2)
            am = min(2.5, 1.7 * mm / math.sqrt(m2 * m2 + m3 * m3 + m4 * m4)) if mm else 1
            le = kt(t1, t2, l) * kl[fl] * 1.0 * l
            pmb, pms, mo, a_s = R.phiMb(sec, 300, le, am)
            print(f'{cid} {fl:6s} {x1/1000:.2f}-{x2/1000:.2f} {t1}{t2} Le={le/1000:.3f} am={am:.3f} as={a_s:.3f} phiMb={pmb:.1f} M*={mm/1e6:.1f} util={mm/1e6/pmb:.1%}')
        V = max(abs(v) for v in [R.moment(a, 0, L)] ) if False else None
    pv, lam = R.phiVv(sec, 300)
    for cid, fl, f in combos:
        a = acts(f)
        ra = sum((ld[1] * (ld[3] - ld[2]) * (L - (ld[2] + ld[3]) / 2) / L) if ld[0] == 'dist' else ld[1] * (L - ld[2]) / L for ld in a)
        rb = sum((ld[1] * (ld[3] - ld[2]) * ((ld[2] + ld[3]) / 2) / L) if ld[0] == 'dist' else ld[1] * ld[2] / L for ld in a)
        print(f'{cid} V*={max(abs(ra), abs(rb))/1000:.1f} kN  phiVv={pv:.1f}')
