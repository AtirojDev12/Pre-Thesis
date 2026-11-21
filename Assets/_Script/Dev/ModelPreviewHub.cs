using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One model in the ModelPreview scene that the hub can show, hide and animate.
/// Implemented by NpcModelPreview ("NPC" / "GhostNPC") and PlayerModelPreview ("Player").
/// To add a new model type, implement this on its preview component.
/// </summary>
public interface IModelPreview
{
    /// <summary>Tab the model is listed under, e.g. "NPC" or "Player".</summary>
    string Category { get; }
    string DisplayName { get; }
    /// <summary>Animations this model can play, in button order.</summary>
    IReadOnlyList<string> Animations { get; }
    string CurrentAnimation { get; }
    bool IsPaused { get; }
    void PlayAnimation(string animationName);
    void Restart();
    void TogglePause();
    void SetPlaybackSpeed(float speed);
}

/// <summary>
/// On-screen controls for the ModelPreview scene (Game view, Play Mode).
/// Created automatically by the first preview model; nothing to set up.
///
/// Tabs at the top pick a model type (NPC / GhostNPC / Player). Only that type is shown,
/// the camera centres on it, and the buttons list the animations each model has.
///
/// Keyboard: Tab = next type, 1-9 = animation for every model of the type,
/// R = restart, Space = pause/resume, H = hide panel.
/// </summary>
public sealed class ModelPreviewHub : MonoBehaviour
{
    private static ModelPreviewHub instance;

    private sealed class Entry
    {
        public IModelPreview preview;
        public MonoBehaviour component;
        public Renderer[] renderers;
        public Quaternion baseRotation;
    }

    private readonly List<Entry> entries = new();
    private readonly List<string> categories = new();
    private string category;
    private bool visible = true;
    private float speed = 1f;
    private float turn;
    private float armSpread = 10f;
    private Camera previewCamera;
    private Vector3 cameraHome;
    private Vector2 scroll;
    private GUIStyle title, label, small, button, activeButton, tab, activeTab;

    public static void EnsureExists()
    {
        if (instance != null) return;
        instance = new GameObject("Model Preview Hub").AddComponent<ModelPreviewHub>();
    }

    private void Start()
    {
        previewCamera = Camera.main;
        if (previewCamera != null) cameraHome = previewCamera.transform.position;
        Refresh();
        if (categories.Count > 0) SelectCategory(categories[0]);
        foreach (Entry e in entries)
            if (e.component is NpcModelPreview npc) { armSpread = npc.ArmSpreadDegrees; break; }
    }

    private void Refresh()
    {
        entries.Clear();
        categories.Clear();
        foreach (MonoBehaviour component in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (component is not IModelPreview preview) continue;
            entries.Add(new Entry
            {
                preview = preview,
                component = component,
                renderers = component.GetComponentsInChildren<Renderer>(true),
                baseRotation = component.transform.localRotation,
            });
            if (!categories.Contains(preview.Category)) categories.Add(preview.Category);
        }
        // Left-to-right on screen, matching how they stand in the scene.
        entries.Sort((a, b) => a.component.transform.position.x.CompareTo(b.component.transform.position.x));
        categories.Sort((a, b) => Order(a).CompareTo(Order(b)));
    }

    private static int Order(string c) => c == "NPC" ? 0 : c == "GhostNPC" ? 1 : c == "Player" ? 2 : 3;

    // ---- Category ------------------------------------------------------------

    public void SelectCategory(string value)
    {
        category = value;
        foreach (Entry e in entries)
        {
            bool show = e.preview.Category == category;
            foreach (Renderer r in e.renderers) if (r != null) r.enabled = show;
        }
    }

    private IEnumerable<Entry> Shown()
    {
        foreach (Entry e in entries) if (e.preview.Category == category) yield return e;
    }

    /// <summary>Animations shared by every shown model, in the first model's order.</summary>
    private List<string> SharedAnimations()
    {
        var shared = new List<string>();
        bool first = true;
        foreach (Entry e in Shown())
        {
            if (first) { shared.AddRange(e.preview.Animations); first = false; continue; }
            shared.RemoveAll(a => !Contains(e.preview.Animations, a));
        }
        return shared;
    }

    private static bool Contains(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
        return false;
    }

    // ---- Update ----------------------------------------------------------------

    private void Update()
    {
        HandleKeyboard();

        // Turn every shown model (0 = facing the camera).
        foreach (Entry e in Shown())
            e.component.transform.localRotation = e.baseRotation * Quaternion.Euler(0f, turn, 0f);

        // Centre the camera on the shown models.
        if (previewCamera != null)
        {
            float sum = 0f; int count = 0;
            foreach (Entry e in Shown()) { sum += e.component.transform.position.x; count++; }
            Vector3 target = cameraHome;
            if (count > 0) target.x = sum / count;
            if (count == 1) target.z = Mathf.Lerp(cameraHome.z, 0f, 0.25f); // a little closer for a single model
            previewCamera.transform.position = Vector3.Lerp(previewCamera.transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
        }
    }

    private void HandleKeyboard()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;
        if (k.tabKey.wasPressedThisFrame && categories.Count > 0)
            SelectCategory(categories[(categories.IndexOf(category) + 1) % categories.Count]);
        List<string> shared = SharedAnimations();
        for (int i = 0; i < Mathf.Min(9, shared.Count); i++)
            if (k[Key.Digit1 + i].wasPressedThisFrame || k[Key.Numpad1 + i].wasPressedThisFrame)
                ForShown(p => p.PlayAnimation(shared[i]));
        if (k.rKey.wasPressedThisFrame) ForShown(p => p.Restart());
        if (k.spaceKey.wasPressedThisFrame) TogglePauseShown();
        if (k.hKey.wasPressedThisFrame) visible = !visible;
    }

    private void ForShown(System.Action<IModelPreview> action)
    {
        foreach (Entry e in Shown()) action(e.preview);
    }

    private void TogglePauseShown()
    {
        bool anyPaused = false;
        foreach (Entry e in Shown()) anyPaused |= e.preview.IsPaused;
        foreach (Entry e in Shown()) if (e.preview.IsPaused == anyPaused) e.preview.TogglePause();
    }

    // ---- GUI -------------------------------------------------------------------

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

        GUILayout.BeginArea(new Rect(20, 20, 560, 1040), GUI.skin.box);
        GUILayout.Label("Model Preview", title);

        // Model type tabs.
        GUILayout.BeginHorizontal();
        foreach (string c in categories)
        {
            int count = 0;
            foreach (Entry e in entries) if (e.preview.Category == c) count++;
            if (GUILayout.Button($"{c} ({count})", c == category ? activeTab : tab)) SelectCategory(c);
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Tab = switch type", small);
        GUILayout.Space(6);

        // Every model of this type at once.
        List<string> shared = SharedAnimations();
        if (shared.Count > 0)
        {
            GUILayout.Label($"All {category}", label);
            ButtonGrid(shared, null, a => ForShown(p => p.PlayAnimation(a)), numbered: true);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Restart  [R]", button)) ForShown(p => p.Restart());
        bool anyPaused = false;
        foreach (Entry e in Shown()) anyPaused |= e.preview.IsPaused;
        if (GUILayout.Button(anyPaused ? "Resume  [Space]" : "Pause  [Space]", button)) TogglePauseShown();
        GUILayout.EndHorizontal();

        // Options.
        GUILayout.Space(6);
        float newSpeed = Slider($"Speed  {speed:0.00}x", speed, 0f, 2f);
        if (!Mathf.Approximately(newSpeed, speed)) { speed = newSpeed; ForShown(p => p.SetPlaybackSpeed(speed)); }
        turn = Slider($"Turn  {turn:0}°", turn, -180f, 180f);
        bool anyNpc = false;
        foreach (Entry e in Shown()) anyNpc |= e.component is NpcModelPreview;
        if (anyNpc)
        {
            float newSpread = Slider($"Arm spread  {armSpread:0}°", armSpread, 0f, 30f);
            if (!Mathf.Approximately(newSpread, armSpread))
            {
                armSpread = newSpread;
                foreach (Entry e in Shown()) if (e.component is NpcModelPreview npc) npc.SetArmSpread(armSpread);
            }
        }
        foreach (Entry e in Shown())
        {
            if (e.component is not PlayerModelPreview player) continue;
            bool auto = GUILayout.Toggle(player.AutoCycle, "  Auto-cycle every animation", GUI.skin.toggle);
            if (auto != player.AutoCycle) player.SetAutoCycle(auto);
            break;
        }

        // One block per model: its own animations.
        GUILayout.Space(8);
        scroll = GUILayout.BeginScrollView(scroll);
        foreach (Entry e in Shown())
        {
            GUILayout.Label(e.preview.DisplayName, label);
            IModelPreview p = e.preview;
            ButtonGrid(p.Animations, p.CurrentAnimation, a => p.PlayAnimation(a), numbered: false);
            GUILayout.Space(4);
        }
        GUILayout.EndScrollView();

        GUILayout.Label("H = hide", small);
        GUILayout.EndArea();
    }

    private void ButtonGrid(IReadOnlyList<string> names, string current, System.Action<string> onClick, bool numbered)
    {
        const int perRow = 3;
        for (int i = 0; i < names.Count; i += perRow)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < Mathf.Min(i + perRow, names.Count); j++)
            {
                string n = names[j];
                bool active = current != null ? current == n : AllPlaying(n);
                string text = numbered && j < 9 ? $"{n}  [{j + 1}]" : n;
                if (GUILayout.Button(text, active ? activeButton : button, GUILayout.Width(170))) onClick(n);
            }
            GUILayout.EndHorizontal();
        }
    }

    private bool AllPlaying(string animationName)
    {
        bool any = false;
        foreach (Entry e in Shown())
        {
            if (e.preview.CurrentAnimation != animationName) return false;
            any = true;
        }
        return any;
    }

    private float Slider(string text, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(text, label, GUILayout.Width(200));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(24));
        GUILayout.EndHorizontal();
        return value;
    }

    private void BuildStyles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
        label = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
        small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        small.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        button = new GUIStyle(GUI.skin.button) { fontSize = 18, fixedHeight = 40 };
        activeButton = new GUIStyle(button) { fontStyle = FontStyle.Bold };
        activeButton.normal.textColor = activeButton.hover.textColor = new Color(0.45f, 1f, 0.55f);
        tab = new GUIStyle(GUI.skin.button) { fontSize = 22, fixedHeight = 50 };
        activeTab = new GUIStyle(tab) { fontStyle = FontStyle.Bold };
        activeTab.normal.textColor = activeTab.hover.textColor = new Color(0.45f, 1f, 0.55f);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
