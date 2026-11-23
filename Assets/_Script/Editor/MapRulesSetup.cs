using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 23 Nov 2026 (Mr.k). One click for the Map Rules and the map events.
///
///   Tools > Pre-Thesis > One Step > Map Rules (open Cinema_GamePlay first)
///     - "Map Rules" scene object (NetworkIdentity) with: FilmGhostEvent, ToiletGhostEvent,
///       MapRuleVip, MapRuleHunter, MapRuleForeign.
///     - Walkers (MapWalker, hidden until used, NPC ghost model at runtime):
///       FilmGhost x1, ToiletGhostF x1, ToiletGhostM x1, VIP x1, Hunter x1, Foreign x6.
///     - VIP entrance (first exit door), VIP seat (ghost screen room, next to the audience)
///       and the hand-crank projector (next to the ghost room film projector). Move them freely.
///     - The Bat item: Resources/Items/Bat (held) + WorldBat (floor pickup, registered in the
///       NetworkManager), listed in UI Hub > 3D models.
///     - Resources/Tuning/MapRuleTuning.asset (all numbers + sentences).
///     Only adds what is missing. Running it again is safe. Then SAVE the scene (Ctrl+S).
///
///   Tools > Pre-Thesis > Designer Settings > Map Rules
///     Selects MapRuleTuning.asset.
/// </summary>
public static class MapRulesSetup
{
    private const string Title = "Map Rules";
    private const string RootName = "Map Rules";
    private const string WalkersRoot = "Map Rule Walkers";
    private const string ItemsFolder = "Assets/Resources/Items";
    private const string BatPath = ItemsFolder + "/Bat.prefab";
    private const string WorldBatPath = ItemsFolder + "/WorldBat.prefab";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";
    private const string MatFolder = "Assets/Prefab/RuleMockups/Materials";

    private static readonly (string role, int count)[] Walkers =
    {
        (FilmGhostEvent.Role, 1),
        (ToiletGhostEvent.RoleFemale, 1),
        (ToiletGhostEvent.RoleMale, 1),
        (MapRuleVip.Role, 1),
        (MapRuleHunter.Role, 1),
        (MapRuleForeign.Role, 6),
    };

    [MenuItem("Tools/Pre-Thesis/One Step/Map Rules (Cinema)", false, 130)]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog(Title, "There is no MatchDirector in the open scene. Open Cinema_GamePlay first.", "OK");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        var log = new StringBuilder();

        EnsureTuning(log);
        GameObject root = EnsureRoot(scene, log);
        EnsureWalkers(scene, log);
        EnsureVipPoints(root, log);
        EnsureBat(log);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (log.Length == 0) log.AppendLine("Everything was already there. Nothing changed.");
        Debug.Log("[MapRulesSetup]\n" + log);
        EditorUtility.DisplayDialog(Title, log + "\nSave the scene (Ctrl+S).\nNumbers and sentences: Tools > Pre-Thesis > Designer Settings > Map Rules.", "OK");
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Map Rules", false, 25)]
    private static void SelectTuning()
    {
        MapRuleTuning asset = EnsureTuning(new StringBuilder());
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static MapRuleTuning EnsureTuning(StringBuilder log)
    {
        MapRuleTuning asset = AssetDatabase.LoadAssetAtPath<MapRuleTuning>(MapRuleTuning.AssetPath);
        if (asset != null) return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
        asset = ScriptableObject.CreateInstance<MapRuleTuning>();
        AssetDatabase.CreateAsset(asset, MapRuleTuning.AssetPath);
        MapRuleTuning.Use(asset);
        log.AppendLine("+ " + MapRuleTuning.AssetPath);
        return asset;
    }

    // ---- Scene ---------------------------------------------------------------------------

    private static GameObject EnsureRoot(Scene scene, StringBuilder log)
    {
        MapRuleVip existing = Object.FindAnyObjectByType<MapRuleVip>(FindObjectsInactive.Include);
        GameObject root = existing != null ? existing.gameObject : null;
        if (root == null)
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, Title);
            log.AppendLine("+ \"" + RootName + "\" object");
        }
        if (root.GetComponent<NetworkIdentity>() == null) Undo.AddComponent<NetworkIdentity>(root);
        Add<FilmGhostEvent>(root, log);
        Add<ToiletGhostEvent>(root, log);
        Add<MapRuleVip>(root, log);
        Add<MapRuleHunter>(root, log);
        Add<MapRuleForeign>(root, log);
        return root;
    }

    private static void Add<T>(GameObject go, StringBuilder log) where T : Component
    {
        if (go.GetComponent<T>() != null) return;
        Undo.AddComponent<T>(go);
        log.AppendLine("+ " + typeof(T).Name);
    }

    private static void EnsureWalkers(Scene scene, StringBuilder log)
    {
        GameObject holder = GameObject.Find(WalkersRoot);
        if (holder == null)
        {
            holder = new GameObject(WalkersRoot);
            SceneManager.MoveGameObjectToScene(holder, scene);
            Undo.RegisterCreatedObjectUndo(holder, Title);
        }
        Vector3 park = ParkPoint();
        int made = 0;
        foreach ((string role, int count) in Walkers)
        {
            int have = 0;
            foreach (MapWalker w in Object.FindObjectsByType<MapWalker>(FindObjectsInactive.Include))
                if (w.Role == role) have++;
            for (int i = have; i < count; i++)
            {
                var go = new GameObject($"Walker - {role} {i + 1}");
                Undo.RegisterCreatedObjectUndo(go, Title);
                go.transform.SetParent(holder.transform, false);
                go.transform.position = park + new Vector3(made * 0.8f, 0f, 0f);
                go.AddComponent<NetworkIdentity>();
                MapWalker walker = go.AddComponent<MapWalker>();
                walker.EditorSetRole(role);
                made++;
            }
        }
        if (made > 0) log.AppendLine($"+ {made} walker(s) under \"{WalkersRoot}\" (hidden until a rule needs them)");
    }

    /// <summary>Where hidden walkers wait: next to the MatchDirector (they are invisible anyway).</summary>
    private static Vector3 ParkPoint()
    {
        MatchDirector match = Object.FindAnyObjectByType<MatchDirector>();
        return match != null ? match.transform.position : Vector3.zero;
    }

    private static void EnsureVipPoints(GameObject root, StringBuilder log)
    {
        MapRuleVip vip = root.GetComponent<MapRuleVip>();
        var so = new SerializedObject(vip);
        Transform entrance = so.FindProperty("entrance").objectReferenceValue as Transform;
        Transform seat = so.FindProperty("seat").objectReferenceValue as Transform;

        ZoneChoreSpot ghostFilm = null;
        foreach (ZoneChoreSpot s in Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (s.Kind == ZoneTaskKind.FilmChange && s.CountsForZoneBoard) { ghostFilm = s; break; }
        GhostAudience audience = Object.FindAnyObjectByType<GhostAudience>(FindObjectsInactive.Include);

        if (entrance == null)
        {
            ExitPoint exit = Object.FindAnyObjectByType<ExitPoint>(FindObjectsInactive.Include);
            entrance = Point(root.transform, "VIP Entrance", exit != null ? exit.transform.position : root.transform.position);
            log.AppendLine("+ VIP Entrance (at the first exit door; move it freely)");
        }
        if (seat == null)
        {
            Vector3 at = root.transform.position;
            if (audience != null && audience.Count > 0 && audience.Member(0) != null) at = audience.Member(0).position + audience.Member(0).right * 1.2f;
            else if (ghostFilm != null) at = ghostFilm.transform.position;
            seat = Point(root.transform, "VIP Seat (ghost room)", at);
            log.AppendLine("+ VIP Seat in the ghost screen room (move it freely)");
        }
        so.FindProperty("entrance").objectReferenceValue = entrance;
        so.FindProperty("seat").objectReferenceValue = seat;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (Object.FindAnyObjectByType<CrankProjector>(FindObjectsInactive.Include) == null)
        {
            Vector3 at = ghostFilm != null ? ghostFilm.transform.position + ghostFilm.transform.right * 1.2f : seat.position + Vector3.forward;
            MakeCrankProjector(root.scene, at, log);
        }
    }

    private static Transform Point(Transform parent, string name, Vector3 at)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, Title);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        return go.transform;
    }

    private static void MakeCrankProjector(Scene scene, Vector3 at, StringBuilder log)
    {
        var root = new GameObject("Crank Projector (VIP)");
        Undo.RegisterCreatedObjectUndo(root, Title);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = at;
        Material metal = Mat("RM_CrankMetal", new Color(0.22f, 0.22f, 0.24f));
        Material brass = Mat("RM_CrankBrass", new Color(0.7f, 0.55f, 0.25f));
        var model = new GameObject("Model");
        model.transform.SetParent(root.transform, false);
        Part(model.transform, PrimitiveType.Cube, "Stand", new Vector3(0f, 0.45f, 0f), new Vector3(0.35f, 0.9f, 0.35f), metal);
        Part(model.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 1.05f, 0f), new Vector3(0.45f, 0.3f, 0.6f), metal);
        Part(model.transform, PrimitiveType.Cylinder, "Lens", new Vector3(0f, 1.05f, 0.38f), new Vector3(0.14f, 0.08f, 0.14f), brass, new Vector3(90f, 0f, 0f));
        Part(model.transform, PrimitiveType.Cylinder, "Reel A", new Vector3(0f, 1.38f, 0.15f), new Vector3(0.3f, 0.02f, 0.3f), brass, new Vector3(0f, 0f, 90f));
        Part(model.transform, PrimitiveType.Cylinder, "Reel B", new Vector3(0f, 1.38f, -0.18f), new Vector3(0.3f, 0.02f, 0.3f), brass, new Vector3(0f, 0f, 90f));
        var handle = new GameObject("Crank Handle");
        handle.transform.SetParent(model.transform, false);
        handle.transform.localPosition = new Vector3(0.26f, 1.05f, 0f);
        Part(handle.transform, PrimitiveType.Cube, "Arm", new Vector3(0.02f, 0f, 0.1f), new Vector3(0.03f, 0.03f, 0.22f), brass);
        Part(handle.transform, PrimitiveType.Cylinder, "Grip", new Vector3(0.06f, 0f, 0.2f), new Vector3(0.04f, 0.05f, 0.04f), metal, new Vector3(0f, 0f, 90f));
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.8f, 0f);
        box.size = new Vector3(0.6f, 1.6f, 0.8f);
        root.AddComponent<NetworkIdentity>();
        CrankProjector crank = root.AddComponent<CrankProjector>();
        crank.EditorSetHandle(handle.transform);
        log.AppendLine("+ Crank Projector (VIP) next to the ghost room projector (move it freely)");
    }

    // ---- Bat item --------------------------------------------------------------------------

    private static void EnsureBat(StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(ItemsFolder)) AssetDatabase.CreateFolder("Assets/Resources", "Items");
        GameObject held = AssetDatabase.LoadAssetAtPath<GameObject>(BatPath);
        if (held == null)
        {
            var root = new GameObject("Bat");
            try
            {
                Material wood = Mat("RM_BatWood", new Color(0.62f, 0.45f, 0.26f));
                Material tape = Mat("RM_BatTape", new Color(0.1f, 0.1f, 0.1f));
                // Lying along +Z, held at the handle end (origin).
                Part(root.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0f, 0.14f), new Vector3(0.028f, 0.16f, 0.028f), tape, new Vector3(90f, 0f, 0f));
                Part(root.transform, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0f, 0.5f), new Vector3(0.055f, 0.22f, 0.055f), wood, new Vector3(90f, 0f, 0f));
                Part(root.transform, PrimitiveType.Sphere, "Knob", new Vector3(0f, 0f, -0.02f), new Vector3(0.045f, 0.045f, 0.03f), tape);
                held = PrefabUtility.SaveAsPrefabAsset(root, BatPath);
                log.AppendLine("+ " + BatPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        GameObject world = AssetDatabase.LoadAssetAtPath<GameObject>(WorldBatPath);
        if (world == null && held != null)
        {
            var root = new GameObject("WorldBat");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(held);
                model.transform.SetParent(root.transform, false); // first child = the model
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.1f, 0.1f, 0.8f);
                collider.center = new Vector3(0f, 0f, 0.35f);
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 0.9f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                root.AddComponent<NetworkIdentity>();
                NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
                sync.syncDirection = SyncDirection.ServerToClient;
                sync.target = root.transform;
                root.AddComponent<WorldInventoryItem>();
                world = PrefabUtility.SaveAsPrefabAsset(root, WorldBatPath);
                NetworkIdentity identity = world.GetComponent<NetworkIdentity>();
                EditorUtility.SetDirty(identity);
                PrefabUtility.SavePrefabAsset(world);
                if (identity.assetId == 0) log.AppendLine("! " + WorldBatPath + " has no network asset ID. Select it once in the Project window, then run this tool again.");
                log.AppendLine("+ " + WorldBatPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        if (world != null && AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(NetworkManagerPath);
            try
            {
                NetworkManager manager = root.GetComponent<NetworkManager>();
                if (manager != null && !manager.spawnPrefabs.Contains(world))
                {
                    manager.spawnPrefabs.Add(world);
                    PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPath);
                    log.AppendLine("+ WorldBat registered in the NetworkManager");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(GameHudSetup.PrefabPath) != null)
        {
            if (held != null) GameHudSetup.AddModelSlotToPrefab(held);
            if (world != null) GameHudSetup.AddModelSlotToPrefab(world);
        }
    }

    // ---- Small helpers ----------------------------------------------------------------------

    private static void Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Vector3 euler = default)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Mat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefab")) AssetDatabase.CreateFolder("Assets", "Prefab");
        if (!AssetDatabase.IsValidFolder("Assets/Prefab/RuleMockups")) AssetDatabase.CreateFolder("Assets/Prefab", "RuleMockups");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Prefab/RuleMockups", "Materials");
        string path = $"{MatFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        m.color = color;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
