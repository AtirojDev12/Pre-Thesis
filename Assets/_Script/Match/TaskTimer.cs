using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug stopwatch for tasks. Answers the one number the whole difficulty
/// curve depends on: HOW LONG DOES ONE TASK ACTUALLY TAKE?
///
/// Every task-count decision — how many customers a zone wants, how many zones
/// a team can clear before 06:00 — is guesswork until this number is measured.
/// It cannot be reasoned out, because it is mostly walking distance and menu
/// fiddling, and those only exist once the room is built.
///
/// HOW TO USE
/// ----------
///   1. Put this component on any GameObject in the test scene.
///   2. Play, and do tasks normally for a few minutes.
///   3. Read the on-screen box, or press F9 to dump a full report to Console.
///
/// The report also converts the measured seconds into the task count each
/// difficulty should ask for, so the answer is directly usable.
///
/// DEBUG ONLY. Wrapped in a define so it costs nothing in a real build.
/// </summary>
public class TaskTimer : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Show the live box in the top-left corner while playing.")]
    [SerializeField] private bool showOverlay = true;

    [Tooltip("Key that dumps the full report to the Console.")]
    [SerializeField] private Key reportKey = Key.F9;

    [Header("Balance targets")]
    [Tooltip("Round length in real minutes, to convert seconds-per-task into a task count.")]
    [Min(1f)] [SerializeField] private float roundLengthMinutes = 15f;

    [Tooltip("How busy one player should be. 0.57 = 57%. Your ceiling is 0.60.")]
    [Range(0.1f, 1f)] [SerializeField] private float targetBusyFraction = 0.57f;

    /// <summary>One finished task.</summary>
    private struct Sample
    {
        public string zone;
        public string player;
        public float seconds;
        public bool success;
    }

    private static readonly List<Sample> samples = new List<Sample>();

    /// <summary>Tasks that have started but not finished, keyed by zone + task id.</summary>
    private static readonly Dictionary<string, float> open = new Dictionary<string, float>();

    private static TaskTimer instance;

    private void Awake() => instance = this;
    private void OnDestroy() { if (instance == this) instance = null; }

    // ---- Recording ---------------------------------------------------------

    /// <summary>
    /// A task became available — the customer sat down, the paper appeared.
    /// <paramref name="key"/> must match the one passed to Complete.
    /// </summary>
    public static void Begin(string key)
    {
        if (string.IsNullOrEmpty(key)) return;

        open[key] = Time.time;

        // Confirms on the very first task that the hook is wired, instead of
        // leaving you staring at an empty overlay wondering.
        if (samples.Count == 0) Debug.Log($"[TaskTimer] Started timing '{key}'. Finish it to record the first sample.");
    }

    /// <summary>
    /// A task finished. Records how long it sat open.
    ///
    /// NOTE what this measures: wall-clock from the task appearing to it being
    /// done. If the player was across the map when the customer arrived, that
    /// walk is included — which is correct, because that walk is real time the
    /// round has to pay for. It is the number that matters for balancing, not
    /// the time with hands on the machine.
    /// </summary>
    public static void Complete(string key, string zone, string player, bool success)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (!open.TryGetValue(key, out float started))
        {
            // Loud on purpose. The first version returned silently here, so a
            // missing Begin() looked exactly like "nothing is happening" and
            // cost a whole playtest to find.
            Debug.LogWarning(
                $"[TaskTimer] Complete('{key}') with no matching Begin('{key}'). " +
                "The task finished but was never started, so it is not recorded. " +
                "Check that whatever creates the task calls TaskTimer.Begin with the SAME key.");
            return;
        }

        open.Remove(key);

        float seconds = Time.time - started;
        samples.Add(new Sample { zone = zone, player = player, seconds = seconds, success = success });

        Debug.Log($"[TaskTimer] {zone} — {seconds:F1}s by {player} ({(success ? "correct" : "wrong")}). " +
                  $"Average so far: {Average():F1}s over {samples.Count} tasks.");
    }

    /// <summary>
    /// Clears everything. Call between test runs.
    ///
    /// NOT named Reset(): Unity treats Reset as a magic Editor message and
    /// tries to call it on the component instance. A static one makes the
    /// Editor throw "Failed to call static function Reset because an object was
    /// provided" every time the component is added or reset. Same trap applies
    /// to Awake, Start, Update, OnEnable and friends.
    /// </summary>
    public static void ClearSamples()
    {
        samples.Clear();
        open.Clear();
    }

    /// <summary>Right-click the component header in the Inspector to clear without leaving Play mode.</summary>
    [ContextMenu("Clear recorded tasks")]
    private void ClearFromInspector() => ClearSamples();

    // ---- Stats -------------------------------------------------------------

    public static int Count => samples.Count;

    public static float Average()
    {
        if (samples.Count == 0) return 0f;

        float total = 0f;
        for (int i = 0; i < samples.Count; i++) total += samples[i].seconds;
        return total / samples.Count;
    }

    /// <summary>
    /// The median matters more than the mean here. One task where the player
    /// wandered off for two minutes drags the average badly, and that outlier
    /// is not what a normal task costs.
    /// </summary>
    public static float Median()
    {
        if (samples.Count == 0) return 0f;

        List<float> sorted = new List<float>(samples.Count);
        for (int i = 0; i < samples.Count; i++) sorted.Add(samples[i].seconds);
        sorted.Sort();

        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
    }

    public static float Fastest()
    {
        float best = float.MaxValue;
        for (int i = 0; i < samples.Count; i++) best = Mathf.Min(best, samples[i].seconds);
        return samples.Count == 0 ? 0f : best;
    }

    public static float Slowest()
    {
        float worst = 0f;
        for (int i = 0; i < samples.Count; i++) worst = Mathf.Max(worst, samples[i].seconds);
        return worst;
    }

    // ---- Report ------------------------------------------------------------

    private void Update()
    {
        if (Keyboard.current == null) return;
        if (Keyboard.current[reportKey].wasPressedThisFrame) LogReport();
    }

    /// <summary>
    /// Dumps everything, and converts the measurement into the task counts the
    /// difficulty assets should actually use.
    /// </summary>
    public void LogReport()
    {
        if (samples.Count == 0)
        {
            Debug.Log("[TaskTimer] No tasks recorded yet.");
            return;
        }

        float median = Median();
        float roundSeconds = roundLengthMinutes * 60f;
        float workSeconds = roundSeconds * targetBusyFraction;
        float perPlayer = workSeconds / Mathf.Max(0.1f, median);

        var report = new System.Text.StringBuilder();
        report.AppendLine("========== TASK TIMER ==========");
        report.AppendLine($"Tasks recorded : {samples.Count}");
        report.AppendLine($"Median         : {median:F1}s   <-- use this one");
        report.AppendLine($"Average        : {Average():F1}s");
        report.AppendLine($"Fastest        : {Fastest():F1}s");
        report.AppendLine($"Slowest        : {Slowest():F1}s");
        report.AppendLine();
        report.AppendLine($"At a {roundLengthMinutes:F0}-minute round and {targetBusyFraction * 100f:F0}% busy:");
        report.AppendLine($"  tasks per player : {perPlayer:F0}");
        report.AppendLine($"  total for 6p     : {perPlayer * 6f:F0}");
        report.AppendLine($"  total for 1p     : {perPlayer * 1.28f:F0}   (27% harder solo)");
        report.AppendLine();

        // Per-zone breakdown, so a zone that is far slower than the rest shows up.
        var byZone = new Dictionary<string, (int count, float total)>();
        foreach (Sample s in samples)
        {
            byZone.TryGetValue(s.zone, out var entry);
            byZone[s.zone] = (entry.count + 1, entry.total + s.seconds);
        }

        report.AppendLine("Per zone:");
        foreach (var pair in byZone)
            report.AppendLine($"  {pair.Key}: {pair.Value.count} tasks, {pair.Value.total / pair.Value.count:F1}s average");

        report.AppendLine("================================");
        Debug.Log(report.ToString());
    }

    private void OnGUI()
    {
        if (!showOverlay) return;

        string text = samples.Count == 0
            ? "TaskTimer: waiting for first task…"
            : $"TaskTimer   tasks {samples.Count}\n" +
              $"median {Median():F1}s   avg {Average():F1}s\n" +
              $"-> {(roundLengthMinutes * 60f * targetBusyFraction) / Mathf.Max(0.1f, Median()):F0} tasks/player\n" +
              $"F9 = full report";

        GUI.Box(new Rect(10f, 10f, 260f, 74f), text);
    }
}
