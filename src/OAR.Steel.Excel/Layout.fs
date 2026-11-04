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
        match sh with
        | :? Worksheet as ws -> safely (fun () -> restoreSheet ws)
        | _ -> ()

    let hook () =
        if isNull hooked then
            let a = app ()
            a.add_WorkbookBeforePrint(AppEvents_WorkbookBeforePrintEventHandler(fun wb _cancel -> onBeforePrint wb))
            a.add_SheetChange(AppEvents_SheetChangeEventHandler(fun sh _target -> onSheetChange sh))
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
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>"""

    member _.OnTidy(_control: IRibbonControl) = Layout.tidyActiveSheet ()
    member _.OnShowAll(_control: IRibbonControl) = Layout.restoreActiveSheet ()
