using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 31 Oct 2026 (Mr.k). The Missing Parts ghost, in one click.
///
///   Tools > Pre-Thesis > One Step > Ghosts: Missing Parts Ghost
///     1. Assets/Prefab/GhostParts/GhostPart_LeftArm / RightArm / Head / UpperBody.prefab
///        Placeholder models for the designer. Made ONLY if missing: her work is never overwritten.
///     2. Resources/Items/WorldGhostPart_*.prefab — the part lying in the map / dropped / thrown
///        (network object, same as the Map). The designer's model is its first child.
///        Rebuilt every time, and registered in NetworkManager.spawnPrefabs.
///     3. If the open scene has the ticket / popcorn counters (Cinema_GamePlay): adds (or updates)
///        the "Missing Parts Ghost" object and copies the counters' customer routes into it.
///        The zone boxes are only set the first time (move / resize them after). Save the scene.
/// </summary>
public static class MissingPartsGhostSetup
{
    private const string ModelFolder = "Assets/Prefab/GhostParts";
    private const string NetworkManagerPath = "Assets/Prefab/NetworkManager.prefab";
    private const string SceneObjectName = "Missing Parts Ghost";

    [MenuItem("Tools/Pre-Thesis/One Step/Ghosts: Missing Parts Ghost", false, 130)]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Missing Parts Ghost", "Stop Play mode first.", "OK"); return; }
        EnsureFolder("Assets", "Prefab");
        EnsureFolder("Assets/Prefab", "GhostParts");
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Items");

        Material pale = EnsureMaterial(ModelFolder + "/GhostPartPale.mat", new Color(0.82f, 0.86f, 0.9f));
        Material blood = EnsureMaterial(ModelFolder + "/GhostPartBlood.mat", new Color(0.45f, 0.02f, 0.02f));

        int madeModels = 0;
        var worldPrefabs = new System.Collections.Generic.List<GameObject>();
        foreach (GhostBodyPart part in GhostBodyParts.All)
        {
            GameObject model = EnsureModel(part, pale, blood, ref madeModels);
            worldPrefabs.Add(BuildWorldPrefab(part, model));
        }
        RegisterWithMirror(worldPrefabs);
        AssetDatabase.SaveAssets();

        string sceneNote = SetupScene();
        Debug.Log($"[MissingPartsGhostSetup] Part items ready ({madeModels} new placeholder model(s) in {ModelFolder}, " +
                  $"world prefabs rebuilt + registered with Mirror). {sceneNote}");
    }

    // ---- 1. Designer models (only if missing) ----------------------------------------

    private static GameObject EnsureModel(GhostBodyPart part, Material pale, Material blood, ref int made)
    {
        string path = ModelFolder + "/GhostPart_" + GhostBodyParts.Suffix(part) + ".prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject root = new GameObject("GhostPart_" + GhostBodyParts.Suffix(part));
        try
        {
            switch (part)
            {
                case GhostBodyPart.LeftArm:
                case GhostBodyPart.RightArm:
                    Shape(PrimitiveType.Capsule, root.transform, Vector3.zero, new Vector3(0.13f, 0.3f, 0.13f), Vector3.zero, pale);
                    Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.29f, 0f), new Vector3(0.12f, 0.02f, 0.12f), Vector3.zero, blood);
                    break;
                case GhostBodyPart.Head:
                    Shape(PrimitiveType.Sphere, root.transform, Vector3.zero, new Vector3(0.32f, 0.36f, 0.32f), Vector3.zero, pale);
                    Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.17f, 0f), new Vector3(0.14f, 0.02f, 0.14f), Vector3.zero, blood);
                    break;
                default: // upper body: torso + head + both arms
                    Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.3f), Vector3.zero, pale);
                    Shape(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.32f, 0.36f, 0.32f), Vector3.zero, pale);
                    Shape(PrimitiveType.Capsule, root.transform, new Vector3(-0.34f, 0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), Vector3.zero, pale);
                    Shape(PrimitiveType.Capsule, root.transform, new Vector3(0.34f, 0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), Vector3.zero, pale);
                    Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.01f, 0f), new Vector3(0.42f, 0.02f, 0.3f), Vector3.zero, blood);
                    break;
            }
            made++;
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Shape(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Vector3 euler, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.transform.localEulerAngles = euler;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // the world prefab gets one box collider
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material EnsureMaterial(string path, Color color)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ---- 2. World pickup (rebuilt every time) ------------------------------------------

    private static GameObject BuildWorldPrefab(GhostBodyPart part, GameObject modelPrefab)
    {
        string path = "Assets/Resources/" + GhostBodyParts.WorldPrefabPath(part) + ".prefab";
        GameObject root = new GameObject("WorldGhostPart_" + GhostBodyParts.Suffix(part));
        try
        {
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab);
            model.transform.SetParent(root.transform, false);
            // Lying down, centred on the root.
            if (part != GhostBodyPart.UpperBody) model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Bounds bounds = BoundsOf(model);
            model.transform.position -= bounds.center;
            bounds = BoundsOf(model);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = Vector3.Max(bounds.size, new Vector3(0.05f, 0.05f, 0.05f));
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable sync = root.AddComponent<NetworkTransformReliable>();
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = root.transform;
            root.AddComponent<WorldInventoryItem>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new System.InvalidOperationException(path + " has no network asset ID.");
            EditorUtility.SetDirty(identity);
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void RegisterWithMirror(System.Collections.Generic.List<GameObject> prefabs)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(NetworkManagerPath);
        try
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            foreach (GameObject prefab in prefabs)
                if (prefab != null && !manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
            PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ---- 3. The ghost in the open scene --------------------------------------------------

    private static string SetupScene()
    {
        TicketMinigame ticket = Object.FindAnyObjectByType<TicketMinigame>(FindObjectsInactive.Include);
        PopcornMinigameBootstrap popcorn = Object.FindAnyObjectByType<PopcornMinigameBootstrap>(FindObjectsInactive.Include);
        if (ticket == null && popcorn == null)
            return "No ticket / popcorn counter in the open scene: open Cinema_GamePlay and run this again to add the ghost.";

        MissingPartsGhost ghost = Object.FindAnyObjectByType<MissingPartsGhost>(FindObjectsInactive.Include);
        bool created = ghost == null;
        GameObject go;
        if (created)
        {
            go = new GameObject(SceneObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Add Missing Parts Ghost");
            go.AddComponent<NetworkIdentity>();
            ghost = go.AddComponent<MissingPartsGhost>();
        }
        else go = ghost.gameObject;

        if (go.GetComponent<NetworkTransformReliable>() == null)
        {
            NetworkTransformReliable sync = Undo.AddComponent<NetworkTransformReliable>(go);
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.target = go.transform;
        }
        NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
        if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(go);
        agent.enabled = false; // the server turns it on when it starts following
        agent.baseOffset = 1f;
        agent.radius = 0.35f;
        agent.height = 2f;

        var so = new SerializedObject(ghost);
        so.FindProperty("ticketCounter").objectReferenceValue = ticket;
        if (ticket != null)
        {
            var src = new SerializedObject(ticket);
            Copy(src, "spawnPoint", so, "ticketRoute.spawn");
            CopyArray(src, "approachPath", so, "ticketRoute.approach");
            Copy(src, "counterPoint", so, "ticketRoute.counter");
            CopyArray(src, "departurePath", so, "ticketRoute.departure");
            Copy(src, "exitPoint", so, "ticketRoute.exit");
        }
        if (popcorn != null)
        {
            var src = new SerializedObject(popcorn);
            Copy(src, "customerSpawnPoint", so, "popcornRoute.spawn");
            CopyArray(src, "customerApproachPath", so, "popcornRoute.approach");
            Copy(src, "customerWaitPoint", so, "popcornRoute.counter");
            CopyArray(src, "customerDeparturePath", so, "popcornRoute.departure");
            Copy(src, "customerExitPoint", so, "popcornRoute.exit");
        }

        if (created)
        {
            // First time only: a 14 x 5 x 14 m box around each counter (floor = 1 m under the customer point).
            Transform ticketCounter = so.FindProperty("ticketRoute.counter").objectReferenceValue as Transform;
            Transform popcornCounter = so.FindProperty("popcornRoute.counter").objectReferenceValue as Transform;
            if (ticketCounter != null)
                so.FindProperty("ticketZone").boundsValue = new Bounds(ticketCounter.position + Vector3.up * 1.5f, new Vector3(14f, 5f, 14f));
            if (popcornCounter != null)
                so.FindProperty("popcornZone").boundsValue = new Bounds(popcornCounter.position + Vector3.up * 1.5f, new Vector3(14f, 5f, 14f));
            Transform park = so.FindProperty("ticketRoute.spawn").objectReferenceValue as Transform
                ?? so.FindProperty("popcornRoute.spawn").objectReferenceValue as Transform;
            if (park != null) go.transform.SetPositionAndRotation(park.position, park.rotation);
        }

        int agentType = SceneAgentType();
        if (agentType != int.MinValue)
        {
            so.FindProperty("agentTypeID").intValue = agentType;
            agent.agentTypeID = agentType;
        }
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(agent);

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        return (created ? "Added" : "Updated") + " '" + SceneObjectName + "' in the scene (routes copied" +
               (ticket == null ? ", NO ticket counter" : "") + (popcorn == null ? ", NO popcorn counter" : "") +
               "). Check the yellow / orange zone boxes, then SAVE THE SCENE.";
    }

    private static void Copy(SerializedObject src, string from, SerializedObject dst, string to)
    {
        SerializedProperty a = src.FindProperty(from), b = dst.FindProperty(to);
        if (a != null && b != null) b.objectReferenceValue = a.objectReferenceValue;
    }

    private static void CopyArray(SerializedObject src, string from, SerializedObject dst, string to)
    {
        SerializedProperty a = src.FindProperty(from), b = dst.FindProperty(to);
        if (a == null || b == null || !a.isArray || !b.isArray) return;
        b.arraySize = a.arraySize;
        for (int i = 0; i < a.arraySize; i++)
            b.GetArrayElementAtIndex(i).objectReferenceValue = a.GetArrayElementAtIndex(i).objectReferenceValue;
    }

    /// <summary>The agent type of the scene's NavMeshSurface (int.MinValue if there is none).</summary>
    private static int SceneAgentType()
    {
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (behaviour == null || behaviour.GetType().Name != "NavMeshSurface") continue;
            SerializedProperty p = new SerializedObject(behaviour).FindProperty("m_AgentTypeID");
            if (p != null) return p.intValue;
        }
        return int.MinValue;
    }

    // ---- Helpers --------------------------------------------------------------------------

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
