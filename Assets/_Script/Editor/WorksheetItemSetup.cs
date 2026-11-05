using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k). The Work Sheet as a REAL object (drop with Q, anyone picks it up).
///
///   Tools > Pre-Thesis > One Step > Items: Work Sheet
///       1. Assets/Prefab/WorkSheet.prefab — a small memo pad made of boxes, for the designer
///          (cover, pages, rings). Made ONLY if it does not exist yet: never overwritten.
///       2. Resources/Items/WorldWorksheet.prefab — the dropped sheet (network object).
///          It holds WorkSheet.prefab as its first child, so a new model shows up everywhere
///          (dropped, in your hand, in a friend's hand). Rebuilt every time.
///       3. Registers WorldWorksheet in NetworkManager.spawnPrefabs (clients can see it).
///   Also run by "Lobby: Add Staff NPC" when the world prefab is missing.
///   The model's FRONT (pages) faces -Z; the rings are on top (+Y).
/// </summary>
public static class WorksheetItemSetup
{
    private const string ModelPath = "Assets/Prefab/WorkSheet.prefab";
    private const string WorldPath = "Assets/Resources/Items/WorldWorksheet.prefab";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";
    private const string MaterialFolder = "Assets/Materials/Items";

    /// <summary>Builds the assets only if the world prefab is missing. True = it exists now.</summary>
    public static bool EnsureBuilt()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath) != null) return true;
        Build();
        return AssetDatabase.LoadAssetAtPath<GameObject>(WorldPath) != null;
    }

    [MenuItem("Tools/Pre-Thesis/One Step/Items: Work Sheet", false, 126)]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Work Sheet", "Stop Play mode first.", "OK"); return; }
        EnsureFolder("Assets", "Prefab");
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Items");

        // ---- 1. Designer's model (only if missing) ----
        GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        bool madeModel = false;
        if (modelPrefab == null)
        {
            GameObject pad = MakePad();
            try { modelPrefab = PrefabUtility.SaveAsPrefabAsset(pad, ModelPath); }
            finally { Object.DestroyImmediate(pad); }
            madeModel = true;
        }

        // ---- 2. World pickup. WorkSheet.prefab is its first child. ----
        GameObject root = new GameObject("WorldWorksheet");
        GameObject worldPrefab;
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.transform.SetParent(root.transform, false);
            Bounds bounds = BoundsOf(model);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest > 0.0001f) model.transform.localScale *= 0.21f / longest; // about A5
            bounds = BoundsOf(model);
            model.transform.position -= bounds.center;
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            bounds = BoundsOf(model);
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, new Vector3(0.02f, 0.02f, 0.02f));
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            worldPrefab = PrefabUtility.SaveAsPrefabAsset(root, WorldPath);
            NetworkIdentity identity = worldPrefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException("World work sheet has no network asset ID.");
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
        Debug.Log("[WorksheetItemSetup] " + (madeModel ? "Created " + ModelPath + " (memo pad for the designer). " : "Kept the designer's " + ModelPath + ". ") +
                  "Built " + WorldPath + " and registered it with Mirror. Run this again after changing WorkSheet.prefab.", modelPrefab);
    }

    /// <summary>A memo pad: red cover, cream pages in front (-Z), a title strip and 6 rings on top.</summary>
    private static GameObject MakePad()
    {
        var pad = new GameObject("WorkSheet");
        Material cover = Mat("WorkSheet_Cover", new Color(0.45f, 0.09f, 0.1f));
        Material paper = Mat("WorkSheet_Paper", new Color(0.96f, 0.94f, 0.86f));
        Material ink = Mat("WorkSheet_Ink", new Color(0.15f, 0.13f, 0.12f));
        Material metal = Mat("WorkSheet_Rings", new Color(0.7f, 0.7f, 0.72f));
        Part(pad.transform, PrimitiveType.Cube, "Cover", Vector3.zero, new Vector3(0.15f, 0.21f, 0.012f), Vector3.zero, cover);
        Part(pad.transform, PrimitiveType.Cube, "Pages", new Vector3(0f, -0.005f, -0.009f), new Vector3(0.138f, 0.192f, 0.008f), Vector3.zero, paper);
        Part(pad.transform, PrimitiveType.Cube, "Title", new Vector3(0f, 0.062f, -0.0135f), new Vector3(0.1f, 0.016f, 0.001f), Vector3.zero, ink);
        for (int i = 0; i < 4; i++)
            Part(pad.transform, PrimitiveType.Cube, "Line " + (i + 1), new Vector3(0f, 0.03f - i * 0.022f, -0.0135f),
                new Vector3(0.11f, 0.003f, 0.001f), Vector3.zero, ink);
        for (int i = 0; i < 6; i++)
            Part(pad.transform, PrimitiveType.Cylinder, "Ring " + (i + 1), new Vector3(-0.055f + i * 0.022f, 0.1f, -0.004f),
                new Vector3(0.01f, 0.006f, 0.01f), new Vector3(90f, 0f, 0f), metal);
        return pad;
    }

    private static void Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Mat(string name, Color color)
    {
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "Items");
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var material = new Material(probe.GetComponent<Renderer>().sharedMaterial);
        Object.DestroyImmediate(probe);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
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
