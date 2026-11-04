using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Pre-Thesis > Scenes > Map: Screen Room Bonus Tasks (Normal Room)   (4 Nov 2026, Mr.k)
///
/// The NORMAL (human) screen room gets the same tasks as the ghost room, but as BONUS
/// tasks: +1 task (money) for the player, NOT counted on the Screen Room zone board,
/// so skipping them is fine.
///   - The 3 normal-room dirt marks become bonus (countsForZoneBoard = off).
///   - New "Change film" at the normal room projector: 00:30 and 02:30 only, hold E 5 s, bonus.
///   - Zone board line "Clean both screen rooms" becomes "Clean the Ghost Room" (target = ghost marks).
///
/// Safe to run again (replaces only the bonus film spot). Nothing else in the scene moves.
/// "Map: Setup Screen + Toilet Zones" also calls this at its end, so a rerun keeps it.
/// Nonnapat's room objects are only READ.
/// </summary>
public static class ScreenRoomBonusSetup
{
    private const string RootName = "Zone Chores (Screen + Toilet)";
    private const string FilmName = "Chore - Change Film (Normal Room) [Bonus]";
    private const string FilmMaterialPath = "Assets/Materials/Chore_FilmNeeded.mat";
    private const string ScreenZone = "zone_screen";

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Screen Room Bonus Tasks (Normal Room)", false, 52)]
    private static void Menu()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform root = Find(scene, RootName);
        if (root == null)
        {
            EditorUtility.DisplayDialog("Screen Room Bonus Tasks",
                "\"" + RootName + "\" not found. Open Cinema_GamePlay (and run \"Map: Setup Screen + Toilet Zones\" once).", "OK");
            return;
        }
        Transform theater = Find(scene, "TeaterZone");
        Transform normalRoom = theater != null ? theater.Find("ScreenRoom") : null;
        string report = Apply(scene, root, normalRoom);
        Selection.activeGameObject = root.gameObject;
        EditorUtility.DisplayDialog("Screen Room Bonus Tasks", report + "\n\nCheck it in the Scene view, then SAVE the scene (Ctrl+S).", "OK");
    }

    /// <summary>Does the 3 steps. Returns a short report.</summary>
    public static string Apply(Scene scene, Transform root, Transform normalRoom)
    {
        // 1. Normal-room dirt marks -> bonus. Count the marks that still count for the board.
        int bonusMarks = 0, boardMarks = 0;
        foreach (ZoneChoreSpot spot in root.GetComponentsInChildren<ZoneChoreSpot>(true))
        {
            if (spot.Kind != ZoneTaskKind.CleanScreenRoom) continue;
            bool normal = spot.name.Contains("(Normal Room");
            var so = new SerializedObject(spot);
            so.FindProperty("countsForZoneBoard").boolValue = !normal;
            so.ApplyModifiedProperties();
            if (normal) bonusMarks++; else boardMarks++;
        }

        // 2. Bonus film spot at the normal room projector (replace an old one).
        Transform old = root.Find(FilmName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        string filmNote;
        Transform projector = normalRoom != null ? normalRoom.Find("Projecter") : null;
        if (projector == null)
            filmNote = "Film: NOT made (TeaterZone/ScreenRoom/Projecter not found).";
        else
        {
            MakeFilmSpot(root, Machine(projector));
            filmNote = "Film: 1 bonus \"Change film\" at the normal room projector (00:30, 02:30).";
        }

        // 3. Zone board: the clean line now counts the ghost room only.
        string boardNote = "Board: Screen Rooms board not found (no change).";
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
        {
            if (board.gameObject.scene != scene || board.ZoneID != ScreenZone) continue;
            var so = new SerializedObject(board);
            SerializedProperty tasks = so.FindProperty("tasks");
            for (int i = 0; i < tasks.arraySize; i++)
            {
                SerializedProperty t = tasks.GetArrayElementAtIndex(i);
                if (t.FindPropertyRelative("kind").enumValueIndex != (int)ZoneTaskKind.CleanScreenRoom) continue;
                t.FindPropertyRelative("label").stringValue = "Clean the Ghost Room (after 01:00)";
                t.FindPropertyRelative("target").intValue = Mathf.Max(1, boardMarks);
                boardNote = $"Board: \"Clean the Ghost Room\" target = {Mathf.Max(1, boardMarks)}.";
            }
            so.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return $"Normal room dirt marks set to bonus: {bonusMarks} (ghost room marks on the board: {boardMarks}).\n{filmNote}\n{boardNote}";
    }

    private static void MakeFilmSpot(Transform parent, Transform machine)
    {
        Bounds b = Bounds(machine);
        var go = new GameObject(FilmName);
        Undo.RegisterCreatedObjectUndo(go, "Bonus film spot");
        go.transform.SetParent(parent, false);
        go.transform.position = b.center;

        // A box a little bigger than the machine, so the look ray hits this first.
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = b.size + Vector3.one * 0.3f;

        GameObject warn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        warn.name = "Film Needed Light";
        Object.DestroyImmediate(warn.GetComponent<Collider>());
        warn.transform.SetParent(go.transform, false);
        warn.transform.position = new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        warn.transform.localScale = Vector3.one * 0.14f;
        Material light = AssetDatabase.LoadAssetAtPath<Material>(FilmMaterialPath);
        if (light != null) warn.GetComponent<Renderer>().sharedMaterial = light;

        go.AddComponent<NetworkIdentity>();
        ZoneChoreSpot spot = go.AddComponent<ZoneChoreSpot>();
        var so = new SerializedObject(spot);
        so.FindProperty("kind").enumValueIndex = (int)ZoneTaskKind.FilmChange;
        so.FindProperty("zoneID").stringValue = ScreenZone;
        so.FindProperty("actionText").stringValue = "Change film";
        so.FindProperty("holdSeconds").floatValue = 5f;
        so.FindProperty("fromHour").intValue = 0;
        so.FindProperty("fromMinute").intValue = 30;
        so.FindProperty("repeatEveryHours").intValue = 2;
        so.FindProperty("maxTimes").intValue = 2;            // 00:30 and 02:30 only
        so.FindProperty("countsForZoneBoard").boolValue = false;
        so.FindProperty("interactionPrompt").stringValue = "[Hold E] Change film (bonus)";
        SerializedProperty looks = so.FindProperty("showWhenNeeded");
        looks.arraySize = 1;
        looks.GetArrayElementAtIndex(0).objectReferenceValue = warn;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>"Projecter" is the whole booth: the machine is its smallest part (same as the ghost room tool).</summary>
    private static Transform Machine(Transform projector)
    {
        Transform best = projector;
        float bestVolume = float.MaxValue;
        foreach (Renderer r in projector.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 s = r.bounds.size;
            float volume = s.x * s.y * s.z;
            if (volume > 0.001f && volume < bestVolume) { bestVolume = volume; best = r.transform; }
        }
        return best;
    }

    private static Bounds Bounds(Transform t)
    {
        bool has = false;
        var b = new Bounds(t.position, Vector3.one);
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static Transform Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root.transform;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
        return null;
    }
}
