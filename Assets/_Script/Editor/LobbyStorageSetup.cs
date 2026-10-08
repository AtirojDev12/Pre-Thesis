using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 8 Oct (Mr.k). For the NEW lobby (New Lobby Map):
///
///   Tools > Pre-Thesis > Scenes > Lobby: Add Spawn Points + Storage Room
///
///   1. SPAWN POINTS. The old ones lived in "3D Lobby (greybox)" and were deleted
///      from the Lobby scene together with the old shop, so players spawned at the
///      world origin. This adds 6 NetworkStartPosition (2 rows of 3) on the new
///      lobby floor, plus the Offline Test Spawner if the scene has none.
///   2. STORAGE ROOM. A small greybox room in a corner of the new lobby floor with
///      a locker (StorageTerminal): [E] opens YOUR OWN storage.
///
/// Placed from the bounds of "New Lobby Map/Floor". Running it again replaces both.
/// If something already stands there, a dialog says what: move the objects
/// "Lobby Spawn Points" / "Storage Room" by hand, then save the scene.
/// </summary>
public static class LobbyStorageSetup
{
    private const string SpawnGroupName = "Lobby Spawn Points";
    private const string RoomName = "Storage Room";
    private const string PlayerPrefabPath = "Assets/Prefab/Player.prefab";
    private const string MaterialFolder = "Assets/Materials/Lobby";

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Spawn Points + Storage Room", false, 42)]
    private static void Build()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Lobby", "Stop Play mode first.", "OK"); return; }
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Lobby")
        {
            EditorUtility.DisplayDialog("Lobby", "Open Assets/Scenes/Lobby.unity first.", "OK");
            return;
        }

        Bounds floor = FloorBounds(out bool found);
        float groundY = floor.max.y;

        Replace(SpawnGroupName);
        Replace(RoomName);

        // ---- Spawn points: 2 rows of 3, in the open half of the floor, facing the middle.
        var spawns = new GameObject(SpawnGroupName).transform;
        Vector3 centre = floor.center;
        Vector3 spawnCentre = new Vector3(centre.x, groundY, centre.z - Mathf.Min(6f, floor.extents.z * 0.4f));
        int n = 0;
        for (int row = 0; row < 2; row++)
        for (int col = 0; col < 3; col++)
        {
            Vector3 p = spawnCentre + new Vector3((col - 1) * 1.5f, 0f, row * -1.5f);
            p.y = Ground(p, groundY) + 0.05f;
            var go = new GameObject("Lobby Spawn " + (++n));
            go.transform.SetParent(spawns, false);
            go.transform.position = p;
            Vector3 look = new Vector3(centre.x - p.x, 0f, centre.z - p.z);
            go.transform.rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
            go.AddComponent<NetworkStartPosition>();
        }
        EnsureOfflineSpawner(spawns.GetChild(0));

        // ---- Storage room: south-east corner, door facing into the lobby (+Z).
        const float width = 6f, depth = 5f, height = 3f, wall = 0.2f, door = 1.6f;
        Vector3 roomCentre = new Vector3(floor.max.x - width * 0.5f - 0.5f, groundY, floor.min.z + depth * 0.5f + 0.5f);
        var room = new GameObject(RoomName).transform;
        room.position = roomCentre;

        Material wallMat = Mat("Storage_Wall", new Color(0.32f, 0.3f, 0.28f));
        Material lockerMat = Mat("Storage_Locker", new Color(0.25f, 0.33f, 0.38f));
        Material shelfMat = Mat("Storage_Shelf", new Color(0.4f, 0.27f, 0.15f));

        float hw = width * 0.5f, hd = depth * 0.5f, h = height * 0.5f;
        Box(room, "Wall Back", new Vector3(0f, h, -hd), new Vector3(width, height, wall), wallMat);
        Box(room, "Wall Left", new Vector3(-hw, h, 0f), new Vector3(wall, height, depth), wallMat);
        Box(room, "Wall Right", new Vector3(hw, h, 0f), new Vector3(wall, height, depth), wallMat);
        float side = (width - door) * 0.5f;
        Box(room, "Wall Front L", new Vector3(-hw + side * 0.5f, h, hd), new Vector3(side, height, wall), wallMat);
        Box(room, "Wall Front R", new Vector3(hw - side * 0.5f, h, hd), new Vector3(side, height, wall), wallMat);
        Box(room, "Wall Front Top", new Vector3(0f, height - 0.35f, hd), new Vector3(door, 0.7f, wall), wallMat);

        GameObject locker = Box(room, "Storage Locker (press E)", new Vector3(0f, 1f, -hd + 0.45f), new Vector3(1.8f, 2f, 0.6f), lockerMat);
        locker.AddComponent<StorageTerminal>();
        Box(room, "Shelf Left", new Vector3(-hw + 0.45f, 1f, -0.4f), new Vector3(0.6f, 2f, 2.4f), shelfMat);
        Box(room, "Shelf Right", new Vector3(hw - 0.45f, 1f, -0.4f), new Vector3(0.6f, 2f, 2.4f), shelfMat);

        Sign(room, "Sign Outside", new Vector3(0f, height + 0.35f, hd + 0.15f), Quaternion.identity,
            "<color=#FFCC4D>STORAGE</color>\n<size=55%>your own items only</size>", 2.2f);
        Sign(room, "Sign Locker", new Vector3(0f, 2.35f, -hd + 0.8f), Quaternion.identity,
            "<color=#FFCC4D>[E]</color> Storage", 1.2f);

        var lamp = new GameObject("Light");
        lamp.transform.SetParent(room, false);
        lamp.transform.localPosition = new Vector3(0f, height - 0.4f, 0f);
        Light light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 8f;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.9f, 0.75f);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = room.gameObject;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(roomCentre, new Vector3(12f, 6f, 12f)), false);

        // Anything already standing there?
        string blockers = Blockers(new Bounds(roomCentre + Vector3.up * h, new Vector3(width, height - 0.2f, depth)), room)
                        + Blockers(new Bounds(spawnCentre + new Vector3(0f, 1f, -0.75f), new Vector3(4f, 1.6f, 2.5f)), spawns);
        string message = (found ? "Placed on the New Lobby Map floor." : "No 'New Lobby Map/Floor' found: placed around the Scene view.")
                         + "\n\n6 spawn points + Storage Room added. Save the scene (Ctrl+S) and commit."
                         + (string.IsNullOrEmpty(blockers) ? "" : "\n\nSomething is already there, move them by hand:\n" + blockers);
        Debug.Log("[LobbyStorageSetup] " + message.Replace("\n", " "));
        EditorUtility.DisplayDialog("Spawn Points + Storage Room", message, "OK");
    }

    private static Bounds FloorBounds(out bool found)
    {
        GameObject map = GameObject.Find("New Lobby Map");
        Transform floor = map != null ? map.transform.Find("Floor") : null;
        Renderer r = floor != null ? floor.GetComponent<Renderer>() : null;
        found = r != null;
        if (found) return r.bounds;
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        return new Bounds(new Vector3(pivot.x, 0f, pivot.z), new Vector3(24f, 0f, 24f));
    }

    private static float Ground(Vector3 p, float fallback)
    {
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return fallback;
    }

    private static void EnsureOfflineSpawner(Transform spawnPoint)
    {
        OfflinePlayerSpawner existing = Object.FindAnyObjectByType<OfflinePlayerSpawner>(FindObjectsInactive.Include);
        OfflinePlayerSpawner spawner = existing != null ? existing
            : new GameObject("Offline Test Spawner").AddComponent<OfflinePlayerSpawner>();
        var so = new SerializedObject(spawner);
        SerializedProperty prefab = so.FindProperty("playerPrefab");
        if (prefab != null && prefab.objectReferenceValue == null)
            prefab.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        SerializedProperty point = so.FindProperty("spawnPoint");
        if (point != null) point.objectReferenceValue = spawnPoint;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Replace(string name)
    {
        GameObject old = GameObject.Find(name);
        if (old != null && old.transform.parent == null) Object.DestroyImmediate(old);
    }

    private static string Blockers(Bounds area, Transform ignore)
    {
        var names = new List<string>();
        foreach (Collider c in Physics.OverlapBox(area.center, area.extents * 0.95f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (c.transform.IsChildOf(ignore) || c.name == "Floor") continue;
            string n = c.transform.root.name == c.name ? c.name : c.transform.root.name + "/" + c.name;
            if (!names.Contains(n)) names.Add(n);
            if (names.Count >= 6) break;
        }
        return names.Count == 0 ? "" : " - " + string.Join("\n - ", names) + "\n";
    }

    private static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static void Sign(Transform parent, string name, Vector3 localPosition, Quaternion rotation, string text, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = rotation;
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = 4f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(width, 1f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 6f;
        // TextMeshPro reads from its -Z side: turn it so it faces +Z (into the lobby).
        go.transform.localRotation = rotation * Quaternion.Euler(0f, 180f, 0f);
    }

    private static Material Mat(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Lobby");
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
}
