using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 24 Oct 2026 (Mr.k). The Map item.
///
///   Tools > Pre-Thesis > One Step > Items: Map
///       1. Assets/Prefab/Map.prefab — a plain cube for the designer (material, size, shape).
///          Made ONLY if it does not exist yet: the designer's work is never overwritten.
///       2. Resources/Items/WorldMap.prefab — the dropped / thrown map (network object).
///          It holds Map.prefab as a child, so changes to Map.prefab show up everywhere
///          (dropped map and the map in your hand). Rebuilt every time.
///       3. Registers WorldMap in NetworkManager.spawnPrefabs (clients can see a dropped map).
///
///   Tools > Pre-Thesis > Scenes > Map: Make Selected the Map Box
///       Adds MapPickup to the selected object in the open scene (E = get the map).
/// </summary>
public static class MapSetup
{
    private const string MapPath = "Assets/Prefab/Map.prefab";
    private const string WorldPath = "Assets/Resources/Items/WorldMap.prefab";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";

    [MenuItem("Tools/Pre-Thesis/One Step/Items: Map", false, 125)]
    public static void BuildAssets()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Map", "Stop Play mode first.", "OK"); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Prefab")) AssetDatabase.CreateFolder("Assets", "Prefab");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Items")) AssetDatabase.CreateFolder("Assets/Resources", "Items");

        // ---- 1. Designer's model (only if missing) ----
        GameObject mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
        bool madeMap = false;
        if (mapPrefab == null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Map";
            try { mapPrefab = PrefabUtility.SaveAsPrefabAsset(cube, MapPath); }
            finally { Object.DestroyImmediate(cube); }
            madeMap = true;
        }

        // ---- 2. World pickup (like WorldFlashlight). Map.prefab is its first child. ----
        GameObject root = new GameObject("WorldMap");
        GameObject worldPrefab;
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
            model.transform.SetParent(root.transform, false);
            // Fit the dropped map to about 30 cm, whatever size the designer's cube is.
            Bounds bounds = BoundsOf(model);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > 0.0001f) model.transform.localScale *= 0.3f / longest;
            // 26 Oct: thin like paper (1% of its width), so dropped / friends' maps are not a fat cube.
            bounds = BoundsOf(model);
            float face = Mathf.Max(bounds.size.x, bounds.size.y);
            if (bounds.size.z > face * 0.01f)
            {
                Vector3 s = model.transform.localScale;
                s.z *= face * 0.01f / bounds.size.z;
                model.transform.localScale = s;
            }
            bounds = BoundsOf(model);
            model.transform.position -= bounds.center;
            // The cube's own collider is used (pickup ray + physics). No collider on the model = add a box.
            if (model.GetComponentInChildren<Collider>(true) == null)
            {
                bounds = BoundsOf(model);
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = bounds.center;
                collider.size = Vector3.Max(bounds.size, new Vector3(0.02f, 0.02f, 0.02f));
            }
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            worldPrefab = PrefabUtility.SaveAsPrefabAsset(root, WorldPath);
            NetworkIdentity identity = worldPrefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException("World map has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(worldPrefab);
        }
        finally { Object.DestroyImmediate(root); }

        // ---- 3. Register with Mirror ----
        root = PrefabUtility.LoadPrefabContents(NetworkManagerPath);
        try
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            if (!manager.spawnPrefabs.Contains(worldPrefab)) manager.spawnPrefabs.Add(worldPrefab);
            PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        AssetDatabase.SaveAssets();
        Debug.Log("[MapSetup] " + (madeMap ? "Created " + MapPath + " (plain cube for the designer). " : "Kept the designer's " + MapPath + ". ") +
                  "Built " + WorldPath + " and registered it with Mirror. Run this again after changing Map.prefab's size.", mapPrefab);
        Selection.activeObject = mapPrefab;
        EditorGUIUtility.PingObject(mapPrefab);
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Make Selected the Map Box", false, 60)]
    private static void MakeMapBox()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Map Box", "Stop Play mode first.", "OK"); return; }
        GameObject box = Selection.activeGameObject;
        if (box == null || !box.scene.IsValid())
        {
            EditorUtility.DisplayDialog("Map Box", "Select the box in the Hierarchy (a scene object) first.", "OK");
            return;
        }
        // 24 Oct: Mirror's NetworkBehaviour does not add a NetworkIdentity by itself, so add it here.
        if (box.GetComponent<NetworkIdentity>() == null) Undo.AddComponent<NetworkIdentity>(box);
        if (box.GetComponent<MapPickup>() == null) Undo.AddComponent<MapPickup>(box);
        if (box.GetComponentInChildren<Collider>() == null) Undo.AddComponent<BoxCollider>(box);
        EditorSceneManager.MarkSceneDirty(box.scene);
        Debug.Log("[MapSetup] " + box.name + " is now the map box (E = get the map). Save the scene.", box);
    }

    private static Bounds BoundsOf(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
