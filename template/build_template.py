# Builds the OAR steel beam calc sheet template (openpyxl).
# Usage: python template/build_template.py "template/Steel Beam Design - Base.xlsx" template/assets/oar-logo.png

from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Border, Side, Alignment
from openpyxl.drawing.image import Image as XLImage
from openpyxl.worksheet.table import Table, TableStyleInfo
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.comments import Comment
from openpyxl.worksheet.pagebreak import Break
from openpyxl.worksheet.formula import ArrayFormula
from openpyxl.formatting.rule import Rule
from openpyxl.styles.differential import DifferentialStyle
from openpyxl.styles.numbers import NumberFormat
from PIL import Image
import os
import sys

OUT = sys.argv[1]
LOGO = sys.argv[2]

GREEN = "465A52"; TAUPE = "B8ADA5"; GREY = "595959"; DARK = "262626"
F_IN = PatternFill("solid", fgColor="F3EEE9")    # input (warm taupe tint)
F_OUT = PatternFill("solid", fgColor="E6ECE9")   # calculated (green tint)
F_HDR = PatternFill("solid", fgColor="DCD5CF")   # table header
F_BAR = PatternFill("solid", fgColor=GREEN)
F_IMG = PatternFill("solid", fgColor="FAF8F6")

def font(sz=9, b=False, i=False, color=DARK, u=None):
    return Font(name="Arial", size=sz, bold=b, italic=i, color=color, underline=u)

hair = Side(style="hair", color="8C8C8C")
thin = Side(style="thin", color="8C8C8C")
gline = Side(style="medium", color=GREEN)
B_IN = Border(left=hair, right=hair, top=hair, bottom=hair)
B_BOX = Border(left=thin, right=thin, top=thin, bottom=thin)

wb = Workbook()
ws = wb.active
ws.title = "B1"
ws.sheet_view.showGridLines = False
ws.sheet_properties.tabColor = GREEN

widths = {"A": 1.5, "B": 8, "C": 9, "D": 30, "E": 10, "F": 10, "G": 11, "H": 10, "I": 10, "J": 11, "K": 1.5}
for k, v in widths.items():
    ws.column_dimensions[k].width = v
for r in range(1, 140):
    ws.row_dimensions[r].height = 14

# default font for all used cells is applied per cell below
def put(cell, val=None, f=None, fill=None, border=None, al=None, nf=None):
    c = ws[cell]
    if val is not None:
        c.value = val
    c.font = f or font()
    if fill: c.fill = fill
    if border: c.border = border
    if al: c.alignment = al
    if nf: c.number_format = nf
    return c

L = Alignment(horizontal="left", vertical="center", indent=1)
C = Alignment(horizontal="center", vertical="center")
R = Alignment(horizontal="right", vertical="center")

def inp(cell, val=None, nf=None, al=C):
    return put(cell, val, font(9, color="000000"), F_IN, B_IN, al, nf)

def out(cell, val=None, nf=None, al=C):
    return put(cell, val, font(9), F_OUT, B_IN, al, nf)

def merge_inp(rng, val=None, al=L, nf=None):
    first = rng.split(":")[0]
    ws.merge_cells(rng)
    for row in ws[rng]:
        for c in row:
            c.fill = F_IN; c.border = B_IN
    return inp(first, val, nf, al)

def label(cell, text, sz=9, color=DARK, al=L, b=False, i=False):
    return put(cell, text, font(sz, b=b, i=i, color=color), al=al)

def heading(row, text):
    ws.row_dimensions[row].height = 20
    put(f"B{row}", text, font(10, b=True, color=GREEN), al=Alignment(vertical="bottom"))
    for col in "BCDEFGHIJ":
        ws[f"{col}{row}"].border = Border(bottom=Side(style="thin", color=TAUPE))

names = {}
def name(nm, ref):
    names[nm] = ref

# ---------------- Letterhead block ----------------
for r in (1, 2, 3):
    ws.row_dimensions[r].height = 17
im = Image.open(LOGO)
im.thumbnail((1200, 400))
im.save(LOGO + ".small.png")
logo = XLImage(LOGO + ".small.png")
h = 40
logo.height = h
logo.width = int(h * im.width / im.height)
ws.add_image(logo, "B1")

small = font(7, color=GREY)
label("F1", "JOB No.", 7, GREY, R); inp("G1", "26-000")
label("H1", "DATE", 7, GREY, R); ws.merge_cells("I1:J1"); import datetime; inp("I1", datetime.date.today(), "dd-mmm-yy")
ws["J1"].fill = F_IN; ws["J1"].border = B_IN
label("F2", "PROJECT", 7, GREY, R); merge_inp("G2:J2", "Project name / address")
label("F3", "BEAM", 7, GREY, R); inp("G3", "B1", al=C)
label("H3", "LOCATION", 7, GREY, R); merge_inp("I3:J3", "Roof – grid A/1-3")
label("F4", "BY", 7, GREY, R); inp("G4", "BO")
label("H4", "CHECKED", 7, GREY, R); ws.merge_cells("I4:J4"); inp("I4", "")
ws["J4"].fill = F_IN; ws["J4"].border = B_IN
ws.row_dimensions[4].height = 17
label("B4", "OAR Consulting Engineers Pty Ltd  ·  22 Martin Street, Nerang QLD 4211  ·  (07) 5527 2288", 7, GREY)
for col in "BCDEFGHIJ":
    ws[f"{col}5"].border = Border(top=gline)
ws.row_dimensions[5].height = 6
name("BeamRef", "$G$3"); name("BeamLocation", "$I$3")
name("JobNo", "$G$1"); name("CalcDate", "$I$1")

# Title bar
ws.row_dimensions[6].height = 22
ws.merge_cells("B6:J6")
put("B6", '="STEEL BEAM DESIGN  –  "&IF(BeamRef="","(beam ref)",BeamRef)&IF(BeamLocation="","","  ·  "&BeamLocation)',
    font(11, b=True, color="FFFFFF"), F_BAR, al=Alignment(horizontal="left", vertical="center", indent=1))
for col in "CDEFGHIJ":
    ws[f"{col}6"].fill = F_BAR

# Legend
label("G7", "Legend:", 7, GREY, R)
inp("H7", "Input").font = font(7, color="000000")
out("I7", "Calculated").font = font(7)

# ---------------- Design summary ----------------
from openpyxl.formatting.rule import DataBarRule, FormulaRule, CellIsRule
s0 = 8
heading(s0, "Design Summary")
# compact panel: labels row, values row, sub-line row
ws.row_dimensions[s0+2].height = 20
label(f"B{s0+1}", "Adopted section", 7, GREY)
ws.merge_cells(f"B{s0+2}:D{s0+2}")
put(f"B{s0+2}", '=IF(SectionSize="","–",SectionSize&"  Gr "&Grade)',
    font(12, b=True, color=GREEN), al=L)
put(f"B{s0+3}", '="Span "&TEXT(Span,"#,##0")&" mm  ·  "&Support&IF(IncludeSW="Yes","  ·  incl. self-weight","")',
    font(7, color=GREY), al=L)
label(f"F{s0+1}", "Max utilisation", 7, GREY, al=L)
ws.merge_cells(f"F{s0+2}:G{s0+2}")
label(f"I{s0+1}", "Status", 7, GREY, al=C); ws.merge_cells(f"I{s0+1}:J{s0+1}")
ws.merge_cells(f"I{s0+2}:J{s0+2}")
sum_hdr = s0 + 5
ws.row_dimensions[sum_hdr].height = 16
for col, txt in zip("BCDEFGHIJ", ["Check", "", "Basis", "Actual", "Limit", "Unit", "L / x", "Utilisation", "Status"]):
    put(f"{col}{sum_hdr}", txt, font(8, b=True), F_HDR, None, C)
ws.merge_cells(f"B{sum_hdr}:C{sum_hdr}")
sum_rows = [
    ("grp", "Serviceability"),
    ("dfl", "Δg", "Dead load", "Res_Dg", 0),
    ("dfl", "Δg+ψl·q", "Long term", "Res_Dl", 1),
    ("dfl", "Δg+ψs·q", "Short term", "Res_Ds", 2),
    ("dfl", "Δq", "Live load only", "Res_Dq", 3),
    ("dfl", "Δ1kN", "1 kN at midspan", "Res_D1", 4),
    ("grp", "Strength"),
    ("str", "M* / φMbx", "Bending – gravity (top flange critical)", "Res_Mdn", "Res_phiMdn", "kN·m"),
    ("str", "M* / φMbx", "Bending – uplift (bottom flange critical)", "Res_Mup", "Res_phiMup", "kN·m"),
    ("str", "V* / φVv", "Shear", "Res_V", "Res_phiV", "kN"),
]
util_cells = []; defl_rows = []
rr = sum_hdr
for row in sum_rows:
    rr += 1
    if row[0] == "grp":
        put(f"B{rr}", row[1], font(8, b=True, color=GREEN), al=Alignment(vertical="center"))
        for col in "BCDEFGHIJ":
            ws[f"{col}{rr}"].border = Border(bottom=Side(style="hair", color=TAUPE))
        continue
    ws.merge_cells(f"B{rr}:C{rr}")
    put(f"B{rr}", row[1], font(9), al=C)
    put(f"D{rr}", row[2], font(8, color=GREY), al=L)
    if row[0] == "dfl":
        out(f"E{rr}", None, "0.00"); name(row[3], f"$E${rr}")
        out(f"F{rr}", None, "0.00")          # limit filled after criteria table
        label(f"G{rr}", "mm", 8, GREY, al=C)
        out(f"H{rr}", f'=IF(N(E{rr})=0,"",Span/E{rr})', '"L/"0')
        defl_rows.append((rr, row[4]))
    else:
        out(f"E{rr}", None, "0.0"); name(row[3], f"$E${rr}")
        out(f"F{rr}", None, "0.0"); name(row[4], f"$F${rr}")
        label(f"G{rr}", row[5], 8, GREY, al=C)
        put(f"H{rr}", "", font(9))
    out(f"I{rr}", f'=IF(OR(E{rr}="",N(F{rr})=0),"",ABS(E{rr})/F{rr})', "0%")
    put(f"J{rr}", f'=IF(I{rr}="","–",IF(I{rr}<=1,"OK","NG"))', font(9, b=True), al=C)
    util_cells.append(f"I{rr}")
sum_last = rr
urng = f"I{sum_hdr+1}:I{sum_last}"
put(f"F{s0+2}", f'=IF(COUNT({urng})=0,"–",MAX({urng}))', font(12, b=True, color=GREEN),
    al=Alignment(horizontal="left", vertical="center", indent=1), nf="0%")
thin_t = Side(style="thin", color=TAUPE)
put(f"I{s0+2}", f'=IF(COUNT({urng})=0,"PENDING",IF(MAX({urng})<=1,"PASS","FAIL"))', font(10, b=True, color="8C8C8C"),
    al=C, border=Border(left=thin_t, top=thin_t, bottom=thin_t))
ws[f"J{s0+2}"].border = Border(right=thin_t, top=thin_t, bottom=thin_t)
put(f"F{s0+3}", f'=IF(COUNT({urng})=0,"","Governs: "&INDEX(D{sum_hdr+1}:D{sum_last},MATCH(MAX({urng}),{urng},0)))',
    font(7, color=GREY), al=L)
name("Sum_MaxUtil", f"$F${s0+2}"); name("Sum_Status", f"$I${s0+2}")
# conditional formats
ws.conditional_formatting.add(urng, DataBarRule(start_type="num", start_value=0, end_type="num", end_value=1,
                                                color="9DB3A9", showValue=True))
red_txt = DifferentialStyle(font=Font(color="C00000", bold=True))
ws.conditional_formatting.add(urng, Rule(type="expression", dxf=red_txt, formula=[f"AND(ISNUMBER(I{sum_hdr+1}),I{sum_hdr+1}>1)"]))
ws.conditional_formatting.add(f"J{sum_hdr+1}:J{sum_last}", Rule(type="cellIs", operator="equal", formula=['"NG"'],
    dxf=DifferentialStyle(font=Font(color="FFFFFF", bold=True), fill=PatternFill("solid", bgColor="C00000"))))
ws.conditional_formatting.add(f"J{sum_hdr+1}:J{sum_last}", Rule(type="cellIs", operator="equal", formula=['"OK"'],
    dxf=DifferentialStyle(font=Font(color=GREEN, bold=True))))
ws.conditional_formatting.add(f"I{s0+2}:J{s0+2}", Rule(type="expression", formula=[f'$I${s0+2}="PASS"'],
    dxf=DifferentialStyle(font=Font(color=GREEN, bold=True), fill=PatternFill("solid", bgColor="E6ECE9"))))
ws.conditional_formatting.add(f"I{s0+2}:J{s0+2}", Rule(type="expression", formula=[f'$I${s0+2}="FAIL"'],
    dxf=DifferentialStyle(font=Font(color="C00000", bold=True), fill=PatternFill("solid", bgColor="F6E1E1"))))
ws.conditional_formatting.add(f"F{s0+2}", Rule(type="expression", formula=[f'AND(ISNUMBER(F{s0+2}),F{s0+2}>1)'],
    dxf=DifferentialStyle(font=Font(color="C00000", bold=True))))

# Image frame - off to the right, outside the print area
IMG_COLS = "MNOPQRST"
ws.column_dimensions["L"].width = 4
for col in IMG_COLS:
    ws.column_dimensions[col].width = 11
img_top, img_bot = 8, 40
ws.row_dimensions[6].height = 22
put("M6", "Beam Location  (not printed)", font(10, b=True, color=GREEN), al=Alignment(vertical="bottom"))
for col in IMG_COLS:
    ws[f"{col}6"].border = Border(bottom=Side(style="thin", color=TAUPE))
ws.merge_cells(f"M{img_top}:T{img_bot}")
put(f"M{img_top}", "Paste beam location screenshot / plan extract here\n(right-click > Paste as Picture, then resize to fit this frame)",
    font(9, i=True, color="A6A6A6"), F_IMG, al=Alignment(horizontal="center", vertical="center", wrap_text=True))
dash = Side(style="dashed", color=TAUPE)
for rr in range(img_top, img_bot + 1):
    for col in IMG_COLS:
        c = ws[f"{col}{rr}"]
        c.fill = F_IMG
        c.border = Border(left=dash if col == IMG_COLS[0] else None, right=dash if col == IMG_COLS[-1] else None,
                          top=dash if rr == img_top else None, bottom=dash if rr == img_bot else None)
name("LocationImage", f"$M${img_top}:$T${img_bot}")

# ---------------- Geometry & section ----------------
r = sum_last + 2
heading(r, "Geometry and Section")
rows = [
    ("Section type", "SectionType", "UB", None, None),
    ("Section size", "SectionSize", "310UB40.4", None, None),
    ("Steel grade", "Grade", 300, "0", "MPa"),
    ("Span (centre to centre of supports)", "Span", 8300, "#,##0", "mm"),
    ("Support condition", "Support", "Simply supported", None, None),
    ("Elastic modulus", "E_mod", 200000, "#,##0", "MPa"),
    ("Include self-weight", "IncludeSW", "Yes", None, None),
]
for i, (lab, nm, val, nf, unit) in enumerate(rows):
    rr = r + 1 + i
    label(f"B{rr}", lab)
    inp(f"E{rr}", val, nf)
    if unit: label(f"F{rr}", unit, color=GREY)
    name(nm, f"$E${rr}")
dv_type = DataValidation(type="list", formula1='"UB,UC,WB,PFC"', allow_blank=False)
dv_yn = DataValidation(type="list", formula1='"Yes,No"')
dv_sup = DataValidation(type="list", formula1='"Simply supported"')
dv_grade = DataValidation(type="list", formula1='"250,300,350,400"')
for dv in (dv_type, dv_yn, dv_sup, dv_grade): ws.add_data_validation(dv)
dv_type.add(f"E{r+1}"); dv_grade.add(f"E{r+3}"); dv_sup.add(f"E{r+5}"); dv_yn.add(f"E{r+7}")
ws[f"E{r+5}"].alignment = L
ws.merge_cells(f"E{r+5}:F{r+5}")

SECTION_PROP_CELLS = []
props = [
    ("Mass", "Mass", "kg/m", "0.0"), ("Ix", "Ix", "×10⁶ mm⁴", "0.0"), ("Iy", "Iy", "×10⁶ mm⁴", "0.00"),
    ("J", "J", "×10³ mm⁴", "0.0"), ("Iw", "Iw", "×10⁹ mm⁶", "0.0"), ("Zex", "Zex", "×10³ mm³", "0"),
    ("φMsx", "phiMsx", "kN·m", "0.0"),
]
label(f"G{r+1}", "Section properties (add-in)", 8, GREY, i=True)
for i, (lab, nm, unit, nf) in enumerate(props):
    rr = r + 2 + i
    label(f"G{rr}", lab, al=R)
    out(f"H{rr}", None, nf)
    label(f"I{rr}", unit, color=GREY)
    name(nm, f"$H${rr}")
    SECTION_PROP_CELLS.append((f"H{rr}", {"Zex": "Ze", "phiMsx": "phiMsx"}.get(nm, nm)))

# ---------------- Serviceability criteria ----------------
r = r + 10
heading(r, "Serviceability Criteria")
hdr = r + 1
for col, txt in zip("BCDEFG", ["Case", "", "Description", "Span / ", "Max (mm)", "Factor"]):
    put(f"{col}{hdr}", txt, font(8, b=True), F_HDR, B_IN, C)
ws.merge_cells(f"B{hdr}:C{hdr}")
sls = [
    ("Δg", "Dead load", 500, 20, None),
    ("Δg+ψl·q", "Long term (ψl)", 360, 25, 0.4),
    ("Δg+ψs·q", "Short term (ψs)", 300, 30, 0.7),
    ("Δq", "Live load only", 250, 30, None),
    ("Δ1kN", "1 kN point load at midspan", 1000, 1.5, None),
]
for i, (case, desc, ratio, lim, psi) in enumerate(sls):
    rr = hdr + 1 + i
    ws.merge_cells(f"B{rr}:C{rr}")
    put(f"B{rr}", case, font(9), border=B_IN, al=C); ws[f"C{rr}"].border = B_IN
    put(f"D{rr}", desc, font(9), border=B_IN, al=L)
    inp(f"E{rr}", ratio, "0")
    inp(f"F{rr}", lim, "0.0")
    if psi is not None:
        inp(f"G{rr}", psi, "0.0")
    else:
        put(f"G{rr}", "–", font(9, color=GREY), border=B_IN, al=C)
name("DeflCriteria", f"$B${hdr+1}:$G${hdr+5}")
for srow, k in defl_rows:
    cr = hdr + 1 + k
    ws[f"F{srow}"] = f'=IF(N(E{cr})=0,"",IF(N(F{cr})=0,Span/E{cr},MIN(Span/E{cr},F{cr})))'
name("PsiL", f"$G${hdr+2}"); name("PsiS", f"$G${hdr+3}")
label(f"H{hdr+1}", "Limit = lesser of L/ratio and max", 7, GREY)
label(f"H{hdr+2}", "Blank max = ratio only", 7, GREY)

# ---------------- Loads ----------------
r = r + 8
ws.row_breaks.append(Break(id=r - 1))
heading(r, "Applied Loads")
label(f"B{r+1}", "Load = trib × pressure + direct.  UDL/PUDL: trib in m → kN/m.  Point: trib in m² → kN, start = position.  "
      "UDL: start/end blank = full span.", 7, GREY, i=True)
label(f"B{r+2}", "Magnitudes positive; direction set by case: Wd, Wid act down; Wu, Wiu act up.  "
      "Wi = internal pressure, combined with the external wind case.", 7, GREY, i=True)
th = r + 3
cols = ["Case", "Type", "Description", "Trib (m | m²)", "Press. (kPa)", "+ Direct (kN/m | kN)", "Start (mm)", "End (mm)", "Load"]
for col, txt in zip("BCDEFGHIJ", cols):
    put(f"{col}{th}", txt, font(8, b=True), al=Alignment(horizontal="center", vertical="center", wrap_text=True))
ws.row_dimensions[th].height = 26
loads = [
    ("G", "UDL", "Roof sheeting + purlins", 5.65, "LIB", None, None, None),
    ("G", "UDL", "Ceiling + services", 5.65, "LIB", None, None, None),
    ("G", "UDL", "Beam bracket / misc.", None, None, 0.05, None, None),
    ("Q", "UDL", "Roof – non-trafficable", 5.65, "LIB", None, None, None),
    ("Wu", "UDL", "External wind – net uplift", 5.65, 1.72, None, None, None),
    ("Wd", "UDL", "External wind – downward", 5.65, 0.0, None, None, None),
    ("Wiu", "UDL", "Internal pressure (+cpi)", 5.65, 0.0, None, None, None),
    ("Wid", "UDL", "Internal suction (−cpi)", 5.65, 0.0, None, None, None),
    ("G", "Point", "Mech. unit on platform (example)", 1.5, 0.5, 2.0, 4150, None),
    (None, None, None, None, None, None, None, None),
    (None, None, None, None, None, None, None, None),
]
first = th + 1
for i, rowv in enumerate(loads):
    rr = first + i
    for col, v, nf in zip("BCDEFGHI", rowv, [None, None, None, "0.00", "0.00", "0.00", "#,##0", "#,##0"]):
        if v == "LIB":
            v = f'=IFERROR(INDEX(LoadLib_B1[kPa],MATCH(D{rr},LoadLib_B1[Description],0)),"")'
        inp(f"{col}{rr}", v, nf, L if col == "D" else C)
    out(f"J{rr}", f'=IF(B{rr}="","",N(E{rr})*N(F{rr})+N(G{rr}))', '0.000 "kN/m"')
last = first + len(loads) - 1
# unit-aware number formats: Point rows show m² / kN
from openpyxl.formatting.rule import Rule
from openpyxl.styles.differential import DifferentialStyle
from openpyxl.styles.numbers import NumberFormat
def nf_rule(fmt, fid):
    return Rule(type="expression", dxf=DifferentialStyle(numFmt=NumberFormat(numFmtId=fid, formatCode=fmt)),
                formula=[f'$C{first}="Point"'])
ws.conditional_formatting.add(f"J{first}:J{last}", nf_rule('0.000 "kN"', 190))
ws.conditional_formatting.add(f"E{first}:E{last}", nf_rule('0.00 "m²"', 191))
for rr in range(first, last + 1):
    ws[f"E{rr}"].number_format = '0.00 "m"'
    ws[f"G{rr}"].number_format = '0.00'
tbl = Table(displayName="Loads_B1", ref=f"B{th}:J{last}")
tbl.tableStyleInfo = TableStyleInfo(name="TableStyleLight15", showRowStripes=False)
from openpyxl.worksheet.table import TableColumn
tbl.tableColumns = [TableColumn(id=i + 1, name=str(ws[f"{col}{th}"].value)) for i, col in enumerate("BCDEFGHIJ")]
# Load column as a table calculated column, so Excel fills it on new or cleared rows
from openpyxl.worksheet.table import TableFormula
TR = "Loads_B1[[#This Row],[{}]]"
load_f = (f'IF({TR.format("Case")}="","",N({TR.format("Trib (m | m²)")})*N({TR.format("Press. (kPa)")})'
          f'+N({TR.format("+ Direct (kN/m | kN)")}))')
press_f = f'IFERROR(INDEX(LoadLib_B1[kPa],MATCH({TR.format("Description")},LoadLib_B1[Description],0)),"")'
for tc in tbl.tableColumns:
    if tc.name == "Load":
        tc.calculatedColumnFormula = TableFormula(attr_text=load_f)
    if tc.name == "Press. (kPa)":
        tc.calculatedColumnFormula = TableFormula(attr_text=press_f)
for rr in range(first, last + 1):
    v = ws[f"F{rr}"].value
    if v is None or (isinstance(v, str) and v.startswith("=IFERROR(INDEX(LoadLib_B1")):
        ws[f"F{rr}"].value = "=" + press_f
for rr in range(first, last + 1):
    ws[f"J{rr}"].value = "=" + load_f
ws.add_table(tbl)
for col in "BCDEFGHIJ":
    ws[f"{col}{th}"].fill = F_HDR
dv_case = DataValidation(type="list", formula1='"G,Q,Wd,Wu,Wid,Wiu"', allow_blank=True)
dv_lt = DataValidation(type="list", formula1='"UDL,PUDL,Point"', allow_blank=True)
ws.add_data_validation(dv_case); ws.add_data_validation(dv_lt)
dv_case.add(f"B{first}:B{last}"); dv_lt.add(f"C{first}:C{last}")

sr = last + 2
label(f"B{sr+1}", "Full-span UDL totals (excl. self-weight):", 8, GREY, i=True)
for col, case in zip("EFGHIJ", ["G", "Q", "Wd", "Wu", "Wid", "Wiu"]):
    put(f"{col}{sr}", case, font(8, b=True), F_HDR, B_IN, C)
    out(f"{col}{sr+1}", f'=SUMIFS(Loads_B1[Load],Loads_B1[Case],"{case}",Loads_B1[Type],"UDL")', '0.000 "kN/m"')
    name(f"w_{case}", f"${col}${sr+1}")

HX = 26  # column Z: start of the add-in helper area
from openpyxl.utils import get_column_letter as gcl
hc = lambda i: gcl(HX + i)
# ---------------- Load library (right-hand side, not printed) ----------------
LL = [hc(16), hc(17), hc(18), hc(19)]  # AP..AS
for col, w in zip(LL, [7, 38, 8, 46]):
    ws.column_dimensions[col].width = w
put(f"{LL[0]}6", "Load Library  (not printed)", font(10, b=True, color=GREEN), al=Alignment(vertical="bottom"))
label(f"{LL[0]}7", "Typical starting values only – verify for each job. Picking a Description in the loads table fills Press. (kPa); type over it to change. "
      "Keep rows grouped by case (insert new rows inside the case's block).", 7, GREY, i=True)
kg = lambda m: round(m * 9.81 / 1000, 3)
load_lib = [
    ("G", "Roof sheeting + purlins", 0.10, "allowance – check sheeting and purlin weights"),
    ("G", "Light sheet roof (20 kg/m²)", kg(20), "20 kg/m² x 9.81"),
    ("G", "Sheet roof (40 kg/m²)", kg(40), "40 kg/m² x 9.81"),
    ("G", "Tiled roof (90 kg/m²)", kg(90), "90 kg/m² x 9.81"),
    ("G", "Ceiling + services", 0.15, "allowance"),
    ("G", "Services allowance", 0.25, "allowance"),
    ("G", "Typical floor (60 kg/m²)", kg(60), "60 kg/m² x 9.81"),
    ("G", "RC slab – per 100 mm (25 kN/m³)", 2.50, "scale by thickness / 100"),
    ("G", "Partitions allowance", 1.00, "allowance – check AS/NZS 1170.1"),
    ("Q", "Roof – non-trafficable", 0.25, "AS/NZS 1170.1 Cl. 3.5.1: 0.12 + 1.2/A ≥ 0.25 kPa"),
    ("Q", "Residential – habitable areas", 1.50, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Residential – balconies", 2.00, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Offices – general use", 3.00, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Retail", 4.00, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Assembly / crowds", 5.00, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Car park – light vehicles", 2.50, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Plant room – light", 5.00, "AS/NZS 1170.1 Table 3.1 – verify category"),
    ("Q", "Storage – per metre of height", 2.40, "multiply by storage height"),
    ("Wd", "External wind – downward", None, "AS/NZS 1170.2 – enter pressure"),
    ("Wu", "External wind – net uplift", None, "AS/NZS 1170.2 – enter pressure"),
    ("Wid", "Internal suction (−cpi)", None, "AS/NZS 1170.2 – enter pressure"),
    ("Wiu", "Internal pressure (+cpi)", None, "AS/NZS 1170.2 – enter pressure"),
]
llh = 9
for col, txt in zip(LL, ["Case", "Description", "kPa", "Source / note"]):
    put(f"{col}{llh}", txt, font(8, b=True), F_HDR, None, C)
for i, (cs, d, kpa, note) in enumerate(load_lib):
    rr = llh + 1 + i
    inp(f"{LL[0]}{rr}", cs); inp(f"{LL[1]}{rr}", d, al=L); inp(f"{LL[2]}{rr}", kpa, "0.00"); inp(f"{LL[3]}{rr}", note, al=L)
    ws[f"{LL[3]}{rr}"].font = font(8, color=GREY)
ll_last = llh + len(load_lib)
tll = Table(displayName="LoadLib_B1", ref=f"{LL[0]}{llh}:{LL[3]}{ll_last}")
tll.tableStyleInfo = TableStyleInfo(name="TableStyleLight15", showRowStripes=False)
ws.add_table(tll)
# Description dropdown in the loads table, filtered by the row's case (free text still allowed)
ll_case = f"${LL[0]}${llh+1}:${LL[0]}${ll_last+40}"
dv_desc = DataValidation(type="list",
    formula1=f"OFFSET(${LL[1]}${llh+1},MATCH($B{first},{ll_case},0)-1,0,COUNTIF({ll_case},$B{first}),1)",
    showErrorMessage=False, allow_blank=True)
ws.add_data_validation(dv_desc)
dv_desc.add(f"D{first}:D{last}")

# ---------------- Load combination library (right-hand side, not printed) ----------------
LIB_COLS = "MNOPQRSTUVWX"
for col, w in zip(LIB_COLS, [8, 7, 9, 7, 9, 26, 6.5, 6.5, 6.5, 6.5, 6.5, 6.5]):
    ws.column_dimensions[col].width = w
lr = 43
ws.row_dimensions[lr].height = 20
put(f"M{lr}", "Load Combination Library  (not printed)", font(10, b=True, color=GREEN), al=Alignment(vertical="bottom"))
for col in LIB_COLS:
    ws[f"{col}{lr}"].border = Border(bottom=Side(style="thin", color=TAUPE))
label(f"M{lr+1}", "Include: blank = follow the Combination set;  Yes / No = force on or off.  Active rows appear in the calc.", 7, GREY, i=True)
label(f"M{lr+2}", "Groups: Gravity (Floor, Roof, All), Wind (Roof, All), Storage (All only), Custom (always, unless Include = No).  Add rows at the bottom for custom combinations.", 7, GREY, i=True)
lh = lr + 3
lcols = ["Include", "Active", "Group", "ID", "Crit. flange", "Combination", "G", "Q", "Wd", "Wu", "Wid", "Wiu"]
for col, txt in zip(LIB_COLS, lcols):
    put(f"{col}{lh}", txt, font(8, b=True), F_HDR, None, C)
lib = [
    ("Gravity", "ULS1", "Top", "1.35G", [1.35, 0, 0, 0, 0, 0]),
    ("Gravity", "ULS2", "Top", "1.2G + 1.5Q", [1.2, 1.5, 0, 0, 0, 0]),
    ("Storage", "ULS3", "Top", "1.2G + 1.5ψlQ", [1.2, "=1.5*PsiL", 0, 0, 0, 0]),
    ("Wind", "ULS4", "Top", "1.2G + ψcQ + Wd + Wi(−cpi)", [1.2, "=PsiC", 1.0, 0, 1.0, 0]),
    ("Wind", "ULS5", "Bottom", "0.9G + Wu + Wi(+cpi)", [0.9, 0, 0, 1.0, 0, 1.0]),
    ("Custom", None, None, None, [None] * 6),
    ("Custom", None, None, None, [None] * 6),
    ("Custom", None, None, None, [None] * 6),
]
lfirst = lh + 1
for i, (grp, cid, fl, desc, fac) in enumerate(lib):
    rr = lfirst + i
    inp(f"M{rr}", None)
    out(f"N{rr}", f'=IF(M{rr}="No",0,IF(P{rr}="",0,IF(M{rr}="Yes",1,IF(OR(CombSet="All",O{rr}="Custom",'
                  f'AND(O{rr}="Gravity",CombSet<>"Custom"),AND(O{rr}="Wind",CombSet="Roof")),1,0))))', '"✓";;')
    inp(f"O{rr}", grp); inp(f"P{rr}", cid); inp(f"Q{rr}", fl); inp(f"R{rr}", desc, al=L)
    for col, v in zip("STUVWX", fac):
        inp(f"{col}{rr}", v, "0.00")
llast = lfirst + len(lib) - 1
tl = Table(displayName="CombLib_B1", ref=f"M{lh}:X{llast}")
tl.tableStyleInfo = TableStyleInfo(name="TableStyleLight15", showRowStripes=False)
ws.add_table(tl)
dv_inc = DataValidation(type="list", formula1='"Yes,No"', allow_blank=True)
dv_grp = DataValidation(type="list", formula1='"Gravity,Wind,Storage,Custom"')
dv_fl = DataValidation(type="list", formula1='"Top,Bottom"')
for dv in (dv_inc, dv_grp, dv_fl): ws.add_data_validation(dv)
dv_inc.add(f"M{lfirst}:M{llast+30}"); dv_grp.add(f"O{lfirst}:O{llast+30}"); dv_fl.add(f"Q{lfirst}:Q{llast+30}")
# count of active uplift (bottom flange) combinations, used to grey out the uplift summary row
label(f"R{llast+2}", "Active uplift (bottom flange) combinations:", 7, GREY, al=R)
out(f"S{llast+2}", '=COUNTIFS(CombLib_B1[Active],1,CombLib_B1[Crit. flange],"Bottom")', "0")
name("NumUpliftCombos", f"$S${llast+2}")

# ---------------- Load combinations (printed: active rows only) ----------------
r = sr + 3
heading(r, "Load Combinations (AS/NZS 1170.0)")
label(f"B{r+1}", "Combination set", 9); inp(f"E{r+1}", "Roof"); name("CombSet", f"$E${r+1}")
dv_set = DataValidation(type="list", formula1='"Floor,Roof,All,Custom"')
ws.add_data_validation(dv_set); dv_set.add(f"E{r+1}")
label(f"F{r+1}", "ψc", 9, al=R); inp(f"G{r+1}", 0.4, "0.0"); name("PsiC", f"$G${r+1}")
label(f"H{r+1}", "Table 4.1 (roofs 0.0)", 7, GREY)
label(f"B{r+2}", "Floor = gravity only.  Roof = gravity + wind.  All = every library row.  Custom = only rows set to Yes / Custom.  Edit in the library to the right →",
      7, GREY, i=True)
ch = r + 3
for col, txt in zip("BCDEFGHIJ", ["ID", "Crit. flange", "Combination", "G", "Q", "Wd", "Wu", "Wid", "Wiu"]):
    put(f"{col}{ch}", txt, font(8, b=True), F_HDR, None, C)
COMBO_ANCHOR = f"B{ch+1}"
ws[COMBO_ANCHOR] = ArrayFormula(COMBO_ANCHOR, '=_xlfn._xlws.FILTER(CombLib_B1[[ID]:[Wiu]],CombLib_B1[Active]=1,"No combinations selected")')
spill_rows = len(lib)
for rr in range(ch + 1, ch + 1 + spill_rows):
    for col in "BCDEFGHIJ":
        c = ws[f"{col}{rr}"]
        c.font = font(9)
        c.alignment = L if col == "D" else C
        if col in "EFGHIJ": c.number_format = "0.00"
ws.conditional_formatting.add(f"B{ch+1}:J{ch+spill_rows}", Rule(type="expression", formula=[f'$B{ch+1}<>""'],
    dxf=DifferentialStyle(fill=PatternFill("solid", bgColor="E6ECE9"), border=Border(left=hair, right=hair, top=hair, bottom=hair))))
clast = ch + spill_rows

# ---------------- Lateral restraints ----------------
r = clast + 1
heading(r, "Lateral Restraints (AS 4100 Cl. 5.4)")
label(f"B{r+1}", "Restraint type at each cross-section for the critical flange: F, P, L or U (– = none). Supports must be F or P.", 7, GREY, i=True)
rh = r + 2
rcols = ["x (mm)", "Ref", "Restraint provided by", "Top flange", "Bottom fl.", "αm top →", "αm bot. →"]
for col, txt in zip("BCDEFGH", rcols):
    put(f"{col}{rh}", txt, font(8, b=True), F_HDR, None, C)
restr = [("=0", "A", "Left support – cleat", "F", "F", None, None)] + \
        [(None, None, None, None, None, None, None)] * 5 + \
        [("=Span", "B", "Right support – cleat", "F", "F", None, None)]
for i, rv in enumerate(restr):
    rr = rh + 1 + i
    for col, v, nf in zip("BCDEFGH", rv, ["#,##0", None, None, None, None, "0.00", "0.00"]):
        if isinstance(v, str) and v.startswith("="):
            out(f"{col}{rr}", v, nf)
        else:
            inp(f"{col}{rr}", v, nf, L if col == "D" else C)
rlast = rh + len(restr)
trs = Table(displayName="Restraints_B1", ref=f"B{rh}:H{rlast}")
trs.tableStyleInfo = TableStyleInfo(name="TableStyleLight15", showRowStripes=False)
ws.add_table(trs)
dv_r = DataValidation(type="list", formula1='"F,P,L,U,–"')
ws.add_data_validation(dv_r); dv_r.add(f"E{rh+1}:F{rlast}")
label(f"B{rlast+1}", "αm top / αm bot. → override αm for that flange's segment from this row to the next restraint on that flange "
      "(blank = Cl. 5.6.1.1(a)(iii)). Flagged * in the output.", 7, GREY, i=True)
kb = rlast + 3
for col, txt in zip("EFG", ["Factor", "Top", "Bottom"]):
    put(f"{col}{kb}", txt, font(8, b=True), F_HDR, None, C)
kf = [("kl", 1.4, 1.0, "kl", "0.00"), ("kr", 1.0, 1.0, "kr", "0.00"), ("Continuous", "No", "No", "Cont", None)]
for i, (lab, top, bot, nm, nf) in enumerate(kf):
    rr = kb + 1 + i
    label(f"E{rr}", lab, 9, al=C)
    inp(f"F{rr}", top, nf); inp(f"G{rr}", bot, nf)
    name(f"{nm}_Top", f"$F${rr}"); name(f"{nm}_Bot", f"$G${rr}")
dv_cont = DataValidation(type="list", formula1='"Yes,No"')
ws.add_data_validation(dv_cont); dv_cont.add(f"F{kb+3}:G{kb+3}")
KF_RANGE = f"$F${kb+1}:$G${kb+3}"
label(f"H{kb+1}", "kl: load height, Table 5.6.3(2)", 7, GREY, i=True)
label(f"H{kb+2}", "kr: rotation, Table 5.6.3(3); kt from restraints", 7, GREY, i=True)
label(f"H{kb+3}", "Yes = flange continuously restrained: φMb = φMsx", 7, GREY, i=True)
rlast = kb + 3

# uplift summary row: say so and grey it out when no uplift combinations are active
for srow in range(sum_hdr + 1, sum_last + 1):
    if ws[f"D{srow}"].value == "Bending – uplift (bottom flange critical)":
        ws[f"D{srow}"] = '=IF(NumUpliftCombos=0,"Bending – uplift (no uplift combinations)","Bending – uplift (bottom flange critical)")'
        ws.conditional_formatting.add(f"B{srow}:J{srow}", Rule(type="expression", formula=["NumUpliftCombos=0"],
            dxf=DifferentialStyle(font=Font(color="BFBFBF"))))

# ---------------- Outputs (add-in) ----------------
from openpyxl.chart import ScatterChart, Reference, Series
from openpyxl.chart.axis import ChartLines
from openpyxl.chart.shapes import GraphicalProperties
from openpyxl.drawing.line import LineProperties
from openpyxl.chart.layout import Layout, ManualLayout
CRIT_RANGE = f"$E${hdr+1}:$F${hdr+5}"
LOADS = "Loads_B1[[Case]:[End (mm)]]"
COMBOS = f"_xlfn.ANCHORARRAY({COMBO_ANCHOR})"
ARGS_ULS = f"SectionSize,Grade,Span,E_mod,IncludeSW,{LOADS},{COMBOS},Restraints_B1[#Data],{KF_RANGE}"

def dyn(cell, formula, nf=None, f=None, al=None):
    c = ws[cell]
    c.value = ArrayFormula(cell, formula)
    c.font = f or font(9)
    if nf: c.number_format = nf
    if al: c.alignment = al
    return c

def out_header(row, cols, labels):
    for col, txt in zip(cols, labels):
        put(f"{col}{row}", txt, font(8, b=True), F_HDR, None, Alignment(horizontal="center", vertical="center", wrap_text=True))

def spill_format(r1, r2, cols, fmts, util_col=None):
    for rr in range(r1, r2 + 1):
        for col, nf in zip(cols, fmts):
            c = ws[f"{col}{rr}"]
            c.font = font(9); c.alignment = C
            if nf: c.number_format = nf
    ws.conditional_formatting.add(f"{cols[0]}{r1}:{cols[-1]}{r2}", Rule(type="expression", formula=[f'${cols[0]}{r1}<>""'],
        dxf=DifferentialStyle(fill=PatternFill("solid", bgColor="E6ECE9"), border=Border(left=hair, right=hair, top=hair, bottom=hair))))
    if util_col:
        ws.conditional_formatting.add(f"{util_col}{r1}:{util_col}{r2}", Rule(type="expression",
            formula=[f'AND(ISNUMBER({util_col}{r1}),{util_col}{r1}>1)'], dxf=DifferentialStyle(font=Font(color="C00000", bold=True))))

# helper area (right, not printed) for chart data and governing values
HX = 26  # column Z
from openpyxl.utils import get_column_letter as gcl
hc = lambda i: gcl(HX + i)
for i in range(11):
    ws.column_dimensions[hc(i)].width = 9
put(f"{hc(0)}6", "Add-in data for summary and charts  (not printed)", font(10, b=True, color=GREEN), al=Alignment(vertical="bottom"))
GOV_ANCHOR = f"{hc(0)}8"
label(f"{hc(0)}7", "Governing (OAR.GOVERNING)", 7, GREY, i=True)
dyn(GOV_ANCHOR, f"=OAR.GOVERNING({ARGS_ULS})")
ENV_ANCHOR = f"{hc(0)}13"; DEF_ANCHOR = f"{hc(6)}13"
# section list for the Section size dropdown, filtered by Section type
ws.column_dimensions[hc(12)].width = 12
label(f"{hc(12)}7", "Sections (OAR.SECTIONLIST)", 7, GREY, i=True)
SL_ANCHOR = f"{hc(12)}8"
dyn(SL_ANCHOR, "=OAR.SECTIONLIST(SectionType)")
sl_col = hc(12)
# static fallback list (used if the add-in has not loaded, so the dropdown never offers "#NAME?")
import csv as _csv
_rows = [l for l in open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "OAR.Steel.Core", "Data", "sections.csv"), encoding="utf-8") if l.strip() and not l.startswith("#")]
_all = [r["Designation"] for r in _csv.DictReader(_rows)]
fb_col = hc(14)
ws.column_dimensions[fb_col].width = 12
label(f"{fb_col}7", "All sections (fallback)", 7, GREY, i=True)
for i_, d_ in enumerate(_all):
    ws[f"{fb_col}{8+i_}"] = d_
    ws[f"{fb_col}{8+i_}"].font = font(8, color=GREY)
fb_rng = f"${fb_col}$8:${fb_col}${7+len(_all)}"
dv_size = DataValidation(type="list",
                         formula1=f"IF(ISERROR(${sl_col}$8),{fb_rng},OFFSET(${sl_col}$8,0,0,MAX(1,COUNTA(${sl_col}$8:${sl_col}$300)),1))",
                         showErrorMessage=True, errorTitle="Section size", error="Pick a section from the list for the selected Section type.")
ws.add_data_validation(dv_size)
dv_size.add(names["SectionSize"].replace("$", ""))
for cell, prop in SECTION_PROP_CELLS:
    dyn(cell, f'=OAR.SECTION(SectionSize,"{prop}",Grade)', ws[cell].number_format).fill = F_OUT
    ws[cell].border = B_IN; ws[cell].alignment = C
NPTS = 60
label(f"{hc(0)}12", "Envelope (OAR.ENVELOPE)", 7, GREY, i=True)
label(f"{hc(6)}12", "Deflected shapes (OAR.DEFLECTIONS)", 7, GREY, i=True)
dyn(ENV_ANCHOR, f"=OAR.ENVELOPE(SectionSize,Span,IncludeSW,{LOADS},{COMBOS},{NPTS})")
dyn(DEF_ANCHOR, f"=OAR.DEFLECTIONS(SectionSize,Span,E_mod,IncludeSW,{LOADS},PsiL,PsiS,{NPTS})")

def chart(title, xcol, ycols, first_row, ytitle, reverse, colors, w=17.6, h=5.6, names=None):
    """Scatter chart over a fixed block: x in xcol, series in ycols, data rows first_row .. first_row + NPTS.
    Series names are literal (not cell references) so Excel never regroups the series."""
    ch_ = ScatterChart()
    ch_.title = None
    ch_.style = 2
    ch_.height = h; ch_.width = w
    ch_.legend.position = "t"
    x = Reference(ws, min_col=xcol, min_row=first_row, max_row=first_row + NPTS)
    for k, (ycol, colr) in enumerate(zip(ycols, colors)):
        y = Reference(ws, min_col=ycol, min_row=first_row, max_row=first_row + NPTS)
        se = Series(y, x, title=names[k])
        se.marker.symbol = "none"
        se.smooth = False
        se.graphicalProperties.line.solidFill = colr
        se.graphicalProperties.line.width = 19050
        ch_.series.append(se)
    ch_.x_axis.title = None
    ch_.y_axis.title = ytitle
    from openpyxl.chart.axis import ChartLines
    from openpyxl.chart.shapes import GraphicalProperties
    from openpyxl.drawing.line import LineProperties
    grid = ChartLines(); grid.spPr = GraphicalProperties(ln=LineProperties(solidFill="E3E3E3", w=6350))
    ch_.y_axis.majorGridlines = grid
    ch_.x_axis.delete = False; ch_.y_axis.delete = False
    ch_.x_axis.scaling.min = 0
    ch_.x_axis.majorGridlines = None
    ch_.x_axis.tickLblPos = "high" if reverse else "low"  # keep x labels at the bottom when y is reversed
    ch_.y_axis.tickLblPos = "low"
    if reverse:
        ch_.y_axis.scaling.orientation = "maxMin"
    return ch_

env_r = int(ENV_ANCHOR[len(hc(0)):]); def_r = int(DEF_ANCHOR[len(hc(6)):])

# fixed chart blocks: plain cells that copy the spilled data (or #N/A), so the charts always see
# a stable numeric range even while the add-in is loading or a spill is being resized
CB = 46  # column AT
label(f"{gcl(CB)}12", "Chart data (fixed copies of the spills above)", 7, GREY, i=True)
def mirror(anchor, src_cols, dest_col):
    for i in range(NPTS + 1):
        for j, sc in enumerate(src_cols):
            cell = f"{gcl(dest_col + j)}{13 + i}"
            dyn(cell, f"=IFERROR(INDEX(_xlfn.ANCHORARRAY({anchor}),{i + 2},{sc}),NA())", "0.000")
            ws[cell].font = font(7, color=GREY)
DEF_BLOCK = CB            # AT..AX: x, Δg, Δg+ψl·q, Δg+ψs·q, Δq
ENV_BLOCK = CB + 6        # AZ..BB: x, M* max, M* min
mirror(DEF_ANCHOR, [1, 2, 3, 4, 5], DEF_BLOCK)
mirror(ENV_ANCHOR, [1, 2, 3], ENV_BLOCK)
for i in range(9):
    ws.column_dimensions[gcl(CB + i)].width = 7


r = rlast + 2
ws.row_breaks.append(Break(id=r - 1))

# Serviceability
heading(r, "Serviceability Checks")
out_header(r + 1, "BCDEFG", ["Case", "Δ max (mm)", "at x (mm)", "Limit (mm)", "L / Δ", "Utilisation"])
SLS_ANCHOR = f"B{r+2}"
dyn(SLS_ANCHOR, f"=OAR.SLS(SectionSize,Span,E_mod,IncludeSW,{LOADS},{CRIT_RANGE},PsiL,PsiS)")
spill_format(r + 2, r + 6, "BCDEFG", [None, "0.00", "#,##0", "0.00", '"L/"0', "0%"], util_col="G")
label(f"H{r+2}", "Δ1kN: 1 kN at midspan,", 7, GREY); label(f"H{r+3}", "beam alone", 7, GREY)
label(f"B{r+8}", "Deflected shapes (mm, plotted downward;  x in m)", 8, GREEN, b=True)
ws.add_chart(chart("", DEF_BLOCK, [DEF_BLOCK + 1, DEF_BLOCK + 2, DEF_BLOCK + 3, DEF_BLOCK + 4], 13, "δ (mm)", True,
                   [GREEN, "8FA89C", TAUPE, "595959"], h=5.4, names=["Δg", "Δg+ψl·q", "Δg+ψs·q", "Δq"]), f"B{r+9}")
r = r + 21

# ULS bending
heading(r, "ULS Strength Checks – Bending (AS 4100 Cl. 5.6)")
label(f"B{r+1}", "Bending moment envelope, all active combinations (kN·m, sagging plotted down;  x in m)", 8, GREEN, b=True)
ws.add_chart(chart("", ENV_BLOCK, [ENV_BLOCK + 1, ENV_BLOCK + 2], 13, "M* (kN·m)", True, [GREEN, TAUPE], h=5.4,
                   names=["M* max", "M* min"]), f"B{r+2}")
r = r + 14
out_header(r, "BCDEFGHIJ", ["Combo", "Segment (m)", "Ends", "Le (m)", "αm", "αs", "φMb (kN·m)", "M* (kN·m)", "Utilisation"])
ws.row_dimensions[r].height = 16
ULS_ANCHOR = f"B{r+1}"
dyn(ULS_ANCHOR, f"=OAR.ULS({ARGS_ULS})")
spill_format(r + 1, r + 16, "BCDEFGHIJ", [None, None, None, "0.00", "0.000", "0.000", "0.0", "0.0", "0%"], util_col="J")
ws.conditional_formatting.add(f"C{r+1}:C{r+16}", Rule(type="expression", formula=[f'RIGHT(C{r+1},1)="*"'],
    dxf=DifferentialStyle(font=Font(color="9C5700", bold=True))))
label(f"B{r+17}", "* αm overridden in the restraints table.  Full working for the governing segment follows.", 7, GREY, i=True)
r = r + 19
ws.row_breaks.append(Break(id=r - 1))

heading(r, "Governing Segment – Working")
label(f"B{r+1}", "OAR.ULSDETAIL: change the comboId / segment arguments to show any other segment.", 7, GREY, i=True)
DET_ANCHOR = f"D{r+2}"
dyn(DET_ANCHOR, f"=OAR.ULSDETAIL({ARGS_ULS})")
for rr in range(r + 2, r + 24):
    ws[f"D{rr}"].font = font(9); ws[f"D{rr}"].alignment = L
    ws[f"E{rr}"].font = font(9, b=True); ws[f"E{rr}"].alignment = C; ws[f"E{rr}"].number_format = "0.000"
    ws[f"F{rr}"].font = font(8, color=GREY); ws[f"F{rr}"].alignment = L
    for col in "DE":
        ws[f"{col}{rr}"].border = Border(bottom=Side(style="hair", color="D9D9D9"))
r = r + 25

heading(r, "Shear (AS 4100 Cl. 5.11)")
out_header(r + 1, "BCDEFG", ["Combo", "V* (kN)", "at x (m)", "φVv (kN)", "Utilisation", "Note"])
SH_ANCHOR = f"B{r+2}"
dyn(SH_ANCHOR, f"=OAR.SHEAR(SectionSize,Grade,Span,IncludeSW,{LOADS},{COMBOS})")
spill_format(r + 2, r + 9, "BCDEF", [None, "0.0", "0.00", "0.0", "0%"], util_col="F")
for rr in range(r + 2, r + 10):
    ws[f"G{rr}"].font = font(7, color="9C5700"); ws[f"G{rr}"].alignment = L
r = r + 11

# ---------------- Support reactions (named for transfer to other beams) ----------------
heading(r, "Support Reactions")
label(f"B{r+1}", "Unfactored, per case, positive in the case direction (Wu/Wiu = uplift). Self-weight is in G.  "
      "To load another beam: add a Point row with the same case and set Direct to e.g. ='B1'!RxnR_G", 7, GREY, i=True)
rx = r + 2
for col, txt in zip("BCDE", ["Case", "", "Left (kN)", "Right (kN)"]):
    put(f"{col}{rx}", txt, font(8, b=True), F_HDR, None, C)
ws.merge_cells(f"B{rx}:C{rx}")
put(f"F{rx}", "Transfer names", font(8, b=True), F_HDR, None, C); ws.merge_cells(f"F{rx}:G{rx}")
reac_args = 'SectionSize,Span,IncludeSW,Loads_B1[[Case]:[End (mm)]],' + f'_xlfn.ANCHORARRAY({COMBO_ANCHOR})'
for k, case in enumerate(["G", "Q", "Wd", "Wu", "Wid", "Wiu"]):
    rr = rx + 1 + k
    ws.merge_cells(f"B{rr}:C{rr}")
    put(f"B{rr}", case, font(9), border=B_IN, al=C); ws[f"C{rr}"].border = B_IN
    sgn = "-" if case in ("Wu", "Wiu") else ""
    out(f"D{rr}", ArrayFormula(f"D{rr}", f"={sgn}INDEX(OAR.REACTIONS({reac_args}),{k+1},2)"), "0.00")
    out(f"E{rr}", ArrayFormula(f"E{rr}", f"={sgn}INDEX(OAR.REACTIONS({reac_args}),{k+1},3)"), "0.00")
    put(f"F{rr}", f"RxnL_{case}  ·  RxnR_{case}", font(7, color=GREY), al=C); ws.merge_cells(f"F{rr}:G{rr}")
    name(f"RxnL_{case}", f"$D${rr}"); name(f"RxnR_{case}", f"$E${rr}")
r = rx + 8
label(f"B{r}", "All cases and active combinations (kN, + = upward reaction; uplift cases show negative)", 7, GREY, i=True)
out_header(r + 1, "BCD", ["ID", "Left (kN)", "Right (kN)"])
RX_ANCHOR = f"B{r+2}"
dyn(RX_ANCHOR, f"=OAR.REACTIONS(SectionSize,Span,IncludeSW,{LOADS},{COMBOS})")
spill_format(r + 2, r + 15, "BCD", [None, "0.00", "0.00"])
name("Out_Reactions", f"$B${r+2}")
r = r + 7
lastrow = r + 9

# ---------------- Summary cells wired to add-in results ----------------
sls_map = {"Res_Dg": 1, "Res_Dl": 2, "Res_Ds": 3, "Res_Dq": 4, "Res_D1": 5}
for nm, k in sls_map.items():
    cell = names[nm].replace("$", "")
    dyn(cell, f"=INDEX(_xlfn.ANCHORARRAY({SLS_ANCHOR}),{k},2)", "0.00").fill = F_OUT
gov_map = {"Res_Mdn": (1, 2), "Res_phiMdn": (1, 3), "Res_Mup": (2, 2), "Res_phiMup": (2, 3), "Res_V": (3, 2), "Res_phiV": (3, 3)}
for nm, (i, j) in gov_map.items():
    cell = names[nm].replace("$", "")
    dyn(cell, f"=INDEX(_xlfn.ANCHORARRAY({GOV_ANCHOR}),{i},{j})", "0.0").fill = F_OUT
for cell in ws[f"B{sum_hdr}:J{sum_last}"]:
    for c in cell:
        if c.value is not None and not isinstance(c.value, str) and hasattr(c.value, "text"):
            c.border = B_IN

# ---------------- Names, print setup ----------------
for nm, ref in names.items():
    ws.defined_names[nm] = DefinedName(nm, attr_text=f"'{ws.title}'!{ref}")

ws.print_area = f"A1:K{lastrow}"
ws.page_setup.paperSize = ws.PAPERSIZE_A4
ws.page_setup.orientation = "portrait"
ws.page_setup.fitToWidth = 1
ws.page_setup.fitToHeight = 0
ws.sheet_properties.pageSetUpPr.fitToPage = True
ws.page_margins.left = ws.page_margins.right = 0.5
ws.page_margins.top = 0.5; ws.page_margins.bottom = 0.6
ws.page_margins.header = 0.2; ws.page_margins.footer = 0.25
ws.oddFooter.left.text = "OAR Consulting Engineers Pty Ltd"
ws.oddFooter.left.size = 7; ws.oddFooter.left.font = "Arial"; ws.oddFooter.left.color = GREY
ws.oddFooter.center.text = "&A"
ws.oddFooter.center.size = 7; ws.oddFooter.center.font = "Arial"; ws.oddFooter.center.color = GREY
ws.oddFooter.right.text = "Page &P of &N"
ws.oddFooter.right.size = 7; ws.oddFooter.right.font = "Arial"; ws.oddFooter.right.color = GREY
ws.freeze_panes = None
ws.sheet_view.zoomScale = 115
wb.save(OUT)

# ---- mark array formulas as dynamic-array formulas (spill, no implicit intersection) ----
import zipfile, re, shutil, os
METADATA = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
    '<metadata xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
    'xmlns:xda="http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray">'
    '<metadataTypes count="1"><metadataType name="XLDAPR" minSupportedVersion="120000" copy="1" pasteAll="1" '
    'pasteValues="1" merge="1" splitFirst="1" rowColShift="1" clearFormats="1" clearComments="1" assign="1" '
    'coerce="1" cellMeta="1"/></metadataTypes><futureMetadata name="XLDAPR" count="1"><bk><extLst>'
    '<ext uri="{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}"><xda:dynamicArrayProperties fDynamic="1" fCollapsed="0"/>'
    '</ext></extLst></bk></futureMetadata><cellMetadata count="1"><bk><rc t="1" v="0"/></bk></cellMetadata></metadata>')
tmp = OUT + ".tmp"
with zipfile.ZipFile(OUT) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
    for item in zin.infolist():
        data = zin.read(item.filename)
        if item.filename.startswith("xl/worksheets/sheet"):
            txt = data.decode("utf-8")
            txt = re.sub(r'<c r="([A-Z]+[0-9]+)"((?:(?!>).)*)><f t="array"', lambda m: f'<c r="{m.group(1)}"{m.group(2)} cm="1"><f t="array"', txt)
            data = txt.encode("utf-8")
        elif item.filename == "[Content_Types].xml":
            data = data.decode("utf-8").replace("</Types>",
                '<Override PartName="/xl/metadata.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheetMetadata+xml"/></Types>').encode("utf-8")
        elif item.filename == "xl/_rels/workbook.xml.rels":
            data = data.decode("utf-8").replace("</Relationships>",
                '<Relationship Id="rIdMeta1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sheetMetadata" Target="metadata.xml"/></Relationships>').encode("utf-8")
        zout.writestr(item, data)
    zout.writestr("xl/metadata.xml", METADATA)
shutil.move(tmp, OUT)
print("saved", OUT, "lastrow", lastrow)
