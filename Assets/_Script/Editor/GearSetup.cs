using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 7). One-click setup for the free / paid gear.
///
///   Tools > Pre-Thesis > One Step > Items: Free + Paid Walkies, Flashlights, Syringes
///     Makes (only what is missing, never overwrites):
///       Resources/Items/WalkieTalkieFree + WorldWalkieTalkieFree   (orange copy of the walkie)
///       Resources/Items/FlashlightFree   + WorldFlashlightFree     (yellow copy of the flashlight)
///       Resources/Items/Syringe          + WorldSyringe            (paid: green liquid)
///       Resources/Items/SyringeFree      + WorldSyringeFree        (free: red liquid)
///     registers the world ones in NetworkManager.spawnPrefabs, lists every model in the
///     GAME HUD's Model Slots (UI Hub > 3D models), and makes the GearTuning settings file.
///     Simple shapes for now: swap the model inside a prefab when the art is ready.
///
///   Tools > Pre-Thesis > Designer Settings > Walkies + Syringes
///     Creates (first time) and selects Resources/Tuning/GearTuning.asset.
/// </summary>
public static class GearSetup
{
    private const string Folder = "Assets/Resources/Items";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";

    private static readonly Color FreeWalkieColor = new Color(0.85f, 0.42f, 0.1f);
    private static readonly Color FreeFlashlightColor = new Color(0.8f, 0.65f, 0.12f);
    private static readonly Color SyringeBody = new Color(0.86f, 0.9f, 0.92f);
    private static readonly Color PaidLiquid = new Color(0.2f, 0.9f, 0.7f);
    private static readonly Color FreeLiquid = new Color(0.9f, 0.2f, 0.2f);

    [MenuItem("Tools/Pre-Thesis/One Step/Items: Free + Paid Walkies, Flashlights, Syringes", false, 121)]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Gear", "Stop Play mode first.", "OK"); return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Items");

        var log = new StringBuilder();
        var worlds = new List<GameObject>();
        var models = new List<GameObject>();

        // ---- Free walkie: a coloured copy of the walkie ----
        GameObject walkieFree = CopyHeld(Folder + "/WalkieTalkie.prefab", Folder + "/WalkieTalkieFree.prefab", "WalkieTalkieFree",
            go => { foreach (WalkieTalkieVisual v in go.GetComponentsInChildren<WalkieTalkieVisual>(true)) v.EditorSetBodyColor(FreeWalkieColor); }, log);
        if (walkieFree != null)
        {
            models.Add(walkieFree);
            GameObject world = BuildWorld(walkieFree, Folder + "/WorldWalkieTalkieFree.prefab", "WorldWalkieTalkieFree",
                new Vector3(.08f, .27f, .055f), new Vector3(0, .045f, 0), .35f, log);
            if (world != null) worlds.Add(world);
        }

        // ---- Free flashlight: a coloured copy of the flashlight ----
        GameObject flashlightFree = CopyHeld(Folder + "/Flashlight.prefab", Folder + "/FlashlightFree.prefab", "FlashlightFree",
            go => { foreach (FlashlightVisual v in go.GetComponentsInChildren<FlashlightVisual>(true)) v.EditorSetBodyColor(FreeFlashlightColor); }, log);
        if (flashlightFree != null)
        {
            models.Add(flashlightFree);
            GameObject world = BuildWorld(flashlightFree, Folder + "/WorldFlashlightFree.prefab", "WorldFlashlightFree",
                new Vector3(0.05f, 0.05f, 0.2f), new Vector3(0, 0, 0.03f), 0.3f, log);
            if (world != null) worlds.Add(world);
        }

        // ---- Syringes ----
        foreach (bool free in new[] { false, true })
        {
            string held = free ? "SyringeFree" : "Syringe";
            GameObject model = BuildSyringe(Folder + "/" + held + ".prefab", held, free ? FreeLiquid : PaidLiquid, log);
            if (model == null) continue;
            models.Add(model);
            GameObject world = BuildWorld(model, Folder + "/World" + held + ".prefab", "World" + held,
                new Vector3(0.03f, 0.03f, 0.22f), new Vector3(0, 0, -0.01f), 0.08f, log);
            if (world != null) worlds.Add(world);
        }

        // ---- Mirror: clients must know the world prefabs ----
        if (AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath) != null && worlds.Count > 0)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(NetworkManagerPath);
            try
            {
                NetworkManager manager = root.GetComponent<NetworkManager>();
                int added = 0;
                if (manager != null)
                    foreach (GameObject w in worlds)
                        if (!manager.spawnPrefabs.Contains(w)) { manager.spawnPrefabs.Add(w); added++; }
                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPath);
                    log.AppendLine($"+ {added} world prefab(s) registered in the NetworkManager.");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        else if (worlds.Count > 0) log.AppendLine("! " + NetworkManagerPath + " not found: register the world prefabs by hand.");

        // ---- UI Hub: list the models (GAME HUD > Model Slots) ----
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GameHudSetup.PrefabPath) != null)
        {
            foreach (GameObject m in models) GameHudSetup.AddModelSlotToPrefab(m);
            foreach (GameObject w in worlds) GameHudSetup.AddModelSlotToPrefab(w);
        }

        // ---- Designer settings ----
        if (AssetDatabase.LoadAssetAtPath<GearTuning>(GearTuning.AssetPath) == null)
        {
            CreateTuning();
            log.AppendLine("+ " + GearTuning.AssetPath + " (prices, carry limits, heal, battery)");
        }

        AssetDatabase.SaveAssets();
        if (log.Length == 0) log.AppendLine("Everything was already there. Nothing changed.");
        Debug.Log("[GearSetup]\n" + log);
        EditorUtility.DisplayDialog("Gear", log + "\nPrices, carry limits, heal and battery: Tools > Pre-Thesis > Designer Settings > Walkies + Syringes.", "OK");
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Walkies + Syringes", false, 21)]
    public static void OpenSettings()
    {
        GearTuning asset = AssetDatabase.LoadAssetAtPath<GearTuning>(GearTuning.AssetPath) ?? CreateTuning();
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static GearTuning CreateTuning()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
        GearTuning asset = ScriptableObject.CreateInstance<GearTuning>();
        AssetDatabase.CreateAsset(asset, GearTuning.AssetPath);
        AssetDatabase.SaveAssets();
        GearTuning.Use(asset);
        return asset;
    }

    // ---- Builders ------------------------------------------------------------------------

    /// <summary>A copy of a held model with a different colour. Kept as it is if it already exists.</summary>
    private static GameObject CopyHeld(string source, string target, string rootName, System.Action<GameObject> tint, StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(target);
        if (existing != null) return existing;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
        {
            log.AppendLine("! " + source + " not found: run its own setup tool first. " + target + " was not made.");
            return null;
        }
        if (!AssetDatabase.CopyAsset(source, target)) { log.AppendLine("! Could not copy " + source + "."); return null; }
        GameObject root = PrefabUtility.LoadPrefabContents(target);
        try
        {
            root.name = rootName;
            tint(root);
            PrefabUtility.SaveAsPrefabAsset(root, target);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        log.AppendLine("+ " + target);
        return AssetDatabase.LoadAssetAtPath<GameObject>(target);
    }

    /// <summary>A floor pickup (network object) around a held model, the same way as the walkie / flashlight tools.</summary>
    private static GameObject BuildWorld(GameObject heldPrefab, string path, string rootName, Vector3 size, Vector3 centre, float mass, StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject(rootName);
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(heldPrefab);
            model.transform.SetParent(root.transform, false); // first child = the model (other players' view uses it)
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = size;
            collider.center = centre;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            // Force Mirror's editor-only asset ID and keep it for builds (same as the other item tools).
            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(prefab);
            if (identity.assetId == 0) log.AppendLine("! " + path + " has no network asset ID. Select it once in the Project window, then run this tool again.");
            log.AppendLine("+ " + path);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>A simple syringe pointing along +Z (about 20 cm): barrel, liquid, plunger, needle.</summary>
    private static GameObject BuildSyringe(string path, string rootName, Color liquid, StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject(rootName);
        try
        {
            Part(root.transform, "Barrel", new Vector3(0, 0, 0), new Vector3(0.022f, 0.05f, 0.022f));
            Renderer juice = Part(root.transform, "Liquid", new Vector3(0, 0, 0.012f), new Vector3(0.0235f, 0.034f, 0.0235f));
            Part(root.transform, "Plunger", new Vector3(0, 0, -0.075f), new Vector3(0.007f, 0.03f, 0.007f));
            Part(root.transform, "Plunger Cap", new Vector3(0, 0, -0.106f), new Vector3(0.028f, 0.003f, 0.028f));
            Part(root.transform, "Hub", new Vector3(0, 0, 0.055f), new Vector3(0.01f, 0.007f, 0.01f));
            Part(root.transform, "Needle", new Vector3(0, 0, 0.085f), new Vector3(0.0018f, 0.025f, 0.0018f));
            root.AddComponent<SyringeVisual>().EditorSetup(juice, SyringeBody, liquid);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            log.AppendLine("+ " + path);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>A cylinder lying along +Z, no collider.</summary>
    private static Renderer Part(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = scale;
        return go.GetComponent<Renderer>();
    }
}
