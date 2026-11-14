using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector buttons for NpcModelPreview: play Idle / Walk, restart, pause,
/// for the selected NPC or for every NPC in the scene at once.
/// </summary>
[CustomEditor(typeof(NpcModelPreview))]
[CanEditMultipleObjects]
public sealed class NpcModelPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Animation Preview", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Animations play in Play Mode. Press the button below (or Play) first.", MessageType.Info);
            if (GUILayout.Button("Enter Play Mode", GUILayout.Height(28))) EditorApplication.isPlaying = true;
            return;
        }

        // This NPC (or every selected NPC when several are selected).
        EditorGUILayout.LabelField(targets.Length > 1 ? $"Selected ({targets.Length})" : "This NPC", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (Button("Play Idle", !AnyWalking() && !AnyPaused())) ForSelected(p => p.PlayIdle());
            if (Button("Play Walk", AllWalking() && !AnyPaused())) ForSelected(p => p.PlayWalk());
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Restart", GUILayout.Height(24))) ForSelected(p => p.Restart());
            if (GUILayout.Button(AnyPaused() ? "Resume" : "Pause", GUILayout.Height(24))) ForSelected(p => p.TogglePause());
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("All NPCs in the scene", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("All: Idle", GUILayout.Height(24))) ForAll(p => p.PlayIdle());
            if (GUILayout.Button("All: Walk", GUILayout.Height(24))) ForAll(p => p.PlayWalk());
            if (GUILayout.Button("All: Restart", GUILayout.Height(24))) ForAll(p => p.Restart());
        }

        // Keep the button highlight in sync while playing.
        Repaint();
    }

    private static bool Button(string label, bool active)
    {
        Color previous = GUI.backgroundColor;
        if (active) GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
        bool pressed = GUILayout.Button(label, GUILayout.Height(30));
        GUI.backgroundColor = previous;
        return pressed;
    }

    private void ForSelected(System.Action<NpcModelPreview> action)
    {
        foreach (Object t in targets)
            if (t is NpcModelPreview preview) action(preview);
    }

    private static void ForAll(System.Action<NpcModelPreview> action)
    {
        foreach (NpcModelPreview preview in Object.FindObjectsByType<NpcModelPreview>(FindObjectsSortMode.None))
            action(preview);
    }

    private bool AnyWalking() { foreach (Object t in targets) if (t is NpcModelPreview p && p.IsWalking) return true; return false; }
    private bool AllWalking() { foreach (Object t in targets) if (t is NpcModelPreview p && !p.IsWalking) return false; return true; }
    private bool AnyPaused() { foreach (Object t in targets) if (t is NpcModelPreview p && p.IsPaused) return true; return false; }
}
