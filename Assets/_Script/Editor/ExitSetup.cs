using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 31 Oct 2026 (Mr.k): add another exit to the open map in one click.
///
///   Tools > Pre-Thesis > Scenes > Map: Add Another Exit
///       Copies the first ExitPoint in the scene (with its walk-in box and glow),
///       gives it a new network scene ID and the name "Exit 2" (3, 4...), and puts it
///       where the Scene view is looking. Move it to the right place, then save the scene.
/// </summary>
public static class ExitSetup
{
    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Add Another Exit", false, 61)]
    private static void AddExit()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Add Exit", "Stop Play mode first.", "OK"); return; }
        ExitPoint[] exits = Object.FindObjectsByType<ExitPoint>(FindObjectsInactive.Include);
        if (exits.Length == 0)
        {
            EditorUtility.DisplayDialog("Add Exit", "No ExitPoint in the open scene to copy. Open Cinema_GamePlay first.", "OK");
            return;
        }
        ExitPoint source = exits[0];
        int number = exits.Length + 1;

        GameObject copy = Object.Instantiate(source.gameObject, source.transform.parent);
        copy.name = "Exit " + number;
        Undo.RegisterCreatedObjectUndo(copy, "Add Exit");

        // Where the Scene view looks (keep the original height so it stays on the floor).
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            Vector3 p = view.pivot;
            copy.transform.position = new Vector3(p.x, source.transform.position.y, p.z);
        }
        else copy.transform.position = source.transform.position + source.transform.right * 3f;

        // New identity: a copied scene ID would clash with the original exit.
        NetworkIdentity identity = copy.GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            var so = new SerializedObject(identity);
            SerializedProperty sceneId = so.FindProperty("sceneId");
            if (sceneId != null) { sceneId.longValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
        }
        ExitPoint exit = copy.GetComponent<ExitPoint>();
        if (exit != null)
        {
            var so = new SerializedObject(exit);
            SerializedProperty name = so.FindProperty("exitName");
            if (name != null) { name.stringValue = "Exit " + number; so.ApplyModifiedPropertiesWithoutUndo(); }
        }

        EditorSceneManager.MarkSceneDirty(copy.scene);
        Selection.activeGameObject = copy;
        EditorGUIUtility.PingObject(copy);
        Debug.Log($"[ExitSetup] Added '{copy.name}' (copy of '{source.name}'). Move it into place and save the scene.", copy);
    }
}
