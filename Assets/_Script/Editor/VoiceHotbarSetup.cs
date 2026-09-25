using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One click: adds voice chat + hotbar + Walkie-Talkie to Player.prefab.
/// Menu: Tools > Pre-Thesis > Add Voice + Hotbar to Player Prefab
///
/// Adds (if missing): PlayerVoice, PlayerInventory, WalkieTalkieController.
/// Safe to run again. Nothing else in the prefab is changed.
/// VoiceChatManager needs no setup: it creates itself when the game starts.
/// </summary>
public static class VoiceHotbarSetup
{
    private const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";

    [MenuItem("Tools/Pre-Thesis/Add Voice + Hotbar to Player Prefab")]
    private static void AddToPlayerPrefab()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Voice + Hotbar", "Stop Play mode first.", "OK");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Voice + Hotbar", "Could not open " + PlayerPrefabPath, "OK");
            return;
        }

        try
        {
            if (root.GetComponent<NetworkIdentity>() == null)
            {
                EditorUtility.DisplayDialog("Voice + Hotbar", "Player.prefab has no NetworkIdentity on its root. Nothing changed.", "OK");
                return;
            }

            // Order matters: WalkieTalkieController requires the other two.
            int added = 0;
            if (root.GetComponent<PlayerVoice>() == null) { root.AddComponent<PlayerVoice>(); added++; }
            if (root.GetComponent<PlayerInventory>() == null) { root.AddComponent<PlayerInventory>(); added++; }
            if (root.GetComponent<WalkieTalkieController>() == null) { root.AddComponent<WalkieTalkieController>(); added++; }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);

            string message = added == 0
                ? "Player.prefab already has PlayerVoice, PlayerInventory and WalkieTalkieController."
                : $"Added {added} component(s) to Player.prefab.\n\nNext: Tools > Pre-Thesis > Rebuild Settings + Pause Menu (adds the Controls section to Settings).";
            Debug.Log("[VoiceHotbarSetup] " + message);
            EditorUtility.DisplayDialog("Voice + Hotbar", message, "OK");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
