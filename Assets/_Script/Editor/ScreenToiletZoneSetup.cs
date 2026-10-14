using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Pre-Thesis > Scenes > Map: Setup Screen + Toilet Zones   (14 Oct 2026, Mr.k)
///
/// Open Cinema_GamePlay first. Adds, under one new object "Zone Chores (Screen + Toilet)":
///   - Screen Room zone (zone_screen):
///       * Change film in the GHOST room "TeaterZone/ScreenRoom (1)" at 02:00 and 04:00 (hold E 5 s)
///       * 3 dirt marks in EACH screen room, from 01:00 (hold E 3 s)
///   - Toilet zone (zone_toilet): 3 dirt marks in the men's and 3 in the women's toilet, from 03:00
///   - 2 task boards ("SCREEN ROOMS", "TOILETS") next to the Tickets board.
/// Nonnapat's room objects are only READ (positions), never changed.
/// Running it again replaces everything it made before. Then SAVE the scene (Ctrl+S).
/// </summary>
public static class ScreenToiletZoneSetup
{
    private const string RootName = "Zone Chores (Screen + Toilet)";
    private const string DirtMaterialPath = "Assets/Materials/Chore_Dirt.mat";
    private const string FilmMaterialPath = "Assets/Materials/Chore_FilmNeeded.mat";

    private const string ScreenZone = "zone_screen";
    private const string ToiletZone = "zone_toilet";

    private const int ScreenCleanHour = 1;
    private const int ToiletCleanHour = 3;
    private const int FilmFirstHour = 2;
    private const int FilmEveryHours = 2;
    private const int FilmChangesPerNight = 2; // 02:00 and 04:00 (the night ends at 06:00)

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Setup Screen + Toilet Zones", false, 52)]
    private static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog("Screen + Toilet Zones",
                "There is no MatchDirector in the open scene. Open Cinema_GamePlay first.", "OK");
            return;
        }

        Transform theater = FindInScene(scene, "TeaterZone");
        Transform normalRoom = theater != null ? theater.Find("ScreenRoom") : null;
        Transform ghostRoom = theater != null ? theater.Find("ScreenRoom (1)") : null;
        Transform toilet = FindInScene(scene, "Toilet");
        Transform men = toilet != null ? toilet.Find("MenToilet") : null;
        Transform women = toilet != null ? toilet.Find("WomenToilet") : null;

        var missing = new List<string>();
        if (normalRoom == null) missing.Add("TeaterZone/ScreenRoom");
        if (ghostRoom == null) missing.Add("TeaterZone/ScreenRoom (1)");
        if (men == null) missing.Add("Toilet/MenToilet");
        if (women == null) missing.Add("Toilet/WomenToilet");
        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("Screen + Toilet Zones",
                "Not found in the open scene:\n- " + string.Join("\n- ", missing) +
                "\n\nNothing was changed.", "OK");
            return;
        }

        // Replace what an earlier run made.
        Transform old = FindInScene(scene, RootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup Screen + Toilet Zones");
        SceneManager.MoveGameObjectToScene(root, scene);

        Material dirt = GetMaterial(DirtMaterialPath, new Color(0.16f, 0.11f, 0.06f), false);
        Material filmLight = GetMaterial(FilmMaterialPath, new Color(1f, 0.08f, 0.08f), true);
        Physics.SyncTransforms();

        // ---- Screen Room zone ---------------------------------------------------
        Transform projector = ghostRoom.Find("Projecter");
        MakeFilmSpot(root.transform, projector != null ? projector : ghostRoom, filmLight);

        int screenSpots = 0;
        foreach (Transform room in new[] { normalRoom, ghostRoom })
        {
            string roomName = room == ghostRoom ? "Ghost Room" : "Normal Room";
            List<Vector3> points = RoomPoints(room, 3);
            for (int i = 0; i < points.Count; i++)
            {
                MakeDirtSpot(root.transform, $"Chore - Dirt ({roomName} {i + 1})", points[i], dirt,
                    ZoneTaskKind.CleanScreenRoom, ScreenZone, ScreenCleanHour);
                screenSpots++;
            }
        }

        // ---- Toilet zone ----------------------------------------------------------
        int toiletSpots = 0;
        toiletSpots += MakeToiletSpots(root.transform, men, "Men", new[] { "M_Basin", "Urinal", "MPartion" }, dirt);
        toiletSpots += MakeToiletSpots(root.transform, women, "Women", new[] { "WM_Basin", "WomenToiletRoom", "Mirror" }, dirt);

        // ---- Task boards (next to the Tickets board) ------------------------------
        RemoveBoardOutside(root.transform, ScreenZone);
        RemoveBoardOutside(root.transform, ToiletZone);
        BoardPlacement(out Vector3 boardPos, out Quaternion boardRot, out Vector3 step);

        MakeBoard(root.transform, "Task Board - Screen Rooms", ScreenZone, "SCREEN ROOMS", boardPos + step, boardRot, so =>
        {
            AddTask(so, "Change film in the Ghost Room (02:00, 04:00)", ZoneTaskKind.FilmChange, FilmChangesPerNight);
            AddTask(so, "Clean both screen rooms (after 01:00)", ZoneTaskKind.CleanScreenRoom, screenSpots);
        });
        MakeBoard(root.transform, "Task Board - Toilets", ToiletZone, "TOILETS", boardPos + step * 2f, boardRot, so =>
        {
            AddTask(so, "Clean both toilets (after 03:00)", ZoneTaskKind.CleanToilet, toiletSpots);
        });

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        EditorUtility.DisplayDialog("Screen + Toilet Zones",
            $"Done.\n\nScreen Room: 1 film spot (Ghost Room) + {screenSpots} dirt marks.\n" +
            $"Toilets: {toiletSpots} dirt marks.\n2 task boards next to the Tickets board.\n\n" +
            "Check the dirt marks and boards in the Scene view (move any that look wrong), then SAVE the scene (Ctrl+S).",
            "OK");
    }

    // ---- Spots ----------------------------------------------------------------------

    private static void MakeFilmSpot(Transform parent, Transform projector, Material lightMaterial)
    {
        Bounds b = RendererBounds(projector, null);
        var go = new GameObject("Chore - Change Film (Ghost Room)");
        Undo.RegisterCreatedObjectUndo(go, "Film spot");
        go.transform.SetParent(parent, false);
        go.transform.position = b.center;

        // A box a little BIGGER than the projector, so the look ray hits this first.
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = b.size + Vector3.one * 0.3f;

        GameObject warn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        warn.name = "Film Needed Light";
        Object.DestroyImmediate(warn.GetComponent<Collider>());
        warn.transform.SetParent(go.transform, false);
        warn.transform.position = new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        warn.transform.localScale = Vector3.one * 0.14f;
        warn.GetComponent<Renderer>().sharedMaterial = lightMaterial;

        Configure(go, ZoneTaskKind.FilmChange, ScreenZone, "Change film", 5f, FilmFirstHour, FilmEveryHours, warn);
    }

    private static int MakeToiletSpots(Transform parent, Transform toilet, string label, string[] parts, Material dirt)
    {
        int made = 0;
        Bounds whole = RendererBounds(toilet, null);
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = toilet.Find(parts[i]);
            Vector3 above = part != null ? RendererBounds(part, null).center : whole.center;
            // Pull slightly towards the room centre so wall parts (mirrors) do not put the mark in the wall.
            above = Vector3.Lerp(above, new Vector3(whole.center.x, above.y, whole.center.z), 0.25f);
            Vector3 floor = FloorBelow(above, whole);
            MakeDirtSpot(parent, $"Chore - Dirt ({label} Toilet {i + 1})", floor, dirt,
                ZoneTaskKind.CleanToilet, ToiletZone, ToiletCleanHour);
            made++;
        }
        return made;
    }

    private static void MakeDirtSpot(Transform parent, string name, Vector3 floorPoint, Material dirt,
        ZoneTaskKind kind, string zoneID, int fromHour)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Dirt spot");
        go.transform.SetParent(parent, false);
        go.transform.position = floorPoint;

        // Thin, so nobody trips on it; the look ray (non-trigger only) still hits it.
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.03f, 0f);
        box.size = new Vector3(1f, 0.06f, 1f);

        GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
        mark.name = "Dirt Mark";
        Object.DestroyImmediate(mark.GetComponent<Collider>());
        mark.transform.SetParent(go.transform, false);
        mark.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        mark.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies flat, faces up
        mark.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        mark.GetComponent<Renderer>().sharedMaterial = dirt;

        Configure(go, kind, zoneID, "Clean", 3f, fromHour, 0, mark);
    }

    private static void Configure(GameObject go, ZoneTaskKind kind, string zoneID, string action, float hold,
        int fromHour, int repeat, GameObject look)
    {
        go.AddComponent<NetworkIdentity>();
        ZoneChoreSpot spot = go.AddComponent<ZoneChoreSpot>();
        var so = new SerializedObject(spot);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("zoneID").stringValue = zoneID;
        so.FindProperty("actionText").stringValue = action;
        so.FindProperty("holdSeconds").floatValue = hold;
        so.FindProperty("fromHour").intValue = fromHour;
        so.FindProperty("repeatEveryHours").intValue = repeat;
        so.FindProperty("interactionPrompt").stringValue = "[Hold E] " + action;
        SerializedProperty looks = so.FindProperty("showWhenNeeded");
        looks.arraySize = 1;
        looks.GetArrayElementAtIndex(0).objectReferenceValue = look;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- Geometry (read only) -----------------------------------------------------------

    /// <summary>Points spread along the room's long side, dropped onto the floor / seats.</summary>
    private static List<Vector3> RoomPoints(Transform room, int count)
    {
        Bounds b = RendererBounds(room, "Roof");
        bool alongX = b.size.x >= b.size.z;
        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);
            Vector3 p = alongX
                ? new Vector3(Mathf.Lerp(b.min.x, b.max.x, t), b.center.y, b.center.z)
                : new Vector3(b.center.x, b.center.y, Mathf.Lerp(b.min.z, b.max.z, t));
            points.Add(FloorBelow(p, b));
        }
        return points;
    }

    private static Vector3 FloorBelow(Vector3 point, Bounds room)
    {
        Vector3 start = new Vector3(point.x, room.max.y - 0.3f, point.z);
        float distance = room.size.y + 1f;
        RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 result = new Vector3(point.x, room.min.y, point.z);
        foreach (RaycastHit hit in hits)
        {
            if (hit.normal.y < 0.5f || hit.distance >= best) continue; // floors / seats only, not walls
            best = hit.distance;
            result = hit.point;
        }
        return result;
    }

    private static Bounds RendererBounds(Transform t, string skipNameContaining)
    {
        bool has = false;
        Bounds bounds = new Bounds(t.position, Vector3.one);
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!string.IsNullOrEmpty(skipNameContaining) && HasParentNamed(r.transform, t, skipNameContaining)) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    private static bool HasParentNamed(Transform t, Transform stop, string part)
    {
        for (Transform p = t; p != null && p != stop; p = p.parent)
            if (p.name.Contains(part)) return true;
        return false;
    }

    // ---- Boards -----------------------------------------------------------------------------

    private static void BoardPlacement(out Vector3 position, out Quaternion rotation, out Vector3 step)
    {
        ZoneTaskList tickets = FindBoard("zone_ticket");
        ZoneTaskList popcorn = FindBoard("zone_popcorn");
        if (tickets != null)
        {
            position = tickets.transform.position;
            rotation = tickets.transform.rotation;
            Vector3 away = popcorn != null
                ? Vector3.ProjectOnPlane(tickets.transform.position - popcorn.transform.position, Vector3.up)
                : Vector3.zero;
            step = away.sqrMagnitude > 0.01f ? away.normalized * 2.4f : tickets.transform.right * 2.4f;
            return;
        }

        SceneView view = SceneView.lastActiveSceneView;
        position = view != null ? view.pivot + Vector3.up * 1.6f : Vector3.up * 1.6f;
        rotation = Quaternion.identity;
        step = Vector3.right * 2.4f;
    }

    private static void RemoveBoardOutside(Transform root, string zoneID)
    {
        ZoneTaskList board = FindBoard(zoneID);
        if (board != null && !board.transform.IsChildOf(root)) Undo.DestroyObjectImmediate(board.gameObject);
    }

    private static ZoneTaskList FindBoard(string zoneID)
    {
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
            if (board.ZoneID == zoneID) return board;
        return null;
    }

    private static void MakeBoard(Transform parent, string name, string zoneID, string title, Vector3 position,
        Quaternion rotation, System.Action<SerializedObject> fillTasks)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Add task board");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<NetworkIdentity>();
        ZoneTaskList list = go.AddComponent<ZoneTaskList>();

        var so = new SerializedObject(list);
        so.FindProperty("zoneID").stringValue = zoneID;
        so.FindProperty("title").stringValue = title;
        so.FindProperty("tasks").ClearArray();
        fillTasks(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddTask(SerializedObject so, string label, ZoneTaskKind kind, int target)
    {
        SerializedProperty tasks = so.FindProperty("tasks");
        int i = tasks.arraySize;
        tasks.InsertArrayElementAtIndex(i);
        SerializedProperty t = tasks.GetArrayElementAtIndex(i);
        t.FindPropertyRelative("label").stringValue = label;
        t.FindPropertyRelative("kind").enumValueIndex = (int)kind;
        t.FindPropertyRelative("flavor").enumValueIndex = (int)PopcornFlavor.None;
        t.FindPropertyRelative("movie").intValue = 0;
        t.FindPropertyRelative("customer").enumValueIndex = (int)ZoneTaskCustomer.Any;
        t.FindPropertyRelative("target").intValue = Mathf.Max(1, target);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static Transform FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root.transform;
        }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        }
        return null;
    }

    private static Material GetMaterial(string path, Color color, bool glow)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
        if (glow)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 3f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }
}
