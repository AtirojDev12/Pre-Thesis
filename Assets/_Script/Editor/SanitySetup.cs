using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 8 Oct (Mr.k). One click to set up the sanity system:
///
///   Tools > Pre-Thesis > One Step > Player + Items: Sanity
///     1. Creates Resources/Tuning/SanitySettings.asset (the designer's numbers)
///     2. Adds PlayerSanity to Assets/Prefab/Player.prefab (needed for online sync)
///     3. Builds the dropped-item prefabs (Snack, Holy Book, Amulet, Cross) in
///        Resources/Items and registers them in NetworkManager.spawnPrefabs
///   Safe to run again: it only adds what is missing and rebuilds the item shapes.
///
///   Tools > Pre-Thesis > Designer Settings > Sanity   (selects the settings asset)
///
/// Commit everything it changes (Player.prefab, NetworkManager.prefab, the new assets).
/// </summary>
public static class SanitySetup
{
    private const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";
    private const string ItemFolder = "Assets/Resources/Items";
    private const string MaterialFolder = "Assets/Resources/Items/Materials";

    [MenuItem("Tools/Pre-Thesis/One Step/Player + Items: Sanity", false, 112)]
    public static void Setup()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        Folder("Assets/Resources");
        Folder("Assets/Resources/Tuning");
        Folder(ItemFolder);
        Folder(MaterialFolder);

        SanitySettings settings = SettingsAsset();
        bool addedToPlayer = AddToPlayerPrefab();

        GameObject snack = BuildItem("WorldSnack", root =>
            Part(root, "Bag", PrimitiveType.Cube, Vector3.zero, new Vector3(0.14f, 0.2f, 0.05f), "Snack"));
        GameObject book = BuildItem("WorldHolyBook", root =>
        {
            Part(root, "Book", PrimitiveType.Cube, Vector3.zero, new Vector3(0.16f, 0.04f, 0.22f), "HolyBook");
            Part(root, "Cross V", PrimitiveType.Cube, new Vector3(0f, 0.021f, 0f), new Vector3(0.015f, 0.003f, 0.1f), "Gold");
            Part(root, "Cross H", PrimitiveType.Cube, new Vector3(0f, 0.021f, 0.02f), new Vector3(0.06f, 0.003f, 0.015f), "Gold");
        });
        GameObject amulet = BuildItem("WorldAmulet", root =>
        {
            Part(root, "Frame", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.06f, 0.006f, 0.06f), "Gold");
            Part(root, "Face", PrimitiveType.Cylinder, new Vector3(0f, 0.004f, 0f), new Vector3(0.045f, 0.004f, 0.045f), "AmuletFace");
        });
        GameObject cross = BuildItem("WorldHolyCross", root =>
        {
            Part(root, "Vertical", PrimitiveType.Cube, Vector3.zero, new Vector3(0.03f, 0.2f, 0.02f), "Cross");
            Part(root, "Horizontal", PrimitiveType.Cube, new Vector3(0f, 0.045f, 0f), new Vector3(0.12f, 0.03f, 0.02f), "Cross");
        });

        GameObject manager = PrefabUtility.LoadPrefabContents(NetworkManagerPath);
        try
        {
            NetworkManager nm = manager.GetComponent<NetworkManager>();
            foreach (GameObject prefab in new[] { snack, book, amulet, cross })
                if (prefab != null && !nm.spawnPrefabs.Contains(prefab)) nm.spawnPrefabs.Add(prefab);
            PrefabUtility.SaveAsPrefabAsset(manager, NetworkManagerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(manager); }

        AssetDatabase.SaveAssets();
        Selection.activeObject = settings;
        Debug.Log("[SanitySetup] Done. " + (addedToPlayer ? "Added PlayerSanity to Player.prefab. " : "Player.prefab already had PlayerSanity. ") +
                  "Built Snack / Holy Book / Amulet / Cross and registered them with Mirror. Commit the changed prefabs and new assets.", settings);
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Sanity", false, 21)]
    public static void SelectSettings()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        Folder("Assets/Resources/Tuning");
        SanitySettings settings = SettingsAsset();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    private static SanitySettings SettingsAsset()
    {
        var asset = AssetDatabase.LoadAssetAtPath<SanitySettings>(SanitySettings.AssetPath);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<SanitySettings>();
        AssetDatabase.CreateAsset(asset, SanitySettings.AssetPath);
        AssetDatabase.SaveAssets();
        SanitySettings.Use(asset);
        Debug.Log("[SanitySetup] Created " + SanitySettings.AssetPath, asset);
        return asset;
    }

    /// <summary>Adds PlayerSanity to Player.prefab only (used by "Player Prefab: Add All Components").</summary>
    public static bool AddToPlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (root.GetComponent<PlayerSanity>() != null) return false;
            if (root.GetComponent<NetworkIdentity>() == null)
                throw new System.InvalidOperationException(PlayerPrefabPath + " has no NetworkIdentity on its root.");
            root.AddComponent<PlayerSanity>();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static GameObject BuildItem(string name, System.Action<Transform> buildShape)
    {
        string path = ItemFolder + "/" + name + ".prefab";
        var root = new GameObject(name);
        try
        {
            buildShape(root.transform);

            // Collider around all the parts.
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, new Vector3(0.04f, 0.04f, 0.04f));

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException(name + " has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, string material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + material + ".mat")
                     ?? MaterialFor(material, MaterialColour(material));
        part.GetComponent<Renderer>().sharedMaterial = m;
    }

    private static Color MaterialColour(string material)
    {
        switch (material)
        {
            case "Gold": return new Color(0.85f, 0.68f, 0.2f);
            case "AmuletFace": return new Color(0.75f, 0.7f, 0.6f);
            case "HolyBook": return new Color(0.35f, 0.05f, 0.05f);
            case "Cross": return new Color(0.45f, 0.28f, 0.12f);
            case "Snack": return new Color(1f, 0.62f, 0.1f);
            default: return Color.grey;
        }
    }

    private static Material MaterialFor(string name, Color colour)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // Same shader as Unity's default primitive material (URP Lit in this project).
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Material material = new Material(probe.GetComponent<Renderer>().sharedMaterial);
        Object.DestroyImmediate(probe);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
        if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
