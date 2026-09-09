#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor-only helper for the one setup step Mirror cannot do for you: every
/// GameObject carrying a NetworkBehaviour (PlayerHealth, PlayerInteractor,
/// PlayerMovement, anything inheriting InteractableBase...) needs a
/// NetworkIdentity on itself or on a parent, or Mirror logs
/// "<Type> on <name> requires a NetworkIdentity" and the object will never
/// replicate.
///
/// Unity only auto-adds required components at the moment you add a script in
/// the Inspector, so components that BECAME NetworkBehaviours after the fact --
/// which is exactly what happened during the networking conversion -- have to be
/// fixed by hand. This does it in one pass, and is worth re-running whenever
/// someone adds a new interactable.
///
/// Tools > Pre-Thesis > Scan   -- report only, changes nothing.
/// Tools > Pre-Thesis > Fix    -- adds the missing NetworkIdentity components.
/// </summary>
public static class NetworkSetupFixer
{
    private const string MenuRoot = "Tools/Pre-Thesis/";

    [MenuItem(MenuRoot + "Scan for missing NetworkIdentity")]
    private static void ScanOnly() => Run(false);

    [MenuItem(MenuRoot + "Fix missing NetworkIdentity")]
    private static void FixAll() => Run(true);

    private static void Run(bool apply)
    {
        var report = new StringBuilder();
        int found = 0;

        found += FixPrefabs(apply, report);
        found += FixOpenScenes(apply, report);

        ReportNetworkManagerSetup(report);

        if (found == 0)
        {
            Debug.Log($"[NetworkSetupFixer] No missing NetworkIdentity components found.\n{report}");
            return;
        }

        if (apply)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[NetworkSetupFixer] Added {found} NetworkIdentity component(s).\n{report}");
        }
        else
        {
            Debug.LogWarning(
                $"[NetworkSetupFixer] {found} object(s) are missing a NetworkIdentity. " +
                $"Run Tools > Pre-Thesis > Fix missing NetworkIdentity to add them.\n{report}");
        }
    }

    /// <summary>
    /// Mirror accepts the identity on the object itself or on any ancestor
    /// (see NetworkBehaviour.OnValidate). GetComponentInParent(true) already
    /// includes the object itself and inactive parents, which matters because
    /// prefab contents are not considered active.
    /// </summary>
    private static bool HasIdentity(GameObject go) =>
        go.GetComponentInParent<NetworkIdentity>(true) != null;

    private static int FixPrefabs(bool apply, StringBuilder report)
    {
        int found = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Leave third-party prefabs alone -- Mirror's own examples are
            // already correct, and we should not be editing package content.
            if (!path.StartsWith("Assets/")) continue;
            if (path.StartsWith("Assets/Mirror/")) continue;
            if (path.StartsWith("Assets/TextMesh Pro/")) continue;
            if (path.StartsWith("Assets/Samples/")) continue;

            // Check the read-only asset FIRST and only open the prefab for
            // editing when something actually needs changing. LoadPrefabContents
            // spins up a hidden scene and runs Awake on everything inside, which
            // is slow across a whole project and makes unrelated prefabs (TMP's
            // samples, for one) spew their own startup warnings into the console.
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            var needsIdentity = new List<string>();
            foreach (NetworkBehaviour behaviour in asset.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                if (behaviour == null || HasIdentity(behaviour.gameObject)) continue;

                found++;
                needsIdentity.Add(behaviour.gameObject.name);
                report.AppendLine($"  prefab  {path} -> {behaviour.gameObject.name} ({behaviour.GetType().Name})");
            }

            if (!apply || needsIdentity.Count == 0) continue;

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (NetworkBehaviour behaviour in contents.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    if (behaviour == null || HasIdentity(behaviour.gameObject)) continue;
                    behaviour.gameObject.AddComponent<NetworkIdentity>();
                    changed = true;
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                // Always unload, even if something above threw, or the prefab
                // stays open in memory and Unity leaks the editing scene.
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        return found;
    }

    private static int FixOpenScenes(bool apply, StringBuilder report)
    {
        int found = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (NetworkBehaviour behaviour in root.GetComponentsInChildren<NetworkBehaviour>(true))
                {
                    if (behaviour == null || HasIdentity(behaviour.gameObject)) continue;

                    // Objects that came from a prefab are fixed by editing the
                    // prefab asset above; adding it here would only create a
                    // local override on this one instance.
                    if (PrefabUtility.IsPartOfPrefabInstance(behaviour.gameObject)) continue;

                    found++;
                    report.AppendLine($"  scene   {scene.name} -> {behaviour.gameObject.name} ({behaviour.GetType().Name})");

                    if (apply)
                    {
                        Undo.AddComponent<NetworkIdentity>(behaviour.gameObject);
                        changed = true;
                    }
                }
            }

            if (changed) EditorSceneManager.MarkSceneDirty(scene);
        }

        return found;
    }

    /// <summary>
    /// Read-only sanity check. Deliberately does not auto-assign anything --
    /// which prefab is "the player" is a decision, not something to guess.
    /// </summary>
    private static void ReportNetworkManagerSetup(StringBuilder report)
    {
        // No FindObjectsSortMode overload -- Unity 6.5 marks that one obsolete.
        var managers = Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include);

        foreach (NetworkManager manager in managers)
        {
            if (manager.playerPrefab == null)
            {
                report.AppendLine(
                    $"  NOTE    NetworkManager '{manager.name}' has no Player Prefab assigned -- " +
                    "nobody will spawn into a match until you set it.");
            }
            else if (manager.playerPrefab.GetComponent<NetworkIdentity>() == null)
            {
                report.AppendLine(
                    $"  NOTE    NetworkManager '{manager.name}' Player Prefab " +
                    $"'{manager.playerPrefab.name}' has no NetworkIdentity.");
            }
        }
    }
}
#endif
