#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 6 Oct (Mr.k): "I can't see if the difficulty works."
///
/// Small box at the top-left during a match, Editor and Development Builds ONLY
/// (the whole file is removed from a normal build). F3 = hide / show.
///
/// Shows what this round REALLY runs on, as the server decided it:
///   - difficulty + which DifficultyProfile asset was used
///   - task boards: per-line extra, random extras, and every board's targets
///   - place rules, sanity drain, ghost COUNT (difficulty does not touch ghost timing)
///
/// Reads synced values only, so a client sees the same numbers as the host.
/// Created by MatchDirector.Start; lives in the gameplay scene only.
/// </summary>
public sealed class DifficultyDebugHUD : MonoBehaviour
{
    private static DifficultyDebugHUD instance;

    private bool visible = true;
    private GUIStyle boxStyle;
    private readonly StringBuilder text = new StringBuilder(512);
    private float nextRefresh;
    private string shown = "";
    private string measured;
    private readonly GUIContent content = new GUIContent();
    private Vector2 size;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("Difficulty Debug HUD (dev)").AddComponent<DifficultyDebugHUD>();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) visible = !visible;

        // Rebuild the text a few times a second, not every OnGUI call (no garbage per frame).
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        shown = BuildText();
    }

    private string BuildText()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null) return "";

        text.Clear();
        text.Append("<b>DIFFICULTY (dev build)</b>   F3 hide\n");
        text.Append("Difficulty: <b>").Append(Label(match.Difficulty)).Append("</b>   asset: ")
            .Append(string.IsNullOrEmpty(match.ProfileName) ? "<color=#FF6060>NONE (built-in defaults!)</color>" : match.ProfileName)
            .Append('\n');
        text.Append("Players at start: ").Append(match.StartPlayerCount).Append('\n');

        text.Append("Tasks: +").Append(match.TaskExtraPerLine).Append(" per line, +")
            .Append(match.ExtraTasksThisRound).Append(" random (max ")
            .Append(match.MaxExtraTasksPerBoard > 0 ? match.MaxExtraTasksPerBoard.ToString() : "no cap")
            .Append(" per board)\n");

        var boards = ZoneTaskList.All;
        for (int b = 0; b < boards.Count; b++)
        {
            ZoneTaskList board = boards[b];
            if (board == null) continue;
            text.Append("   ").Append(board.Title).Append(": ");
            for (int i = 0; i < board.TaskCount; i++)
            {
                if (i > 0) text.Append(", ");
                int baseTarget = board.TaskAt(i).target;
                int now = board.TargetOf(i);
                text.Append(now);
                if (now != baseTarget) text.Append(" <color=#9a9a9a>(").Append(baseTarget).Append(")</color>");
            }
            text.Append('\n');
        }

        text.Append("Place rules: ").Append(match.PlaceRuleCount)
            .Append("   Sanity drain: x").Append(match.SanityDrainMultiplier.ToString("0.##")).Append('\n');

        int randomSpawned = Mathf.Max(0, match.TotalGhostCount - match.GuaranteedGhostCount);
        text.Append("Ghosts: ").Append(match.TotalGhostCount).Append(" (map ").Append(match.GuaranteedGhostCount)
            .Append(" + random ").Append(randomSpawned);
        if (randomSpawned < match.RandomGhostCountAsked)
            text.Append(" <color=#FFCC4D>of ").Append(match.RandomGhostCountAsked).Append(" asked, pool too small</color>");
        text.Append(")\n<color=#9a9a9a>Ghost timing/strength: each ghost's own script</color>");
        return text.ToString();
    }

    private static string Label(DifficultyLevel level) =>
        level == DifficultyLevel.ThirteenRules ? "13 Rules" : level.ToString();

    private void OnGUI()
    {
        if (!visible || string.IsNullOrEmpty(shown)) return;
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                richText = true,
                fontSize = 14,
                wordWrap = false,
                padding = new RectOffset(10, 10, 8, 8),
            };
            boxStyle.normal.textColor = Color.white;
        }

        if (!ReferenceEquals(measured, shown))
        {
            measured = shown;
            content.text = shown;
            size = boxStyle.CalcSize(content);
        }
        GUI.Box(new Rect(12f, 12f, size.x, size.y), content, boxStyle);
    }
}
#endif
