using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Nov 2026 (Mr.k): notes every UI change a designer makes in Play mode (Inspector,
/// Rect tool, anchor presets ...) so the UI Hub can save it into the UI Layout asset.
///
/// Only edits by a person are noted (Unity's Undo system reports them), never changes
/// the game code makes by itself. Nothing is saved until Save is pressed. When Play
/// stops with unsaved edits, a dialog asks Save / Discard (the objects still exist then).
/// </summary>
[InitializeOnLoad]
public static class UiChangeRecorder
{
    public struct Pending
    {
        public string screen;
        public string path;
        public UiLookGroup group;
        public Transform target;
    }

    private static readonly List<Pending> pending = new List<Pending>();

    public static IReadOnlyList<Pending> PendingList => pending;
    public static event Action Changed;

    static UiChangeRecorder()
    {
        Undo.postprocessModifications += OnModifications;
        ObjectChangeEvents.changesPublished += OnChangesPublished;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // ---- Noting edits -----------------------------------------------------------------------

    private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] mods)
    {
        if (!Application.isPlaying || mods == null) return mods;
        for (int i = 0; i < mods.Length; i++)
        {
            PropertyModification m = mods[i].currentValue;
            if (m == null || m.target == null) continue;
            UiLookGroup? group = GroupOf(m.target, m.propertyPath);
            if (group.HasValue) Note(m.target, group.Value);
        }
        return mods;
    }

    // Anchor presets and some Scene-view tools change a RectTransform without a property report.
    private static void OnChangesPublished(ref ObjectChangeEventStream stream)
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < stream.length; i++)
        {
            if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
            stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs e);
            UnityEngine.Object obj = EditorUtility.EntityIdToObject(e.entityId);
            if (obj is RectTransform rt && Selection.Contains(rt.gameObject)) Note(rt, UiLookGroup.Layout);
        }
    }

    private static bool StartsWith(string s, string prefix) => s.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Which saved group a changed property belongs to (null = not saved).</summary>
    private static UiLookGroup? GroupOf(UnityEngine.Object target, string p)
    {
        if (target is GameObject) return p == "m_IsActive" ? UiLookGroup.Hide : (UiLookGroup?)null;
        if (target is RectTransform) return UiLookGroup.Layout;
        if (target is TMP_Text)
        {
            if (p == "m_text") return UiLookGroup.Text;
            if (StartsWith(p, "m_fontColor") || StartsWith(p, "m_Color")) return UiLookGroup.Color;
            if (StartsWith(p, "m_fontAsset") || StartsWith(p, "m_fontSize") || StartsWith(p, "m_fontStyle") || StartsWith(p, "m_fontWeight")
                || StartsWith(p, "m_HorizontalAlignment") || StartsWith(p, "m_VerticalAlignment") || StartsWith(p, "m_textAlignment")
                || StartsWith(p, "m_enableAutoSizing") || StartsWith(p, "m_characterSpacing") || StartsWith(p, "m_lineSpacing")
                || StartsWith(p, "m_TextWrappingMode") || StartsWith(p, "m_enableWordWrapping"))
                return UiLookGroup.TextStyle;
            return null;
        }
        if (target is Text)
        {
            if (p == "m_Text") return UiLookGroup.Text;
            if (StartsWith(p, "m_FontData")) return UiLookGroup.TextStyle;
            if (StartsWith(p, "m_Color")) return UiLookGroup.Color;
            return null;
        }
        if (target is Image && StartsWith(p, "m_Sprite")) return UiLookGroup.Sprite;
        if (target is Graphic && StartsWith(p, "m_Color")) return UiLookGroup.Color;
        if (target is HorizontalOrVerticalLayoutGroup && (StartsWith(p, "m_Spacing") || StartsWith(p, "m_Padding"))) return UiLookGroup.Spacing;
        if (target is LayoutElement && (StartsWith(p, "m_Min") || StartsWith(p, "m_Preferred"))) return UiLookGroup.Size;
        return null;
    }

    private static void Note(UnityEngine.Object target, UiLookGroup group)
    {
        Transform t = target is Component c ? c.transform : target is GameObject go ? go.transform : null;
        if (t == null || EditorUtility.IsPersistent(t) || PrefabStageUtility.GetPrefabStage(t.gameObject) != null) return;
        Canvas canvas = t.GetComponentInParent<Canvas>(true);
        if (canvas == null) return;
        Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        if (t == root.transform) return; // whole-screen settings live in the UI Hub
        if (root.name.StartsWith(UiLayoutRuntime.DesignPrefix)) return; // the designer's own prefab: edit the prefab itself

        string screen = UiLayoutRuntime.ScreenName(root.transform);
        string path = UiLayoutRuntime.PathOf(root.transform, t);
        for (int i = 0; i < pending.Count; i++)
            if (pending[i].screen == screen && pending[i].path == path && pending[i].group == group) return;

        pending.Add(new Pending { screen = screen, path = path, group = group, target = t });
        UiLayoutRuntime.EditorSuspended.Add(UiLayoutRuntime.Key(screen, path, group)); // don't set it back while editing
        Changed?.Invoke();
    }

    // ---- Save / discard ---------------------------------------------------------------------

    /// <summary>The project's UI Layout asset (made in Assets/Resources/UI if missing).</summary>
    public static UiLayoutData EnsureData()
    {
        UiLayoutData data = UiLayoutData.Load();
        if (data != null) return data;
        const string folder = "Assets/Resources/UI";
        string path = folder + "/UI Layout.asset";
        data = AssetDatabase.LoadAssetAtPath<UiLayoutData>(path);
        if (data == null)
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            data = ScriptableObject.CreateInstance<UiLayoutData>();
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[UI Hub] Made " + path);
        }
        UiLayoutData.SetLoaded(data);
        return data;
    }

    /// <summary>Saves every noted edit. Returns how many were saved.</summary>
    public static int Save()
    {
        if (pending.Count == 0) return 0;
        UiLayoutData data = EnsureData();
        Undo.RecordObject(data, "Save UI changes");
        int saved = 0, lost = 0;
        foreach (Pending p in pending)
        {
            Transform t = p.target != null ? p.target : FindLive(p.screen, p.path);
            if (t == null) { lost++; continue; }
            // The screen has an editable prefab: write the edit into it (the prefab stays the one place to edit).
            if (UiSkinTools.WriteToSkin(p.screen, p.path, p.group, t)) { saved++; continue; }
            UiScreenLook screen = data.GetOrAddScreen(p.screen);
            UiElementLook e = screen.GetOrAddElement(p.path);
            if (!UiLayoutRuntime.Capture(t, e, p.group)) { lost++; continue; }
            if (p.group == UiLookGroup.Hide && !e.hide) e.SetUses(UiLookGroup.Hide, false); // shown again = no hide change
            if (e.Empty) screen.elements.Remove(e);
            saved++;
        }
        ClearPending();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        data.MarkChanged();
        Debug.Log($"[UI Hub] Saved {saved} UI change(s)" + (lost > 0 ? $"; {lost} could not be saved (the object was gone)." : "."));
        return saved;
    }

    public static void Discard()
    {
        ClearPending();
        UiLayoutData data = UiLayoutData.Load();
        if (data != null) data.MarkChanged(); // saved looks come back
    }

    private static void ClearPending()
    {
        pending.Clear();
        UiLayoutRuntime.EditorSuspended.Clear();
        Changed?.Invoke();
    }

    public static Transform FindLive(string screen, string path)
    {
        foreach (UiLayoutRuntime.LiveScreen s in UiLayoutRuntime.Live)
        {
            if (s.root == null || s.name != screen) continue;
            Transform t = string.IsNullOrEmpty(path) ? s.root : UiLayoutRuntime.Resolve(s.root, path);
            if (t != null) return t;
        }
        return null;
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode && pending.Count > 0)
        {
            bool save = EditorUtility.DisplayDialog("UI Hub",
                $"You changed {pending.Count} UI part(s) in Play mode.\n\nSave them? They will come back every time the screen appears.",
                "Save", "Discard");
            if (save) Save(); else ClearPending();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            ClearPending();
        }
    }
}
