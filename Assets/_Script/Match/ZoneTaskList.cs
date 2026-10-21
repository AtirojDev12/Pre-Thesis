using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What kind of sale a task counts.</summary>
public enum ZoneTaskKind
{
    Popcorn = 0,  // Cheese / BBQ / Paprika
    Water = 1,    // drinks (serialized name retained for existing boards)
    Ticket = 2,   // a movie ticket

    // 14 Oct: chores (ZoneChoreSpot, hold E). Counted by ServerReportSale(kind, None, -1, false).
    FilmChange = 3,       // Screen Room: change the film in the ghost room
    CleanScreenRoom = 4,  // Screen Room: clean both rooms
    CleanToilet = 5,      // Toilets: clean both toilets
}

public static class ZoneTaskKinds
{
    /// <summary>Chores have a fixed number of spots: difficulty never adds to them.</summary>
    public static bool IsChore(ZoneTaskKind kind) => kind >= ZoneTaskKind.FilmChange;
}

/// <summary>Which customers a task counts.</summary>
public enum ZoneTaskCustomer
{
    Any = 0,
    Human = 1,
    Ghost = 2,
}

/// <summary>One line on a task board, e.g. "Sell BBQ popcorn  0/15".</summary>
[System.Serializable]
public class ZoneTask
{
    [Tooltip("Text on the board, e.g. \"Sell BBQ popcorn\".")]
    public string label = "Sell popcorn";

    public ZoneTaskKind kind = ZoneTaskKind.Popcorn;

    [Tooltip("Popcorn/drinks: which flavor counts. None = any flavor.")]
    public PopcornFlavor flavor = PopcornFlavor.None;

    [Tooltip("Ticket only: 0 = any movie, 1 = Movie 1, 2 = Movie 2, 3 = Movie 3.")]
    [Min(0)] public int movie;

    public ZoneTaskCustomer customer = ZoneTaskCustomer.Any;

    [Tooltip("Correct sales needed.")]
    [Min(1)] public int target = 15;

    /// <param name="movieIndex">0-based movie index (tickets only, -1 otherwise).</param>
    public bool Matches(ZoneTaskKind saleKind, PopcornFlavor saleFlavor, int movieIndex, bool ghostCustomer)
    {
        if (saleKind != kind) return false;
        if (customer == ZoneTaskCustomer.Human && ghostCustomer) return false;
        if (customer == ZoneTaskCustomer.Ghost && !ghostCustomer) return false;
        if ((kind == ZoneTaskKind.Popcorn || kind == ZoneTaskKind.Water) && flavor != PopcornFlavor.None && flavor != saleFlavor) return false;
        if (kind == ZoneTaskKind.Ticket && movie > 0 && movie - 1 != movieIndex) return false;
        return true;
    }
}

/// <summary>
/// PROTOTYPE TASK BOARD (Mr.k, 29 Sep). Put one in every zone that has tasks.
/// It is both the zone's task list AND the board players read.
///
///   - Scene object with a NetworkIdentity (Tools > Pre-Thesis > Setup Gameplay Loop makes them).
///   - Lists its tasks, e.g. "Sell BBQ popcorn 7/15". A finished line gets a
///     line drawn through it, left to right.
///   - When EVERY line is done, the zone counts as done for MatchDirector.
///     The exit opens when every zone is done AND it is 06:00 or later.
///   - The text faces the object's FORWARD (blue arrow): stand in front of the arrow to read it.
///
/// SERVER AUTHORITY: only the server counts sales (popcorn / ticket code calls
/// <see cref="ServerReportSale"/>). Progress reaches clients through a SyncList.
/// One sale adds +1 to EVERY unfinished line it matches.
///
/// DIFFICULTY (6 Oct): a line's 'target' in the Inspector is the BASE. Tonight's
/// real target = base + the difficulty's per-line extra + random extras for each
/// player past the first (DifficultyProfile). The server works it out at round
/// start and sends it to everyone (SyncList 'targets').
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class ZoneTaskList : NetworkBehaviour
{
    [Header("Zone")]
    [Tooltip("Unique per zone. MatchDirector counts zones by this ID.")]
    [SerializeField] private string zoneID = "zone_popcorn";
    [SerializeField] private string title = "POPCORN & WATER";
    [SerializeField] private List<ZoneTask> tasks = new List<ZoneTask>();

    [Header("Board look")]
    [Tooltip("Board width in metres.")]
    [Min(0.3f)] [SerializeField] private float boardWidth = 1.6f;

    // Server writes, clients read. One entry per task, same order as 'tasks'.
    private readonly SyncList<int> progress = new SyncList<int>();
    // Offline test (Play pressed in the map, no host): Mirror refuses to write a
    // SyncList when no server/client runs, so offline uses a plain array.
    private int[] offlineProgress;

    // 6 Oct: tonight's target per line (base + difficulty). Server writes, clients read.
    private readonly SyncList<int> targets = new SyncList<int>();
    private int[] offlineTargets;

    private static readonly List<ZoneTaskList> active = new List<ZoneTaskList>();
    private bool zoneReported;

    public string ZoneID => zoneID;
    public string Title => title;
    public int TaskCount => tasks.Count;

    /// <summary>Every task board in the scene (read only). Used by the dev overlay.</summary>
    public static IReadOnlyList<ZoneTaskList> All => active;

    /// <summary>Tonight's target for line i: base + difficulty. Falls back to the base until the server has sent it.</summary>
    public int TargetOf(int i)
    {
        if (offlineTargets != null && i < offlineTargets.Length) return offlineTargets[i];
        if (i < targets.Count) return targets[i];
        return tasks[i].target;
    }
    public ZoneTask TaskAt(int i) => tasks[i];
    public int ProgressOf(int i)
    {
        if (offlineProgress != null) return i < offlineProgress.Length ? offlineProgress[i] : 0;
        return i < progress.Count ? progress[i] : 0;
    }

    private bool ProgressReady => NetworkMode.IsOffline
        ? offlineProgress != null && offlineProgress.Length == tasks.Count
          && offlineTargets != null && offlineTargets.Length == tasks.Count
        : progress.Count == tasks.Count && targets.Count == tasks.Count;

    private void SetProgress(int i, int value)
    {
        if (offlineProgress != null) offlineProgress[i] = value;
        else progress[i] = value;
    }
    public bool IsTaskDone(int i) => ProgressOf(i) >= TargetOf(i);

    /// <summary>Whether this sale can advance any active, unfinished task line.</summary>
    public static bool HasUnfinishedSale(ZoneTaskKind kind, PopcornFlavor flavor, int movieIndex, bool ghost)
    {
        foreach (ZoneTaskList board in active)
        {
            if (board == null) continue;
            for (int i = 0; i < board.tasks.Count; i++)
                if (!board.IsTaskDone(i) && board.tasks[i].Matches(kind, flavor, movieIndex, ghost))
                    return true;
        }
        return false;
    }

    public bool AllDone
    {
        get
        {
            for (int i = 0; i < tasks.Count; i++) if (!IsTaskDone(i)) return false;
            return tasks.Count > 0;
        }
    }

    /// <summary>True if this scene uses task boards (then the old shared popcorn score no longer decides zones).</summary>
    public static bool ExistsInScene() =>
        active.Count > 0 || FindAnyObjectByType<ZoneTaskList>(FindObjectsInactive.Include) != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        active.Clear();
        lastDifficultyLog = null;
    }

    private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    private void OnDisable() => active.Remove(this);

    private void Start()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerInit();
        BuildBoard();
    }

    private void ServerInit()
    {
        if (NetworkMode.IsOffline)
        {
            if (offlineProgress == null || offlineProgress.Length != tasks.Count) offlineProgress = new int[tasks.Count];
            if (offlineTargets == null || offlineTargets.Length != tasks.Count) offlineTargets = BaseTargets();
        }
        else
        {
            if (progress.Count != tasks.Count)
            {
                progress.Clear();
                for (int i = 0; i < tasks.Count; i++) progress.Add(0);
            }
            if (targets.Count != tasks.Count)
            {
                targets.Clear();
                for (int i = 0; i < tasks.Count; i++) targets.Add(Mathf.Max(1, tasks[i].target));
            }
        }

        // Tell the match this zone exists (every zone is required).
        if (MatchDirector.Instance != null) MatchDirector.Instance.ServerRegisterZone(zoneID);

        // Tonight's targets (works whichever of MatchDirector / this board starts first).
        ServerApplyDifficulty();
    }

    private int[] BaseTargets()
    {
        var result = new int[tasks.Count];
        for (int i = 0; i < tasks.Count; i++) result[i] = Mathf.Max(1, tasks[i].target);
        return result;
    }

    /// <summary>Indexes of the sale lines (not chores). Difficulty extras only go here.</summary>
    private List<int> SaleLines()
    {
        var lines = new List<int>(tasks.Count);
        for (int i = 0; i < tasks.Count; i++)
            if (!ZoneTaskKinds.IsChore(tasks[i].kind)) lines.Add(i);
        return lines;
    }

    private bool HasAnyProgress()
    {
        for (int i = 0; i < tasks.Count; i++) if (ProgressOf(i) > 0) return true;
        return false;
    }

    private void ServerSetTargets(int[] values)
    {
        if (NetworkMode.IsOffline)
        {
            offlineTargets = values;
            return;
        }
        if (targets.Count != values.Length)
        {
            targets.Clear();
            for (int i = 0; i < values.Length; i++) targets.Add(values[i]);
            return;
        }
        for (int i = 0; i < values.Length; i++)
            if (targets[i] != values[i]) targets[i] = values[i]; // only changed lines are sent
    }

    private static string lastDifficultyLog;

    /// <summary>
    /// SERVER / OFFLINE (6 Oct). Sets every board's targets from tonight's difficulty:
    ///   base + per-line extra on every line, then the random extras (one sale each)
    ///   on random lines of random boards, at most 'cap' per board.
    /// Same seed and same boards = same result, so calling it again is safe.
    /// Boards that already have progress are left alone.
    /// </summary>
    public static void ServerApplyDifficulty()
    {
        MatchDirector director = MatchDirector.Instance;
        int perLine = 0, extras = 0, cap = 0, seed = 0;
        bool planned = director != null && director.ServerTaskPlan(out perLine, out extras, out cap, out seed);
        if (!planned) return; // no round yet: the boards keep their base targets

        var boards = new List<ZoneTaskList>();
        for (int b = 0; b < active.Count; b++)
        {
            ZoneTaskList board = active[b];
            if (board == null || board.tasks.Count == 0 || !NetworkMode.HasServerAuthority(board)) continue;
            if (!board.ProgressReady || board.HasAnyProgress()) continue;
            if (board.SaleLines().Count == 0) continue; // 14 Oct: chore-only boards keep their spot counts
            boards.Add(board);
        }
        if (boards.Count == 0) return;
        boards.Sort((x, y) => string.CompareOrdinal(x.zoneID, y.zoneID));

        var plan = new List<int[]>(boards.Count);
        for (int b = 0; b < boards.Count; b++)
        {
            int[] values = boards[b].BaseTargets();
            for (int i = 0; i < values.Length; i++)
                if (!ZoneTaskKinds.IsChore(boards[b].tasks[i].kind)) values[i] += perLine;
            plan.Add(values);
        }

        int dropped = 0;
        if (extras > 0)
        {
            var rng = new System.Random(seed);
            var perBoard = new int[boards.Count];
            var open = new List<int>(boards.Count);
            for (int e = 0; e < extras; e++)
            {
                open.Clear();
                for (int b = 0; b < boards.Count; b++)
                    if (cap <= 0 || perBoard[b] < cap) open.Add(b);
                if (open.Count == 0) { dropped = extras - e; break; }

                int pick = open[rng.Next(open.Count)];
                List<int> lines = boards[pick].SaleLines(); // never a chore line (14 Oct)
                plan[pick][lines[rng.Next(lines.Count)]]++;
                perBoard[pick]++;
            }
        }

        var summary = new System.Text.StringBuilder();
        for (int b = 0; b < boards.Count; b++)
        {
            boards[b].ServerSetTargets(plan[b]);
            summary.Append(" ").Append(boards[b].title).Append(" [").Append(string.Join(",", plan[b])).Append("]");
        }

        string log = $"[ZoneTaskList] Difficulty {director.Difficulty}: +{perLine} per line, {extras} random extras " +
                     $"(max {cap}/board{(dropped > 0 ? $", {dropped} did not fit" : "")}).{summary}";
        if (log != lastDifficultyLog)
        {
            lastDifficultyLog = log;
            Debug.Log(log);
        }
    }

    // ---- Server: counting ----------------------------------------------------

    /// <summary>
    /// SERVER / OFFLINE ONLY. A correct sale happened somewhere. Every task
    /// board checks its own lines.
    /// </summary>
    /// <param name="movieIndex">0-based movie index for tickets, -1 for popcorn / water.</param>
    public static void ServerReportSale(ZoneTaskKind kind, PopcornFlavor flavor, int movieIndex, bool ghostCustomer)
    {
        for (int i = 0; i < active.Count; i++)
            if (active[i] != null) active[i].ServerCount(kind, flavor, movieIndex, ghostCustomer);
    }

    private void ServerCount(ZoneTaskKind kind, PopcornFlavor flavor, int movieIndex, bool ghostCustomer)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        if (MatchDirector.Instance != null && !MatchDirector.Instance.RoundRunning) return;
        if (!ProgressReady) ServerInit();

        bool changed = false;
        for (int i = 0; i < tasks.Count; i++)
        {
            int value = ProgressOf(i);
            if (value >= TargetOf(i)) continue;
            if (!tasks[i].Matches(kind, flavor, movieIndex, ghostCustomer)) continue;
            SetProgress(i, value + 1);
            changed = true;
        }

        if (changed && !zoneReported && AllDone)
        {
            zoneReported = true;
            if (MatchDirector.Instance != null) MatchDirector.Instance.ServerReportZoneCompleted(zoneID);
            Debug.Log($"[ZoneTaskList] '{title}' ({zoneID}) — every task done.", this);
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>SERVER. Dev cheat (14 Oct): fill one line of this board (and report the zone if that finished it).</summary>
    public void ServerDevCompleteLine(int line)
    {
        if (!NetworkMode.HasServerAuthority(this) || line < 0 || line >= tasks.Count) return;
        if (!ProgressReady) ServerInit();
        SetProgress(line, TargetOf(line));
        ServerDevReportIfDone();
    }

    /// <summary>SERVER. Dev cheat (14 Oct): fill every line of this board.</summary>
    public void ServerDevCompleteBoard()
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        if (!ProgressReady) ServerInit();
        for (int i = 0; i < tasks.Count; i++) SetProgress(i, TargetOf(i));
        ServerDevReportIfDone();
    }

    private void ServerDevReportIfDone()
    {
        if (zoneReported || !AllDone) return;
        zoneReported = true;
        if (MatchDirector.Instance != null) MatchDirector.Instance.ServerReportZoneCompleted(zoneID);
    }

    /// <summary>SERVER. Dev cheat: fill every line of every task board (and report the zones).</summary>
    public static void ServerDevCompleteAll()
    {
        for (int b = 0; b < active.Count; b++)
        {
            ZoneTaskList board = active[b];
            if (board == null || !NetworkMode.HasServerAuthority(board)) continue;
            if (!board.ProgressReady) board.ServerInit();
            for (int i = 0; i < board.tasks.Count; i++) board.SetProgress(i, board.TargetOf(i));
            if (!board.zoneReported)
            {
                board.zoneReported = true;
                if (MatchDirector.Instance != null) MatchDirector.Instance.ServerReportZoneCompleted(board.zoneID);
            }
        }
    }
#endif

    // ---- Board (every machine) -----------------------------------------------

    private const float PixelsPerMetre = 500f;
    private const float StrikeSeconds = 0.5f;
    private static readonly Color DoneColor = new Color(0.55f, 0.55f, 0.55f);
    private static readonly Color StrikeColor = new Color(0.95f, 0.2f, 0.2f);

    private TMP_Text[] lineTexts;
    // 14 Oct: one strike per text row (long lines wrap onto 2+ rows).
    private List<RectTransform>[] strikes;
    private float[] strikeStart;
    private bool[] strikeFinished;
    private int[] shownProgress;
    private int[] shownTarget;

    private void BuildBoard()
    {
        float width = boardWidth * PixelsPerMetre;

        var canvasGo = new GameObject("Task Board Canvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        // A world canvas is read from its -Z side; turn it so it reads from the object's forward.
        canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        canvasGo.transform.localScale = Vector3.one / PixelsPerMetre;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRt = (RectTransform)canvasGo.transform;
        canvasRt.sizeDelta = new Vector2(width, 150f + 80f * Mathf.Max(1, tasks.Count)); // final height set below

        Image background = NewImage("Background", canvasRt, new Color(0.05f, 0.04f, 0.04f, 0.92f));
        Stretch(background.rectTransform, 0f, 0f);

        TMP_Text titleText = NewText("Title", canvasRt, title, 56, new Color(1f, 0.8f, 0.3f), FontStyles.Bold);
        titleText.alignment = TextAlignmentOptions.Center;
        Anchor(titleText.rectTransform, 30f, 20f, 90f);

        lineTexts = new TMP_Text[tasks.Count];
        strikes = new List<RectTransform>[tasks.Count];
        strikeStart = new float[tasks.Count];
        strikeFinished = new bool[tasks.Count];
        shownProgress = new int[tasks.Count];
        shownTarget = new int[tasks.Count];

        // 14 Oct: long lines WRAP to a new row instead of being cut with "...".
        // Each line is as tall as its rows; the board grows to fit.
        const float side = 40f;
        float top = 130f;
        for (int i = 0; i < tasks.Count; i++)
        {
            TMP_Text line = NewText("Task " + (i + 1), canvasRt, "", 42, Color.white, FontStyles.Normal);
            line.alignment = TextAlignmentOptions.MidlineLeft;
            line.textWrappingMode = TextWrappingModes.Normal;
            line.overflowMode = TextOverflowModes.Overflow;
            // Measure with the widest numbers the line will show ("00/00").
            string sample = $"{i + 1}. {tasks[i].label}   00/00";
            float lineHeight = Mathf.Max(70f, line.GetPreferredValues(sample, width - side * 2f, 0f).y + 8f);
            Anchor(line.rectTransform, side, top, lineHeight);
            top += lineHeight + 10f;
            lineTexts[i] = line;

            strikes[i] = new List<RectTransform>(2);
            strikeStart[i] = -1f;
            shownProgress[i] = -1;
        }
        canvasRt.sizeDelta = new Vector2(width, top + 10f);
    }

    /// <summary>
    /// Draws the strike lines of task i, 0..1 of the way across. One per text row,
    /// read from TMP's row info, so a wrapped line is struck on every row.
    /// </summary>
    private void SetStrikes(int i, float t)
    {
        List<RectTransform> list = strikes[i];
        int rows = 0;
        TMP_TextInfo info = null;
        if (t > 0f)
        {
            lineTexts[i].ForceMeshUpdate();
            info = lineTexts[i].textInfo;
            rows = info.lineCount;
        }

        while (list.Count < rows) list.Add(NewStrike(lineTexts[i].rectTransform));
        for (int r = 0; r < list.Count; r++)
        {
            bool on = r < rows;
            if (list[r].gameObject.activeSelf != on) list[r].gameObject.SetActive(on);
            if (!on) continue;
            TMP_LineInfo row = info.lineInfo[r];
            float left = row.lineExtents.min.x;
            float right = row.lineExtents.max.x;
            list[r].anchoredPosition = new Vector2(left, (row.ascender + row.descender) * 0.5f);
            list[r].sizeDelta = new Vector2(Mathf.Max(0f, right - left) * t, 7f);
        }
    }

    private static RectTransform NewStrike(RectTransform text)
    {
        RectTransform srt = NewImage("Strike", text, StrikeColor).rectTransform;
        // TMP row positions are measured from the text's pivot, so anchor there.
        srt.anchorMin = srt.anchorMax = text.pivot;
        srt.pivot = new Vector2(0f, 0.5f);
        srt.sizeDelta = new Vector2(0f, 7f);
        return srt;
    }

    private void LateUpdate()
    {
        if (lineTexts == null) return;

        for (int i = 0; i < lineTexts.Length; i++)
        {
            int target = TargetOf(i);
            int value = Mathf.Min(ProgressOf(i), target);
            bool done = value >= target;

            if (value != shownProgress[i] || target != shownTarget[i])
            {
                shownProgress[i] = value;
                shownTarget[i] = target;
                lineTexts[i].text = $"{i + 1}. {tasks[i].label}   {value}/{target}";
                lineTexts[i].color = done ? DoneColor : Color.white;
                if (done && strikeStart[i] < 0f) { strikeStart[i] = Time.time; strikeFinished[i] = false; }
                if (!done) { strikeStart[i] = -1f; strikeFinished[i] = false; SetStrikes(i, 0f); }
                else if (strikeFinished[i]) SetStrikes(i, 1f); // text changed after the strike: redo rows
            }

            if (strikeStart[i] >= 0f && !strikeFinished[i])
            {
                float t = Mathf.Clamp01((Time.time - strikeStart[i]) / StrikeSeconds);
                SetStrikes(i, t);
                if (t >= 1f) strikeFinished[i] = true;
            }
        }
    }

    private static TMP_Text NewText(string name, RectTransform parent, string text, float size, Color color, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Image NewImage(string name, RectTransform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rt, float x, float y)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(x, y);
        rt.offsetMax = new Vector2(-x, -y);
    }

    /// <summary>Full width minus side margins, 'top' pixels from the top, 'height' tall.</summary>
    private static void Anchor(RectTransform rt, float side, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(side, -top - height);
        rt.offsetMax = new Vector2(-side, -top);
    }

#if UNITY_EDITOR
    // Shows where the board will be and which way it reads, before pressing Play.
    private void OnDrawGizmos()
    {
        // Approximate: in Play, lines that wrap make the board a bit taller.
        float height = (150f + 80f * Mathf.Max(1, tasks.Count)) / PixelsPerMetre;
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(boardWidth, height, 0.02f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.6f);
    }
#endif
}
