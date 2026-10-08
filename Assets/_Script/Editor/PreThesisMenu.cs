using UnityEditor;
using UnityEngine;

/// <summary>
/// 8 Oct (Mr.k): "too many tools". The Tools > Pre-Thesis menu is now grouped:
///
///   Player Prefab: Add All Components    voice + hotbar, held items, sanity (adds only, safe)
///   Build All Item Prefabs               walkie, flashlight + battery, sanity items (REBUILDS them)
///   Designer Settings >                  Flashlight, Sanity
///   Scenes >                             Menu: ... / Lobby: ... / Map: ...
///   Network >                            Scan / Fix missing NetworkIdentity
///   One Step >                           each setup step on its own
///   Audio >, Lighting >                  unchanged
///
/// The two combined commands just run the "One Step" items in order.
/// </summary>
public static class PreThesisMenu
{
    private const string Root = "Tools/Pre-Thesis/";
    private const string VoiceHotbar = Root + "One Step/Player: Voice + Hotbar";
    private const string HeldItems = Root + "One Step/Player: Network Held Items";
    private const string Walkie = Root + "One Step/Items: Walkie-Talkie";
    private const string Flashlight = Root + "One Step/Items: Flashlight + Battery";
    private const string Sanity = Root + "One Step/Player + Items: Sanity";

    [MenuItem(Root + "Player Prefab: Add All Components", false, 0)]
    private static void PlayerPrefabAll()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Player Prefab", "Stop Play mode first.", "OK"); return; }
        Run(VoiceHotbar);
        Run(HeldItems);
        bool added = SanitySetup.AddToPlayerPrefab();
        Debug.Log("[PreThesisMenu] Player.prefab checked: voice + hotbar, held items" +
                  (added ? ", PlayerSanity added." : ", sanity (already there).") + " Only missing parts were added.");
    }

    [MenuItem(Root + "Build All Item Prefabs", false, 1)]
    private static void AllItemPrefabs()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Item Prefabs", "Stop Play mode first.", "OK"); return; }
        if (!EditorUtility.DisplayDialog("Build All Item Prefabs",
                "Rebuilds from code: Walkie-Talkie, Flashlight, Battery, Snack, Holy Book, Amulet, Cross.\n\n" +
                "Any change made BY HAND inside those prefabs (models, materials) is lost.\n\nContinue?",
                "Rebuild", "Cancel"))
            return;
        Run(Walkie);
        Run(Flashlight);
        Run(Sanity);
        Debug.Log("[PreThesisMenu] All item prefabs rebuilt and registered with Mirror. Commit the changed prefabs.");
    }

    private static void Run(string menuPath)
    {
        if (!EditorApplication.ExecuteMenuItem(menuPath))
            Debug.LogError("[PreThesisMenu] Menu item not found: " + menuPath);
    }
}
