using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// On-screen controls for the ModelPreview scene (Game view, Play Mode).
/// Created automatically by the first NpcModelPreview; nothing to set up.
///
/// Keyboard: 1 = all Idle, 2 = all Walk, R = restart, Space = pause/resume, H = hide panel.
/// </summary>
public sealed class NpcPreviewControls : MonoBehaviour
{
    private static NpcPreviewControls instance;

    private NpcModelPreview[] previews = new NpcModelPreview[0];
    private bool visible = true;
    private float speed = 1f;
    private float armSpread = 10f;
    private GUIStyle title, label, button, activeButton;

    public static void EnsureExists()
    {
        if (instance != null) return;
        instance = new GameObject("NPC Preview Controls").AddComponent<NpcPreviewControls>();
    }

    private void Start()
    {
        Refresh();
        if (previews.Length > 0)
        {
            speed = previews[0].PlaybackSpeed;
            armSpread = previews[0].ArmSpreadDegrees;
        }
    }

    private void Refresh()
    {
        previews = FindObjectsByType<NpcModelPreview>(FindObjectsSortMode.None);
        // Left-to-right on screen, matching how they stand in the scene.
        System.Array.Sort(previews, (a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) ForAll(p => p.PlayIdle());
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) ForAll(p => p.PlayWalk());
        if (keyboard.rKey.wasPressedThisFrame) ForAll(p => p.Restart());
        if (keyboard.spaceKey.wasPressedThisFrame) TogglePauseAll();
        if (keyboard.hKey.wasPressedThisFrame) visible = !visible;
    }

    private void OnGUI()
    {
        // Lay out at 1080p and scale to the real Game view size.
        float scale = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        BuildStyles();

        if (!visible)
        {
            GUI.Label(new Rect(20, 20, 400, 30), "H = show controls", label);
            return;
        }

        float rowHeight = 46f;
        float height = 230f + previews.Length * rowHeight;
        GUILayout.BeginArea(new Rect(20, 20, 470, height), GUI.skin.box);
        GUILayout.Label("NPC Animation", title);

        // All NPCs.
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Idle  [1]", AllWalking(false) ? activeButton : button)) ForAll(p => p.PlayIdle());
        if (GUILayout.Button("Walk  [2]", AllWalking(true) ? activeButton : button)) ForAll(p => p.PlayWalk());
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Restart  [R]", button)) ForAll(p => p.Restart());
        if (GUILayout.Button(AnyPaused() ? "Resume  [Space]" : "Pause  [Space]", button)) TogglePauseAll();
        GUILayout.EndHorizontal();

        // Shared sliders.
        GUILayout.Space(6);
        float newSpeed = Slider($"Speed  {speed:0.00}x", speed, 0f, 2f);
        if (!Mathf.Approximately(newSpeed, speed)) { speed = newSpeed; ForAll(p => p.SetPlaybackSpeed(speed)); }
        float newSpread = Slider($"Arm spread  {armSpread:0}°", armSpread, 0f, 30f);
        if (!Mathf.Approximately(newSpread, armSpread)) { armSpread = newSpread; ForAll(p => p.SetArmSpread(armSpread)); }

        // One row per NPC.
        GUILayout.Space(6);
        foreach (NpcModelPreview p in previews)
        {
            if (p == null) continue;
            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight - 6));
            GUILayout.Label(p.name, label, GUILayout.Width(190));
            if (GUILayout.Button("Idle", !p.IsWalking ? activeButton : button)) p.PlayIdle();
            if (GUILayout.Button("Walk", p.IsWalking ? activeButton : button)) p.PlayWalk();
            GUILayout.EndHorizontal();
        }

        GUILayout.Label("H = hide", label);
        GUILayout.EndArea();
    }

    private float Slider(string text, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(text, label, GUILayout.Width(190));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(24));
        GUILayout.EndHorizontal();
        return value;
    }

    private void BuildStyles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
        label = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        button = new GUIStyle(GUI.skin.button) { fontSize = 20, fixedHeight = 40 };
        activeButton = new GUIStyle(button);
        activeButton.normal.textColor = activeButton.hover.textColor = new Color(0.45f, 1f, 0.55f);
        activeButton.fontStyle = FontStyle.Bold;
    }

    private void ForAll(System.Action<NpcModelPreview> action)
    {
        foreach (NpcModelPreview p in previews) if (p != null) action(p);
    }

    private void TogglePauseAll()
    {
        bool pause = !AnyPaused();
        ForAll(p => { if (p.IsPaused != pause) p.TogglePause(); });
    }

    private bool AnyPaused()
    {
        foreach (NpcModelPreview p in previews) if (p != null && p.IsPaused) return true;
        return false;
    }

    private bool AllWalking(bool walking)
    {
        if (previews.Length == 0) return false;
        foreach (NpcModelPreview p in previews) if (p != null && p.IsWalking != walking) return false;
        return true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
