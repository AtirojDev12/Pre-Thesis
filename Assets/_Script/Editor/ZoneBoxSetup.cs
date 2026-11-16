using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 15 Nov 2026 (Mr.k): organises the map's ZONE BOXES (RuleZone).
///
///   Tools > Pre-Thesis > Scenes > Map: Zone Boxes - First Guess + Link Zone Objects
///     1. Old boxes were as big as the whole room model (the Ghost Screen Room was 65 x 18 x 27 m,
///        so rules fired from outside). Each box is SHRUNK to a first guess around its own rule
///        objects and chores (never bigger than before). Then adjust it by hand: select the zone
///        and drag the dots on the box faces.
///     2. Sets each box's Zone ID (zone_screen / zone_toilet / zone_ticket) and puts the object's
///        pivot in the middle of its box.
///     3. Adds the boxes that were missing: "Screen Room (Human Room)" and "Ticket Zone".
///     4. Parents every chore (dirt, film) and task board under its zone box, so moving the box
///        moves everything of that zone. World positions do not change.
///   Safe to run again: a box that already has a Zone ID is not resized. Undo works. Save after.
/// </summary>
public static class ZoneBoxSetup
{
    private const string RulesRoot = "Zone Rules (Screen + Toilet)";
    private const string ScreenID = "zone_screen";
    private const string ToiletID = "zone_toilet";
    private const string TicketID = ZoneTaskList.TicketZoneID;
    private const string HumanRoomName = "Screen Room (Human Room)";
    private const string TicketZoneName = "Ticket Zone";

    /// <summary>True once this tool has run on the open map (a zone box has a Zone ID).</summary>
    public static bool MapWasOrganised()
    {
        foreach (RuleZone z in Object.FindObjectsByType<RuleZone>(FindObjectsInactive.Include))
            if (z.gameObject.scene.IsValid() && !string.IsNullOrEmpty(z.ZoneID)) return true;
        return false;
    }

    /// <summary>The old one-time setup tools ask before they rebuild an organised map.</summary>
    public static bool ConfirmOldTool(string toolName)
    {
        if (!MapWasOrganised()) return true;
        return EditorUtility.DisplayDialog(toolName,
            "This map was already organised by \"Zone Boxes\": chores and task boards now sit under the zone boxes.\n\n" +
            "This older tool rebuilds things from scratch. It can delete your edits or make doubles.\n\nRun it anyway?",
            "Run anyway", "Cancel");
    }

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Zone Boxes - First Guess + Link Zone Objects", false, 54)]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Zone Boxes", "Stop Play mode first.", "OK"); return; }
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog("Zone Boxes", "There is no MatchDirector in the open scene. Open Cinema_GamePlay first.", "OK");
            return;
        }

        var zones = new List<RuleZone>();
        foreach (RuleZone z in Object.FindObjectsByType<RuleZone>(FindObjectsInactive.Include))
            if (z.gameObject.scene.IsValid()) zones.Add(z);
        var chores = new List<ZoneChoreSpot>();
        foreach (ZoneChoreSpot c in Object.FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.gameObject.scene.IsValid()) chores.Add(c);

        var log = new System.Text.StringBuilder();
        Transform root = FindRoot(zones);

        // Which zone each chore is in NOW (before any box is resized).
        var choreZone = new Dictionary<ZoneChoreSpot, RuleZone>();
        foreach (ZoneChoreSpot c in chores)
            foreach (RuleZone z in zones)
                if (z.Contains(c.transform.position)) { choreZone[c] = z; break; }

        // 1 + 2. Existing boxes: Zone ID, first-guess size, pivot.
        foreach (RuleZone z in zones)
        {
            if (!string.IsNullOrEmpty(z.ZoneID)) continue; // already done once: your hand edits stay
            string id = GuessID(z.ZoneName + " " + z.name);
            Bounds old = z.Box;
            Bounds fit = FirstGuess(z, id, old, chores, choreZone);
            Undo.RecordObject(z, "Zone boxes");
            z.Set(z.ZoneName, fit);
            SetString(z, "zoneID", id);
            RuleZoneEditor.CentrePivot(z);
            log.AppendLine($"- {z.ZoneName} ({id}): {Size(old)} -> {Size(fit)}");
        }

        // 3. Missing boxes.
        RuleZone human = MakeHumanRoom(root, zones, chores, choreZone, log);
        if (human != null) zones.Add(human);
        RuleZone ticket = MakeTicketZone(root, zones, log);
        if (ticket != null) zones.Add(ticket);

        // 4. Chores and task boards under their zone box.
        int moved = 0;
        foreach (ZoneChoreSpot c in chores)
            if (choreZone.TryGetValue(c, out RuleZone z) && c.transform.parent != z.transform)
            {
                Undo.SetTransformParent(c.transform, z.transform, "Link zone objects");
                moved++;
            }
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
        {
            if (!board.gameObject.scene.IsValid()) continue;
            RuleZone z = ZoneFor(zones, board.CountedZoneID, board.transform.position);
            if (z == null || board.transform.parent == z.transform) continue;
            Undo.SetTransformParent(board.transform, z.transform, "Link zone objects");
            moved++;
        }
        if (moved > 0) log.AppendLine($"- Parented {moved} chore(s) / task board(s) under their zone box (world positions unchanged).");

        // The Missing Parts ghost uses the Ticket Zone box from now on.
        RuleZone ticketZone = ZoneFor(zones, TicketID, Vector3.positiveInfinity);
        foreach (MissingPartsGhost ghost in Object.FindObjectsByType<MissingPartsGhost>(FindObjectsInactive.Include))
        {
            var so = new SerializedObject(ghost);
            SerializedProperty box = so.FindProperty("ticketZoneBox");
            if (box == null || box.objectReferenceValue != null || ticketZone == null) continue;
            box.objectReferenceValue = ticketZone;
            so.ApplyModifiedProperties();
            log.AppendLine("- Missing Parts Ghost now uses the Ticket Zone box.");
        }

        if (log.Length == 0) log.AppendLine("Everything was already organised. Nothing changed.");
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[ZoneBoxSetup]\n" + log);
        EditorUtility.DisplayDialog("Zone Boxes", log +
            "\nThe sizes are a FIRST GUESS. Select each zone (yellow box) and drag the dots on its faces until it fits the room.\n" +
            "Keep a zone's chores (dirt, film) inside its box: its rules look for them there.\n\nThen save the scene (Ctrl+S).", "OK");
    }

    // ---- First guess ------------------------------------------------------------------------

    private static Bounds FirstGuess(RuleZone zone, string id, Bounds old, List<ZoneChoreSpot> chores,
        Dictionary<ZoneChoreSpot, RuleZone> choreZone)
    {
        var points = new PointBox();

        foreach (Transform t in zone.GetComponentsInChildren<Transform>(true))
        {
            if (t == zone.transform || IsEntranceObject(t, zone.transform)) continue;
            points.Add(t.position);
        }
        foreach (ZoneChoreSpot c in chores)
            if (choreZone.TryGetValue(c, out RuleZone z) && z == zone) points.Add(c.transform.position);

        if (id == ScreenID)
        {
            // The cinema screen is the far wall of the hall: the box must reach it.
            Transform screen = FindScreen(p => old.Contains(p));
            if (screen != null && RendererBounds(screen, out Bounds sb))
            {
                points.Add(sb.min);
                points.Add(sb.max);
                foreach (RuleStaffTime rule in zone.GetComponentsInChildren<RuleStaffTime>(true))
                {
                    var so = new SerializedObject(rule);
                    SerializedProperty field = so.FindProperty("screen");
                    if (field != null && field.objectReferenceValue == null) { field.objectReferenceValue = screen; so.ApplyModifiedProperties(); }
                }
            }
        }
        if (!points.any) return old;
        Bounds fit = points.box;

        float pad = id == ScreenID ? 2f : 1f;
        Vector3 min = old.min, max = old.max;
        Shrink(ref min.x, ref max.x, fit.min.x - pad, fit.max.x + pad);
        Shrink(ref min.z, ref max.z, fit.min.z - pad, fit.max.z + pad);
        var result = new Bounds();
        result.SetMinMax(min, max); // height stays as it was
        return result;
    }

    /// <summary>A box that grows to hold every point added.</summary>
    private sealed class PointBox
    {
        public bool any;
        public Bounds box;
        public void Add(Vector3 p)
        {
            if (!any) { box = new Bounds(p, Vector3.zero); any = true; }
            else box.Encapsulate(p);
        }
    }

    /// <summary>Moves min / max inwards to the wanted range. Never outwards; an axis that would become tiny is kept.</summary>
    private static void Shrink(ref float min, ref float max, float wantMin, float wantMax)
    {
        float newMin = Mathf.Max(min, wantMin), newMax = Mathf.Min(max, wantMax);
        if (newMax - newMin < Mathf.Max(3f, (max - min) * 0.35f)) return;
        min = newMin;
        max = newMax;
    }

    /// <summary>Things that stand at the door (outside the room): they must not stretch the box.</summary>
    private static bool IsEntranceObject(Transform t, Transform stopAt)
    {
        for (Transform x = t; x != null && x != stopAt; x = x.parent)
            if (x.name.Contains("Light Switch") || x.name.Contains("Black Box Place")) return true;
        return false;
    }

    // ---- Missing boxes ----------------------------------------------------------------------

    private static RuleZone MakeHumanRoom(Transform root, List<RuleZone> zones, List<ZoneChoreSpot> chores,
        Dictionary<ZoneChoreSpot, RuleZone> choreZone, System.Text.StringBuilder log)
    {
        foreach (RuleZone z in zones) if (z.name == HumanRoomName) return null;

        var mine = new List<ZoneChoreSpot>();
        foreach (ZoneChoreSpot c in chores)
            if (!choreZone.ContainsKey(c) && GetString(c, "zoneID") == ScreenID) mine.Add(c);
        if (mine.Count == 0) return null;

        var box = new Bounds(mine[0].transform.position, Vector3.zero);
        foreach (ZoneChoreSpot c in mine) box.Encapsulate(c.transform.position);
        Vector3 middle = box.center;
        Transform screen = FindScreen(p => { foreach (RuleZone z in zones) if (z.Contains(p)) return false; return true; }, middle);
        if (screen != null && RendererBounds(screen, out Bounds sb)) { box.Encapsulate(sb.min); box.Encapsulate(sb.max); }
        box.Expand(new Vector3(4f, 0f, 4f));
        box.SetMinMax(new Vector3(box.min.x, box.min.y - 1.5f, box.min.z), new Vector3(box.max.x, box.max.y + 4f, box.max.z));

        RuleZone zone = NewZone(root, HumanRoomName, "Screen Room", ScreenID, box);
        foreach (ZoneChoreSpot c in mine) choreZone[c] = zone;
        log.AppendLine($"- NEW {HumanRoomName} ({ScreenID}): {Size(box)} around its {mine.Count} bonus chore(s).");
        return zone;
    }

    private static RuleZone MakeTicketZone(Transform root, List<RuleZone> zones, System.Text.StringBuilder log)
    {
        foreach (RuleZone z in zones) if (z.ZoneID == TicketID || z.name == TicketZoneName) return null;

        // Best source: the two boxes the Missing Parts ghost already uses (ticket + popcorn areas).
        MissingPartsGhost ghost = Object.FindAnyObjectByType<MissingPartsGhost>(FindObjectsInactive.Include);
        if (ghost != null)
        {
            var so = new SerializedObject(ghost);
            Bounds ticket = so.FindProperty("ticketZone").boundsValue;
            Bounds popcorn = so.FindProperty("popcornZone").boundsValue;
            if (ticket.size.sqrMagnitude > 0.01f && ticket.center.sqrMagnitude + popcorn.center.sqrMagnitude > 0.01f)
            {
                RuleZone zone = NewZone(root, TicketZoneName, "Ticket Zone", TicketID, ticket);
                if (popcorn.size.sqrMagnitude > 0.01f)
                {
                    var zso = new SerializedObject(zone);
                    SerializedProperty more = zso.FindProperty("moreBoxes");
                    more.InsertArrayElementAtIndex(0);
                    more.GetArrayElementAtIndex(0).boundsValue = new Bounds(popcorn.center - zone.transform.position, popcorn.size);
                    zso.ApplyModifiedPropertiesWithoutUndo();
                }
                log.AppendLine($"- NEW {TicketZoneName} ({TicketID}): ticket area {Size(ticket)} + popcorn area {Size(popcorn)} (from the Missing Parts ghost's boxes).");
                return zone;
            }
        }

        // Fallback: around the Ticket Zone task boards and signs.
        bool any = false;
        var box = new Bounds();
        foreach (ZoneTaskList board in Object.FindObjectsByType<ZoneTaskList>(FindObjectsInactive.Include))
            if (board.gameObject.scene.IsValid() && board.CountedZoneID == TicketID)
            {
                if (!any) { box = new Bounds(board.transform.position, Vector3.zero); any = true; } else box.Encapsulate(board.transform.position);
            }
        foreach (RestBreakSign sign in Object.FindObjectsByType<RestBreakSign>(FindObjectsInactive.Include))
            if (sign.gameObject.scene.IsValid())
            {
                if (!any) { box = new Bounds(sign.transform.position, Vector3.zero); any = true; } else box.Encapsulate(sign.transform.position);
            }
        if (!any) return null;
        box.Expand(new Vector3(10f, 0f, 10f));
        box.SetMinMax(new Vector3(box.min.x, box.min.y - 2f, box.min.z), new Vector3(box.max.x, box.min.y + 5f, box.max.z));
        RuleZone made = NewZone(root, TicketZoneName, "Ticket Zone", TicketID, box);
        log.AppendLine($"- NEW {TicketZoneName} ({TicketID}): {Size(box)} around its task boards and signs.");
        return made;
    }

    private static RuleZone NewZone(Transform root, string objectName, string displayName, string id, Bounds box)
    {
        var go = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(go, "Zone boxes");
        if (root != null) go.transform.SetParent(root, false);
        go.transform.position = box.center;
        RuleZone zone = go.AddComponent<RuleZone>();
        zone.Set(displayName, box);
        SetString(zone, "zoneID", id);
        return zone;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static Transform FindRoot(List<RuleZone> zones)
    {
        GameObject root = GameObject.Find(RulesRoot);
        if (root != null) return root.transform;
        if (zones.Count > 0 && zones[0].transform.parent != null) return zones[0].transform.parent;
        root = new GameObject("Zones");
        Undo.RegisterCreatedObjectUndo(root, "Zone boxes");
        return root.transform;
    }

    private static string GuessID(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("toilet")) return ToiletID;
        if (n.Contains("ticket") || n.Contains("popcorn")) return TicketID;
        return ScreenID;
    }

    /// <summary>The zone of this quest zone that holds the point; else the first zone with that ID.</summary>
    private static RuleZone ZoneFor(List<RuleZone> zones, string id, Vector3 point)
    {
        RuleZone first = null;
        foreach (RuleZone z in zones)
        {
            if (z == null || z.ZoneID != id) continue;
            if (first == null) first = z;
            if (z.Contains(point)) return z;
        }
        return first;
    }

    /// <summary>A scene object named "Screen" with a mesh, whose middle passes the test (nearest to 'near' if given).</summary>
    private static Transform FindScreen(System.Func<Vector3, bool> accept, Vector3? near = null)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (t.name != "Screen" || !t.gameObject.scene.IsValid() || !RendererBounds(t, out Bounds b) || !accept(b.center)) continue;
            float d = near.HasValue ? (b.center - near.Value).sqrMagnitude : 0f;
            if (best == null || d < bestDistance) { best = t; bestDistance = d; }
        }
        return best;
    }

    private static bool RendererBounds(Transform t, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static string Size(Bounds b) => $"{b.size.x:0.#} x {b.size.y:0.#} x {b.size.z:0.#} m";

    private static void SetString(Object target, string field, string value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) return;
        p.stringValue = value;
        so.ApplyModifiedProperties();
    }

    private static string GetString(Object target, string field)
    {
        SerializedProperty p = new SerializedObject(target).FindProperty(field);
        return p != null ? p.stringValue : "";
    }
}
