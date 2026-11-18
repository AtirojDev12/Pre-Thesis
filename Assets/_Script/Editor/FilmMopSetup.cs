using System.Collections.Generic;
using System.Text;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5). One click for film reels, projector glow, deadline and mops.
///
///   Tools > Pre-Thesis > Scenes > Map: Film Reels + Mops (Task 5)
///     Open Cinema_GamePlay first. Then:
///       - Every film-change chore needs THE RIGHT film reel: each reel has a film name + colour; the
///         projector prompt names the film it needs now (a wrong film is refused, no penalty).
///         Ghost room = hotbar reels, normal room = hand reels. A film bookshelf next to each projector
///         holds one reel per film change tonight + wrong ones, mixed up. Rerun = shelves rebuilt in place.
///       - Every film-change chore gets a FilmDeadline: projector glow 75 s before (both rooms),
///         deadline + penalties (ghost room only).
///       - Every cleaning chore (screen rooms + toilets) needs a mop.
///       - 2 mops in the Staff Room (in front of the Staff Room board copies; else the middle of "StaffRoom").
///       - Makes the normal room's bonus film chore if the scene has none (17 Nov fix).
///     Only adds what is missing. Running it again is safe. Then SAVE the scene (Ctrl+S).
///     Mockup models: Assets/Prefab/RuleMockups/RM_Mop, RM_FilmReel, RM_FilmReelGhost, RM_FilmShelf
///     (listed in UI Hub > 3D models; swap the model inside the prefab when the art is ready).
///
///   Tools > Pre-Thesis > Designer Settings > Film Reels + Mops
///     Creates (first time) and selects Resources/Tuning/FilmMopTuning.asset.
/// </summary>
public static class FilmMopSetup
{
    private const string Title = "Film Reels + Mops";
    private const string Folder = "Assets/Prefab/RuleMockups";
    private const string MatFolder = Folder + "/Materials";
    private const string MopPath = Folder + "/RM_Mop.prefab";
    private const string ReelPath = Folder + "/RM_FilmReel.prefab";
    private const string GhostReelPath = Folder + "/RM_FilmReelGhost.prefab";
    private const string ShelfPath = Folder + "/RM_FilmShelf.prefab"; // 17 Nov: a bookshelf for film reels
    private const string MopsRoot = "Mops (Staff Room)";
    private const string StaffBoardsRoot = "Task Board Copies (Staff Room)";
    private const int MopCount = 2;

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Film Reels + Mops (Task 5)", false, 63)]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
        Scene scene = SceneManager.GetActiveScene();
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog(Title, "There is no MatchDirector in the open scene. Open Cinema_GamePlay first.", "OK");
            return;
        }
        var log = new StringBuilder();
        Physics.SyncTransforms();
        EnsureNormalRoomFilm(scene, log); // 17 Nov fix: the scene had no normal-room film chore

        var spots = new List<ZoneChoreSpot>(Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include));
        if (spots.Count == 0)
        {
            EditorUtility.DisplayDialog(Title, "No chores in the open scene. Run the zone tools first.", "OK");
            return;
        }
        EnsureTuning(log);
        GameObject mopPrefab = MakePrefab(MopPath, BuildMop, log);
        GameObject reelPrefab = MakePrefab(ReelPath, () => BuildReel(new Color(0.72f, 0.74f, 0.78f), "RM_FilmReel_Metal"), log);
        GameObject ghostReelPrefab = MakePrefab(GhostReelPath, () => BuildReel(new Color(0.45f, 0.12f, 0.5f), "RM_FilmReel_Ghost"), log);
        GameObject shelfPrefab = MakePrefab(ShelfPath, BuildFilmShelfModel, log);
        AssetDatabase.SaveAssets();
        Physics.SyncTransforms();

        int films = 0, cleans = 0, deadlines = 0, shelves = 0, reels = 0;
        foreach (ZoneChoreSpot spot in spots)
        {
            if (spot == null || !spot.gameObject.scene.IsValid()) continue;
            if (spot.Kind == ZoneTaskKind.FilmChange)
            {
                bool ghost = spot.CountsForZoneBoard; // the normal room's film is a bonus chore
                string id = ghost ? CarryProp.FilmReelGhost : CarryProp.FilmReelNormal;
                Undo.RecordObject(spot, Title);
                spot.EditorSetNeedsItem(id, "Needs a film reel (take one from the shelf next to the projector)", true, true);
                EditorUtility.SetDirty(spot);
                films++;

                if (spot.GetComponent<FilmDeadline>() == null)
                {
                    FilmDeadline deadline = Undo.AddComponent<FilmDeadline>(spot.gameObject);
                    deadline.EditorSetup(ghost, ProjectorParts(spot));
                    EditorUtility.SetDirty(deadline);
                    deadlines++;
                }
                reels += BuildShelf(spot, ghost, ghost ? ghostReelPrefab : reelPrefab, shelfPrefab);
                shelves++;
            }
            else if (spot.Kind == ZoneTaskKind.CleanScreenRoom || spot.Kind == ZoneTaskKind.CleanToilet)
            {
                Undo.RecordObject(spot, Title);
                spot.EditorSetNeedsItem(CarryProp.Mop, "Needs a mop (Staff Room)", false);
                EditorUtility.SetDirty(spot);
                cleans++;
            }
        }
        log.AppendLine($"- Film chores: {films} need the film reel for their time ({deadlines} new glow / deadline timer(s)).");
        log.AppendLine($"- {shelves} film shelf(s) (re)built with {reels} reels (right films + wrong ones, each with a name and colour).");
        log.AppendLine($"- Cleaning chores: {cleans} need a mop.");

        int mops = MakeMops(scene, mopPrefab, log);
        if (mops > 0) log.AppendLine($"- {mops} mop(s) added.");

        // 17 Nov fix: Mirror needs a NetworkIdentity on every carried prop.
        int ids = 0;
        foreach (CarryProp prop in Object.FindObjectsByType<CarryProp>(FindObjectsInactive.Include))
        {
            if (prop == null || !prop.gameObject.scene.IsValid() || prop.GetComponent<NetworkIdentity>() != null) continue;
            Undo.AddComponent<NetworkIdentity>(prop.gameObject);
            ids++;
        }
        if (ids > 0) log.AppendLine($"- Fixed: NetworkIdentity added to {ids} mop / reel object(s).");

        foreach (GameObject model in new[] { mopPrefab, reelPrefab, ghostReelPrefab, shelfPrefab })
            if (model != null) GameHudSetup.AddModelSlotToPrefab(model);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[FilmMopSetup]\n" + log);
        EditorUtility.DisplayDialog(Title, log +
            "\nCheck the reel shelves and the mops in the Scene view (move them if they look wrong), then SAVE the scene (Ctrl+S).", "OK");
    }

    [MenuItem("Tools/Pre-Thesis/Designer Settings/Film Reels + Mops", false, 213)]
    private static void SelectTuning()
    {
        var log = new StringBuilder();
        FilmMopTuning asset = EnsureTuning(log);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static FilmMopTuning EnsureTuning(StringBuilder log)
    {
        FilmMopTuning asset = AssetDatabase.LoadAssetAtPath<FilmMopTuning>(FilmMopTuning.AssetPath);
        if (asset != null) return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Tuning")) AssetDatabase.CreateFolder("Assets/Resources", "Tuning");
        asset = ScriptableObject.CreateInstance<FilmMopTuning>();
        AssetDatabase.CreateAsset(asset, FilmMopTuning.AssetPath);
        AssetDatabase.SaveAssets();
        FilmMopTuning.Use(asset);
        log.AppendLine("+ Settings file: " + FilmMopTuning.AssetPath);
        return asset;
    }

    // ---- Scene ---------------------------------------------------------------------------------

    private static bool SceneHas(string carryId)
    {
        foreach (CarryProp prop in Object.FindObjectsByType<CarryProp>(FindObjectsInactive.Include))
            if (prop != null && prop.gameObject.scene.IsValid() && prop.CarryId == carryId) return true;
        return false;
    }

    /// <summary>The projector parts inside the film chore's box (they glow). The box itself if none.</summary>
    private static Transform[] ProjectorParts(ZoneChoreSpot spot)
    {
        var parts = new List<Transform>();
        BoxCollider box = spot.GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 size = Vector3.Scale(box.size, spot.transform.lossyScale);
            var area = new Bounds(spot.transform.TransformPoint(box.center), size);
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || !r.gameObject.scene.IsValid() || r.transform.IsChildOf(spot.transform)) continue;
                Bounds b = r.bounds;
                if (!area.Contains(b.center)) continue;
                if (b.size.x > size.x * 1.5f || b.size.y > size.y * 1.5f || b.size.z > size.z * 1.5f) continue; // a wall
                parts.Add(r.transform);
            }
        }
        if (parts.Count == 0) parts.Add(spot.transform);
        return parts.ToArray();
    }

    // Bookshelf slots (17 Nov): 3 rows x 3 reels standing up, face to the front.
    private static readonly float[] RowHeights = { 0.07f, 0.62f, 1.17f };
    private static readonly float[] SlotX = { -0.42f, 0f, 0.42f };

    /// <summary>
    /// 17 Nov: (re)builds the film bookshelf next to a projector: one reel for each time this projector
    /// needs a film tonight, plus wrong ones (other times), mixed up. Each reel shows its time.
    /// An old shelf keeps its place (if you moved it, it stays there). Returns the number of reels.
    /// </summary>
    private static int BuildShelf(ZoneChoreSpot spot, bool ghost, GameObject reelPrefab, GameObject shelfPrefab)
    {
        string shelfName = ghost ? "Film Reel Shelf (Ghost Room)" : "Film Reel Shelf (Normal Room)";
        Transform old = FindByName(shelfName);
        Vector3 position;
        Quaternion rotation;
        Transform parent = spot.transform.parent;
        if (old != null)
        {
            position = old.position;
            rotation = old.rotation;
            parent = old.parent;
            Undo.DestroyObjectImmediate(old.gameObject);
        }
        else
        {
            position = FreeFloorNear(spot);
            Vector3 look = spot.transform.position - position;
            look.y = 0f;
            rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-look.normalized, Vector3.up) : Quaternion.identity;
        }

        var root = new GameObject(shelfName);
        Undo.RegisterCreatedObjectUndo(root, Title);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(position, rotation);
        if (shelfPrefab != null)
        {
            var shelf = (GameObject)PrefabUtility.InstantiatePrefab(shelfPrefab, root.transform);
            shelf.transform.localPosition = Vector3.zero;
            shelf.transform.localRotation = Quaternion.identity;
        }

        List<int> films = ShelfFilms(spot, ghost);
        string[] names = ghost ? GhostFilms : HumanFilms;
        int capacity = RowHeights.Length * SlotX.Length;
        for (int i = 0; i < films.Count && i < capacity; i++)
        {
            int row = RowHeights.Length - 1 - i / SlotX.Length; // fill from the top row (eye level) down
            Vector3 slot = new Vector3(SlotX[i % SlotX.Length], RowHeights[row], 0f);
            int f = films[i];
            MakeReel(root.transform, slot, ghost, names[f], ColorNames[f], Colors[f], reelPrefab);
        }
        return Mathf.Min(films.Count, capacity);
    }

    // 17 Nov (Mr.k, #31): each reel = a film name + a colour. The projector prompt names the film it needs.
    private static readonly string[] GhostFilms = { "The Pale Bride", "Room 13", "Hollow Choir", "The Last Usher", "Midnight Reel", "Silent Aisle" };
    private static readonly string[] HumanFilms = { "Summer Road", "Space Cadets", "The Big Race", "Happy Chef", "Love in Paris", "Robot Pals" };
    private static readonly string[] ColorNames = { "red", "blue", "green", "yellow", "white", "orange" };
    private static readonly Color[] Colors =
    {
        new Color(0.8f, 0.12f, 0.1f), new Color(0.15f, 0.3f, 0.85f), new Color(0.15f, 0.65f, 0.25f),
        new Color(0.95f, 0.8f, 0.15f), new Color(0.92f, 0.92f, 0.92f), new Color(0.95f, 0.5f, 0.1f),
    };

    /// <summary>
    /// The films on one shelf: the right film for each change tonight (in order) plus wrong ones
    /// (one more than the right ones, at least 2), mixed up. Also tells the chore which film it needs when.
    /// Returns (film index) list; index = into the room's film list and the colour list.
    /// </summary>
    private static List<int> ShelfFilms(ZoneChoreSpot spot, bool ghost)
    {
        string[] films = ghost ? GhostFilms : HumanFilms;
        int right = Mathf.Clamp(spot.ScheduleMinutes().Count, 1, films.Length - 2);
        int wrong = Mathf.Min(films.Length - right, Mathf.Max(2, right + 1));
        var names = new string[right];
        var colors = new string[right];
        for (int i = 0; i < right; i++) { names[i] = films[i]; colors[i] = ColorNames[i]; }
        Undo.RecordObject(spot, Title);
        spot.EditorSetFilms(names, colors);
        EditorUtility.SetDirty(spot);

        var all = new List<int>();
        for (int i = 0; i < right + wrong; i++) all.Add(i);
        var rng = new System.Random(ghost ? 13 : 7);
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int swap = all[i];
            all[i] = all[j];
            all[j] = swap;
        }
        return all;
    }

    private static void MakeReel(Transform shelf, Vector3 localSlot, bool ghost, string label, string colorName, Color color, GameObject reelPrefab)
    {
        var reel = new GameObject((ghost ? "Ghost Film - " : "Film - ") + label + " (" + colorName + (ghost ? ", hotbar)" : ", hand)"));
        Undo.RegisterCreatedObjectUndo(reel, Title);
        reel.transform.SetParent(shelf, false);
        reel.transform.localPosition = localSlot;
        reel.transform.localRotation = Quaternion.identity;
        if (reelPrefab != null)
        {
            // The reel model lies flat; stand it up with its face to the front (+Z).
            var model = (GameObject)PrefabUtility.InstantiatePrefab(reelPrefab, reel.transform);
            model.transform.localPosition = new Vector3(0f, 0.19f, -0.03f);
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // The film's colour on the reel itself.
            Transform body = model.transform.Find("Reel");
            if (body != null && body.TryGetComponent(out Renderer bodyRenderer))
                bodyRenderer.sharedMaterial = Mat("RM_FilmReel_" + colorName, color);
        }

        // The film name, big and clear on the front of the reel.
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "Label Plate";
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(reel.transform, false);
        plate.transform.localPosition = new Vector3(0f, 0.19f, 0.035f);
        plate.transform.localScale = new Vector3(0.3f, 0.1f, 0.01f);
        plate.GetComponent<Renderer>().sharedMaterial = Mat("RM_FilmReel_Label", new Color(0.95f, 0.93f, 0.85f));
        var text = new GameObject("Label " + label);
        text.transform.SetParent(reel.transform, false);
        text.transform.localPosition = new Vector3(0f, 0.19f, 0.042f);
        text.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // readable from the front
        TextMeshPro tmp = text.AddComponent<TextMeshPro>();
        tmp.text = label;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.15f;
        tmp.fontSizeMax = 0.6f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.08f, 0.06f, 0.05f);
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(0.28f, 0.09f);

        reel.AddComponent<NetworkIdentity>();
        BoxCollider look = reel.AddComponent<BoxCollider>();
        look.center = new Vector3(0f, 0.19f, 0.01f);
        look.size = new Vector3(0.38f, 0.38f, 0.12f);
        CarryProp prop = reel.AddComponent<CarryProp>();
        prop.EditorSetup(ghost ? CarryProp.FilmReelGhost : CarryProp.FilmReelNormal,
            ghost ? "Ghost Film Reel" : "Film Reel", ghost ? ItemCatalog.FilmReelGhost : "", true, label);
        // In first person: face (and its time) turned to you.
        prop.EditorSetHold(new Vector3(0.22f, -0.42f, 0.6f), new Vector3(0f, 180f, 0f), 1f,
            new Vector3(0.25f, 0.8f, 0.35f), new Vector3(0f, 0f, 0f));
        EditorUtility.SetDirty(prop);
    }

    private static Transform FindByName(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    /// <summary>A free floor spot about 1 m beside the projector box (else just in front of it).</summary>
    private static Vector3 FreeFloorNear(ZoneChoreSpot spot)
    {
        BoxCollider box = spot.GetComponent<BoxCollider>();
        Vector3 center = spot.transform.position;
        Vector3 extents = box != null ? Vector3.Scale(box.size, spot.transform.lossyScale) * 0.5f : Vector3.one * 0.5f;
        if (box != null) center = spot.transform.TransformPoint(box.center);
        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        foreach (Vector3 d in directions)
        {
            float reach = Mathf.Abs(Vector3.Dot(extents, d)) + 0.8f;
            Vector3 above = center + d * reach + Vector3.up * 0.5f;
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            Vector3 floor = hit.point;
            if (Physics.CheckBox(floor + Vector3.up * 0.95f, new Vector3(0.7f, 0.85f, 0.2f), Quaternion.identity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            return floor;
        }
        Vector3 fallback = center + Vector3.right * (extents.x + 0.6f);
        if (Physics.Raycast(fallback + Vector3.up, Vector3.down, out RaycastHit h, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            fallback = h.point;
        return fallback;
    }

    private static int MakeMops(Scene scene, GameObject mopPrefab, StringBuilder log)
    {
        bool found = StaffRoomSpot(out Vector3 origin, out Vector3 forward, out string where);
        GameObject root = GameObject.Find(MopsRoot);

        // 17 Nov fix: the first version put the mops near the world centre. Move them to the Staff Room.
        if (root != null && found && (root.transform.position - origin).sqrMagnitude > 6f * 6f)
        {
            Undo.RecordObject(root.transform, Title);
            root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward, Vector3.up));
            log.AppendLine("- Mops moved " + where + ".");
        }

        int have = 0;
        foreach (CarryProp prop in Object.FindObjectsByType<CarryProp>(FindObjectsInactive.Include))
            if (prop != null && prop.gameObject.scene.IsValid() && prop.CarryId == CarryProp.Mop) have++;
        if (have >= MopCount)
        {
            if (log.ToString().IndexOf("Mops moved", System.StringComparison.Ordinal) < 0) log.AppendLine("- Mops: already in the scene.");
            return 0;
        }
        if (!found)
        {
            SceneView view = SceneView.lastActiveSceneView;
            origin = view != null ? view.pivot : Vector3.zero;
            forward = Vector3.forward;
            where = "where the Scene view looks (Staff Room not found: move them there)";
        }
        log.AppendLine("- Mops placed " + where + ".");

        if (root == null)
        {
            root = new GameObject(MopsRoot);
            Undo.RegisterCreatedObjectUndo(root, Title);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(forward, Vector3.up));
        }
        int made = 0;
        for (int i = have; i < MopCount; i++)
        {
            var mop = new GameObject($"Mop {i + 1}");
            Undo.RegisterCreatedObjectUndo(mop, Title);
            mop.transform.SetParent(root.transform, false);
            mop.transform.localPosition = new Vector3(i * 0.5f - 0.25f, 0f, 0f);
            mop.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f); // leaning a little
            if (mopPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(mopPrefab, mop.transform);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
            }
            BoxCollider look = mop.AddComponent<BoxCollider>();
            look.center = new Vector3(0f, 0.65f, 0f);
            look.size = new Vector3(0.35f, 1.35f, 0.2f);
            mop.AddComponent<NetworkIdentity>();
            CarryProp prop = mop.AddComponent<CarryProp>();
            prop.EditorSetup(CarryProp.Mop, "Mop", "", false);
            prop.EditorSetHold(new Vector3(0.3f, -1.0f, 1.1f), new Vector3(-40f, 0f, 0f), 0.8f,
                new Vector3(0.3f, 0.1f, 0.9f), new Vector3(-40f, 0f, 0f));
            EditorUtility.SetDirty(prop);
            made++;
        }
        return made;
    }

    /// <summary>
    /// A free floor spot in the Staff Room: 1.5 m out from the Staff Room board copies (their real
    /// positions; their parent sits at 0,0,0), else the middle of Nonnapat's "StaffRoom".
    /// </summary>
    private static bool StaffRoomSpot(out Vector3 spot, out Vector3 forward, out string where)
    {
        spot = Vector3.zero;
        forward = Vector3.forward;
        where = "";
        GameObject boards = GameObject.Find(StaffBoardsRoot);
        if (boards != null && boards.transform.childCount > 0)
        {
            Vector3 middle = Vector3.zero;
            foreach (Transform board in boards.transform) middle += board.position;
            middle /= boards.transform.childCount;
            Transform first = boards.transform.GetChild(0);
            Vector3 f = Vector3.ProjectOnPlane(first.forward, Vector3.up).normalized;
            Vector3 r = Vector3.ProjectOnPlane(first.right, Vector3.up).normalized;
            foreach (Vector3 d in new[] { -f, f, r, -r })
            {
                if (d.sqrMagnitude < 0.01f) continue;
                if (!FreeFloor(middle + d * 1.5f, out Vector3 floor)) continue;
                spot = floor;
                forward = -d; // the mops face the boards' wall
                where = "in the Staff Room (in front of the Staff Room board copies)";
                return true;
            }
        }
        GameObject room = GameObject.Find("StaffRoom");
        if (room != null)
        {
            var renderers = room.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                if (FreeFloor(new Vector3(b.center.x, b.min.y + 1.5f, b.center.z), out Vector3 floor))
                {
                    spot = floor;
                    where = "in the middle of the Staff Room";
                    return true;
                }
            }
        }
        return false;
    }

    private static bool FreeFloor(Vector3 near, out Vector3 floor)
    {
        floor = near;
        if (!Physics.Raycast(near + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        floor = hit.point;
        return !Physics.CheckBox(floor + Vector3.up * 0.75f, new Vector3(0.35f, 0.6f, 0.2f), Quaternion.identity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// 17 Nov fix: the normal (human) room had no film chore in the scene. Make the bonus one again
    /// (00:30 and 02:30, hold E 5 s, money only) at TeaterZone/ScreenRoom/Projecter, next to the
    /// normal room's dirt marks. Same settings as "Screen Room Bonus Tasks".
    /// </summary>
    private static void EnsureNormalRoomFilm(Scene scene, StringBuilder log)
    {
        foreach (ZoneChoreSpot spot in Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (spot != null && spot.gameObject.scene.IsValid() && spot.Kind == ZoneTaskKind.FilmChange && !spot.CountsForZoneBoard) return;

        GameObject theater = GameObject.Find("TeaterZone");
        Transform room = theater != null ? theater.transform.Find("ScreenRoom") : null;
        Transform projector = room != null ? room.Find("Projecter") : null;
        if (projector == null)
        {
            log.AppendLine("! Normal room film NOT made: TeaterZone/ScreenRoom/Projecter not found.");
            return;
        }
        Transform machine = projector;
        float bestVolume = float.MaxValue;
        foreach (Renderer r in projector.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 size = r.bounds.size;
            float volume = size.x * size.y * size.z;
            if (volume > 0.001f && volume < bestVolume) { bestVolume = volume; machine = r.transform; }
        }
        Bounds b = new Bounds(machine.position, Vector3.one);
        bool has = false;
        foreach (Renderer r in machine.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }

        // Put it next to the normal room's dirt marks (same zone box), else at the scene root.
        Transform parent = null;
        foreach (ZoneChoreSpot spot in Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (spot != null && spot.name.Contains("(Normal Room")) { parent = spot.transform.parent; break; }

        var go = new GameObject("Chore - Change Film (Normal Room) [Bonus]");
        Undo.RegisterCreatedObjectUndo(go, Title);
        if (parent != null) go.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = b.center;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = b.size + Vector3.one * 0.3f;

        GameObject warn = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        warn.name = "Film Needed Light";
        Object.DestroyImmediate(warn.GetComponent<Collider>());
        warn.transform.SetParent(go.transform, false);
        warn.transform.position = new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        warn.transform.localScale = Vector3.one * 0.14f;
        Material light = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Chore_FilmNeeded.mat");
        if (light != null) warn.GetComponent<Renderer>().sharedMaterial = light;

        go.AddComponent<NetworkIdentity>();
        ZoneChoreSpot chore = go.AddComponent<ZoneChoreSpot>();
        var so = new SerializedObject(chore);
        so.FindProperty("kind").enumValueIndex = (int)ZoneTaskKind.FilmChange;
        so.FindProperty("zoneID").stringValue = "zone_screen";
        so.FindProperty("actionText").stringValue = "Change film";
        so.FindProperty("holdSeconds").floatValue = 5f;
        so.FindProperty("fromHour").intValue = 0;
        so.FindProperty("fromMinute").intValue = 30;
        so.FindProperty("repeatEveryHours").intValue = 2;
        so.FindProperty("maxTimes").intValue = 2; // 00:30 and 02:30 only
        so.FindProperty("countsForZoneBoard").boolValue = false;
        so.FindProperty("interactionPrompt").stringValue = "[Hold E] Change film (bonus)";
        SerializedProperty looks = so.FindProperty("showWhenNeeded");
        looks.arraySize = 1;
        looks.GetArrayElementAtIndex(0).objectReferenceValue = warn;
        so.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        log.AppendLine("+ Normal room film chore made again (bonus, 00:30 and 02:30) at its projector.");
    }

    // ---- Mockup prefabs (only made when missing) ------------------------------------------------

    private static GameObject MakePrefab(string path, System.Func<GameObject> build, StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder("Assets/Prefab")) AssetDatabase.CreateFolder("Assets", "Prefab");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefab", "RuleMockups");
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
        GameObject temp = build();
        temp.name = System.IO.Path.GetFileNameWithoutExtension(path);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        log.AppendLine("+ Model: " + path);
        return saved;
    }

    // Pivot = the floor under the mop head. The stick points up (+Y).
    private static GameObject BuildMop()
    {
        var root = new GameObject();
        Material wood = Mat("RM_Mop_Stick", new Color(0.55f, 0.4f, 0.24f));
        Material head = Mat("RM_Mop_Head", new Color(0.82f, 0.8f, 0.72f));
        Part(root, PrimitiveType.Cylinder, "Stick", new Vector3(0f, 0.7f, 0f), new Vector3(0.035f, 0.62f, 0.035f), wood);
        Part(root, PrimitiveType.Cube, "Head Bar", new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.04f, 0.06f), wood);
        Part(root, PrimitiveType.Cube, "Head", new Vector3(0f, 0.035f, 0f), new Vector3(0.32f, 0.07f, 0.12f), head);
        return root;
    }

    // Pivot = the bottom of the reel (lying flat).
    private static GameObject BuildReel(Color color, string materialName)
    {
        var root = new GameObject();
        Material body = Mat(materialName, color);
        Material film = Mat("RM_FilmReel_Film", new Color(0.12f, 0.1f, 0.08f));
        Part(root, PrimitiveType.Cylinder, "Reel", new Vector3(0f, 0.025f, 0f), new Vector3(0.36f, 0.025f, 0.36f), body);
        Part(root, PrimitiveType.Cylinder, "Film", new Vector3(0f, 0.026f, 0f), new Vector3(0.28f, 0.027f, 0.28f), film);
        Part(root, PrimitiveType.Cylinder, "Hub", new Vector3(0f, 0.03f, 0f), new Vector3(0.07f, 0.032f, 0.07f), body);
        return root;
    }

    // 17 Nov: a bookshelf for film reels. Pivot = the floor, front = +Z. 1.4 m wide, 1.75 m high, 3 rows.
    private static GameObject BuildFilmShelfModel()
    {
        var root = new GameObject();
        Material wood = Mat("RM_FilmShelf_Wood", new Color(0.32f, 0.2f, 0.12f));
        Material back = Mat("RM_FilmShelf_Back", new Color(0.18f, 0.11f, 0.07f));
        Part(root, PrimitiveType.Cube, "Back", new Vector3(0f, 0.875f, -0.16f), new Vector3(1.4f, 1.75f, 0.03f), back);
        Part(root, PrimitiveType.Cube, "Side L", new Vector3(-0.69f, 0.875f, 0f), new Vector3(0.03f, 1.75f, 0.35f), wood);
        Part(root, PrimitiveType.Cube, "Side R", new Vector3(0.69f, 0.875f, 0f), new Vector3(0.03f, 1.75f, 0.35f), wood);
        foreach (float y in new[] { 0.05f, 0.6f, 1.15f, 1.73f })
            Part(root, PrimitiveType.Cube, "Shelf", new Vector3(0f, y, 0f), new Vector3(1.38f, 0.04f, 0.35f), wood);
        return root;
    }

    private static void Part(GameObject parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // looking at it uses the root's box
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material Mat(string name, Color color)
    {
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
