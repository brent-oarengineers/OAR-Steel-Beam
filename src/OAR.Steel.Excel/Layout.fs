/// Print tidy-up: hides the unused rows of variable-length blocks (combinations, loads, restraints,
/// ULS / shear / reaction spills) when a sheet is printed or saved as PDF, so the printout has no gaps.
///
/// A block is a sheet-scoped name starting "OAR_Flex" that refers to one column of key cells
/// (e.g. the ID column of the combinations spill). A row whose key cell shows nothing is hidden.
/// The rows come back on the next edit to that sheet, or with OAR > Show all rows.
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
            let rows = rng.EntireRow
            let allShown =
                match rows.Hidden with
                | :? bool as h -> not h
                | _ -> false // mixed
            if not allShown then rows.Hidden <- box false

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

    let tidyActiveSheet () =
        match app().ActiveSheet with
        | :? Worksheet as ws -> safely (fun () -> tidySheet ws)
        | _ -> ()

    let restoreActiveSheet () =
        match app().ActiveSheet with
        | :? Worksheet as ws -> safely (fun () -> restoreSheet ws)
        | _ -> ()

    // keep the Application object alive so its event connections are not collected
    let mutable private hooked: Application = null

    let private onBeforePrint (wb: Workbook) =
        safely (fun () -> tidyWorkbook wb)

    let private onSheetChange (sh: obj) =
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
                  screentip="Hide unused rows in the combination, load, restraint and result tables on this sheet. Printing does this automatically; your next edit brings the rows back." />
          <button id="oarShowAll" label="Show all rows" onAction="OnShowAll"
                  screentip="Unhide the rows hidden for printing on this sheet." />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>"""

    member _.OnTidy(_control: IRibbonControl) = Layout.tidyActiveSheet ()
    member _.OnShowAll(_control: IRibbonControl) = Layout.restoreActiveSheet ()
