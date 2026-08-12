#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SaveTester))]
public class SaveTesterEditor : Editor
{
    public override bool RequiresConstantRepaint()
    {
        // Continuously update the inspector only while playing so we see live value changes
        return Application.isPlaying;
    }

    public override void OnInspectorGUI()
    {
        // Draw the default inspector (which draws the ContextMenu script buttons)
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live Save Data (Read-Only)", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to view live save data.", MessageType.Info);
            return;
        }

        if (SaveManager.Current == null)
        {
            EditorGUILayout.HelpBox("SaveManager.Current is not initialized.", MessageType.Warning);
            return;
        }

        // Constraint 1: Must be strictly read-only
        EditorGUI.BeginDisabledGroup(true); 

        // Currency
        EditorGUILayout.IntField("Currency", SaveManager.Current.currency);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Consumables", EditorStyles.boldLabel);
        if (SaveManager.Current.consumables == null || SaveManager.Current.consumables.Count == 0)
        {
            EditorGUILayout.LabelField("  [Empty]");
        }
        else
        {
            foreach (var item in SaveManager.Current.consumables)
            {
                EditorGUILayout.IntField($"  {item.itemID}", item.quantity);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Permanent Items", EditorStyles.boldLabel);
        if (SaveManager.Current.permanentItems == null || SaveManager.Current.permanentItems.Count == 0)
        {
            EditorGUILayout.LabelField("  [Empty]");
        }
        else
        {
            foreach (var item in SaveManager.Current.permanentItems)
            {
                EditorGUILayout.Toggle($"  {item.itemID}", item.isOwned);
            }
        }

        EditorGUI.EndDisabledGroup();
    }
}
#endif
