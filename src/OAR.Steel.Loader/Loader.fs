/// OAR Steel Loader: the small add-in Excel registers. Every time Excel starts it checks the office share
/// for the approved OAR Steel version, copies it into its own version folder under %LOCALAPPDATA%\OAR if it
/// isn't there yet, and loads it. Each version lives in its own folder, so a copy Excel has open never
/// blocks an update (the reason the add-in can't simply be overwritten in place).
///
/// Share layout (from the release zip):  <share>\VERSION.txt              e.g. "0.8.6" – the approved version
///                                       <share>\OAR.Steel-AddIn64.xll    (and -AddIn32.xll)
///                                       <share>\Releases\v0.8.6\...      archive copies
/// Local layout:                         %LOCALAPPDATA%\OAR\versions\v0.8.6\OAR.Steel-AddIn64.xll
///                                       %LOCALAPPDATA%\OAR\versions\current.txt   last version loaded
///                                       %LOCALAPPDATA%\OAR\loader.log
namespace OAR.Steel.Loader

open System
open System.IO
open System.Threading.Tasks
open ExcelDna.Integration

/// The decisions, kept free of files and Excel so they can be checked on their own
module Logic =

    /// "v0.8.6", "0.8.6" or "0.8.6\r\n" -> 0.8.6
    let parseVersion (s: string) =
        let t = (if isNull s then "" else s).Trim().TrimStart('v', 'V')
        match Version.TryParse t with
        | true, v -> Some v
        | _ -> None

    /// Which version to load:
    /// - the share's approved version when the share was reached (also allows a roll-back: an older zip
    ///   extracted over the share makes that version the approved one again);
    /// - otherwise the version loaded last time, if it is still on this PC;
    /// - otherwise the newest version on this PC.
    let choose (approved: Version option) (local: Version list) (last: Version option) =
        match approved with
        | Some v -> Some v
        | None ->
            match last with
            | Some v when List.contains v local -> Some v
            | _ -> if local.IsEmpty then None else Some(List.max local)

    /// Local version folders to delete: everything except the newest `keep` and the one in use
    let toRemove (keep: int) (inUse: Version option) (local: Version list) =
        let newest = local |> List.sortDescending |> List.truncate keep
        local |> List.filter (fun v -> not (List.contains v newest) && Some v <> inUse)

module private Run =

    let shares =
        [ @"\\LOA-TrueNAS\Data\Programs\Spreadsheets\Add-Ins\OAR Steel"
          @"X:\Programs\Spreadsheets\Add-Ins\OAR Steel" ]

    let root = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData, "OAR")
    let versions = Path.Combine(root, "versions")
    let logFile = Path.Combine(root, "loader.log")
    let fileName = if Environment.Is64BitProcess then "OAR.Steel-AddIn64.xll" else "OAR.Steel-AddIn32.xll"

    let log (msg: string) =
        try
            Directory.CreateDirectory root |> ignore
            if File.Exists logFile && FileInfo(logFile).Length > 256L * 1024L then File.Delete logFile
            File.AppendAllText(logFile, sprintf "%s  %s%s" (DateTime.Now.ToString "yyyy-MM-dd HH:mm:ss") msg Environment.NewLine)
        with _ ->
            ()

    let folderOf (v: Version) = Path.Combine(versions, "v" + v.ToString())

    /// Versions on this PC that have the add-in file for this Excel (32 or 64-bit)
    let localVersions () =
        if Directory.Exists versions then
            Directory.GetDirectories versions
            |> Array.choose (fun d -> Logic.parseVersion (Path.GetFileName d))
            |> Array.filter (fun v -> File.Exists(Path.Combine(folderOf v, fileName)))
            |> List.ofArray
        else
            []

    /// The share's approved version and the file for it, or None when no share answers in time.
    /// Older releases have no VERSION.txt: then the newest Releases\vX folder is used.
    let fromShare () : (Version * string) option =
        let look () =
            shares
            |> List.tryPick (fun share ->
                if not (Directory.Exists share) then
                    None
                else
                    let versionFile = Path.Combine(share, "VERSION.txt")
                    let top = Path.Combine(share, fileName)
                    match (if File.Exists versionFile then Logic.parseVersion (File.ReadAllText versionFile) else None) with
                    | Some v when File.Exists top -> Some(v, top)
                    | _ ->
                        let rel = Path.Combine(share, "Releases")
                        if Directory.Exists rel then
                            Directory.GetDirectories rel
                            |> Array.choose (fun d ->
                                match Logic.parseVersion (Path.GetFileName d) with
                                | Some v when File.Exists(Path.Combine(d, fileName)) -> Some(v, Path.Combine(d, fileName))
                                | _ -> None)
                            |> Array.sortByDescending fst
                            |> Array.tryHead
                        else
                            None)
        // a share that isn't reachable (laptop away from the office) can take a long time to fail:
        // don't hold Excel's start-up for more than a few seconds
        let task = Task.Run(fun () -> look ())
        if task.Wait(TimeSpan.FromSeconds 6.0) then task.Result else None

    /// Copy a version from the share into its own folder (via a temporary name, so a half-copied file is never loaded)
    let install (v: Version) (source: string) =
        let dir = folderOf v
        let target = Path.Combine(dir, fileName)
        if not (File.Exists target) then
            Directory.CreateDirectory dir |> ignore
            let tmp = target + ".copying"
            File.Copy(source, tmp, true)
            File.Move(tmp, target)
            log (sprintf "copied v%O from %s" v source)

    let readLast () =
        try
            Logic.parseVersion (File.ReadAllText(Path.Combine(versions, "current.txt")))
        with _ ->
            None

    let writeLast (v: Version) =
        try
            File.WriteAllText(Path.Combine(versions, "current.txt"), v.ToString())
        with _ ->
            ()

    let status (text: string) =
        ExcelAsyncUtil.QueueAsMacro(
            ExcelAction(fun () ->
                try
                    let app = ExcelDnaUtil.Application
                    app.GetType().InvokeMember("StatusBar", Reflection.BindingFlags.SetProperty, null, app, [| box text |]) |> ignore
                with _ ->
                    ())
        )

    let start () =
        try
            let approved = fromShare ()
            match approved with
            | Some(v, source) ->
                try
                    install v source
                with e ->
                    log (sprintf "could not copy v%O from %s: %s" v source e.Message)
            | None -> log "office share not reachable – using the add-in already on this PC"
            let local = localVersions ()
            let last = readLast ()
            let approvedHere = approved |> Option.map fst |> Option.filter (fun v -> List.contains v local)
            match Logic.choose approvedHere local last with
            | Some v ->
                let path = Path.Combine(folderOf v, fileName)
                ExcelIntegration.RegisterXLL path |> ignore
                log (sprintf "loaded v%O (%s)" v path)
                if last.IsSome && last <> Some v then status (sprintf "OAR Steel updated to v%O" v)
                writeLast v
                for old in Logic.toRemove 3 (Some v) local do
                    try
                        Directory.Delete(folderOf old, true)
                        log (sprintf "removed old v%O" old)
                    with _ ->
                        () // still open in another Excel window: try again next time
            | None ->
                // nothing in the version folders: the copy the old login script installed, if it is there
                let legacy = Path.Combine(root, fileName)
                if File.Exists legacy then
                    ExcelIntegration.RegisterXLL legacy |> ignore
                    log (sprintf "loaded %s (no version folders yet)" legacy)
                else
                    log "no OAR Steel add-in found on the share or this PC"
                    status "OAR Steel: add-in not found – check the office share is reachable"
        with e ->
            log ("loader failed: " + e.ToString())

type LoaderAddIn() =
    interface IExcelAddIn with
        member _.AutoOpen() = Run.start ()
        member _.AutoClose() = ()
