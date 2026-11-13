/// OAR > Review loads: a window that draws the beam with the loads of any load case or combination
/// (factored), the support reactions, and the shear, moment and deflection diagrams.
///
/// All the engineering and the picture itself come from OAR.Steel.Core (Review module). This file only
/// reads the active sheet's inputs and paints the picture, so the window shows what the sheet calculates.
namespace OAR.Steel.Excel

open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Windows.Forms
open ExcelDna.Integration
open OAR.Steel

/// What goes in the header of a printed review page, and where a PDF is saved by default
type PageInfo =
    { Job: string
      Project: string
      By: string
      Date: string
      BeamRef: string
      Folder: string }

/// Reading the inputs of a steel beam sheet through the Excel object model
module private SheetInputs =

    type XlSheet = Microsoft.Office.Interop.Excel.Worksheet
    type XlRange = Microsoft.Office.Interop.Excel.Range

    /// A value read through the object model, in the shape the worksheet functions get from Excel-DNA:
    /// empty cells as ExcelEmpty, error cells as ExcelError, ranges as zero-based arrays
    let private norm (v: obj) : obj =
        let one (o: obj) : obj =
            match o with
            | null -> box ExcelEmpty.Value
            | :? int -> box ExcelError.ExcelErrorValue // error cells arrive as an Int32 code
            | :? decimal as d -> box (float d)
            | :? DateTime as d -> box (d.ToOADate())
            | other -> other
        match v with
        | :? (obj[,]) as a ->
            let b0, b1 = a.GetLowerBound 0, a.GetLowerBound 1
            box (Array2D.init (a.GetLength 0) (a.GetLength 1) (fun i j -> one (a.GetValue(i + b0, j + b1))))
        | other -> one other

    let private tryRange (ws: XlSheet) (name: string) : XlRange option =
        try
            match ws.Range(name, Type.Missing) with
            | null -> None
            | r -> Some r
        with _ ->
            None

    let private missing = box ExcelMissing.Value

    /// Value of a named cell or range on the sheet; ExcelMissing when the name is not there (older layouts)
    let private value (ws: XlSheet) (name: string) : obj =
        match tryRange ws name with
        | Some r -> norm r.Value2
        | None -> missing

    let private text (o: obj) =
        match o with
        | :? string as s -> s
        | :? float as v -> v.ToString(Globalization.CultureInfo.InvariantCulture)
        | _ -> ""

    /// The loads table, columns Type to Dir (the whole table on layouts without those headers)
    let private loads (ws: XlSheet) : obj =
        let lo =
            match tryRange ws "OAR_Flex_Loads" with
            | Some r -> r.ListObject
            | None -> null
        if isNull lo || isNull lo.DataBodyRange then failwith "the loads table was not found on this sheet"
        let body = norm lo.DataBodyRange.Value2
        let headers =
            match norm lo.HeaderRowRange.Value2 with
            | :? (obj[,]) as h -> [ for j in 0 .. Array2D.length2 h - 1 -> (text h.[0, j]).Trim() ]
            | h -> [ (text h).Trim() ]
        match body, List.tryFindIndex (fun h -> h = "Type") headers, List.tryFindIndex (fun h -> h = "Dir") headers with
        | (:? (obj[,]) as a), Some i0, Some i1 when i1 > i0 && i1 < Array2D.length2 a ->
            box (Array2D.init (Array2D.length1 a) (i1 - i0 + 1) (fun i j -> a.[i, j + i0]))
        | _ -> body

    /// The active combinations: the spilled range that starts at the first cell of the combinations block
    let private combos (ws: XlSheet) : obj =
        match tryRange ws "OAR_Flex_Combos" with
        | None -> failwith "the combinations table was not found on this sheet"
        | Some flex ->
            let anchor = flex.Cells.[1, 1] :?> XlRange
            let spill =
                try
                    // not in the Excel 2013 object model the add-in is built against, so asked for by name
                    match anchor.GetType().InvokeMember("SpillingToRange", System.Reflection.BindingFlags.GetProperty, null, anchor, null) with
                    | :? XlRange as r -> Some r
                    | _ -> None
                with _ ->
                    None
            match spill with
            | Some r -> norm r.Value2
            | None ->
                let first = text (norm anchor.Value2)
                if first.StartsWith("No combinations", StringComparison.OrdinalIgnoreCase) then
                    box first
                else
                    // no spill (older Excel): the block's rows, eight columns ID to Dir; blank rows are ignored
                    norm (ws.Range(anchor, flex.Cells.[flex.Rows.Count, 8]).Value2)

    /// Job details from the sheet header (blank where the sheet has none), for the printed page
    let pageInfo (ws: XlSheet) : PageInfo =
        let safe (f: unit -> string) =
            try
                f ()
            with _ ->
                ""
        // PROJECT and BY sit under JOB No. in the header block (rows +1 and +3)
        let under (rows: int) =
            safe (fun () ->
                match tryRange ws "JobNo" with
                | Some r -> text (norm (ws.Cells.[r.Row + rows, r.Column] :?> XlRange).Value2)
                | None -> "")
        let project = (under 1).Trim()
        { Job = safe (fun () -> (text (value ws "JobNo")).Trim())
          Project = (if project.StartsWith("Project name", StringComparison.OrdinalIgnoreCase) then "" else project)
          By = (under 3).Trim()
          Date =
            safe (fun () ->
                match value ws "CalcDate" with
                | :? float as d when d > 1.0 -> DateTime.FromOADate(d).ToString("dd/MM/yyyy")
                | :? string as t -> t.Trim()
                | _ -> "")
          BeamRef = safe (fun () -> (text (value ws "BeamRef")).Trim())
          Folder = safe (fun () -> (ws.Parent :?> Microsoft.Office.Interop.Excel.Workbook).Path) }

    /// Read the sheet and solve nothing yet: the beam, loads and the list of cases / combinations to review
    let read (ws: XlSheet) : ReviewData =
        let size = value ws "SectionSize"
        let span =
            match tryRange ws "Geom" with
            | Some r -> norm r.Value2
            | None -> value ws "Span"
        if Object.ReferenceEquals(size, missing) || Object.ReferenceEquals(span, missing) then
            failwith "this isn't an OAR steel beam sheet, or its layout is too old for Review loads (needs v0.3.6 or later)"
        Functions.reviewData
            (text (value ws "BeamRef"))
            size
            span
            (value ws "E_mod")
            (value ws "IncludeSW")
            (loads ws)
            (combos ws)
            (value ws "PsiL")
            (value ws "PsiS")
            (value ws "ShearDefl")
            (value ws "WindSLSFactor")

/// The drawing surface: double-buffered so the hover line doesn't flicker
type private Canvas() as this =
    inherit Control()

    do
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint
            ||| ControlStyles.UserPaint
            ||| ControlStyles.OptimizedDoubleBuffer
            ||| ControlStyles.ResizeRedraw,
            true
        )

module private Painter =

    let private colour (hex: string) (opacity: float) =
        let c = ColorTranslator.FromHtml hex
        Color.FromArgb(int (255.0 * max 0.0 (min 1.0 opacity)), c)

    let private points (pts: (float * float) list) =
        pts |> List.map (fun (x, y) -> PointF(float32 x, float32 y)) |> List.toArray

    let draw (g: Graphics) (scene: Scene) =
        g.SmoothingMode <- SmoothingMode.AntiAlias
        g.TextRenderingHint <- System.Drawing.Text.TextRenderingHint.ClearTypeGridFit
        for p in scene.Prims do
            match p with
            | PLine(x1, y1, x2, y2, c, w, dashed) ->
                use pen = new Pen(colour c 1.0, float32 w)
                if dashed then pen.DashStyle <- DashStyle.Dash
                g.DrawLine(pen, float32 x1, float32 y1, float32 x2, float32 y2)
            | PPolygon(pts, fill, opacity, stroke, w) ->
                let arr = points pts
                if arr.Length >= 3 then
                    use brush = new SolidBrush(colour fill opacity)
                    g.FillPolygon(brush, arr)
                    if stroke <> "" then
                        use pen = new Pen(colour stroke 1.0, float32 w)
                        g.DrawPolygon(pen, arr)
            | PPolyline(pts, c, w) ->
                let arr = points pts
                if arr.Length >= 2 then
                    use pen = new Pen(colour c 1.0, float32 w)
                    pen.LineJoin <- LineJoin.Round
                    g.DrawLines(pen, arr)
            | PText(x, y, t, c, size, align, bold) ->
                let style = if bold then FontStyle.Bold else FontStyle.Regular
                use font = new Font("Segoe UI", float32 (size * 96.0 / 72.0), style, GraphicsUnit.Pixel)
                use brush = new SolidBrush(colour c 1.0)
                use fmt = new StringFormat(StringFormat.GenericTypographic)
                fmt.Alignment <-
                    (match align with
                     | Align.Left -> StringAlignment.Near
                     | Align.Centre -> StringAlignment.Center
                     | Align.Right -> StringAlignment.Far)
                fmt.LineAlignment <- StringAlignment.Center
                fmt.FormatFlags <- fmt.FormatFlags ||| StringFormatFlags.NoWrap ||| StringFormatFlags.NoClip
                g.DrawString(t, font, brush, PointF(float32 x, float32 y), fmt)

/// Printing the picture (to a printer, or straight to a PDF file through Windows' PDF printer)
module private Printer =

    let pdfPrinter = "Microsoft Print to PDF"

    let hasPdfPrinter () =
        try
            [ for p in Printing.PrinterSettings.InstalledPrinters -> string p ] |> List.exists (fun p -> p = pdfPrinter)
        with _ ->
            false

    /// A4 landscape, 12 mm margins, on whichever printer the document is set to
    let setPage (doc: Printing.PrintDocument) =
        let a4 =
            [ for o in doc.PrinterSettings.PaperSizes -> box o ]
            |> List.tryPick (fun o ->
                match o with
                | :? Printing.PaperSize as sz when sz.Kind = Printing.PaperKind.A4 -> Some sz
                | _ -> None)
        match a4 with
        | Some sz -> doc.DefaultPageSettings.PaperSize <- sz
        | None -> ()
        doc.DefaultPageSettings.Landscape <- true
        doc.DefaultPageSettings.Margins <- Printing.Margins(47, 47, 47, 47)

    /// One page: job header, the picture scaled to the page width, footer.
    /// `makeScene width height` builds the picture for a drawing area of that shape.
    let page (info: PageInfo) (makeScene: float -> float -> Scene) (e: Printing.PrintPageEventArgs) =
        let g = e.Graphics
        let m = e.MarginBounds
        let left, top, width, height = float32 m.Left, float32 m.Top, float32 m.Width, float32 m.Height
        use bold = new Font("Segoe UI", 10.0f, FontStyle.Bold)
        use small = new Font("Segoe UI", 8.0f)
        use dark = new SolidBrush(Color.FromArgb(34, 34, 34))
        use grey = new SolidBrush(Color.FromArgb(110, 110, 110))
        use rule = new Pen(Color.FromArgb(70, 90, 82), 1.5f)
        use far = new StringFormat(Alignment = StringAlignment.Far)
        // header: firm and job on the left, date and initials on the right (units are 1/100 inch)
        g.DrawString("OAR Consulting Engineers Pty Ltd", bold, dark, left, top)
        let job =
            [ (if info.Job = "" then "" else "Job " + info.Job); info.Project; info.BeamRef ]
            |> List.filter (fun t -> t <> "")
        g.DrawString(String.Join("   ·   ", job), small, grey, left, top + 18.0f)
        let right = RectangleF(left, top, width, 34.0f)
        g.DrawString("Loads and analysis", bold, dark, right, far)
        let stamp = [ info.Date; (if info.By = "" then "" else "By " + info.By) ] |> List.filter (fun t -> t <> "")
        g.DrawString(String.Join("   ·   ", stamp), small, grey, RectangleF(left, top + 18.0f, width, 16.0f), far)
        g.DrawLine(rule, left, top + 38.0f, left + width, top + 38.0f)
        // footer
        let version = Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3)
        g.DrawString(sprintf "OAR Steel %s  ·  same engine and inputs as the calculation sheet" version, small, grey, left, top + height - 12.0f)
        g.DrawString(DateTime.Now.ToString("dd/MM/yyyy HH:mm"), small, grey, RectangleF(left, top + height - 12.0f, width, 14.0f), far)
        // the picture: drawn 1,050 logical pixels wide, scaled to the space between header and footer
        let areaTop, areaHeight = top + 44.0f, height - 44.0f - 18.0f
        let logicalWidth = 1050.0
        let k = float width / logicalWidth
        let scene = makeScene logicalWidth (float areaHeight / k)
        let state = g.Save()
        g.TranslateTransform(left, areaTop)
        g.ScaleTransform(float32 k, float32 k)
        Painter.draw g scene
        g.Restore state
        e.HasMorePages <- false

    let document (title: string) (info: PageInfo) (makeScene: float -> float -> Scene) =
        let doc = new Printing.PrintDocument()
        doc.DocumentName <- title
        doc.PrintController <- new Printing.StandardPrintController()
        doc.PrintPage.Add(fun e -> page info makeScene e)
        doc

type private ReviewForm(load: unit -> ReviewData, pageInfo: unit -> PageInfo) as this =
    inherit Form()

    let groups = [ "Load cases"; "Serviceability"; "Combinations" ]
    let tree =
        new TreeView(Dock = DockStyle.Left, Width = 215, HideSelection = false, ShowLines = false, ShowRootLines = false,
                     ShowPlusMinus = false, FullRowSelect = true, ShowNodeToolTips = true, BorderStyle = BorderStyle.None)
    let top = new FlowLayoutPanel(Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = Padding(10, 7, 8, 0))
    let check (text: string) = new CheckBox(Text = text, Checked = true, AutoSize = true, Margin = Padding(0, 3, 16, 0))
    let cbLoads = check "Loads"
    let cbReactions = check "Reactions"
    let cbShear = check "Shear"
    let cbMoment = check "Moment"
    let cbDeflection = check "Deflection"
    let refresh = new Button(Text = "Refresh from sheet", AutoSize = true, Margin = Padding(12, 0, 0, 0))
    let savePdf = new Button(Text = "Save PDF", AutoSize = true, Margin = Padding(6, 0, 0, 0))
    let print = new Button(Text = "Print…", AutoSize = true, Margin = Padding(6, 0, 0, 0))
    let status =
        new Label(Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = Padding(10, 0, 0, 0),
                  BackColor = Color.FromArgb(244, 244, 244))
    let canvas = new Canvas(Dock = DockStyle.Fill, BackColor = Color.White)
    let hint = "Move the mouse along the beam for the values at a point.  Pick a case or combination on the left."

    let mutable data: ReviewData option = None
    let mutable solution: ReviewSolution option = None
    let mutable scene: Scene option = None
    let mutable problem = ""
    let mutable hover = nan

    let scale () =
        use g = canvas.CreateGraphics()
        float g.DpiX / 96.0

    let view () : ReviewView =
        { Loads = cbLoads.Checked
          Reactions = cbReactions.Checked
          Shear = cbShear.Checked
          Moment = cbMoment.Checked
          Deflection = cbDeflection.Checked }

    /// Rebuild the picture for the current case, tick boxes and window size
    let redraw () =
        scene <- None
        match data, solution with
        | Some d, Some sol ->
            try
                let s = scale ()
                scene <- Some(Review.scene d sol (view ()) (float canvas.ClientSize.Width / s) (float canvas.ClientSize.Height / s))
                problem <- ""
            with e ->
                problem <- e.Message
        | _ -> ()
        canvas.Invalidate()

    let select (item: ReviewItem) =
        solution <- None
        match data with
        | Some d ->
            try
                solution <- Some(Review.solve d item)
                problem <- ""
            with e ->
                problem <- item.Id + ": " + e.Message
        | None -> ()
        hover <- nan
        status.Text <- hint
        redraw ()

    /// Print what is showing (the selected case and the ticked parts) on one A4 landscape page:
    /// to a PDF file when `toPdf`, otherwise through the print dialog
    let output (toPdf: bool) =
        match data, solution with
        | Some d, Some sol ->
            try
                let info = pageInfo ()
                let v = view ()
                let title = sprintf "%s %s - loads and analysis" (if info.BeamRef = "" then "Beam" else info.BeamRef) sol.Item.Id
                use doc = Printer.document title info (fun w h -> Review.scene d sol v w h)
                if toPdf && Printer.hasPdfPrinter () then
                    let clean = String(title |> Seq.map (fun c -> if Array.contains c (IO.Path.GetInvalidFileNameChars()) then '-' else c) |> Seq.toArray)
                    use dialog = new SaveFileDialog(Title = "Save loads and analysis as PDF", Filter = "PDF (*.pdf)|*.pdf", FileName = clean + ".pdf", OverwritePrompt = true)
                    if info.Folder <> "" && IO.Directory.Exists info.Folder then dialog.InitialDirectory <- info.Folder
                    if dialog.ShowDialog(this) = DialogResult.OK then
                        doc.PrinterSettings.PrinterName <- Printer.pdfPrinter
                        doc.PrinterSettings.PrintToFile <- true
                        doc.PrinterSettings.PrintFileName <- dialog.FileName
                        Printer.setPage doc
                        doc.Print()
                        status.Text <- "Saved " + dialog.FileName
                        try
                            Diagnostics.Process.Start(dialog.FileName) |> ignore
                        with _ ->
                            ()
                else
                    if toPdf then
                        MessageBox.Show(this, "The 'Microsoft Print to PDF' printer isn't installed on this computer, so pick a printer (or another PDF printer) instead.", "OAR Steel – Review loads") |> ignore
                    use dialog = new PrintDialog(Document = doc, UseEXDialog = true, AllowSomePages = false)
                    Printer.setPage doc
                    if dialog.ShowDialog(this) = DialogResult.OK then
                        Printer.setPage doc
                        doc.Print()
                        status.Text <- "Sent to " + doc.PrinterSettings.PrinterName
            with e ->
                MessageBox.Show(this, "Couldn't print: " + e.Message, "OAR Steel – Review loads", MessageBoxButtons.OK, MessageBoxIcon.Warning) |> ignore
        | _ -> ()

    /// Read the sheet again and fill the list, keeping the selected case if it is still there
    let reload () =
        let keep =
            match solution with
            | Some s -> s.Item.Id
            | None -> ""
        try
            let d = load ()
            data <- Some d
            problem <- ""
            this.Text <- "OAR Steel – Review loads – " + d.Title
            tree.BeginUpdate()
            tree.Nodes.Clear()
            let mutable first: TreeNode = null
            let mutable kept: TreeNode = null
            for g in groups do
                let items = d.Items |> List.filter (fun i -> i.Group = g)
                if not items.IsEmpty then
                    let parent = tree.Nodes.Add g
                    parent.ForeColor <- Color.FromArgb(110, 110, 110)
                    for i in items do
                        let node = parent.Nodes.Add i.Id
                        node.Tag <- box i
                        node.ToolTipText <- i.Description
                        if isNull first then first <- node
                        if i.Id = keep then kept <- node
            tree.ExpandAll()
            tree.EndUpdate()
            let pick = if isNull kept then first else kept
            if isNull pick then
                solution <- None
                problem <- "There are no loads or combinations on this sheet to review."
                redraw ()
            else
                // selecting the node runs `select` through AfterSelect
                tree.SelectedNode <- null
                tree.SelectedNode <- pick
        with e ->
            data <- None
            solution <- None
            tree.Nodes.Clear()
            problem <- e.Message
            redraw ()

    do
        this.Text <- "OAR Steel – Review loads"
        this.Font <- new Font("Segoe UI", 9.0f)
        this.ClientSize <- Size(1220, 860)
        this.MinimumSize <- Size(820, 560)
        this.StartPosition <- FormStartPosition.CenterScreen
        this.ShowIcon <- false
        this.BackColor <- Color.White
        status.Text <- hint
        top.Controls.AddRange([| cbLoads :> Control; cbReactions :> Control; cbShear :> Control; cbMoment :> Control
                                 cbDeflection :> Control; refresh :> Control; savePdf :> Control; print :> Control |])
        this.Controls.Add canvas
        this.Controls.Add top
        this.Controls.Add status
        this.Controls.Add tree
        // the fill control has to be in front of the docked ones to get the space that is left
        canvas.BringToFront()

        tree.AfterSelect.Add(fun e ->
            match (if isNull e.Node then null else e.Node.Tag) with
            | :? ReviewItem as item -> select item
            | _ -> ())
        for cb in [ cbLoads; cbReactions; cbShear; cbMoment; cbDeflection ] do
            cb.CheckedChanged.Add(fun _ -> redraw ())
        refresh.Click.Add(fun _ -> reload ())
        savePdf.Click.Add(fun _ -> output true)
        print.Click.Add(fun _ -> output false)
        canvas.Resize.Add(fun _ -> redraw ())
        canvas.MouseLeave.Add(fun _ ->
            hover <- nan
            status.Text <- hint
            canvas.Invalidate())
        canvas.MouseMove.Add(fun e ->
            match scene, solution with
            | Some sc, Some sol ->
                let x = float e.X / scale ()
                if x >= sc.X0 && x <= sc.X1 then
                    hover <- x
                    status.Text <- Review.readout sol (sc.Length * (x - sc.X0) / (sc.X1 - sc.X0))
                else
                    hover <- nan
                    status.Text <- hint
                canvas.Invalidate()
            | _ -> ())
        canvas.Paint.Add(fun e ->
            let g = e.Graphics
            g.Clear Color.White
            let s = float32 (float g.DpiX / 96.0)
            g.ScaleTransform(s, s)
            match scene with
            | Some sc ->
                Painter.draw g sc
                if not (Double.IsNaN hover) then
                    use pen = new Pen(Color.FromArgb(150, 90, 90, 90), 1.0f)
                    g.DrawLine(pen, float32 hover, 46.0f, float32 hover, float32 sc.Height - 4.0f)
            | None -> ()
            if problem <> "" then
                use font = new Font("Segoe UI", 10.0f)
                use brush = new SolidBrush(Color.FromArgb(192, 57, 43))
                let area = RectangleF(20.0f, 60.0f, float32 canvas.ClientSize.Width / s - 40.0f, 200.0f)
                g.DrawString("Can't draw this: " + problem, font, brush, area))
        this.Shown.Add(fun _ -> reload ())

module ReviewWindow =

    let mutable private current: Form = null

    /// Open (or bring forward and refresh) the review window for a sheet. The window stays open beside
    /// Excel; Refresh from sheet reads the inputs again.
    let show (excelHwnd: int) (ws: Microsoft.Office.Interop.Excel.Worksheet) =
        if not (isNull current) && not current.IsDisposed then current.Close()
        let form = new ReviewForm((fun () -> SheetInputs.read ws), (fun () -> SheetInputs.pageInfo ws))
        current <- form :> Form
        let owner =
            { new IWin32Window with
                member _.Handle = IntPtr excelHwnd }
        form.Show owner
