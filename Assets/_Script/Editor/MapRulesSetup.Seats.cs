using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 23 Nov 2026 (Mr.k): part of Tools > Pre-Thesis > One Step > Map Rules.
///   Seats    : every ScreenRoomChair cushion (polySurface5 / 8 / 11 of the model) becomes a CinemaSeat
///              under "Cinema Seats" (rebuilt on every run, so the indices always match SeatSystem).
///   Ghosts   : the audience ghosts sit on the seats nearest to them, the VIP gets the back row middle seat,
///              the Staff Time shadow spots move onto free seats.
///   Projector: the VIP crank projector (RM_Projector model + crank handle) stands at the ghost room
///              projector's place, facing the screen, hidden. It is swapped in only while the VIP is here.
/// </summary>
public static partial class MapRulesSetup
{
    private const string SeatsRoot = "Cinema Seats";
    private const string ProjectorPrefabPath = "Assets/Prefab/RuleMockups/RM_Projector.prefab";
    private const string ProjectorModelName = "Projector Model";
    private static readonly HashSet<string> CushionNames = new HashSet<string> { "polySurface5", "polySurface8", "polySurface11" };
    // 23 Nov: the backrest part next to each cushion part of the ScreenRoomChair model.
    private static readonly Dictionary<string, string> BackOf = new Dictionary<string, string>
        { { "polySurface5", "polySurface6" }, { "polySurface11", "polySurface13" }, { "polySurface8", "polySurface7" } };

    // ---- Seats --------------------------------------------------------------------------------

    private static void EnsureSeats(Scene scene, GameObject root, StringBuilder log)
    {
        // Rebuild from scratch: the cushions are only moved in Play mode, never here.
        Physics.SyncTransforms();
        GameObject old = GameObject.Find(SeatsRoot);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var cushions = new List<MeshFilter>();
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            if (mf == null || mf.sharedMesh == null || !CushionNames.Contains(mf.gameObject.name)) continue;
            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (path.Contains("ScreenRoomChair")) cushions.Add(mf);
        }
        if (cushions.Count == 0) { log.AppendLine("! No ScreenRoomChair seats found in the scene: no CinemaSeats made."); return; }
        // Stable order: room, then position.
        cushions.Sort((a, b) =>
        {
            int r = InGhostRoom(a.transform).CompareTo(InGhostRoom(b.transform));
            if (r != 0) return -r;
            Vector3 pa = a.transform.position, pb = b.transform.position;
            int x = pa.x.CompareTo(pb.x);
            return x != 0 ? x : pa.z.CompareTo(pb.z);
        });

        var holder = new GameObject(SeatsRoot);
        SceneManager.MoveGameObjectToScene(holder, scene);
        Undo.RegisterCreatedObjectUndo(holder, Title);
        var seats = new List<CinemaSeat>();
        foreach (MeshFilter mf in cushions)
        {
            CinemaSeat seat = BuildSeat(mf, holder.transform, seats.Count);
            if (seat != null) seats.Add(seat);
        }

        SeatSystem system = root.GetComponent<SeatSystem>();
        if (system == null) system = Undo.AddComponent<SeatSystem>(root);
        system.EditorSetSeats(seats.ToArray());
        EditorUtility.SetDirty(system);
        int ghost = 0;
        foreach (CinemaSeat s in seats) if (s.GhostRoom) ghost++;
        log.AppendLine($"+ {seats.Count} cinema seats ({ghost} in the ghost room): E = sit, SPACE = stand up");
    }

    private static bool InGhostRoom(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
            if (p.name.Contains("GhostScreenRoom")) return true;
        return false;
    }

    private static CinemaSeat BuildSeat(MeshFilter mf, Transform holder, int index)
    {
        Transform cushion = mf.transform;
        Vector3[] local = mf.sharedMesh.vertices;
        if (local.Length < 4) return null;
        var world = new Vector3[local.Length];
        float minY = float.MaxValue, maxY = float.MinValue;
        Vector3 centre = Vector3.zero;
        for (int i = 0; i < local.Length; i++)
        {
            world[i] = cushion.TransformPoint(local[i]);
            minY = Mathf.Min(minY, world[i].y);
            maxY = Mathf.Max(maxY, world[i].y);
            centre += world[i];
        }
        centre /= world.Length;
        float band = (maxY - minY) * 0.25f;
        Vector3 bottom = Vector3.zero, top = Vector3.zero;
        int nb = 0, nt = 0;
        foreach (Vector3 v in world)
        {
            if (v.y <= minY + band) { bottom += v; nb++; }
            if (v.y >= maxY - band) { top += v; nt++; }
        }
        bottom /= Mathf.Max(1, nb);
        top /= Mathf.Max(1, nt);

        // The folded cushion leans a little toward the front: that is the seat's forward (to the screen).
        Vector3 along = top - bottom;
        Vector3 forward = new Vector3(along.x, 0f, along.z);
        if (forward.sqrMagnitude < 0.0001f) forward = cushion.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, forward); // left-right of the seat

        // Open = turn around its middle until the long side lies flat, front end forward.
        float angle = Vector3.SignedAngle(along, forward * along.magnitude, axis);
        Vector3 closedPos = cushion.localPosition;
        Quaternion closedRot = cushion.localRotation;
        cushion.RotateAround(centre, axis, angle);
        Vector3 openPos = cushion.localPosition;
        Quaternion openRot = cushion.localRotation;
        cushion.localPosition = closedPos;
        cushion.localRotation = closedRot;

        // Size of the folded cushion in the seat's own frame (for the collider).
        Quaternion frame = Quaternion.LookRotation(forward, Vector3.up);
        Quaternion inverse = Quaternion.Inverse(frame);
        Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
        foreach (Vector3 v in world)
        {
            Vector3 q = inverse * (v - centre);
            lo = Vector3.Min(lo, q);
            hi = Vector3.Max(hi, q);
        }

        Vector3 floor = FloorUnder(centre, minY - 0.5f);
        Vector3 standAt = floor + forward * 0.6f;
        standAt = FloorUnder(standAt + Vector3.up * 0.6f, floor.y);

        var go = new GameObject($"Seat {index + 1}");
        go.transform.SetParent(holder, false);
        go.transform.SetPositionAndRotation(centre, frame);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = (lo + hi) * 0.5f;
        box.size = Vector3.Max(hi - lo, new Vector3(0.1f, 0.1f, 0.12f));
        CinemaSeat seat = go.AddComponent<CinemaSeat>();
        seat.EditorSetup(index, InGhostRoom(cushion), cushion, closedPos, closedRot, openPos, openRot,
            floor, standAt, Quaternion.LookRotation(forward).eulerAngles.y);
        return seat;
    }

    /// <summary>The floor under a point (seat steps / ground), skipping ghosts, walkers and seats.</summary>
    private static Vector3 FloorUnder(Vector3 from, float fallbackY)
    {
        RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up * 0.05f, Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 point = new Vector3(from.x, fallbackY, from.z);
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.GetComponentInParent<GhostAudience>() != null || t.GetComponentInParent<MapWalker>() != null ||
                t.GetComponentInParent<CinemaSeat>() != null || t.GetComponentInParent<RuleInteractable>() != null) continue;
            if (h.distance < best) { best = h.distance; point = h.point; }
        }
        return point;
    }

    // ---- Ghosts on seats ----------------------------------------------------------------------

    private static void SeatGhosts(GameObject root, StringBuilder log)
    {
        SeatSystem system = root.GetComponent<SeatSystem>();
        if (system == null || system.Count == 0) return;
        var ghostSeats = new List<CinemaSeat>();
        for (int i = 0; i < system.Count; i++) if (system.Seat(i) != null && system.Seat(i).GhostRoom) ghostSeats.Add(system.Seat(i));
        if (ghostSeats.Count == 0) return;
        var taken = new HashSet<CinemaSeat>();

        // VIP: the back row (farthest from the screen), the most middle seat.
        Vector3 meanPos = Vector3.zero, meanForward = Vector3.zero;
        foreach (CinemaSeat s in ghostSeats) { meanPos += s.FloorPoint; meanForward += s.Facing * Vector3.forward; }
        meanPos /= ghostSeats.Count;
        meanForward.y = 0f;
        meanForward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, meanForward);
        float maxDepth = float.MinValue;
        foreach (CinemaSeat s in ghostSeats) maxDepth = Mathf.Max(maxDepth, -Vector3.Dot(s.FloorPoint - meanPos, meanForward));
        CinemaSeat vipSeat = null;
        float bestSide = float.MaxValue;
        foreach (CinemaSeat s in ghostSeats)
        {
            float depth = -Vector3.Dot(s.FloorPoint - meanPos, meanForward);
            if (depth < maxDepth - 0.35f) continue;
            float side = Mathf.Abs(Vector3.Dot(s.FloorPoint - meanPos, right));
            if (side < bestSide) { bestSide = side; vipSeat = s; }
        }
        MapRuleVip vip = root.GetComponent<MapRuleVip>();
        if (vipSeat != null && vip != null)
        {
            vipSeat.EditorSetGhost(true, false);
            EditorUtility.SetDirty(vipSeat);
            taken.Add(vipSeat);
            Transform point = new SerializedObject(vip).FindProperty("seat").objectReferenceValue as Transform;
            if (point != null)
            {
                Undo.RecordObject(point, Title);
                point.SetPositionAndRotation(vipSeat.FloorPoint, vipSeat.Facing);
            }
            MarkVipSeat(vipSeat);
            log.AppendLine($"- VIP seat: {vipSeat.name} (back row, middle): gold seat + floating VIP sign, gold light while the VIP is here");
        }

        // Audience: each member on the free seat nearest to it, open all night.
        GhostAudience audience = Object.FindAnyObjectByType<GhostAudience>(FindObjectsInactive.Include);
        if (audience != null)
        {
            int moved = 0;
            for (int i = 0; i < audience.Count; i++)
            {
                Transform m = audience.Member(i);
                if (m == null) continue;
                CinemaSeat s = NearestFree(ghostSeats, taken, m.position);
                if (s == null) break;
                taken.Add(s);
                s.EditorSetGhost(true, true);
                EditorUtility.SetDirty(s);
                Undo.RecordObject(m, Title);
                m.SetPositionAndRotation(s.FloorPoint, s.Facing);
                moved++;
            }
            if (moved > 0) log.AppendLine($"- {moved} audience ghost(s) sit on seats (those seats stay open)");
        }

        // Staff Time: every shadow spot on a free seat (players sit there with E).
        RuleStaffTime staff = Object.FindAnyObjectByType<RuleStaffTime>(FindObjectsInactive.Include);
        if (staff != null)
        {
            SerializedProperty spots = new SerializedObject(staff).FindProperty("shadowSpots");
            int done = 0;
            for (int i = 0; spots != null && i < spots.arraySize; i++)
            {
                Transform spot = spots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (spot == null) continue;
                CinemaSeat s = NearestFree(ghostSeats, taken, spot.position);
                if (s == null) break;
                taken.Add(s);
                Undo.RecordObject(spot, Title);
                spot.SetPositionAndRotation(s.FloorPoint, s.Facing);
                done++;
            }
            if (done > 0) log.AppendLine($"- {done} Staff Time shadow spot(s) moved onto seats (E = sit on the shadow)");
        }
    }

    /// <summary>23 Nov: the VIP seat is gold with a "VIP" sign (VipSeatMarker).</summary>
    private static void MarkVipSeat(CinemaSeat seat)
    {
        var parts = new List<Renderer>();
        Transform cushion = seat.Cushion;
        if (cushion != null)
        {
            if (cushion.TryGetComponent(out Renderer r)) parts.Add(r);
            if (BackOf.TryGetValue(cushion.name, out string backName) && cushion.parent != null)
            {
                Transform back = cushion.parent.Find(backName);
                if (back != null && back.TryGetComponent(out Renderer b)) parts.Add(b);
            }
        }
        VipSeatMarker marker = seat.GetComponent<VipSeatMarker>();
        if (marker == null) marker = Undo.AddComponent<VipSeatMarker>(seat.gameObject);
        marker.EditorSetup(parts.ToArray());
        EditorUtility.SetDirty(marker);
    }

    private static CinemaSeat NearestFree(List<CinemaSeat> seats, HashSet<CinemaSeat> taken, Vector3 near)
    {
        CinemaSeat best = null;
        float bestSqr = float.MaxValue;
        foreach (CinemaSeat s in seats)
        {
            if (s == null || taken.Contains(s)) continue;
            float sqr = (s.FloorPoint - near).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = s; }
        }
        return best;
    }

    // ---- Projectors ---------------------------------------------------------------------------

    /// <summary>
    /// The room projectors stay as they are. The VIP crank projector stands at the ghost room
    /// projector's place, facing the screen, hidden: it is swapped in only while the VIP is here.
    /// (Also undoes the first version, which replaced the projectors for good.)
    /// </summary>
    private static void SwapProjectors(Scene scene, StringBuilder log)
    {
        GameObject prefab = EnsureProjectorPrefab(log);
        if (prefab == null) return;
        foreach (ProjectorAudio audio in Object.FindObjectsByType<ProjectorAudio>(FindObjectsInactive.Include))
        {
            Transform holder = audio.transform;
            // Undo the first version: no fixed model, the placeholder shows again.
            Transform oldModel = holder.Find(ProjectorModelName);
            if (oldModel != null) { Undo.DestroyObjectImmediate(oldModel.gameObject); log.AppendLine("- Removed the fixed projector model (the room projector stays)"); }
            Renderer placeholder = null;
            foreach (Renderer r in holder.GetComponentsInChildren<Renderer>(true))
                if (r.transform.parent == holder) { placeholder = r; break; }
            if (placeholder != null && !placeholder.enabled) { Undo.RecordObject(placeholder, Title); placeholder.enabled = true; }
            if (!InGhostRoom(holder)) continue;

            Transform screen = holder.parent != null ? holder.parent.Find("Screen") : null;
            Bounds where = placeholder != null ? placeholder.bounds : new Bounds(holder.position, Vector3.one);
            Vector3 basePoint = new Vector3(where.center.x, where.min.y, where.center.z);
            Vector3 face = screen != null ? screen.position - basePoint : holder.forward;
            face.y = 0f;
            if (face.sqrMagnitude < 0.01f) face = Vector3.forward;
            Collider chore = RestoreFilmChore();
            PlaceCrank(scene, prefab, basePoint, Quaternion.LookRotation(face.normalized, Vector3.up), holder.gameObject, chore, log);
            log.AppendLine($"- VIP crank projector at the ghost room projector, facing {(screen != null ? "the screen" : "forward (no Screen found)")}; swapped in only while the VIP is here");
        }
    }

    /// <summary>The ghost room film chore box back to its own size (the first version shrank it). Returns it.</summary>
    private static Collider RestoreFilmChore()
    {
        foreach (ZoneChoreSpot c in Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
        {
            if (c.Kind != ZoneTaskKind.FilmChange || !c.CountsForZoneBoard) continue;
            BoxCollider box = c.GetComponent<BoxCollider>();
            if (box == null) return c.GetComponent<Collider>();
            if (Vector3.Distance(box.size, new Vector3(0.9f, 1.7f, 0.9f)) < 0.01f)
            {
                Undo.RecordObject(box, Title);
                box.center = Vector3.zero;
                box.size = new Vector3(1.6001577f, 2.71086f, 1.9078677f);
            }
            return box;
        }
        return null;
    }

    /// <summary>The VIP crank projector: the projector model + a crank handle on its right side. Hidden until the VIP comes.</summary>
    private static void PlaceCrank(Scene scene, GameObject prefab, Vector3 at, Quaternion facing, GameObject original, Collider chore, StringBuilder log)
    {
        CrankProjector crank = Object.FindAnyObjectByType<CrankProjector>(FindObjectsInactive.Include);
        GameObject root;
        if (crank == null)
        {
            root = new GameObject("Crank Projector (VIP)");
            Undo.RegisterCreatedObjectUndo(root, Title);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<NetworkIdentity>();
            crank = root.AddComponent<CrankProjector>();
        }
        else
        {
            root = crank.gameObject;
            Undo.RecordObject(root, Title);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            foreach (BoxCollider b in root.GetComponents<BoxCollider>()) Undo.DestroyObjectImmediate(b);
        }
        root.name = "Crank Projector (VIP)";
        Undo.RecordObject(root.transform, Title);
        root.transform.SetPositionAndRotation(at, facing);

        var look = new GameObject("Model");
        look.transform.SetParent(root.transform, false);
        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        body.transform.SetParent(look.transform, false);
        Material metal = Mat("RM_CrankMetal", new Color(0.22f, 0.22f, 0.24f));
        Material brass = Mat("RM_CrankBrass", new Color(0.7f, 0.55f, 0.25f));
        var crankRoot = new GameObject("Crank");
        crankRoot.transform.SetParent(look.transform, false);
        crankRoot.transform.localPosition = new Vector3(0.5f, 1.05f, 0f);
        Part(crankRoot.transform, PrimitiveType.Cylinder, "Axle", new Vector3(-0.12f, 0f, 0f), new Vector3(0.04f, 0.12f, 0.04f), metal, new Vector3(0f, 0f, 90f));
        var handle = new GameObject("Handle");
        handle.transform.SetParent(crankRoot.transform, false);
        Part(handle.transform, PrimitiveType.Cube, "Arm", new Vector3(0f, 0f, 0.09f), new Vector3(0.03f, 0.03f, 0.2f), brass);
        Part(handle.transform, PrimitiveType.Cylinder, "Grip", new Vector3(0.05f, 0f, 0.18f), new Vector3(0.035f, 0.05f, 0.035f), metal, new Vector3(0f, 0f, 90f));
        look.SetActive(false); // swapped in at runtime

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0.1f, 0.85f, 0f);
        box.size = new Vector3(1.1f, 1.75f, 0.9f);
        box.enabled = false; // only while swapped in
        crank.EditorSetup(handle.transform, look, original, chore);
        EditorUtility.SetDirty(crank);
    }

    /// <summary>The projector model (lens on +Z). Made once; swap the art inside the prefab later.</summary>
    private static GameObject EnsureProjectorPrefab(StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectorPrefabPath);
        if (existing != null) return existing;
        Mat("RM_CrankMetal", Color.gray); // makes the folders
        var root = new GameObject("RM_Projector");
        try
        {
            Material metal = Mat("RM_CrankMetal", new Color(0.22f, 0.22f, 0.24f));
            Material brass = Mat("RM_CrankBrass", new Color(0.7f, 0.55f, 0.25f));
            Material dark = Mat("RM_ProjectorDark", new Color(0.08f, 0.08f, 0.09f));
            Part(root.transform, PrimitiveType.Cube, "Stand", new Vector3(0f, 0.45f, 0f), new Vector3(0.35f, 0.9f, 0.35f), dark);
            Part(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 1.05f, 0f), new Vector3(0.45f, 0.32f, 0.65f), metal);
            Part(root.transform, PrimitiveType.Cylinder, "Lens", new Vector3(0f, 1.05f, 0.4f), new Vector3(0.15f, 0.08f, 0.15f), brass, new Vector3(90f, 0f, 0f));
            Part(root.transform, PrimitiveType.Cylinder, "Lens Glass", new Vector3(0f, 1.05f, 0.48f), new Vector3(0.11f, 0.005f, 0.11f), dark, new Vector3(90f, 0f, 0f));
            Part(root.transform, PrimitiveType.Cylinder, "Reel Front", new Vector3(0f, 1.4f, 0.16f), new Vector3(0.32f, 0.02f, 0.32f), brass, new Vector3(0f, 0f, 90f));
            Part(root.transform, PrimitiveType.Cylinder, "Reel Back", new Vector3(0f, 1.4f, -0.2f), new Vector3(0.32f, 0.02f, 0.32f), brass, new Vector3(0f, 0f, 90f));
            Part(root.transform, PrimitiveType.Cube, "Reel Arm", new Vector3(0f, 1.25f, -0.02f), new Vector3(0.03f, 0.06f, 0.42f), dark);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectorPrefabPath);
            log.AppendLine("+ " + ProjectorPrefabPath + " (projector model, lens = +Z; swap the art inside freely)");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GameHudSetup.PrefabPath) != null) GameHudSetup.AddModelSlotToPrefab(prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(root); }
    }

    // ---- Memo Book sentences ------------------------------------------------------------------

    private static void UpdateMemoTexts(StringBuilder log)
    {
        MemoGuideTexts texts = AssetDatabase.LoadAssetAtPath<MemoGuideTexts>(MemoGuideTexts.AssetPath);
        if (texts == null) return;
        bool changed = false;
        if (texts.staffFindShadow != null && texts.staffFindShadow.Contains("stand on it"))
        {
            texts.staffFindShadow = "The screen shows static. Find the black shadow on a seat, look at that seat and press E to sit on it.";
            changed = true;
        }
        if (texts.staffStay != null && texts.staffStay.StartsWith("Stay on the shadow"))
        {
            texts.staffStay = "Stay seated until the screen comes back (SPACE = stand up).";
            changed = true;
        }
        // 23 Nov: the VIP steps now name the front door and the VIP film.
        if (texts.vipBring == "A VIP guest is here and the clock is stopped. Press E on the VIP and lead it to its seat in the ghost screen room.") { texts.vipBring = "A VIP guest is at the front door and the clock is stopped. Press E on the VIP and lead it to its seat in the ghost screen room. Someone bring the VIP film (gold reel, top of the ghost room film shelf) to the crank projector."; changed = true; }
        if (texts.vipCrank == "The VIP is seated. Crank the projector in the ghost room: press E fast until the film is done.") { texts.vipCrank = "The VIP is seated. Put the VIP film (gold reel) in the crank projector if it is not in yet, then press E fast until the film is done."; changed = true; }
        if (!changed) return;
        EditorUtility.SetDirty(texts);
        log.AppendLine("- Memo Book: Staff Time steps now say \"press E to sit\"");
    }
}
