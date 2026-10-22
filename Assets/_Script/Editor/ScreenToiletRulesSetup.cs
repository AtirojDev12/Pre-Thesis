using System.Collections.Generic;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Pre-Thesis > Scenes > Map: Setup Screen + Toilet Rules   (16 Oct 2026, Mr.k)
///
/// Open Cinema_GamePlay first (and run "Map: Setup Screen + Toilet Zones" before,
/// the rules use those chores). Builds everything under one new object
/// "Zone Rules (Screen + Toilet)":
///
///   Ghost Screen Room: No noise, Don't look at the audience, The audience stands,
///                      Staff time (+ audience, shadow seat, ghost movie, rule ghosts)
///   Men's toilet:      Cracked mirror, Cigarette smoke, Ghost food (+ light switch, black box)
///   Women's toilet:    Cracked mirror, The last stall (crying), Ghost food (+ same)
///
/// Looks = prefab instances from Assets/Prefab/RuleMockups (made if missing,
/// never overwritten). Nonnapat's room objects are only READ for positions.
/// Running it again replaces everything it made before. Then SAVE (Ctrl+S).
/// </summary>
public static class ScreenToiletRulesSetup
{
    private const string RootName = "Zone Rules (Screen + Toilet)";
    private const int AudienceCount = 10;
    private const int ShadowSpotCount = 3;
    private const int LampsPerToilet = 5;

    private static Dictionary<string, GameObject> prefabs;
    private static readonly List<string> notes = new List<string>();

    [MenuItem("Tools/Pre-Thesis/Scenes/Map: Setup Screen + Toilet Rules", false, 53)]
    private static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (Object.FindAnyObjectByType<MatchDirector>() == null)
        {
            EditorUtility.DisplayDialog("Screen + Toilet Rules", "There is no MatchDirector in the open scene. Open Cinema_GamePlay first.", "OK");
            return;
        }

        Transform theater = Find(scene, "TeaterZone");
        Transform ghostRoom = theater != null ? theater.Find("GhostScreenRoom") : null;
        if (ghostRoom == null && theater != null) ghostRoom = theater.Find("ScreenRoom (1)");
        Transform toilet = Find(scene, "Toilet");
        Transform men = toilet != null ? toilet.Find("MenToilet") : null;
        Transform women = toilet != null ? toilet.Find("WomenToilet") : null;

        var missing = new List<string>();
        if (ghostRoom == null) missing.Add("TeaterZone/GhostScreenRoom");
        if (men == null) missing.Add("Toilet/MenToilet");
        if (women == null) missing.Add("Toilet/WomenToilet");
        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("Screen + Toilet Rules", "Not found in the open scene:\n- " + string.Join("\n- ", missing) + "\n\nNothing was changed.", "OK");
            return;
        }

        int newPrefabs = RuleMockupBuilder.BuildMissing(out prefabs);
        notes.Clear();
        Physics.SyncTransforms();

        Transform old = Find(scene, RootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup Screen + Toilet Rules");
        SceneManager.MoveGameObjectToScene(root, scene);

        BuildScreenRoom(root.transform, ghostRoom);
        BuildToilet(root.transform, men, "Men's Toilet", true);
        BuildToilet(root.transform, women, "Women's Toilet", false);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        string extra = notes.Count > 0 ? "\n\nCheck:\n- " + string.Join("\n- ", notes) : "";
        EditorUtility.DisplayDialog("Screen + Toilet Rules",
            $"Done. {newPrefabs} new mockup prefab(s) in {RuleMockupBuilder.Folder}.\n\n" +
            "Look at the yellow zone boxes, the light switches, box places and audience in the Scene view " +
            "(move anything that looks wrong), then SAVE the scene (Ctrl+S)." + extra, "OK");
    }

    // ---- Ghost Screen Room ------------------------------------------------------------------

    private static void BuildScreenRoom(Transform root, Transform room)
    {
        Bounds roomBox = RendererBounds(room);
        RuleZone zone = Zone(root, "Screen Room (Ghost Room)", "Ghost Screen Room", roomBox);
        Transform parent = zone.transform;
        Vector3 center = roomBox.center;

        Transform chairs = room.Find("ChairBase");
        Transform screen = room.Find("Screen");
        Bounds chairBox = chairs != null ? RendererBounds(chairs) : roomBox;
        Vector3 screenCenter = screen != null ? RendererBounds(screen).center : roomBox.center + Vector3.right * roomBox.extents.x;
        if (chairs == null) notes.Add("GhostScreenRoom/ChairBase not found: audience placed on a grid.");

        // Seats = step tops of the ChairBase, spread out.
        List<Vector3> seats = SeatPoints(chairs, chairBox);
        var used = new List<Vector3>();
        List<Vector3> audiencePoints = Spread(seats, used, AudienceCount, 1.6f);
        List<Vector3> shadowPoints = Spread(seats, used, ShadowSpotCount, 1.6f);
        if (audiencePoints.Count < AudienceCount) notes.Add($"Only {audiencePoints.Count} audience seats found.");

        // Audience
        GameObject audienceGo = NetObject(parent, "Ghost Audience", center);
        GhostAudience audience = audienceGo.AddComponent<GhostAudience>();
        var members = new List<Transform>();
        for (int i = 0; i < audiencePoints.Count; i++)
        {
            GameObject m = Instance(RuleMockupBuilder.GhostAudience, audienceGo.transform, $"Customer {i + 1}");
            m.transform.SetPositionAndRotation(audiencePoints[i], FaceFlat(audiencePoints[i], screenCenter));
            members.Add(m.transform);
        }
        SetArray(audience, "members", members.ToArray());

        // Rule ghosts
        RuleGhost[] chasers = new RuleGhost[3];
        for (int i = 0; i < chasers.Length; i++) chasers[i] = Ghost(parent, $"Rule Ghost - Audience {i + 1}", center, 10f, 5f, false);
        RuleGhost roamer = Ghost(parent, "Rule Ghost - Roams All Night", center, 20f, 10f, true);
        Set(roamer, "speed", 2.2f);

        // 1. No noise
        RuleNoNoise noNoise = Rule<RuleNoNoise>(parent, "Rule 1 - No Noise", center, zone, "No noise",
            "Inside the ghost room do not make noise over the limit (loud talking, sprinting). Big sanity drop.");

        // 2. Don't look
        RuleDontLookAtAudience dontLook = Rule<RuleDontLookAtAudience>(parent, "Rule 2 - Don't Look At The Audience", center, zone,
            "Don't look at the audience", "Do not look at the customers watching the movie. You lose health while you look.");
        Set(dontLook, "audience", audience);

        // 3. Audience stands
        RuleAudienceStands stands = Rule<RuleAudienceStands>(parent, "Rule 3 - The Audience Stands", center, zone,
            "The audience stands", "If every customer stands and stares at you: stay still. When they sit, leave at once. Else they chase you (inside the room only).");
        Set(stands, "audience", audience);
        SetArray(stands, "chasers", chasers);

        // 4. Staff time
        RuleStaffTime staff = Rule<RuleStaffTime>(parent, "Rule 4 - Staff Time", center, zone, "Staff time",
            "If a ghost movie plays and the room is almost empty when you come to change the film: sit on the shadow's seat until the screen goes dark, then change the film. Else a ghost roams all night.");
        Set(staff, "audience", audience);
        Set(staff, "roamer", roamer);

        GameObject movie = Instance(RuleMockupBuilder.GhostMovie, staff.transform, "Ghost Movie (on the screen)");
        if (screen != null)
        {
            Bounds sb = RendererBounds(screen);
            Vector3 towardSeats = Flat(chairBox.center - sb.center).normalized;
            movie.transform.SetPositionAndRotation(sb.center + towardSeats * 0.08f, Quaternion.LookRotation(-towardSeats, Vector3.up));
            float width = Mathf.Max(sb.size.x, sb.size.z) * 0.95f;
            movie.transform.localScale = new Vector3(width, sb.size.y * 0.9f, 1f);
        }
        else notes.Add("GhostScreenRoom/Screen not found: move \"Ghost Movie\" onto the screen.");
        movie.SetActive(false);
        Set(staff, "ghostMovie", movie);

        var spotsRoot = new GameObject("Shadow Spots (seats the shadow may use)").transform;
        spotsRoot.SetParent(parent, false);
        var spots = new List<Transform>();
        for (int i = 0; i < shadowPoints.Count; i++)
        {
            var s = new GameObject($"Shadow Spot {i + 1}").transform;
            s.SetParent(spotsRoot, false);
            s.SetPositionAndRotation(shadowPoints[i], FaceFlat(shadowPoints[i], screenCenter));
            spots.Add(s);
        }
        SetArray(staff, "shadowSpots", spots.ToArray());

        GameObject seatGo = NetObject(parent, "Shadow Seat", shadowPoints.Count > 0 ? shadowPoints[0] : center);
        RuleInteractable seat = seatGo.AddComponent<RuleInteractable>();
        GameObject shadowLook = Instance(RuleMockupBuilder.ShadowSitter, seatGo.transform, "Shadow");
        shadowLook.transform.localPosition = Vector3.zero;
        ConfigureButton(seat, "Sit on the shadow", 1f, new[] { shadowLook });
        Set(staff, "shadowSeat", seat);
        if (shadowPoints.Count == 0) notes.Add("No shadow seats found: add points under \"Shadow Spots\".");

        noNoise.transform.SetSiblingIndex(0);
    }

    // ---- Toilets ----------------------------------------------------------------------------------

    private static void BuildToilet(Transform root, Transform toilet, string label, bool isMen)
    {
        Bounds box = RendererBounds(toilet);
        RuleZone zone = Zone(root, label, label, box);
        Transform parent = zone.transform;
        Vector3 center = box.center;
        float floorY = FloorY(center, box);

        // Mirrors (crack points), row direction
        Transform mirrorRoot = toilet.Find("Mirror");
        var mirrorPoints = new List<Transform>();
        var mirrorsHolder = new GameObject("Mirror Points").transform;
        mirrorsHolder.SetParent(parent, false);
        if (mirrorRoot != null)
            foreach (Transform m in mirrorRoot)
            {
                if (m.GetComponentInChildren<Renderer>() == null) continue;
                Bounds mb = RendererBounds(m);
                Vector3 outward = ThinAxisToward(mb, center);
                var p = new GameObject(m.name + " (glass)").transform;
                p.SetParent(mirrorsHolder, false);
                p.SetPositionAndRotation(mb.center + outward * (ThinExtent(mb) + 0.02f), Quaternion.LookRotation(outward, Vector3.up));
                mirrorPoints.Add(p);
            }
        if (mirrorPoints.Count == 0) notes.Add($"{label}: no mirrors found (Mirror/*). Add points under \"Mirror Points\".");

        // Stall doors
        Transform doorRoot = Deep(toilet, "Door");
        var stallPoints = new List<Transform>();
        var stallsHolder = new GameObject("Stall Door Points").transform;
        stallsHolder.SetParent(parent, false);
        if (doorRoot != null)
            foreach (Transform d in doorRoot)
            {
                if (d.GetComponentInChildren<Renderer>() == null) continue;
                Bounds db = RendererBounds(d);
                Vector3 outward = ThinAxisToward(db, center);
                var p = new GameObject(d.name + " (door)").transform;
                p.SetParent(stallsHolder, false);
                p.SetPositionAndRotation(new Vector3(db.center.x, floorY + 1.0f, db.center.z) + outward * (ThinExtent(db) + 0.03f),
                    Quaternion.LookRotation(outward, Vector3.up));
                stallPoints.Add(p);
            }
        if (stallPoints.Count == 0) notes.Add($"{label}: no stall doors found (…/Door/*).");

        // Light switch: on a post past the end of the mirror row (a guess: move it to the door).
        Vector3 switchPos;
        Quaternion switchRot;
        if (mirrorPoints.Count >= 2)
        {
            Vector3 first = mirrorPoints[0].position, last = mirrorPoints[mirrorPoints.Count - 1].position;
            Vector3 along = Flat(first - last).normalized;
            Vector3 outward = mirrorPoints[0].forward;
            switchPos = new Vector3(first.x, floorY + 1.32f, first.z) + along * 0.8f + outward * 0.25f;
            switchRot = Quaternion.LookRotation(outward, Vector3.up);
        }
        else
        {
            switchPos = new Vector3(center.x, floorY + 1.32f, center.z);
            switchRot = Quaternion.identity;
        }
        notes.Add($"{label}: the light switch + black box place are a guess. Move them next to the toilet door.");

        GameObject switchGo = NetObject(parent, "Light Switch (toilet light)", switchPos);
        switchGo.transform.rotation = switchRot;
        RuleInteractable switchButton = switchGo.AddComponent<RuleInteractable>();
        ConfigureButton(switchButton, "Turn light off", 0f, new GameObject[0]);
        ToiletLight light = switchGo.AddComponent<ToiletLight>();
        Set(light, "buildingZoneId", isMen ? "MaleToilet" : "FemaleToilet");
        GameObject switchLook = Instance(RuleMockupBuilder.LightSwitch, switchGo.transform, "Switch");
        switchLook.transform.localPosition = Vector3.zero;
        switchLook.transform.localRotation = Quaternion.identity;
        // 16 Oct: 5 dim lamps in a row along the room's long side (zone light, not building power).
        bool alongX = box.size.x >= box.size.z;
        float length = alongX ? box.size.x : box.size.z;
        var lamps = new GameObject[LampsPerToilet];
        for (int i = 0; i < LampsPerToilet; i++)
        {
            float f = (i + 0.5f) / LampsPerToilet;
            float along = (alongX ? box.min.x : box.min.z) + length * f;
            Vector3 xz = alongX ? new Vector3(along, 0f, center.z) : new Vector3(center.x, 0f, along);
            lamps[i] = Instance(RuleMockupBuilder.ToiletLamp, switchGo.transform, $"Toilet Lamp {i + 1} (ceiling)");
            lamps[i].transform.SetPositionAndRotation(new Vector3(xz.x, CeilingY(xz + Vector3.up * floorY, floorY), xz.z), Quaternion.identity);
        }
        SetArray(light, "lamps", lamps);

        // Last stall = the door farthest from the switch (switch is near the entrance).
        Transform lastStall = null;
        float far = -1f;
        foreach (Transform s in stallPoints)
        {
            float d = Flat(s.position - switchPos).sqrMagnitude;
            if (d > far) { far = d; lastStall = s; }
        }

        // 1. Cracked mirror
        RuleCrackedMirror mirror = Rule<RuleCrackedMirror>(parent, "Rule 1 - Cracked Mirror", center, zone, "Cracked mirror",
            "If a mirror has a crack, look at it for a while. Else it shatters: jumpscare, damage and sanity for everyone inside.");
        SetArray(mirror, "mirrors", mirrorPoints.ToArray());
        GameObject crack = Instance(RuleMockupBuilder.MirrorCrack, mirror.transform, "Crack");
        GameObject shattered = Instance(RuleMockupBuilder.MirrorShattered, mirror.transform, "Shattered");
        crack.SetActive(false);
        shattered.SetActive(false);
        Set(mirror, "crackLook", crack.transform);
        Set(mirror, "shatteredLook", shattered.transform);

        if (isMen)
        {
            // 3. Cigarette smoke
            RuleSmokingStall smoke = Rule<RuleSmokingStall>(parent, "Rule 3 - Cigarette Smoke", center, zone, "Cigarette smoke",
                "If smoke comes out of a stall: knock on that door, then turn off the light. Else health drains until it goes, and this toilet's chores lock.");
            Set(smoke, "toiletLight", light);
            SetArray(smoke, "stalls", stallPoints.ToArray());
            GameObject smokeLook = Instance(RuleMockupBuilder.Smoke, smoke.transform, "Smoke");
            smokeLook.SetActive(false);
            Set(smoke, "smokeLook", smokeLook);
            GameObject knockGo = NetObject(parent, "Knock Spot (moves to the smoking stall)", stallPoints.Count > 0 ? stallPoints[0].position : center);
            RuleInteractable knock = knockGo.AddComponent<RuleInteractable>();
            GameObject knockLook = Instance(RuleMockupBuilder.KnockSpot, knockGo.transform, "Knock Target");
            knockLook.transform.localPosition = Vector3.zero;
            knockLook.transform.localRotation = Quaternion.identity;
            ConfigureButton(knock, "Knock", 0f, new GameObject[0]);
            Set(smoke, "knock", knock);
        }
        else
        {
            // 2. The last stall (crying)
            RuleCryingStall cry = Rule<RuleCryingStall>(parent, "Rule 2 - The Last Stall (crying)", center, zone, "The last stall",
                "If the last stall is closed and someone cries inside: turn off the toilet light at once and stay still. Else a ghost comes out and chases you.");
            Set(cry, "toiletLight", light);
            Vector3 stallPos = lastStall != null ? lastStall.position : center;
            Quaternion stallRot = lastStall != null ? lastStall.rotation : Quaternion.identity;
            GameObject door = Instance(RuleMockupBuilder.ClosedStallDoor, cry.transform, "Closed Door (last stall)");
            door.transform.SetPositionAndRotation(stallPos - stallRot * Vector3.forward * 0.03f, stallRot);
            door.transform.localScale = new Vector3(0.85f, 1.9f, 1f);
            door.SetActive(false);
            Set(cry, "closedDoorLook", door);
            GameObject sound = Instance(RuleMockupBuilder.CryingSound, cry.transform, "Crying Sound");
            sound.transform.position = stallPos - stallRot * Vector3.forward * 0.6f + Vector3.up * 0.2f;
            Set(cry, "crying", sound.GetComponent<AudioSource>());
            var inside = new GameObject("Ghost Comes Out Here").transform;
            inside.SetParent(cry.transform, false);
            inside.position = new Vector3(stallPos.x, floorY, stallPos.z) - stallRot * Vector3.forward * 0.6f;
            Set(cry, "stallPoint", inside);
            Set(cry, "stallGhost", Ghost(parent, "Rule Ghost - Last Stall", inside.position, 15f, 10f, true));
            if (lastStall == null) notes.Add("Women's Toilet: no stall door found for the crying stall.");
        }

        // 4. Ghost food
        RuleGhostFood food = Rule<RuleGhostFood>(parent, "Rule 4 - Ghost Food", center, zone, "Ghost food",
            "When this toilet is fully cleaned: turn off the light, throw the black box in (drop spot at the last stall) and get out fast. Else the ghosts swarm you.");
        Set(food, "toiletLight", light);

        Vector3 outwardFromWall = switchRot * Vector3.forward;
        Vector3 shelfPos = new Vector3(switchPos.x, floorY, switchPos.z) + outwardFromWall * 0.4f + Flat(switchRot * Vector3.right) * 0.6f;
        GameObject boxPlace = NetObject(parent, "Black Box Place (staff room later)", shelfPos);
        boxPlace.transform.rotation = switchRot;
        RuleInteractable pickup = boxPlace.AddComponent<RuleInteractable>();
        ConfigureButton(pickup, "Pick up black box", 0f, new GameObject[0]);
        BlackBoxCarry carry = boxPlace.AddComponent<BlackBoxCarry>();
        GameObject shelf = Instance(RuleMockupBuilder.BlackBoxShelf, boxPlace.transform, "Shelf");
        shelf.transform.localPosition = Vector3.zero;
        shelf.transform.localRotation = Quaternion.identity;
        GameObject blackBox = Instance(RuleMockupBuilder.BlackBox, boxPlace.transform, "Black Box");
        blackBox.transform.localPosition = new Vector3(0f, 0.92f, 0f);
        blackBox.transform.localRotation = Quaternion.identity;
        Set(carry, "box", blackBox.transform);
        Set(food, "blackBox", carry);

        Vector3 dropPos = lastStall != null
            ? new Vector3(lastStall.position.x, floorY, lastStall.position.z) + lastStall.forward * 0.5f
            : new Vector3(center.x, floorY, center.z);
        GameObject dropGo = NetObject(parent, "Box Drop Spot (last stall)", dropPos);
        RuleInteractable drop = dropGo.AddComponent<RuleInteractable>();
        GameObject dropLook = Instance(RuleMockupBuilder.BoxDropSpot, dropGo.transform, "Drop Spot");
        dropLook.transform.localPosition = Vector3.zero;
        ConfigureButton(drop, "Throw the black box in", 0f, new[] { dropLook });
        Set(food, "dropSpot", drop);

        RuleGhost[] swarm = new RuleGhost[3];
        for (int i = 0; i < swarm.Length; i++) swarm[i] = Ghost(parent, $"Rule Ghost - Swarm {i + 1}", new Vector3(center.x, floorY, center.z), 10f, 10f, false);
        SetArray(food, "swarm", swarm);
    }

    // ---- Building blocks --------------------------------------------------------------------

    private static RuleZone Zone(Transform root, string objectName, string zoneName, Bounds box)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(root, false);
        RuleZone zone = go.AddComponent<RuleZone>();
        zone.Set(zoneName, box);
        EditorUtility.SetDirty(zone);
        return zone;
    }

    /// <summary>A scene object with its own NetworkIdentity (its parents have none: Mirror does not nest identities).</summary>
    private static GameObject NetObject(Transform parent, string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.AddComponent<NetworkIdentity>();
        return go;
    }

    private static T Rule<T>(Transform parent, string name, Vector3 position, RuleZone zone, string title, string text) where T : ZoneRuleBase
    {
        GameObject go = NetObject(parent, name, position);
        T rule = go.AddComponent<T>();
        Set(rule, "zone", zone);
        Set(rule, "ruleTitle", title);
        Set(rule, "ruleText", text);
        return rule;
    }

    private static RuleGhost Ghost(Transform parent, string name, Vector3 position, float damage, float sanity, bool jumpscare)
    {
        GameObject go = NetObject(parent, name, position);
        RuleGhost ghost = go.AddComponent<RuleGhost>();
        GameObject look = Instance(RuleMockupBuilder.RuleGhost, go.transform, "Look");
        look.transform.localPosition = Vector3.zero;
        look.transform.localRotation = Quaternion.identity;
        look.SetActive(false);
        Set(ghost, "visual", look);
        Set(ghost, "touchDamage", damage);
        Set(ghost, "touchSanity", sanity);
        Set(ghost, "jumpscareOnTouch", jumpscare);
        return ghost;
    }

    private static void ConfigureButton(RuleInteractable button, string action, float hold, GameObject[] showWhen)
    {
        Set(button, "actionText", action);
        Set(button, "holdSeconds", hold);
        SetArray(button, "showWhenUsable", showWhen);
    }

    private static GameObject Instance(string prefabName, Transform parent, string name)
    {
        GameObject prefab = prefabs != null && prefabs.TryGetValue(prefabName, out GameObject p) ? p : null;
        GameObject go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent) : new GameObject();
        if (prefab == null) go.transform.SetParent(parent, false);
        go.name = name;
        return go;
    }

    // ---- Serialized fields --------------------------------------------------------------------

    private static void Set(Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogError($"[Rules setup] {target.GetType().Name} has no field '{field}'."); return; }
        if (value is float) p.floatValue = (float)value;
        else if (value is int) p.intValue = (int)value;
        else if (value is bool) p.boolValue = (bool)value;
        else if (value is string) p.stringValue = (string)value;
        else p.objectReferenceValue = value as Object; // null clears it
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray<T>(Object target, string field, T[] values) where T : Object
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogError($"[Rules setup] {target.GetType().Name} has no field '{field}'."); return; }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- Geometry ---------------------------------------------------------------------------------

    private static List<Vector3> SeatPoints(Transform chairs, Bounds box)
    {
        var points = new List<Vector3>();
        const int steps = 14;
        for (int x = 0; x < steps; x++)
            for (int z = 0; z < steps; z++)
            {
                Vector3 top = new Vector3(Mathf.Lerp(box.min.x + 0.6f, box.max.x - 0.6f, x / (steps - 1f)), box.max.y + 1f,
                                          Mathf.Lerp(box.min.z + 0.6f, box.max.z - 0.6f, z / (steps - 1f)));
                RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, box.size.y + 3f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                bool found = false;
                Vector3 point = Vector3.zero;
                foreach (RaycastHit h in hits)
                {
                    if (h.normal.y < 0.7f || h.distance >= best) continue;
                    if (chairs != null && !h.transform.IsChildOf(chairs)) continue;
                    best = h.distance;
                    point = h.point;
                    found = true;
                }
                if (found) points.Add(point);
            }
        if (points.Count == 0) // no collider: flat grid on the top
            for (int x = 0; x < 4; x++)
                for (int z = 0; z < 4; z++)
                    points.Add(new Vector3(Mathf.Lerp(box.min.x + 1f, box.max.x - 1f, x / 3f), box.min.y, Mathf.Lerp(box.min.z + 1f, box.max.z - 1f, z / 3f)));
        return points;
    }

    /// <summary>Picks up to <paramref name="count"/> points, each at least <paramref name="gap"/> m from all used ones (fixed seed = same result each run).</summary>
    private static List<Vector3> Spread(List<Vector3> candidates, List<Vector3> used, int count, float gap)
    {
        var picked = new List<Vector3>();
        var order = new List<Vector3>(candidates);
        var random = new System.Random(13 + used.Count);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            Vector3 t = order[i]; order[i] = order[j]; order[j] = t;
        }
        foreach (Vector3 c in order)
        {
            if (picked.Count >= count) break;
            bool ok = true;
            foreach (Vector3 u in used) if ((u - c).sqrMagnitude < gap * gap) { ok = false; break; }
            if (!ok) continue;
            picked.Add(c);
            used.Add(c);
        }
        return picked;
    }

    private static Bounds RendererBounds(Transform t)
    {
        bool has = false;
        Bounds b = new Bounds(t.position, Vector3.one);
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static float FloorY(Vector3 center, Bounds box)
    {
        Vector3 start = new Vector3(center.x, box.max.y + 0.5f, center.z);
        float best = float.MaxValue, y = box.min.y;
        foreach (RaycastHit h in Physics.RaycastAll(start, Vector3.down, box.size.y + 3f, ~0, QueryTriggerInteraction.Ignore))
            if (h.normal.y > 0.7f && h.point.y < box.min.y + 1.2f && h.distance < best) { best = h.distance; y = h.point.y; }
        return y;
    }

    private static float CeilingY(Vector3 center, float floorY)
    {
        // The lowest ceiling-like surface at least 2.2 m up (skips stall walls, basins, doors).
        Vector3 start = new Vector3(center.x, floorY + 0.5f, center.z);
        float best = float.MaxValue;
        foreach (RaycastHit h in Physics.RaycastAll(start, Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore))
            if (h.point.y >= floorY + 2.2f && h.point.y < best) best = h.point.y;
        return best < float.MaxValue ? best - 0.02f : floorY + 2.7f;
    }

    /// <summary>The bounds' thinnest horizontal axis, pointing toward <paramref name="toward"/>.</summary>
    private static Vector3 ThinAxisToward(Bounds b, Vector3 toward)
    {
        Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        Vector3 d = toward - b.center;
        return Vector3.Dot(d, axis) >= 0f ? axis : -axis;
    }

    private static float ThinExtent(Bounds b) => Mathf.Min(b.extents.x, b.extents.z);

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static Quaternion FaceFlat(Vector3 from, Vector3 to)
    {
        Vector3 d = Flat(to - from);
        return d.sqrMagnitude > 0.001f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.identity;
    }

    private static Transform Find(Scene scene, string name)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == name) return go.transform;
            Transform found = Deep(go.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform Deep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = Deep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
