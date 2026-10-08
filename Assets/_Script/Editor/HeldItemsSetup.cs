using UnityEditor;
using UnityEngine;

public static class HeldItemsSetup
{
    [MenuItem("Tools/Pre-Thesis/One Step/Player: Network Held Items", false, 111)]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        const string path = "Assets/Prefab/Player.prefab";
        GameObject player = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (player.GetComponent<PlayerHeldItems>() == null) player.AddComponent<PlayerHeldItems>();
            PrefabUtility.SaveAsPrefabAsset(player, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
    }
}
