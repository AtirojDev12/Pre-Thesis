using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): tutorial rooms in the LOBBY scene.
///
///   Tools > Pre-Thesis > Scenes > Lobby: Setup Tutorial (door + Cinema room)
///       One click: the lobby door (where the Scene view looks) + the Cinema tutorial room.
///   Tools > Pre-Thesis > Scenes > Lobby: Add Tutorial Door (where the Scene view looks)
///       Moves / remakes only the lobby door.
///   Tools > Pre-Thesis > Scenes > Lobby: Add Tutorial Room For A Map...
///       NEW MAP LATER: type its map ID (RoomConfig.mapID) and a name -> a new empty
///       tutorial room with its own "Back to the lobby" door. Nothing else to code:
///       the lobby door finds the room by the map chosen in Create Room.
///
/// Rooms are greybox boxes far below the lobby (slot = 60 m apart). Put the tutorial
/// content inside the room's "Content (put tutorial things here)" object.
/// </summary>
public static class LobbyTutorialSetup
{
    private const string DoorName = "Tutorial Door (Lobby)";
    private const string MaterialFolder = "Assets/Materials/Lobby";
    private static readonly Vector3 FirstRoomPosition = new Vector3(0f, -300f, 0f);
    private const float RoomSpacing = 60f;

    // Room size (metres).
    private const float Width = 14f, Length = 18f, Height = 4.5f, Wall = 0.3f;

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Setup Tutorial (door + Cinema room)", false, 44)]
    private static void SetupAll()
    {
        if (!CheckLobby()) return;
        PlaceDoor();
        TutorialRoom cinema = FindRoom(RoomConfig.DemoMapID);
        string roomNote = cinema != null ? "Cinema room already there (kept)." : "Cinema tutorial room made.";
        if (cinema == null) cinema = BuildRoom(RoomConfig.DemoMapID, RoomDisplay.MapName(RoomConfig.DemoMapID));
        Finish(cinema.gameObject, "Lobby door placed where the Scene view looks.\n" + roomNote);
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Tutorial Door (where the Scene view looks)", false, 45)]
    private static void DoorOnly()
    {
        if (!CheckLobby()) return;
        GameObject door = PlaceDoor();
        Finish(door, "Lobby tutorial door placed where the Scene view looks. Move / turn it if needed (the blue arrow of \"Come Back Here\" = where players appear when they return).");
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Lobby: Add Tutorial Room For A Map...", false, 46)]
    private static void RoomForMap()
    {
        if (!CheckLobby()) return;
        TutorialRoomWindow.Open();
    }

    // ---- Lobby door ------------------------------------------------------------------

    private static GameObject PlaceDoor()
    {
        GameObject old = GameObject.Find(DoorName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        SceneView view = SceneView.lastActiveSceneView;
        Vector3 at = view != null ? view.pivot : Vector3.zero;
        Vector3 toCamera = view != null ? view.camera.transform.position - at : Vector3.back;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.01f) toCamera = Vector3.back;
        at.y = Ground(at, at.y);

        var root = new GameObject(DoorName);
        Undo.RegisterCreatedObjectUndo(root, "Tutorial door");
        root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toCamera.normalized, Vector3.up)); // front faces the camera

        BuildDoorLook(root.transform, "TUTORIAL", new Color(0.18f, 0.32f, 0.55f));
        var back = new GameObject("Come Back Here").transform;
        back.SetParent(root.transform, false);
        back.localPosition = new Vector3(0f, 0f, 1.6f);
        back.localRotation = Quaternion.identity;

        TutorialDoor door = root.AddComponent<TutorialDoor>();
        door.Set(TutorialDoor.Kind.ToTutorial, back);
        EditorUtility.SetDirty(door);
        return root;
    }

    // ---- Tutorial room ----------------------------------------------------------------

    /// <summary>Builds (or replaces) the tutorial room of one map.</summary>
    public static TutorialRoom BuildRoom(string mapID, string displayName)
    {
        TutorialRoom existing = FindRoom(mapID);
        Vector3 position;
        if (existing != null)
        {
            position = existing.transform.position;
            Undo.DestroyObjectImmediate(existing.gameObject);
        }
        else position = FirstRoomPosition + Vector3.right * RoomSpacing * Object.FindObjectsByType<TutorialRoom>(FindObjectsInactive.Include).Length;

        var root = new GameObject($"Tutorial Room - {displayName} ({mapID})");
        Undo.RegisterCreatedObjectUndo(root, "Tutorial room");
        root.transform.position = position;

        Material floor = Mat("Tutorial_Floor", new Color(0.32f, 0.33f, 0.36f));
        Material wall = Mat("Tutorial_Wall", new Color(0.62f, 0.64f, 0.7f));
        Transform shell = new GameObject("Room (greybox)").transform;
        shell.SetParent(root.transform, false);
        Box(shell, "Floor", new Vector3(0f, -Wall / 2f, 0f), new Vector3(Width, Wall, Length), floor);
        Box(shell, "Ceiling", new Vector3(0f, Height + Wall / 2f, 0f), new Vector3(Width, Wall, Length), wall);
        Box(shell, "Wall Left", new Vector3(-Width / 2f - Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length), wall);
        Box(shell, "Wall Right", new Vector3(Width / 2f + Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Length), wall);
        Box(shell, "Wall Back (exit side)", new Vector3(0f, Height / 2f, -Length / 2f - Wall / 2f), new Vector3(Width + Wall * 2f, Height, Wall), wall);
        Box(shell, "Wall Front", new Vector3(0f, Height / 2f, Length / 2f + Wall / 2f), new Vector3(Width + Wall * 2f, Height, Wall), wall);

        for (int i = 0; i < 3; i++)
        {
            var lamp = new GameObject("Light " + (i + 1));
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(0f, Height - 0.4f, -Length / 3f + i * Length / 3f);
            Light l = lamp.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 11f;
            l.intensity = 2.2f;
            l.color = new Color(1f, 0.96f, 0.88f);
            l.shadows = LightShadows.None;
        }

        // Title on the far wall.
        Sign(root.transform, "Title", new Vector3(0f, Height - 1.1f, Length / 2f - 0.05f), Quaternion.Euler(0f, 180f, 0f),
            "TUTORIAL\n" + displayName, 8f);

        // Arrival point near the exit wall, facing into the room.
        var arrival = new GameObject("Arrival Point").transform;
        arrival.SetParent(root.transform, false);
        arrival.localPosition = new Vector3(0f, 0f, -Length / 2f + 2.2f);
        arrival.localRotation = Quaternion.identity;

        // "Back to the lobby" door on the exit wall, facing into the room.
        var exit = new GameObject("Tutorial Door (Back to the lobby)");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0f, -Length / 2f + 0.15f);
        exit.transform.localRotation = Quaternion.identity; // front (+Z) faces into the room
        BuildDoorLook(exit.transform, "LOBBY", new Color(0.45f, 0.22f, 0.2f));
        exit.AddComponent<TutorialDoor>().Set(TutorialDoor.Kind.BackToLobby, null);

        var content = new GameObject("Content (put tutorial things here)");
        content.transform.SetParent(root.transform, false);

        TutorialRoom room = root.AddComponent<TutorialRoom>();
        room.Set(mapID, arrival);
        EditorUtility.SetDirty(room);
        return room;
    }

    private static TutorialRoom FindRoom(string mapID)
    {
        foreach (TutorialRoom r in Object.FindObjectsByType<TutorialRoom>(FindObjectsInactive.Include))
            if (r.MapID == mapID) return r;
        return null;
    }

    // ---- Shared look -----------------------------------------------------------------

    /// <summary>A simple door: frame + panel (the E target) + a sign above. Front = +Z of the parent.</summary>
    private static void BuildDoorLook(Transform parent, string label, Color panelColor)
    {
        Material frame = Mat("Tutorial_DoorFrame", new Color(0.12f, 0.12f, 0.14f));
        Material panel = Mat("Tutorial_Door_" + label, panelColor);
        Box(parent, "Frame Left", new Vector3(-0.8f, 1.25f, 0f), new Vector3(0.15f, 2.5f, 0.25f), frame);
        Box(parent, "Frame Right", new Vector3(0.8f, 1.25f, 0f), new Vector3(0.15f, 2.5f, 0.25f), frame);
        Box(parent, "Frame Top", new Vector3(0f, 2.55f, 0f), new Vector3(1.75f, 0.15f, 0.25f), frame);
        Box(parent, "Door Panel", new Vector3(0f, 1.2f, 0f), new Vector3(1.45f, 2.4f, 0.12f), panel);
        Sign(parent, "Door Sign", new Vector3(0f, 2.95f, 0.14f), Quaternion.identity, label, 2.2f);
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

    /// <summary>World text facing the parent's +Z (TextMeshPro reads from its -Z side, so it is turned 180).</summary>
    private static void Sign(Transform parent, string name, Vector3 localPosition, Quaternion rotation, string text, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(width, 1.6f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 8f;
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

    private static float Ground(Vector3 p, float fallback)
    {
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return fallback;
    }

    private static bool CheckLobby()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Tutorial", "Stop Play mode first.", "OK"); return false; }
        if (EditorSceneManager.GetActiveScene().name != "Lobby")
        {
            EditorUtility.DisplayDialog("Tutorial", "Open Assets/Scenes/Lobby.unity first.", "OK");
            return false;
        }
        return true;
    }

    private static void Finish(GameObject select, string message)
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = select;
        EditorUtility.DisplayDialog("Tutorial", message + "\n\nThen SAVE the scene (Ctrl+S).", "OK");
    }

    // ---- "Add Tutorial Room For A Map..." window ----------------------------------------

    private sealed class TutorialRoomWindow : EditorWindow
    {
        private string mapID = RoomConfig.DemoMapID;
        private string displayName = "";

        public static void Open()
        {
            var w = GetWindow<TutorialRoomWindow>(true, "Add Tutorial Room", true);
            w.minSize = new Vector2(380f, 150f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("A new map's tutorial: type the map ID it uses in RoomConfig (Create Room). " +
                "The lobby door sends players to the room with the same map ID.", MessageType.Info);
            mapID = EditorGUILayout.TextField("Map ID", mapID);
            displayName = EditorGUILayout.TextField("Name on the wall", string.IsNullOrEmpty(displayName) ? RoomDisplay.MapName(mapID) : displayName);
            GUI.enabled = !string.IsNullOrWhiteSpace(mapID);
            if (GUILayout.Button(FindRoom(mapID.Trim()) != null ? "Rebuild this map's tutorial room" : "Build tutorial room"))
            {
                TutorialRoom room = BuildRoom(mapID.Trim(), displayName.Trim());
                Finish(room.gameObject, $"Tutorial room for \"{mapID.Trim()}\" made (far below the lobby).");
                Close();
            }
            GUI.enabled = true;
        }
    }
}
