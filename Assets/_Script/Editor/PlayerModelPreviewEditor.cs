using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector buttons for PlayerModelPreview: one button per player action,
/// plus restart / pause / auto-cycle, while in Play Mode.
/// </summary>
[CustomEditor(typeof(PlayerModelPreview))]
public sealed class PlayerModelPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Player Animation Preview", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Animations play in Play Mode. Press the button below (or Play) first.", MessageType.Info);
            if (GUILayout.Button("Enter Play Mode", GUILayout.Height(28))) EditorApplication.isPlaying = true;
            return;
        }

        var preview = (PlayerModelPreview)target;
        PlayerModelPreview.PreviewAction[] actions = PlayerModelPreview.Actions;
        for (int i = 0; i < actions.Length; i += 3)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = i; j < Mathf.Min(i + 3, actions.Length); j++)
                {
                    PlayerModelPreview.PreviewAction a = actions[j];
                    if (Button(PlayerModelPreview.Label(a), preview.CurrentAction == a)) preview.Play(a);
                }
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Restart", GUILayout.Height(24))) preview.Restart();
            if (GUILayout.Button(preview.IsPaused ? "Resume" : "Pause", GUILayout.Height(24))) preview.TogglePause();
            if (Button(preview.AutoCycle ? "Auto: On" : "Auto: Off", preview.AutoCycle, 24)) preview.SetAutoCycle(!preview.AutoCycle);
        }

        // Keep the button highlight in sync while playing.
        Repaint();
    }

    private static bool Button(string label, bool active, float height = 30f)
    {
        Color previous = GUI.backgroundColor;
        if (active) GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
        bool pressed = GUILayout.Button(label, GUILayout.Height(height));
        GUI.backgroundColor = previous;
        return pressed;
    }
}
