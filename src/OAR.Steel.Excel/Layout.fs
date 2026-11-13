/// Print tidy-up: hides the unused rows of variable-length blocks (combinations, loads, restraints,
/// ULS / shear / reaction spills) when a sheet is printed or saved as PDF, so the printout has no gaps.
///
/// A block is a sheet-scoped name starting "OAR_Flex" that refers to one column of key cells
/// (e.g. the ID column of the combinations spill). A row whose key cell shows nothing is hidden.
/// The rows come back as soon as the print or PDF export finishes (queued until Excel is idle),
/// on the next edit to that sheet, or with OAR > Show all rows.
namespace OAR.Steel.Excel

open System
open System.Runtime.InteropServices
open ExcelDna.Integration
open ExcelDna.Integration.CustomUI
open Microsoft.Office.Interop.Excel

module Layout =

    let private prefix = "OAR_Flex"

    let private flexRanges (ws: Worksheet) : Range list =
        [ for o in (ws.Names :> Collections.IEnumerable) do
              let n = o :?> Name
              let shortName =
                  let s = n.Name
                  let i = s.LastIndexOf '!'
                  if i >= 0 then s.Substring(i + 1) else s
              if shortName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                  let rng =
                      try
                          Some n.RefersToRange
                      with _ ->
                          None
                  match rng with
                  | Some r when not (isNull r) -> yield r
                  | _ -> () ]

    let private isBlank (c: Range) =
        match c.Text with
        | :? string as s -> s.Trim() = ""
        | null -> true
        | _ -> false

    /// Hide the empty rows of every flex block on the sheet
    let tidySheet (ws: Worksheet) =
        for rng in flexRanges ws do
            for o in (rng.Cells :> Collections.IEnumerable) do
                let c = o :?> Range
                if isBlank c then c.EntireRow.Hidden <- box true

    /// Unhide flex rows on the sheet (only touches the sheet if something is hidden)
    let restoreSheet (ws: Worksheet) =
        for rng in flexRanges ws do
            // one block failing (e.g. a name left pointing at #REF!) must not stop the others coming back
            try
                // check row by row: Hidden on a multi-row range does not reliably report a mix of hidden
                // and shown rows, which left partly hidden blocks hidden. Only touch the sheet when a row
                // is actually hidden, because setting Hidden clears Excel's undo list (this runs on every edit).
                let anyHidden =
                    (rng.Rows :> Collections.IEnumerable)
                    |> Seq.cast<obj>
                    |> Seq.exists (fun o ->
                        match (o :?> Range).EntireRow.Hidden with
                        | :? bool as h -> h
                        | _ -> false)
                if anyHidden then rng.EntireRow.Hidden <- box false
            with _ ->
                ()

    /// Hidden rows in the sheet's used range
    let hiddenRowCount (ws: Worksheet) =
        let mutable n = 0
        for o in (ws.UsedRange.Rows :> Collections.IEnumerable) do
            match (o :?> Range).EntireRow.Hidden with
            | :? bool as h when h -> n <- n + 1
            | _ -> ()
        n

    /// Show every hidden row in the sheet's used range (OAR > Show all rows), whatever hid it.
    /// Returns how many rows were hidden before.
    let showAllRows (ws: Worksheet) =
        let n = hiddenRowCount ws
        ws.UsedRange.EntireRow.Hidden <- box false
        n

    let private worksheets (wb: Workbook) =
        [ for o in (wb.Worksheets :> Collections.IEnumerable) do
              match o with
              | :? Worksheet as ws -> yield ws
              | _ -> () ]

    let safely f =
        try
            f ()
        with _ ->
            ()

    let tidyWorkbook (wb: Workbook) =
        for ws in worksheets wb do
            safely (fun () -> tidySheet ws)

    let private app () =
        ExcelDnaUtil.Application :?> Application

    // the ribbon buttons always say what they did in the status bar, so a button that seems to do
    // nothing can be told apart from one that never ran; the message clears on the next edit
    let mutable private statusSet = false

    let private status (a: Application) (msg: string) =
        a.StatusBar <- box ("OAR: " + msg)
        statusSet <- true

    let clearStatus () =
        if statusSet then
            statusSet <- false
            try
                (app ()).StatusBar <- box false
            with _ ->
                ()

    let private onActiveWorksheet (what: string) (f: Worksheet -> string) =
        let a = app ()
        try
            match a.ActiveSheet with
            | :? Worksheet as ws ->
                try
                    status a (f ws)
                with e ->
                    // say why instead of failing silently (most often a protected sheet)
                    let why = if ws.ProtectContents then "the sheet is protected – unprotect it first" else e.Message
                    status a (what + " failed: " + why)
            | _ -> status a (what + ": the active sheet is not a worksheet")
        with e ->
            status a (what + " failed: " + e.Message)

    let tidyActiveSheet () =
        onActiveWorksheet "Tidy for print" (fun ws ->
            let before = hiddenRowCount ws
            tidySheet ws
            sprintf "Tidy for print – %d unused rows hidden" (hiddenRowCount ws - before))

    let restoreActiveSheet () =
        onActiveWorksheet "Show all rows" (fun ws ->
            let n = showAllRows ws
            if n = 0 then "Show all rows – no rows were hidden on this sheet"
            else sprintf "Show all rows – %d hidden rows shown" n)

    // ---------- Clear inputs (start a fresh beam on this sheet) ----------

    let private listObjects (ws: Worksheet) =
        [ for o in (ws.ListObjects :> Collections.IEnumerable) -> o :?> ListObject ]

    /// The sheet's table whose name starts with the prefix (copied tabs rename Loads_B1 to Loads_B13 etc.)
    let private table (ws: Worksheet) (prefix: string) =
        listObjects ws |> List.tryFind (fun lo -> lo.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

    let private column (lo: ListObject) (header: string) : Range option =
        [ for o in (lo.ListColumns :> Collections.IEnumerable) -> o :?> ListColumn ]
        |> List.tryFind (fun c -> c.Name.Trim() = header)
        |> Option.bind (fun c -> if isNull c.DataBodyRange then None else Some c.DataBodyRange)

    let private formulaText (c: Range) =
        match c.Formula with
        | :? string as s -> s
        | _ -> ""

    let private hasName (ws: Worksheet) (n: string) =
        [ for o in (ws.Names :> Collections.IEnumerable) -> (o :?> Name).Name ]
        |> List.exists (fun full -> full.EndsWith("!" + n, StringComparison.OrdinalIgnoreCase) || full = n)

    /// Reset the active steel beam sheet: no loads, supports only in the restraints table (no intermediate restraints,
    /// no αm / kl overrides), 5.0 m simply supported. Section, grade, combinations and criteria are kept.
    let clearInputs (ws: Worksheet) =
        // geometry
        ws.Range("Span", Type.Missing).Value2 <- box 5000.0
        ws.Range("Support", Type.Missing).Value2 <- box "Simply supported"
        if hasName ws "OverhangIn" then ws.Range("OverhangIn", Type.Missing).Value2 <- box 0.0
        // loads: every row blank; the case columns get the library lookup back, so typing a library description fills them
        match table ws "Loads_" with
        | Some lo when not (isNull lo.DataBodyRange) ->
            lo.DataBodyRange.ClearContents() |> ignore
            match table ws "LoadLib_" with
            | Some lib ->
                for case in [ "G"; "Q"; "Wd"; "Wu" ] do
                    match column lo case with
                    | Some col ->
                        let m = sprintf "MATCH(%s[[#This Row],[Description]],%s[Description],0)" lo.Name lib.Name
                        col.Formula <-
                            box (sprintf "=IFERROR(IF(INDEX(%s[%s],%s)=\"\",\"\",INDEX(%s[%s],%s)),\"\")" lib.Name case m lib.Name case m)
                    | None -> ()
            | None -> ()
        | _ -> ()
        // restraints: keep support A, support B and the tip / support C row; clear everything else
        match table ws "Restraints_" with
        | Some lo when not (isNull lo.DataBodyRange) ->
            let body = lo.DataBodyRange
            let headers = [ for o in (lo.ListColumns :> Collections.IEnumerable) -> (o :?> ListColumn).Name.Trim() ]
            let col (h: string) = headers |> List.tryFindIndex (fun x -> x = h) |> Option.map (fun i -> i + 1)
            let set (row: int) (h: string) (v: obj) =
                match col h with
                | Some c -> (body.Cells.[row, c] :?> Range).Value2 <- v
                | None -> ()
            let overrides = headers |> List.filter (fun h -> h.StartsWith "αm" || h.StartsWith "kl")
            let tipCodes = if hasName ws "Span2" then "=IF(Overhang>0,\"U\",IF(Span2>0,\"F\",\"\"))" else "=IF(Overhang>0,\"U\",\"\")"
            for r in 1 .. body.Rows.Count do
                let x = formulaText (body.Cells.[r, 1] :?> Range)
                if x = "=0" then
                    set r "Ref" (box "A"); set r "Restraint provided by" (box "Left support – cleat")
                    set r "Top flange" (box "F"); set r "Bottom fl." (box "F")
                    for h in overrides do set r h null
                elif x = "=Span" then
                    set r "Ref" (box "B"); set r "Restraint provided by" (box "Support – cleat")
                    set r "Top flange" (box "F"); set r "Bottom fl." (box "F")
                    for h in overrides do set r h null
                elif x.StartsWith("=IF(Overhang", StringComparison.OrdinalIgnoreCase) then
                    for h in [ "Top flange"; "Bottom fl." ] do
                        match col h with
                        | Some c -> (body.Cells.[r, c] :?> Range).Formula <- box tipCodes
                        | None -> ()
                    for h in overrides do set r h null
                else
                    ws.Range(body.Cells.[r, 1], body.Cells.[r, body.Columns.Count]).ClearContents() |> ignore
        | _ -> ()

    let clearActiveSheetInputs () =
        let a = app ()
        match a.ActiveSheet with
        | :? Worksheet as ws when hasName ws "Span" && (table ws "Loads_" |> Option.bind (fun lo -> column lo "G")).IsSome ->
            let msg =
                sprintf "Start a fresh beam on sheet '%s'?\n\nThis clears the loads table and the intermediate flange restraints (and any αm / kl overrides), and resets the beam to a 5.0 m simply supported span. Section, grade, combinations and deflection criteria are kept.\n\nThis can't be undone." ws.Name
            let answer =
                System.Windows.Forms.MessageBox.Show(msg, "OAR Steel – Clear inputs", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Warning)
            if answer = System.Windows.Forms.DialogResult.OK then
                try
                    clearInputs ws
                    status a "Clear inputs – loads and intermediate restraints cleared; 5.0 m simply supported"
                with e ->
                    let why = if ws.ProtectContents then "the sheet is protected – unprotect it first" else e.Message
                    status a ("Clear inputs failed: " + why)
        | _ -> status a "Clear inputs: the active sheet is not an OAR steel beam sheet (v0.3 or later)"

    /// OAR > Review loads: the load / reaction / diagram window for the active beam sheet
    let reviewActiveSheet () =
        let a = app ()
        try
            match a.ActiveSheet with
            | :? Worksheet as ws ->
                ReviewWindow.show a.Hwnd ws
                status a "Review loads – window opened (Refresh from sheet re-reads the inputs)"
            | _ -> status a "Review loads: the active sheet is not a worksheet"
        with e ->
            status a ("Review loads failed: " + e.Message)

    [<ExcelCommand(Name = "OAR.ReviewLoads", Description = "Open the load review window for the active steel beam sheet")>]
    let reviewLoadsCommand () = reviewActiveSheet ()

    /// Macro behind the on-sheet "Clear inputs" button (and available as a command)
    [<ExcelCommand(Name = "OAR.ClearInputs", Description = "Clear the loads and intermediate restraints and reset to a 5 m simply supported beam")>]
    let clearInputsCommand () = clearActiveSheetInputs ()

    // keep the Application object alive so its event connections are not collected
    let mutable private hooked: Application = null

    let restoreWorkbook (wb: Workbook) =
        for ws in worksheets wb do
            safely (fun () -> restoreSheet ws)

    // Excel has no AfterPrint event: a macro queued from BeforePrint runs once Excel is idle again,
    // i.e. after the print / PDF export has finished, so the hidden rows never outlive the printout.
    let private onBeforePrint (wb: Workbook) =
        safely (fun () -> tidyWorkbook wb)
        safely (fun () -> ExcelAsyncUtil.QueueAsMacro(ExcelAction(fun () -> restoreWorkbook wb)))

    let private onSheetChange (sh: obj) =
        clearStatus ()
        // an open Review loads window follows the edit (any sheet: a linked reaction may sit on another tab)
        safely ReviewWindow.sheetChanged
        match sh with
        | :? Worksheet as ws -> safely (fun () -> restoreSheet ws)
        | _ -> ()

    let hook () =
        if isNull hooked then
            let a = app ()
            a.add_WorkbookBeforePrint(AppEvents_WorkbookBeforePrintEventHandler(fun wb _cancel -> onBeforePrint wb))
            a.add_SheetChange(AppEvents_SheetChangeEventHandler(fun sh _target -> onSheetChange sh))
            // an open Review loads window follows the beam tab in front
            a.add_SheetActivate(AppEvents_SheetActivateEventHandler(fun sh -> safely (fun () -> ReviewWindow.sheetActivated sh)))
            a.add_WorkbookActivate(
                AppEvents_WorkbookActivateEventHandler(fun wb -> safely (fun () -> ReviewWindow.sheetActivated wb.ActiveSheet))
            )
            hooked <- a

type AddIn() =
    interface IExcelAddIn with
        member _.AutoOpen() =
            ExcelAsyncUtil.QueueAsMacro(ExcelAction(fun () -> Layout.safely Layout.hook))

        member _.AutoClose() = ()

/// OAR tab: preview the tidy layout before printing, or bring the hidden rows back
[<ComVisible(true)>]
type Ribbon() =
    inherit ExcelRibbon()

    override _.GetCustomUI(_ribbonId: string) =
        """<customUI xmlns="http://schemas.microsoft.com/office/2009/07/customui">
  <ribbon>
    <tabs>
      <tab id="oarTab" label="OAR">
        <group id="oarSteelGroup" label="Steel beam">
          <button id="oarTidy" label="Tidy for print" onAction="OnTidy"
                  screentip="Hide unused rows in the combination, load, restraint and result tables on this sheet. Printing does this automatically and shows the rows again afterwards." />
          <button id="oarShowAll" label="Show all rows" onAction="OnShowAll"
                  screentip="Unhide every hidden row on this sheet." />
          <button id="oarReview" label="Review loads" onAction="OnReview"
                  screentip="Draw the beam with the loads of any load case or factored combination, the support reactions, and the shear, moment and deflection diagrams." />
          <separator id="oarSep1" />
          <button id="oarClear" label="Clear inputs" onAction="OnClear"
                  screentip="Start a fresh beam: clear the loads and intermediate restraints and reset to a 5 m simply supported span (asks first)." />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>"""

    member _.OnTidy(_control: IRibbonControl) = Layout.tidyActiveSheet ()
    member _.OnShowAll(_control: IRibbonControl) = Layout.restoreActiveSheet ()
    member _.OnClear(_control: IRibbonControl) = Layout.clearActiveSheetInputs ()
    member _.OnReview(_control: IRibbonControl) = Layout.reviewActiveSheet ()
