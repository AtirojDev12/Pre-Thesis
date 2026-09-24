using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One click: puts a MatchDirector in the open scene and fills its lists.
/// Menu: Tools > Pre-Thesis > Add MatchDirector to Open Scene
///
/// - Adds a "MatchDirector" GameObject with NetworkIdentity + MatchDirector.
/// - Fills Difficulty Profiles with every DifficultyProfile asset in the project.
/// - Sets the Normal profile as the fallback.
/// - Fills Ghost Rosters with every MapGhostRoster asset.
/// Safe to run again: if a MatchDirector already exists it only refreshes the lists.
/// Save the scene afterwards (Ctrl+S).
/// </summary>
public static class MatchDirectorSetup
{
    private const string GameplayScene = "Cinema_GamePlay";

    [MenuItem("Tools/Pre-Thesis/Add MatchDirector to Open Scene")]
    private static void AddToOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != GameplayScene &&
            !EditorUtility.DisplayDialog("Add MatchDirector",
                $"The open scene is '{scene.name}', not '{GameplayScene}'. Add a MatchDirector here anyway?",
                "Add", "Cancel"))
            return;

        MatchDirector director = Object.FindAnyObjectByType<MatchDirector>();
        bool created = director == null;
        if (created)
        {
            var go = new GameObject("MatchDirector");
            Undo.RegisterCreatedObjectUndo(go, "Add MatchDirector");
            go.AddComponent<NetworkIdentity>();
            director = go.AddComponent<MatchDirector>();
        }

        var so = new SerializedObject(director);

        // Difficulty profiles + Normal as fallback.
        var profiles = so.FindProperty("difficultyProfiles");
        profiles.ClearArray();
        DifficultyProfile normal = null;
        foreach (string guid in AssetDatabase.FindAssets("t:DifficultyProfile"))
        {
            var profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(AssetDatabase.GUIDToAssetPath(guid));
            if (profile == null) continue;
            profiles.InsertArrayElementAtIndex(profiles.arraySize);
            profiles.GetArrayElementAtIndex(profiles.arraySize - 1).objectReferenceValue = profile;
            if (profile.level == DifficultyLevel.Normal) normal = profile;
        }
        so.FindProperty("fallbackProfile").objectReferenceValue = normal;

        // Ghost rosters (one per map).
        var rosters = so.FindProperty("ghostRosters");
        rosters.ClearArray();
        foreach (string guid in AssetDatabase.FindAssets("t:MapGhostRoster"))
        {
            var roster = AssetDatabase.LoadAssetAtPath<MapGhostRoster>(AssetDatabase.GUIDToAssetPath(guid));
            if (roster == null) continue;
            rosters.InsertArrayElementAtIndex(rosters.arraySize);
            rosters.GetArrayElementAtIndex(rosters.arraySize - 1).objectReferenceValue = roster;
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = director.gameObject;

        Debug.Log($"[MatchDirectorSetup] {(created ? "Added" : "Updated")} MatchDirector in '{scene.name}': " +
                  $"{profiles.arraySize} difficulty profiles (fallback: {(normal != null ? normal.name : "NONE")}), " +
                  $"{rosters.arraySize} ghost rosters. Save the scene (Ctrl+S).", director);

        if (normal == null)
            Debug.LogWarning("[MatchDirectorSetup] No Normal DifficultyProfile found — set the Fallback Profile by hand.", director);
    }
}
