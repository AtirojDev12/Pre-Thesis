using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Pre-Thesis > Setup Gameplay Loop (Task Boards + Exit)
///
/// Adds the prototype game-loop objects to the OPEN scene (Cinema_GamePlay):
///   - "Task Board - Popcorn & Water"  (BBQ / Cheese / Paprika / Water, 15 each)
///   - "Task Board - Tickets"          (Humans 15, Ghosts 15)
///   - "Exit (Prototype)"               (walk-in exit, glows when open, fits 6)
/// They appear in front of the Scene view camera. MOVE THEM to the right places,
/// then save the scene (Ctrl+S). Running it again does not make duplicates.
/// Task lines and counts are edited on each board's ZoneTaskList in the Inspector.
/// </summary>
public static class GameplayLoopSetup
{
    private const string GlowMaterialPath = "Assets/Materials/Prototype_ExitGlow.mat";

    [MenuItem("Tools/Pre-Thesis/Setup Gameplay Loop (Task Boards + Exit)")]
    private static void Setup()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog("Setup Gameplay Loop",
                "There is no MatchDirector in this scene. Run Tools > Pre-Thesis > Add MatchDirector to Open Scene first.", "OK");
            return;
        }

        Vector3 origin = Vector3.zero;
        Quaternion facing = Quaternion.identity;
        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            origin = view.pivot;
            Vector3 flat = Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up);
            if (flat.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(-flat.normalized, Vector3.up); // boards face the camera
        }

        int made = 0;
        GameObject last = null;

        if (FindBoard("zone_popcorn") == null)
        {
            last = MakeBoard("Task Board - Popcorn & Water", "zone_popcorn", "POPCORN & WATER",
                origin + facing * new Vector3(-1.2f, 1.6f, 0f), facing, so =>
                {
                    AddTask(so, "Sell BBQ popcorn", ZoneTaskKind.Popcorn, PopcornFlavor.BBQ, 0, 15);
                    AddTask(so, "Sell Cheese popcorn", ZoneTaskKind.Popcorn, PopcornFlavor.Cheese, 0, 15);
                    AddTask(so, "Sell Paprika popcorn", ZoneTaskKind.Popcorn, PopcornFlavor.Paprika, 0, 15);
                    AddTask(so, "Sell Water", ZoneTaskKind.Water, PopcornFlavor.Drink, 0, 15);
                });
            made++;
        }

        if (FindBoard("zone_ticket") == null)
        {
            last = MakeBoard("Task Board - Tickets", "zone_ticket", "TICKETS",
                origin + facing * new Vector3(1.2f, 1.6f, 0f), facing, so =>
                {
                    AddTask(so, "Sell tickets to Humans", ZoneTaskKind.Ticket, PopcornFlavor.None, 0, 15, ZoneTaskCustomer.Human);
                    AddTask(so, "Sell tickets to Ghosts", ZoneTaskKind.Ticket, PopcornFlavor.None, 0, 15, ZoneTaskCustomer.Ghost);
                });
            made++;
        }

        if (GameObject.Find("Exit (Prototype)") == null)
        {
            last = MakeExit(origin + facing * new Vector3(0f, 0f, -3f), facing);
            made++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        if (last != null) Selection.activeGameObject = last;

        Debug.Log(made == 0
            ? "[GameplayLoopSetup] Everything is already in the scene. Nothing added."
            : $"[GameplayLoopSetup] Added {made} object(s) in front of the Scene camera in '{scene.name}'. " +
              "Move them into place (boards: read from the side the cyan line points to), then SAVE the scene (Ctrl+S).");
    }

    // ---- Task boards -----------------------------------------------------------

    private static ZoneTaskList FindBoard(string zoneID)
    {
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>())
            if (board.ZoneID == zoneID) return board;
        return null;
    }

    private static GameObject MakeBoard(string name, string zoneID, string title, Vector3 position, Quaternion rotation,
        System.Action<SerializedObject> fillTasks)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Add task board");
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<NetworkIdentity>();
        ZoneTaskList list = go.AddComponent<ZoneTaskList>();

        var so = new SerializedObject(list);
        so.FindProperty("zoneID").stringValue = zoneID;
        so.FindProperty("title").stringValue = title;
        so.FindProperty("tasks").ClearArray();
        fillTasks(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    private static void AddTask(SerializedObject so, string label, ZoneTaskKind kind, PopcornFlavor flavor, int movie,
        int target, ZoneTaskCustomer customer = ZoneTaskCustomer.Any)
    {
        SerializedProperty tasks = so.FindProperty("tasks");
        int i = tasks.arraySize;
        tasks.InsertArrayElementAtIndex(i);
        SerializedProperty t = tasks.GetArrayElementAtIndex(i);
        t.FindPropertyRelative("label").stringValue = label;
        t.FindPropertyRelative("kind").enumValueIndex = (int)kind;
        t.FindPropertyRelative("flavor").enumValueIndex = (int)flavor;
        t.FindPropertyRelative("movie").intValue = movie;
        t.FindPropertyRelative("customer").enumValueIndex = (int)customer;
        t.FindPropertyRelative("target").intValue = target;
    }

    // ---- Exit ---------------------------------------------------------------------

    private static GameObject MakeExit(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("Exit (Prototype)");
        Undo.RegisterCreatedObjectUndo(root, "Add prototype exit");
        root.transform.SetPositionAndRotation(position, rotation);
        root.AddComponent<NetworkIdentity>();

        // Walk-in area: stand in this box while the exit is open = you are out.
        var area = root.AddComponent<BoxCollider>();
        area.isTrigger = true;
        area.center = new Vector3(0f, 1.5f, 0.6f);
        area.size = new Vector3(2.4f, 3f, 1.6f);

        // The door itself (normal look; solid).
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "Door";
        door.transform.SetParent(root.transform, false);
        door.transform.localPosition = new Vector3(0f, 1.4f, -0.3f);
        door.transform.localScale = new Vector3(2f, 2.8f, 0.2f);

        // Bright glowing panel in front of the door: ON only while open.
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        glow.name = "Glow (on when open)";
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.45f, -0.15f);
        glow.transform.localScale = new Vector3(2.2f, 3f, 0.05f);
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.GetComponent<Renderer>().sharedMaterial = GlowMaterial();
        glow.SetActive(false);

        var lightGo = new GameObject("Glow Light (on when open)");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.6f, 1f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.6f, 1f, 0.7f);
        light.range = 12f;
        light.intensity = 6f;
        lightGo.SetActive(false);

        ExitPoint exit = root.AddComponent<ExitPoint>();
        var so = new SerializedObject(exit);
        so.FindProperty("exitName").stringValue = "Main Exit";
        so.FindProperty("capacity").intValue = 6; // one exit for the whole prototype team
        so.FindProperty("exitArea").objectReferenceValue = area;
        SerializedProperty glows = so.FindProperty("glowWhenOpen");
        glows.arraySize = 2;
        glows.GetArrayElementAtIndex(0).objectReferenceValue = glow;
        glows.GetArrayElementAtIndex(1).objectReferenceValue = lightGo;
        so.ApplyModifiedPropertiesWithoutUndo();

        return root;
    }

    private static Material GlowMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var material = new Material(shader) { name = "Prototype_ExitGlow" };
        Color bright = new Color(0.75f, 1f, 0.8f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", bright);
        if (material.HasProperty("_Color")) material.SetColor("_Color", bright);

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        AssetDatabase.CreateAsset(material, GlowMaterialPath);
        AssetDatabase.SaveAssets();
        return material;
    }
}
