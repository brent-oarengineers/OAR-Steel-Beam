namespace OAR.Steel

/// Load review: the picture behind OAR > Review loads. Everything here is plain data (lines, polygons and
/// text in pixel coordinates) so it can be tested without Excel; the add-in window only paints it.

type Align =
    | Left
    | Centre
    | Right

/// A drawing instruction. Coordinates in pixels, y down. Colours are "#RRGGBB".
type Prim =
    | PLine of x1: float * y1: float * x2: float * y2: float * colour: string * width: float * dashed: bool
    /// stroke "" = no outline
    | PPolygon of pts: (float * float) list * fill: string * opacity: float * stroke: string * width: float
    | PPolyline of pts: (float * float) list * colour: string * width: float
    /// y is the vertical centre of the text
    | PText of x: float * y: float * text: string * colour: string * size: float * align: Align * bold: bool

/// One entry in the list on the left of the window: a load case, a serviceability case, a combination
/// (one Combination) or the envelope of the strength combinations (several).
type ReviewItem =
    { Id: string
      Group: string
      Description: string
      Combos: Combination list
      IsEnvelope: bool
      /// true when the deflection shown is a serviceability value (unfactored / SLS loads)
      Service: bool }

type ReviewView =
    { Loads: bool
      Reactions: bool
      Shear: bool
      Moment: bool
      Deflection: bool
      /// the flange restraints strip (F / P / L / U / C above and below a beam line)
      Restraints: bool }

type ReviewData =
    { Title: string
      Beam: Beam
      Loads: Load list
      Items: ReviewItem list
      /// the restraints table as entered (empty when the sheet has none or it could not be read)
      Restraints: RestraintPoint list
      /// flange continuously restrained over the whole beam (factor box): top, bottom
      ContinuousTop: bool
      ContinuousBottom: bool
      /// why the restraints could not be read, "" when they were
      RestraintsNote: string }

/// One combination solved: the beam made determinate (interior reactions added as point loads)
type ReviewCase =
    { Combo: Combination
      Det: Beam
      /// applied actions plus the interior support reactions
      All: BeamLoad list
      /// applied actions only
      Applied: BeamLoad list
      /// (x mm, R N) at every support, positive upward
      Reactions: (float * float) list }

type ReviewSolution =
    { Item: ReviewItem
      Cases: ReviewCase list
      /// stations along the beam, mm (uniform, plus load points, supports and points of zero shear)
      Xs: float list }

type Scene =
    { Width: float
      Height: float
      Prims: Prim list
      /// pixel x of the left and right ends of the beam, and its length in mm (for the hover readout)
      X0: float
      X1: float
      Length: float }

module Review =

    let private caseOrder =
        function
        | G -> 0
        | Q -> 1
        | Wd -> 2
        | Wid -> 3
        | Wu -> 4
        | Wiu -> 5

    let caseColour =
        function
        | G -> "#465A52"
        | Q -> "#B07D2B"
        | Wd -> "#2F6EBA"
        | Wid -> "#2A8C8C"
        | Wu -> "#7B5EA7"
        | Wiu -> "#B0508A"

    let private caseTitle =
        function
        | G -> "Dead load G (unfactored, incl. self-weight if on)"
        | Q -> "Live load Q (unfactored)"
        | Wd -> "Wind down Wd (ultimate, unfactored)"
        | Wu -> "Wind up Wu (ultimate, unfactored)"
        | Wid -> "Internal pressure down Wid (ultimate, unfactored)"
        | Wiu -> "Internal pressure up Wiu (ultimate, unfactored)"

    /// The list of things that can be reviewed. `combos` = the active combinations already expanded with
    /// their pattern variants (and the equilibrium combination, if any).
    let items (loads: Load list) (combos: Combination list) (psiL: float) (psiS: float) (windFactor: float) : ReviewItem list =
        let has lc = loads |> List.exists (fun l -> l.Case = lc && l.Magnitude <> 0.0)
        let hasIn lc (d: string) =
            loads
            |> List.exists (fun l -> l.Case = lc && l.Magnitude <> 0.0 && (WindDir.normalise l.Dir = "" || WindDir.same l.Dir d))
        let dirs = WindDir.distinct (loads |> List.filter (fun l -> LoadCase.isWind l.Case) |> List.map (fun l -> l.Dir))
        let one group service (c: Combination) : ReviewItem =
            { Id = c.Id
              Group = group
              Description = c.Description
              Combos = [ c ]
              IsEnvelope = false
              Service = service }
        let windCases = [ Wd; Wid; Wu; Wiu ] |> List.filter has
        let cases =
            [ for lc in [ G; Q ] do
                  if has lc then yield one "Load cases" true { Combination.single lc with Description = caseTitle lc }
              if dirs.IsEmpty then
                  for lc in windCases do
                      yield one "Load cases" false { Combination.single lc with Description = caseTitle lc }
              else
                  for d in dirs do
                      for lc in windCases do
                          if hasIn lc d then
                              yield one "Load cases" false { Combination.singleDir lc d with Description = caseTitle lc + ", " + d } ]
        let sls =
            [ if has Q then
                  yield
                      one "Serviceability" true
                          { Combination.ofFactors "G+ψl·Q" [ G, 1.0; Q, psiL ] with
                              Description = sprintf "Long term: G + %gQ (Q on the whole beam)" psiL }
                  yield
                      one "Serviceability" true
                          { Combination.ofFactors "G+ψs·Q" [ G, 1.0; Q, psiS ] with
                              Description = sprintf "Short term: G + %gQ (Q on the whole beam)" psiS }
              if windFactor > 0.0 then
                  for d in (if dirs.IsEmpty then [ "" ] else dirs) do
                      for name, lcs in [ "Ws down", [ Wd; Wid ]; "Ws up", [ Wu; Wiu ] ] do
                          if lcs |> List.exists (fun lc -> hasIn lc d) then
                              let id = if d = "" then name else name + " " + d
                              yield
                                  one "Serviceability" true
                                      { Combination.ofFactors id (lcs |> List.map (fun lc -> lc, windFactor)) with
                                          Dir = d
                                          Description =
                                              sprintf "Wind serviceability: %g × (%s)%s" windFactor
                                                  (String.concat " + " (lcs |> List.filter has |> List.map LoadCase.name))
                                                  (if d = "" then "" else ", " + d) } ]
        let strength = combos |> List.filter (fun c -> c.Strength)
        let envelope =
            if strength.Length < 2 then
                []
            else
                [ { Id = "Envelope"
                    Group = "Combinations"
                    Description = sprintf "Envelope of the %d active strength combinations (incl. patterns)" strength.Length
                    Combos = strength
                    IsEnvelope = true
                    Service = false } ]
        cases @ sls @ envelope @ (combos |> List.map (one "Combinations" false))

    let private solveCase (beam: Beam) (loads: Load list) (c: Combination) : ReviewCase =
        let acts = Analysis.actions beam.Span c loads
        let interior = Analysis.interiorReactions beam acts
        { Combo = c
          Det = { beam with Interior = [] }
          All = acts @ List.map2 (fun x r -> Conc(-r, x)) beam.Interior interior
          Applied = acts
          Reactions = Analysis.supportReactions beam acts }

    let private eps = 1e-3

    /// Root of v between a and b (v changes sign), by bisection
    let private bisect (v: float -> float) (a: float) (b: float) (va: float) =
        let rec go lo hi vlo i =
            if i = 0 then
                (lo + hi) / 2.0
            else
                let mid = (lo + hi) / 2.0
                let vm = v mid
                if vlo * vm <= 0.0 then go lo mid vlo (i - 1) else go mid hi vm (i - 1)
        go a b va 40

    /// Solve an item: every combination in it, and the stations used for the diagrams
    let solve (data: ReviewData) (item: ReviewItem) : ReviewSolution =
        let beam = data.Beam
        let l = beam.Span
        let cases = item.Combos |> List.map (solveCase beam data.Loads)
        let n = 240
        let uniform = [ for i in 0..n -> l * float i / float n ]
        let breaks =
            cases
            |> List.collect (fun c -> Analysis.breakpointsOn beam c.Applied)
            |> List.filter (fun x -> x >= 0.0 && x <= l)
            |> List.collect (fun x -> [ max 0.0 (x - eps); x ])
        let basic = (uniform @ breaks) |> List.distinct |> List.sort
        // points of zero shear (moment peaks), by bisection between stations where V changes sign
        let roots =
            [ for c in cases do
                  let v = Analysis.shear c.Det c.All
                  for a, b in List.pairwise basic do
                      let va, vb = v a, v b
                      if b - a > 2.0 * eps && va * vb < 0.0 then yield bisect v a b va ]
        { Item = item
          Cases = cases
          Xs = (basic @ roots) |> List.distinct |> List.sort }

    /// (min, max) over the cases of a result at x: shear kN, moment kN·m (sagging +), deflection mm (down +)
    let private range (sol: ReviewSolution) (f: ReviewCase -> float -> float) (x: float) =
        let vs = sol.Cases |> List.map (fun c -> f c x)
        List.min vs, List.max vs

    let private shearAt (c: ReviewCase) x = Units.toKN (Analysis.shear c.Det c.All x)
    let private momentAt (c: ReviewCase) x = Units.toKNm (Analysis.moment c.Det c.All x)
    let private deflAt (c: ReviewCase) x = Analysis.deflection c.Det c.All x

    /// Number with about three significant figures (kN, kN·m, mm, kN/m)
    let fmt (v: float) =
        let a = abs v
        let s =
            if a >= 99.95 then sprintf "%.0f" v
            elif a >= 9.995 then sprintf "%.1f" v
            else sprintf "%.2f" v
        if s = "-0.00" || s = "-0.0" || s = "-0" then s.Substring 1 else s

    /// Text for the hover readout at x (mm)
    let readout (sol: ReviewSolution) (x: float) =
        if sol.Cases.IsEmpty then
            ""
        else
            let x = max 0.0 (min x (List.last sol.Xs))
            let show (lo: float, hi: float) = if sol.Item.IsEnvelope then sprintf "%s to %s" (fmt lo) (fmt hi) else fmt hi
            sprintf "x = %.3f m      V = %s kN      M = %s kN·m      δ = %s mm" (x / 1000.0) (show (range sol shearAt x))
                (show (range sol momentAt x)) (show (range sol deflAt x))

    // ---------------------------------------------------------------- drawing

    let private ink = "#222222"
    let private muted = "#777777"
    let private grid = "#C8C8C8"
    let private red = "#C0392B"
    let private beamColour = "#2B2B2B"

    /// rough text width in pixels (the window font is close to 0.56 em per character)
    let private textWidth (size: float) (s: string) = float s.Length * size * 0.74

    /// Arrow from (x, y1) to (x, y2), head at (x, y2)
    let private arrow (x: float) (y1: float) (y2: float) (colour: string) (width: float) (head: float) =
        let dir = if y2 > y1 then 1.0 else -1.0
        [ PLine(x, y1, x, y2 - dir * head * 0.6, colour, width, false)
          PPolygon([ x, y2; x - head * 0.5, y2 - dir * head; x + head * 0.5, y2 - dir * head ], colour, 1.0, "", 0.0) ]

    /// One input load as drawn for a combination: its factored pieces
    type private Drawn =
        { Load: Load
          /// distributed pieces (w N/mm signed +down, a, b) and point pieces (P N signed +down, x)
          Dists: (float * float * float) list
          Points: (float * float) list }

    let private drawnLoads (data: ReviewData) (c: Combination) : Drawn list =
        data.Loads
        |> List.indexed
        |> List.sortBy (fun (i, l) -> caseOrder l.Case, i)
        |> List.map (fun (_, ld) ->
            let acts = Analysis.actions data.Beam.Span c [ ld ]
            // pieces split at the supports for a pattern are joined again when they carry the same load
            let dists =
                acts
                |> List.choose (function
                    | Dist(w, a, b) -> Some(w, a, b)
                    | _ -> None)
                |> List.fold
                    (fun acc (w, a, b) ->
                        match acc with
                        | (w0, a0, b0) :: rest when abs (w0 - w) < 1e-12 && abs (b0 - a) < 1e-6 -> (w0, a0, b) :: rest
                        | _ -> (w, a, b) :: acc)
                    []
                |> List.rev
            { Load = ld
              Dists = dists
              Points =
                acts
                |> List.choose (function
                    | Conc(p, x) -> Some(p, x)
                    | _ -> None) })
        |> List.filter (fun d -> not d.Dists.IsEmpty || not d.Points.IsEmpty)

    /// Label for a factored value: "1.4 × 70.0 = 98.0 kN/m" when a factor applies
    let private valueText (factored: float) (unfactored: float) (unit: string) =
        let f = if unfactored = 0.0 then 1.0 else abs factored / abs unfactored
        if abs (f - 1.0) < 1e-9 then
            sprintf "%s %s" (fmt (abs factored)) unit
        else
            sprintf "%g × %s = %s %s" (System.Math.Round(f, 3)) (fmt (abs unfactored)) (fmt (abs factored)) unit

    /// The picture for one item at the given window size
    let scene (data: ReviewData) (sol: ReviewSolution) (view: ReviewView) (width: float) (height: float) : Scene =
        let beam = data.Beam
        let l = beam.Span
        let item = sol.Item
        let x0, x1 = 70.0, max 200.0 (width - 70.0)
        let px (x: float) = x0 + (x1 - x0) * x / l
        let supports = Beam.supports beam
        let prims = ResizeArray<Prim>()
        let add (p: Prim) = prims.Add p
        let addAll (ps: Prim list) = prims.AddRange ps

        // ---- header
        add (PText(14.0, 16.0, sprintf "%s   –   %s" item.Id data.Title, ink, 11.0, Left, true))
        let totalText =
            match sol.Cases with
            | [ c ] when not item.IsEnvelope ->
                let total =
                    c.Applied
                    |> List.sumBy (function
                        | Dist(w, a, b) -> w * (b - a)
                        | Conc(p, _) -> p)
                let rsum = c.Reactions |> List.sumBy snd
                sprintf "   ·   total load %s kN %s   ·   Σ reactions %s kN" (fmt (Units.toKN (abs total)))
                    (if total >= 0.0 then "down" else "up") (fmt (Units.toKN rsum))
            | _ -> ""
        add (PText(14.0, 34.0, item.Description + totalText, muted, 9.0, Left, false))

        // ---- applied loads (one combination only; an envelope has no single load picture)
        let drawn =
            match sol.Cases with
            | [ c ] when view.Loads && not item.IsEnvelope -> drawnLoads data c.Combo
            | _ -> []
        // distributed blocks: stack per side (down loads above the beam, up loads below), heights in pixels
        let allDists = drawn |> List.collect (fun d -> d.Dists |> List.map (fun p -> d, p))
        let stackMax (side: float) =
            // largest summed intensity on one side, for the height scale
            let ps = allDists |> List.filter (fun (_, (w, _, _)) -> w * side > 0.0)
            let xs = ps |> List.collect (fun (_, (_, a, b)) -> [ a; b ]) |> List.distinct |> List.sort
            xs
            |> List.pairwise
            |> List.map (fun (a, b) ->
                let m = (a + b) / 2.0
                ps |> List.sumBy (fun (_, (w, pa, pb)) -> if m > pa && m < pb then abs w else 0.0))
            |> List.fold max 0.0
        let wMax = max (stackMax 1.0) (stackMax -1.0)
        let minBlock = 17.0
        let blockScale = if wMax > 0.0 then 96.0 / wMax else 1.0
        // place the blocks: (drawn, w, a, b, base px, top px) with base / top measured away from the beam
        let place (side: float) =
            allDists
            |> List.filter (fun (_, (w, _, _)) -> w * side > 0.0)
            |> List.fold
                (fun (placed: (Drawn * float * float * float * float * float) list) (d, (w, a, b)) ->
                    let baseH =
                        placed
                        |> List.filter (fun (_, _, pa, pb, _, _) -> pa < b - 1e-6 && pb > a + 1e-6)
                        |> List.map (fun (_, _, _, _, _, top) -> top)
                        |> List.fold max 0.0
                    placed @ [ d, w, a, b, baseH, baseH + max minBlock (abs w * blockScale) ])
                []
        let squash (blocks: (Drawn * float * float * float * float * float) list) =
            let top = blocks |> List.map (fun (_, _, _, _, _, t) -> t) |> List.fold max 0.0
            let k = if top > 150.0 then 150.0 / top else 1.0
            blocks |> List.map (fun (d, w, a, b, b0, t) -> d, w, a, b, b0 * k, t * k)
        let downBlocks = squash (place 1.0)
        let upBlocks = squash (place -1.0)
        let stackTop (blocks: (Drawn * float * float * float * float * float) list) (x: float) =
            blocks
            |> List.filter (fun (_, _, a, b, _, _) -> x >= a - 1e-6 && x <= b + 1e-6)
            |> List.map (fun (_, _, _, _, _, t) -> t)
            |> List.fold max 0.0
        // point loads: arrow length by size, tail clear of the blocks, labels stepped where they are close
        let allPoints = drawn |> List.collect (fun d -> d.Points |> List.map (fun (p, x) -> d, p, x))
        let pMax = allPoints |> List.map (fun (_, p, _) -> abs p) |> List.fold max 0.0
        let placePoints (side: float) (blocks: (Drawn * float * float * float * float * float) list) =
            allPoints
            |> List.filter (fun (_, p, _) -> p * side > 0.0)
            |> List.sortBy (fun (_, _, x) -> x)
            |> List.fold
                (fun (placed: (Drawn * float * float * float * float * string) list) (d, p, x) ->
                    let tail = stackTop blocks x + 26.0 + 40.0 * abs p / pMax
                    let unf = d.Load.Magnitude * LoadCase.direction d.Load.Case
                    let value = valueText (Units.toKN p) (Units.toKN unf) "kN"
                    let text = if d.Load.Description.Trim() = "" then value else d.Load.Description.Trim() + ":  " + value
                    let text = if textWidth 8.5 text > 250.0 then value else text
                    // label height above the tail: step up while it would sit on a neighbour's label
                    let clash (lab: float) =
                        placed
                        |> List.exists (fun (_, _, x0, _, lab0, t0) ->
                            abs (px x0 - px x) < (textWidth 8.5 t0 + textWidth 8.5 text) / 2.0 + 8.0 && abs (lab0 - lab) < 12.0)
                    let rec free (lab: float) = if clash lab then free (lab + 13.0) else lab
                    placed @ [ d, p, x, tail, free (tail + 9.0), text ])
                []
        let downPoints = placePoints 1.0 downBlocks
        let upPoints = placePoints -1.0 upBlocks
        let extent (blocks: (Drawn * float * float * float * float * float) list) (points: (Drawn * float * float * float * float * string) list) =
            max (blocks |> List.map (fun (_, _, _, _, _, t) -> t) |> List.fold max 0.0)
                (points |> List.map (fun (_, _, _, _, lab, _) -> lab + 8.0) |> List.fold max 0.0)
        let downH = extent downBlocks downPoints
        let upH = extent upBlocks upPoints

        let envelopeNote = view.Loads && item.IsEnvelope
        let beamY = 52.0 + (if envelopeNote then 22.0 else downH) + 6.0
        let drawBlocks (side: float) (blocks: (Drawn * float * float * float * float * float) list) =
            for d, w, a, b, b0, t in blocks do
                let colour = caseColour d.Load.Case
                let xa, xb = px a, px b
                let yIn, yOut = beamY - side * b0, beamY - side * t
                add (PPolygon([ xa, yIn; xb, yIn; xb, yOut; xa, yOut ], colour, 0.22, colour, 1.0))
                // label, longest version that fits
                let unf = d.Load.Magnitude * LoadCase.direction d.Load.Case
                let value = valueText w unf "kN/m"
                let room = xb - xa - 10.0
                let label =
                    [ (if d.Load.Description.Trim() = "" then value else d.Load.Description.Trim() + ":  " + value)
                      value
                      fmt (abs w) + " kN/m" ]
                    |> List.tryFind (fun s -> textWidth 8.5 s <= room)
                let labelHalf =
                    match label with
                    | Some s when t - b0 >= 12.0 ->
                        add (PText((xa + xb) / 2.0, (yIn + yOut) / 2.0, s, ink, 8.5, Centre, false))
                        textWidth 8.5 s / 2.0 + 6.0
                    | _ -> 0.0
                // small arrows along the block showing which way it acts
                let n = max 1 (int ((xb - xa) / 34.0))
                for i in 0..n do
                    let x = xa + (xb - xa) * float i / float n
                    if abs (x - (xa + xb) / 2.0) > labelHalf then
                        addAll (arrow x yOut yIn colour 1.0 5.0)
        drawBlocks 1.0 downBlocks
        drawBlocks -1.0 upBlocks
        let drawPoints (side: float) (points: (Drawn * float * float * float * float * string) list) =
            for d, _, x, tail, lab, text in points do
                let colour = caseColour d.Load.Case
                addAll (arrow (px x) (beamY - side * tail) (beamY - side * 3.0) colour 2.0 9.0)
                if lab > tail + 10.0 then
                    add (PLine(px x, beamY - side * tail, px x, beamY - side * (lab - 6.0), grid, 1.0, true))
                let half = textWidth 8.5 text / 2.0
                let cx = max (8.0 + half) (min (width - 8.0 - half) (px x))
                add (PText(cx, beamY - side * lab, text, ink, 8.5, Centre, false))
        drawPoints 1.0 downPoints
        drawPoints -1.0 upPoints
        // case colours used, top right
        if not drawn.IsEmpty then
            let used = drawn |> List.map (fun d -> d.Load.Case) |> List.distinct |> List.sortBy caseOrder
            let mutable x = width - 14.0
            for lc in List.rev used do
                let name = LoadCase.name lc
                add (PText(x, 16.0, name, ink, 9.0, Right, false))
                x <- x - textWidth 9.0 name - 6.0
                add (PPolygon([ x - 10.0, 11.0; x, 11.0; x, 21.0; x - 10.0, 21.0 ], caseColour lc, 0.45, caseColour lc, 1.0))
                x <- x - 22.0
        elif envelopeNote then
            add (PText((x0 + x1) / 2.0, beamY - 14.0, "Envelope: pick a single combination to see its loads", muted, 9.0, Centre, false))

        // ---- beam and supports
        add (PLine(px 0.0, beamY, px l, beamY, beamColour, 4.0, false))
        for s in supports do
            add (PPolygon([ px s, beamY + 2.0; px s - 8.0, beamY + 15.0; px s + 8.0, beamY + 15.0 ], "#FFFFFF", 1.0, beamColour, 1.5))

        // ---- reactions and support letters
        let below = beamY + max upH 15.0 + 6.0
        let letters = [ "A"; "B"; "C"; "D"; "E"; "F" ]
        let reactionRange (i: int) =
            let rs = sol.Cases |> List.map (fun c -> snd c.Reactions.[i])
            List.min rs, List.max rs
        let yr = below
        let mutable y = below
        if view.Reactions && not sol.Cases.IsEmpty then
            let y = yr
            supports
            |> List.iteri (fun i s ->
                let lo, hi = reactionRange i
                let big = if abs lo > abs hi then lo else hi
                let colour = if big < 0.0 then red else ink
                if abs big > 0.5 then
                    if big > 0.0 then addAll (arrow (px s) (y + 26.0) (y + 2.0) colour 2.0 9.0)
                    else addAll (arrow (px s) (y + 2.0) (y + 26.0) colour 2.0 9.0)
                let text =
                    if item.IsEnvelope then
                        sprintf "%s = %s to %s kN" letters.[i] (fmt (Units.toKN lo)) (fmt (Units.toKN hi))
                    else
                        sprintf "%s = %s kN%s" letters.[i] (fmt (Units.toKN hi)) (if hi < -0.5 then " (hold-down)" else "")
                let half = textWidth 9.0 text / 2.0
                let cx = max (8.0 + half) (min (width - 8.0 - half) (px s))
                add (PText(cx, y + 36.0, text, colour, 9.0, Centre, true)))
        else
            supports |> List.iteri (fun i s -> add (PText(px s, yr + 6.0, letters.[i], ink, 9.0, Centre, true)))
        y <- (if view.Reactions && not sol.Cases.IsEmpty then yr + 48.0 else yr + 16.0)

        // ---- dimensions
        let marks = (0.0 :: l :: supports) |> List.distinct |> List.sort
        add (PLine(px 0.0, y + 8.0, px l, y + 8.0, muted, 1.0, false))
        for m in marks do
            add (PLine(px m, y + 3.0, px m, y + 13.0, muted, 1.0, false))
        for a, b in List.pairwise marks do
            let text = (b - a).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)
            if px b - px a > textWidth 8.5 text + 8.0 then
                add (PText((px a + px b) / 2.0, y + 18.0, text, muted, 8.5, Centre, false))
        y <- y + 30.0

        // ---- flange restraints as entered: codes above the line for the top flange, below for the bottom
        if view.Restraints && (not data.Restraints.IsEmpty || data.RestraintsNote <> "" || data.ContinuousTop || data.ContinuousBottom) then
            let yb = y + 37.0
            add (PText(14.0, y + 9.0, "Flange restraints", ink, 9.0, Left, true))
            add (PText(124.0, y + 9.0, "F full  ·  P partial  ·  L lateral  ·  U unrestrained  ·  C continuous to the next restraint", muted, 8.5, Left, false))
            add (PLine(px 0.0, yb, px l, yb, beamColour, 2.5, false))
            add (PText(x0 - 16.0, yb - 12.0, "top", muted, 8.5, Right, false))
            add (PText(x0 - 16.0, yb + 12.0, "bottom", muted, 8.5, Right, false))
            if data.RestraintsNote <> "" then
                add (PText((x0 + x1) / 2.0, yb - 12.0, "Restraints table: " + data.RestraintsNote, red, 8.5, Centre, false))
            let codeColour =
                function
                | RF -> ink
                | RP -> "#555555"
                | RL -> "#2F6EBA"
                | RU -> red
                | RC -> "#B07D2B"
            let pts = data.Restraints |> List.filter (fun p -> p.X >= -1e-6 && p.X <= l + 1e-6) |> List.sortBy (fun p -> p.X)
            for side, pick, whole in [ -1.0, (fun (p: RestraintPoint) -> p.Top), data.ContinuousTop; 1.0, (fun (p: RestraintPoint) -> p.Bottom), data.ContinuousBottom ] do
                let flange = pts |> List.choose (fun p -> pick p |> Option.map (fun t -> p.X, t))
                let band (a: float) (b: float) =
                    add (PLine(px a, yb + side * 4.0, px b, yb + side * 4.0, codeColour RC, 3.0, false))
                if whole then
                    band 0.0 l
                    add (PText((x0 + x1) / 2.0, yb + side * 12.0, "continuously restrained (factor box)", codeColour RC, 8.5, Centre, true))
                else
                    let nexts = if flange.IsEmpty then [] else (flange.Tail |> List.map Some) @ [ None ]
                    for (x, t), next in List.zip flange nexts do
                        add (PLine(px x, yb, px x, yb + side * 5.0, codeColour t, 1.5, false))
                        add (PText(px x, yb + side * 12.0, RestraintType.code t, codeColour t, 9.5, Centre, true))
                        match t, next with
                        | RC, Some(x2, _) -> band x x2
                        | _ -> ()
            y <- y + 68.0

        // ---- diagrams
        let panels =
            [ if view.Shear then yield "Shear (kN)", shearAt, false, "#5B7A8C"
              if view.Moment then yield "Bending moment (kN·m, sagging plotted down)", momentAt, true, "#465A52"
              if view.Deflection then
                  yield
                      (if item.Service then "Deflection (mm, plotted downward)"
                       else "Deflection under these loads (mm, plotted downward) – not a serviceability value"),
                      deflAt,
                      true,
                      "#8A6D3B" ]
        let y = y
        if not panels.IsEmpty && not sol.Cases.IsEmpty then
            let hp = max 70.0 ((height - 10.0 - y) / float panels.Length)
            panels
            |> List.iteri (fun k (title, f, plotDown, colour) ->
                let yt = y + hp * float k
                add (PText(14.0, yt + 9.0, title, ink, 9.0, Left, true))
                let ya, yb = yt + 34.0, yt + hp - 20.0
                let pts = sol.Xs |> List.map (fun x -> x, range sol f x)
                let vmin = min 0.0 (pts |> List.map (fun (_, (lo, _)) -> lo) |> List.min)
                let vmax = max 0.0 (pts |> List.map (fun (_, (_, hi)) -> hi) |> List.max)
                let span = if vmax - vmin < 1e-9 then 1.0 else vmax - vmin
                let py (v: float) =
                    let t = (v - vmin) / span
                    if plotDown then ya + t * (yb - ya) else yb - t * (yb - ya)
                let zero = if vmax - vmin < 1e-9 then (ya + yb) / 2.0 else py 0.0
                for s in supports do
                    add (PLine(px s, yt + 20.0, px s, yt + hp - 6.0, grid, 1.0, true))
                let curve (pick: float * float -> float) =
                    let line = pts |> List.map (fun (x, r) -> px x, (if vmax - vmin < 1e-9 then zero else py (pick r)))
                    add (PPolygon((px 0.0, zero) :: line @ [ px l, zero ], colour, 0.14, "", 0.0))
                    add (PPolyline(line, colour, 1.8))
                if item.IsEnvelope then
                    // band between the smallest and largest values over the combinations
                    let lineOf (pick: float * float -> float) = pts |> List.map (fun (x, r) -> px x, py (pick r))
                    let lo, hi = lineOf fst, lineOf snd
                    if vmax - vmin >= 1e-9 then
                        add (PPolygon(hi @ List.rev lo, colour, 0.16, "", 0.0))
                        add (PPolyline(lo, colour, 1.8))
                        add (PPolyline(hi, colour, 1.8))
                else
                    curve snd
                add (PLine(px 0.0, zero, px l, zero, beamColour, 1.0, false))
                // peak labels: the largest and smallest values, each on the outer side of the curve
                let label (x: float, v: float) =
                    if abs v > 1e-6 * (abs vmax + abs vmin + 1e-9) then
                        let yv = py v
                        add (PPolygon([ px x - 3.0, yv; px x, yv - 3.0; px x + 3.0, yv; px x, yv + 3.0 ], colour, 1.0, "", 0.0))
                        let text = sprintf "%s  @ %.2f m" (fmt v) (x / 1000.0)
                        let half = textWidth 8.5 text / 2.0
                        let cx = max (8.0 + half) (min (width - 8.0 - half) (px x))
                        add (PText(cx, (if yv < zero then yv - 10.0 else yv + 10.0), text, ink, 8.5, Centre, false))
                let hi = pts |> List.map (fun (x, (_, h)) -> x, h) |> List.maxBy snd
                let lo = pts |> List.map (fun (x, (lw, _)) -> x, lw) |> List.minBy snd
                if snd hi > 0.0 then label hi
                if snd lo < 0.0 then label lo
                if vmax - vmin < 1e-9 then add (PText((x0 + x1) / 2.0, zero - 10.0, "zero", muted, 8.5, Centre, false)))

        { Width = width
          Height = height
          Prims = List.ofSeq prims
          X0 = x0
          X1 = x1
          Length = l }

    /// The picture as SVG text (used for checking the drawing outside Excel, and handy for export)
    let svg (s: Scene) : string =
        let inv = System.Globalization.CultureInfo.InvariantCulture
        let n (v: float) = v.ToString("0.##", inv)
        let esc (t: string) = t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        let pts (ps: (float * float) list) = ps |> List.map (fun (x, y) -> n x + "," + n y) |> String.concat " "
        let body =
            s.Prims
            |> List.map (function
                | PLine(x1, y1, x2, y2, c, w, dashed) ->
                    sprintf "<line x1='%s' y1='%s' x2='%s' y2='%s' stroke='%s' stroke-width='%s'%s/>" (n x1) (n y1) (n x2) (n y2) c (n w)
                        (if dashed then " stroke-dasharray='4 3'" else "")
                | PPolygon(ps, fill, op, stroke, w) ->
                    sprintf "<polygon points='%s' fill='%s' fill-opacity='%s' stroke='%s' stroke-width='%s'/>" (pts ps) fill (n op)
                        (if stroke = "" then "none" else stroke) (n w)
                | PPolyline(ps, c, w) ->
                    sprintf "<polyline points='%s' fill='none' stroke='%s' stroke-width='%s' stroke-linejoin='round'/>" (pts ps) c (n w)
                | PText(x, y, t, c, size, align, bold) ->
                    sprintf "<text x='%s' y='%s' fill='%s' font-size='%spx' text-anchor='%s' dominant-baseline='central'%s>%s</text>" (n x) (n y) c
                        (n (size * 1.33))
                        (match align with
                         | Left -> "start"
                         | Centre -> "middle"
                         | Right -> "end")
                        (if bold then " font-weight='bold'" else "")
                        (esc t))
            |> String.concat "\n"
        sprintf
            "<svg xmlns='http://www.w3.org/2000/svg' width='%s' height='%s' viewBox='0 0 %s %s' font-family='Segoe UI, Arial, sans-serif'>\n<rect width='100%%' height='100%%' fill='white'/>\n%s\n</svg>"
            (n s.Width) (n s.Height) (n s.Width) (n s.Height) body
