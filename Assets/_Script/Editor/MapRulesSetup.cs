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
///     - VIP entrance (first exit door) and VIP seat (ghost room, back row, middle).
///     - 23 Nov: every chair cushion becomes a CinemaSeat (E = sit, SPACE = stand up, folds open);
///       the audience ghosts, the VIP and the Staff Time shadow are put on real seats.
///     - 23 Nov: the VIP crank projector (RM_Projector + handle) waits hidden at the ghost room
///       projector, facing the screen; it is swapped in only while the VIP is here.
///     - The Bat item: Resources/Items/Bat (held) + WorldBat (floor pickup, registered in the
///       NetworkManager), listed in UI Hub > 3D models.
///     - Resources/Tuning/MapRuleTuning.asset (all numbers + sentences).
///     Only adds what is missing. Running it again is safe. Then SAVE the scene (Ctrl+S).
///
///   Tools > Pre-Thesis > Designer Settings > Map Rules
///     Selects MapRuleTuning.asset.
/// </summary>
public static partial class MapRulesSetup
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
        EnsureSeats(scene, root, log);      // 23 Nov: real cinema seats (sit with E, SPACE = stand up)
        SeatGhosts(root, log);              // audience + VIP + Staff Time shadow on seats
        SwapProjectors(scene, log);         // VIP crank projector: swapped in for the ghost room projector only while the VIP is here
        UpdateMemoTexts(log);

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
        // 23 Nov: one film walker per audience member (they all leave their seats at film change time).
        GhostAudience crowd = Object.FindAnyObjectByType<GhostAudience>(FindObjectsInactive.Include);
        int filmWalkers = Mathf.Max(1, crowd != null ? crowd.Count : 1);
        foreach ((string role, int count) in Walkers)
        {
            int want = role == FilmGhostEvent.Role ? filmWalkers : count;
            int have = 0;
            foreach (MapWalker w in Object.FindObjectsByType<MapWalker>(FindObjectsInactive.Include))
                if (w.Role == role) have++;
            for (int i = have; i < want; i++)
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

        // 23 Nov: the VIP comes in at the FRONT DOOR, where the ticket / snack customers come from.
        Vector3? front = FrontDoor();
        if (entrance == null)
        {
            ExitPoint exit = Object.FindAnyObjectByType<ExitPoint>(FindObjectsInactive.Include);
            Vector3 at = front ?? (exit != null ? exit.transform.position : root.transform.position);
            entrance = Point(root.transform, "VIP Entrance", at);
            log.AppendLine("+ VIP Entrance (front door; move it freely)");
        }
        else if (front.HasValue && Vector3.Distance(entrance.position, front.Value) > 0.5f && !entrance.name.EndsWith("(moved by hand)"))
        {
            Undo.RecordObject(entrance, Title);
            entrance.position = front.Value;
            log.AppendLine("- VIP Entrance moved to the front door (where the ticket customers come in). Rename it \"VIP Entrance (moved by hand)\" to keep your own place.");
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
        EnsureVipFilm(root.scene, ghostFilm, log);

    }

    /// <summary>Where the ticket customers appear (TicketMinigame "Spawn Point"), else the popcorn customers. Null = none.</summary>
    private static Vector3? FrontDoor()
    {
        foreach (TicketMinigame t in Object.FindObjectsByType<TicketMinigame>(FindObjectsInactive.Include))
        {
            Transform p = new SerializedObject(t).FindProperty("spawnPoint")?.objectReferenceValue as Transform;
            if (p != null) return p.position;
        }
        foreach (PopcornMinigameBootstrap b in Object.FindObjectsByType<PopcornMinigameBootstrap>(FindObjectsInactive.Include))
        {
            Transform p = new SerializedObject(b).FindProperty("customerSpawnPoint")?.objectReferenceValue as Transform;
            if (p != null) return p.position;
        }
        return null;
    }

    /// <summary>
    /// 23 Nov: the VIP film reel (gold, "VIP") on top of the ghost room film shelf (else next to the projector).
    /// A hotbar reel (CarryProp): take it, press E on the crank projector with it in hand. R = back on the shelf.
    /// </summary>
    private static void EnsureVipFilm(Scene scene, ZoneChoreSpot ghostFilm, StringBuilder log)
    {
        foreach (CarryProp p in Object.FindObjectsByType<CarryProp>(FindObjectsInactive.Include))
            if (p.CarryId == MapRuleVip.FilmId) return; // already there (keeps your place)
        Transform shelf = null;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.name == "Film Reel Shelf (Ghost Room)") { shelf = t; break; }
        Vector3 at = shelf != null ? shelf.position + Vector3.up * 1.78f
            : ghostFilm != null ? ghostFilm.transform.position + Vector3.right * 1f : Vector3.zero;
        Quaternion rot = shelf != null ? shelf.rotation : Quaternion.identity;

        var reel = new GameObject("VIP Film Reel (gold, hotbar)");
        Undo.RegisterCreatedObjectUndo(reel, Title);
        SceneManager.MoveGameObjectToScene(reel, scene);
        reel.transform.SetPositionAndRotation(at, rot);
        Material gold = Mat("RM_FilmReel_VIP", new Color(0.95f, 0.75f, 0.2f));
        Material dark = Mat("RM_ProjectorDark", new Color(0.08f, 0.08f, 0.09f));
        Part(reel.transform, PrimitiveType.Cylinder, "Reel", new Vector3(0f, 0.19f, 0f), new Vector3(0.36f, 0.025f, 0.36f), gold, new Vector3(90f, 0f, 0f));
        Part(reel.transform, PrimitiveType.Cylinder, "Hub", new Vector3(0f, 0.19f, 0f), new Vector3(0.08f, 0.04f, 0.08f), dark, new Vector3(90f, 0f, 0f));
        var text = new GameObject("Label VIP");
        text.transform.SetParent(reel.transform, false);
        text.transform.localPosition = new Vector3(0f, 0.19f, 0.03f);
        text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        TMPro.TextMeshPro tmp = text.AddComponent<TMPro.TextMeshPro>();
        tmp.text = "VIP";
        tmp.fontSize = 0.6f;
        tmp.fontStyle = TMPro.FontStyles.Bold;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.1f, 0.05f, 0f);
        tmp.rectTransform.sizeDelta = new Vector2(0.3f, 0.12f);
        reel.AddComponent<NetworkIdentity>();
        BoxCollider box = reel.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.19f, 0f);
        box.size = new Vector3(0.38f, 0.38f, 0.1f);
        CarryProp prop = reel.AddComponent<CarryProp>();
        prop.EditorSetup(MapRuleVip.FilmId, "VIP Film Reel", ItemCatalog.FilmReelVip, true, "VIP");
        prop.EditorSetHold(new Vector3(0.22f, -0.42f, 0.6f), new Vector3(0f, 180f, 0f), 1f, new Vector3(0.25f, 0.8f, 0.35f), Vector3.zero);
        EditorUtility.SetDirty(prop);
        log.AppendLine($"+ VIP Film Reel (gold) {(shelf != null ? "on top of the ghost room film shelf" : "next to the ghost room projector")}");
    }

    private static Transform Point(Transform parent, string name, Vector3 at)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, Title);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        return go.transform;
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
