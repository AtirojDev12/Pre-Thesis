using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 5 Oct (Mr.k). One-click setup for the flashlights and the darkness.
///
///   Tools > Pre-Thesis > Build Flashlight Prefabs
///       Makes Resources/Items/Flashlight.prefab (in your hand),
///       Resources/Items/WorldFlashlight.prefab and Resources/Items/WorldBattery.prefab
///       (dropped / thrown), and registers the world ones in NetworkManager.spawnPrefabs. Simple shapes for now:
///       swap the model inside the prefab when the art is ready (keep FlashlightVisual).
///
///   Tools > Pre-Thesis > Flashlight Settings
///       Creates (first time) and selects Resources/Tuning/FlashlightTuning.asset:
///       the game designer's page for every flashlight / battery number.
///
///   Tools > Pre-Thesis > Darkness: Add To Open Scene
///       Adds a DarknessController to the open map so its sliders are saved with it.
/// </summary>
public static class FlashlightSetup
{
    private const string HeldPath = "Assets/Resources/Items/Flashlight.prefab";
    private const string WorldPath = "Assets/Resources/Items/WorldFlashlight.prefab";
    private const string BatteryPath = "Assets/Resources/Items/WorldBattery.prefab";

    [MenuItem("Tools/Pre-Thesis/Build Flashlight Prefabs")]
    public static void BuildAssets()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Items")) AssetDatabase.CreateFolder("Assets/Resources", "Items");

        // ---- Held model (also used inside the world prefab) ----
        GameObject root = new GameObject("Flashlight");
        GameObject heldPrefab;
        try
        {
            // Points along +Z, like the camera. About 19 cm long.
            Part(root.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0, 0, -0.02f), new Vector3(0.032f, 0.07f, 0.032f));
            Part(root.transform, "Head", PrimitiveType.Cylinder, new Vector3(0, 0, 0.065f), new Vector3(0.048f, 0.025f, 0.048f));
            Renderer lens = Part(root.transform, "Lens", PrimitiveType.Cylinder, new Vector3(0, 0, 0.091f), new Vector3(0.04f, 0.002f, 0.04f));
            var beamOrigin = new GameObject("Beam Origin").transform;
            beamOrigin.SetParent(root.transform, false);
            beamOrigin.localPosition = new Vector3(0, 0, 0.095f);

            FlashlightVisual visual = root.AddComponent<FlashlightVisual>();
            var so = new SerializedObject(visual);
            so.FindProperty("lensRenderer").objectReferenceValue = lens;
            so.FindProperty("beamOrigin").objectReferenceValue = beamOrigin;
            so.ApplyModifiedPropertiesWithoutUndo();
            heldPrefab = PrefabUtility.SaveAsPrefabAsset(root, HeldPath);
        }
        finally { Object.DestroyImmediate(root); }

        // ---- World pickup (network object, like WorldWalkieTalkie) ----
        root = new GameObject("WorldFlashlight");
        GameObject worldPrefab;
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(heldPrefab);
            model.transform.SetParent(root.transform, false);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.05f, 0.05f, 0.2f);
            collider.center = new Vector3(0, 0, 0.03f);
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            worldPrefab = PrefabUtility.SaveAsPrefabAsset(root, WorldPath);
            NetworkIdentity identity = worldPrefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException("World flashlight has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(worldPrefab);
        }
        finally { Object.DestroyImmediate(root); }

        // ---- Battery pickup (5 Oct): a small AA cell ----
        root = new GameObject("WorldBattery");
        GameObject batteryPrefab;
        try
        {
            Part(root.transform, "Cell", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.015f, 0.025f, 0.015f));
            Part(root.transform, "Tip", PrimitiveType.Cylinder, new Vector3(0, 0, 0.027f), new Vector3(0.006f, 0.003f, 0.006f));
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.03f, 0.03f, 0.06f);
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.05f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            batteryPrefab = PrefabUtility.SaveAsPrefabAsset(root, BatteryPath);
            NetworkIdentity identity = batteryPrefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException("World battery has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(batteryPrefab);
        }
        finally { Object.DestroyImmediate(root); }

        // ---- Register with Mirror, or clients cannot see a dropped flashlight / battery ----
        root = PrefabUtility.LoadPrefabContents("Assets/Prefab/NetworkManager.prefab");
        try
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            if (!manager.spawnPrefabs.Contains(worldPrefab)) manager.spawnPrefabs.Add(worldPrefab);
            if (!manager.spawnPrefabs.Contains(batteryPrefab)) manager.spawnPrefabs.Add(batteryPrefab);
            PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefab/NetworkManager.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        AssetDatabase.SaveAssets();
        Debug.Log("[FlashlightSetup] Built " + HeldPath + ", " + WorldPath + " and " + BatteryPath + ", and registered the world ones with Mirror.");
    }

    [MenuItem("Tools/Pre-Thesis/Flashlight Settings")]
    public static void OpenFlashlightSettings()
    {
        FlashlightTuning asset = AssetDatabase.LoadAssetAtPath<FlashlightTuning>(FlashlightTuning.AssetPath);
        if (asset == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
            asset = ScriptableObject.CreateInstance<FlashlightTuning>(); // the default numbers
            AssetDatabase.CreateAsset(asset, FlashlightTuning.AssetPath);
            AssetDatabase.SaveAssets();
            FlashlightTuning.Use(asset);
            Debug.Log("[FlashlightSetup] Created " + FlashlightTuning.AssetPath + ". Commit it with your changes.", asset);
        }
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    [MenuItem("Tools/Pre-Thesis/Darkness: Add To Open Scene")]
    public static void AddDarkness()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        DarknessController existing = Object.FindAnyObjectByType<DarknessController>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeObject = existing.gameObject;
            Debug.Log("[FlashlightSetup] This scene already has a DarknessController. Selected it.", existing);
            return;
        }
        var go = new GameObject("Darkness");
        go.AddComponent<DarknessController>();
        Undo.RegisterCreatedObjectUndo(go, "Add Darkness");
        Selection.activeObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[FlashlightSetup] Added 'Darkness'. Set the sliders, then save the scene.", go);
    }

    private static Renderer Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis Y -> Z
        part.transform.localScale = scale;
        return part.GetComponent<Renderer>();
    }
}
