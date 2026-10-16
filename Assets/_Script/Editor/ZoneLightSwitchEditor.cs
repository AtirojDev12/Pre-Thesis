using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LightSwitchInteractable))]
[CanEditMultipleObjects]
public sealed class ZoneLightSwitchEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var zone = serializedObject.FindProperty("zoneId");
        var ids = new List<string> { "" };
        var labels = new List<string> { "Whole building (legacy)" };
        BuildingLightManager manager = null;
        foreach (GameObject root in ((LightSwitchInteractable)target).gameObject.scene.GetRootGameObjects())
        {
            manager = root.GetComponentInChildren<BuildingLightManager>(true);
            if (manager != null) break;
        }
        if (manager != null && manager.Zones != null)
            foreach (var entry in manager.Zones)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.zoneId) && !ids.Contains(entry.zoneId))
                {
                    ids.Add(entry.zoneId);
                    labels.Add(entry.zoneId);
                }
        int selected = ids.IndexOf(zone.stringValue);
        if (selected < 0)
        {
            ids.Add(zone.stringValue);
            labels.Add("Missing zone: " + zone.stringValue);
            selected = ids.Count - 1;
        }
        EditorGUI.showMixedValue = zone.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int next = EditorGUILayout.Popup("Controlled Zone", selected, labels.ToArray());
        if (EditorGUI.EndChangeCheck()) zone.stringValue = ids[next];
        EditorGUI.showMixedValue = false;
        if (manager == null || !manager.HasZone(zone.stringValue))
            EditorGUILayout.HelpBox("Choose a zone configured in Building Lights > Zone Lighting. An empty ID is a legacy switch for the entire building.", MessageType.Warning);
        if (manager != null && GUILayout.Button("Select Building Lights / Edit Zones"))
            Selection.activeGameObject = manager.gameObject;
        DrawPropertiesExcluding(serializedObject, "m_Script", "zoneId");
        serializedObject.ApplyModifiedProperties();
    }
}
