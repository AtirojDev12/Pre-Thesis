using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>Authors the shared radio visual, network pickup, and player references.</summary>
public static class WalkieTalkiePrefabSetup
{
    [MenuItem("Tools/Pre-Thesis/One Step/Items: Walkie-Talkie", false, 121)]
    public static void BuildAssets()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Items")) AssetDatabase.CreateFolder("Assets/Resources", "Items");
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/WalkieTalkieBody.mat");
        GameObject root = new GameObject("WalkieTalkie");
        GameObject visualPrefab;
        try
        {
            Part(root.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(.07f, .16f, .04f), material);
            Part(root.transform, "Antenna", PrimitiveType.Cylinder, new Vector3(.021f, .12f, 0), new Vector3(.0105f, .056f, .012f), material);
            Renderer led = Part(root.transform, "Power Indicator", PrimitiveType.Sphere, new Vector3(-.0175f, .0672f, -.022f), new Vector3(.0175f, .016f, .012f), material);
            WalkieTalkieVisual visual = root.AddComponent<WalkieTalkieVisual>();
            SerializedObject settings = new SerializedObject(visual);
            settings.FindProperty("ledRenderer").objectReferenceValue = led;
            settings.ApplyModifiedPropertiesWithoutUndo();
            visualPrefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Items/WalkieTalkie.prefab");
        }
        finally { Object.DestroyImmediate(root); }

        root = new GameObject("WorldWalkieTalkie");
        GameObject worldPrefab;
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab);
            model.transform.SetParent(root.transform, false);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(.08f, .27f, .055f);
            collider.center = new Vector3(0, .045f, 0);
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = .35f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            worldPrefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Items/WorldWalkieTalkie.prefab");
            // Force Mirror's editor-only GUID assignment and persist it for standalone builds.
            NetworkIdentity identity = worldPrefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException("World radio has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(worldPrefab);
        }
        finally { Object.DestroyImmediate(root); }

        root = PrefabUtility.LoadPrefabContents("Assets/Prefab/Player.prefab");
        try
        {
            if (root.GetComponent<PlayerItemThrow>() == null) root.AddComponent<PlayerItemThrow>();
            if (root.GetComponent<PlayerHeldItems>() == null) root.AddComponent<PlayerHeldItems>();
            SerializedObject controller = new SerializedObject(root.GetComponent<WalkieTalkieController>());
            controller.FindProperty("heldPrefab").objectReferenceValue = visualPrefab;
            controller.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefab/Player.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        root = PrefabUtility.LoadPrefabContents("Assets/Prefab/NetworkManager.prefab");
        try
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            if (!manager.spawnPrefabs.Contains(worldPrefab)) manager.spawnPrefabs.Add(worldPrefab);
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefab/NetworkManager.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }

    private static Renderer Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }
}
