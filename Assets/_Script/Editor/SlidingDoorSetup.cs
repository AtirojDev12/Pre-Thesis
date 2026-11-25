using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 25 Nov 2026 (Mr.k): Tools > Pre-Thesis > One Step > Sliding Doors.
/// Every door in the open scene that is NOT an exit becomes a sliding door (E = open / close).
///
/// What counts as a door:
///   - a prefab model whose name has "Door" (CinemaScreenRoomDoor, LobbyDoorCinema, ...), or
///   - a single mesh whose name has "Door" and has a door shape (tall, thin, max 4 m wide).
/// Skipped: exits (ExitPoint, or 3.5 m around one), rule objects ("Zone Rules", "RM_..."),
/// stall door points, neon signs ("AllNeon..."), doors with their own script (Tutorial door),
/// and doors inside a bigger model (logged).
///
/// For each door it makes "Sliding Door (name)" at the doorway (floor), puts the model under it,
/// adds NetworkIdentity + SlidingDoor, and works out the slide: double doors open to both sides,
/// a single door slides along its width. Handles move with their half. Old door Animators are
/// switched off (they would fight the slide). Safe to run again (it refreshes the doors).
/// </summary>
public static class SlidingDoorSetup
{
    private const string Title = "Sliding Doors";
    private const float ExitRadius = 3.5f;

    [MenuItem("Tools/Pre-Thesis/One Step/Sliding Doors (every non-exit door)", false, 131)]
    private static void Run()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
        Scene scene = SceneManager.GetActiveScene();
        var log = new StringBuilder();
        var skipped = new StringBuilder();

        var exits = new List<Vector3>();
        foreach (ExitPoint e in Object.FindObjectsByType<ExitPoint>(FindObjectsInactive.Include))
        {
            exits.Add(e.transform.position);
            foreach (Renderer r in e.GetComponentsInChildren<Renderer>(true)) exits.Add(r.bounds.center);
        }

        var models = new List<GameObject>();
        var seen = new HashSet<GameObject>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            GameObject go = t.gameObject;
            if (go.scene != scene || !go.name.ToLowerInvariant().Contains("door")) continue;
            GameObject model = ModelOf(go, skipped);
            if (model == null || !seen.Add(model)) continue;
            string why = SkipReason(model, exits);
            if (why != null) { skipped.AppendLine($"- {Path(model.transform)}: {why}"); continue; }
            models.Add(model);
        }

        int made = 0, refreshed = 0;
        foreach (GameObject model in models)
        {
            bool had = model.transform.parent != null && model.transform.parent.GetComponent<SlidingDoor>() != null;
            if (Build(scene, model, log, skipped)) { if (had) refreshed++; else made++; }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        string text = $"Sliding doors: {made} new, {refreshed} refreshed.\n" + log +
                      (skipped.Length > 0 ? "\nNot changed:\n" + skipped : "") +
                      "\nSave the scene (Ctrl+S). A wrong one? Delete its SlidingDoor component (or Ctrl+Z).";
        Debug.Log("[SlidingDoorSetup]\n" + text);
        EditorUtility.DisplayDialog(Title, text.Length > 1800 ? text.Substring(0, 1800) + "\n... (full list in the Console)" : text, "OK");
    }

    /// <summary>The door model this object belongs to, or null when it is not a door we can slide.</summary>
    private static GameObject ModelOf(GameObject go, StringBuilder skipped)
    {
        if (go.GetComponent<SlidingDoor>() != null) return null; // our own root: its child is the model
        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null) return null;
            if (root.name.ToLowerInvariant().Contains("door")) return root;
            if (go.GetComponent<Renderer>() != null && LooksLikeDoor(go))
                skipped.AppendLine($"- {Path(go.transform)}: inside the model \"{root.name}\" (cannot be moved out of it)");
            return null;
        }
        // A plain scene object: a door mesh itself, or a parent of door meshes.
        if (go.GetComponent<Renderer>() != null) return LooksLikeDoor(go) ? go : null;
        // A group of door meshes (no prefab inside) that together has a door shape. A folder like
        // "Door" holding several door models is NOT one door (its children are found one by one).
        Renderer[] inside = go.GetComponentsInChildren<Renderer>(true);
        if (inside.Length == 0) return null;
        foreach (Renderer r in inside) if (PrefabUtility.IsPartOfPrefabInstance(r.gameObject)) return null;
        return DoorShape(BoundsOf(go)) ? go : null;
    }

    private static bool DoorShape(Bounds b)
    {
        Vector3 s = b.size;
        float wide = Mathf.Max(s.x, s.z), thin = Mathf.Min(s.x, s.z);
        return s.y >= 1.5f && s.y <= 4.5f && wide <= 4f && thin <= 0.6f;
    }

    private static bool LooksLikeDoor(GameObject go)
    {
        Renderer r = go.GetComponent<Renderer>();
        return r != null && DoorShape(r.bounds);
    }

    private static string SkipReason(GameObject model, List<Vector3> exits)
    {
        if (model.GetComponentInParent<ExitPoint>(true) != null) return "exit";
        for (Transform p = model.transform; p != null; p = p.parent)
        {
            string n = p.name;
            if (n.Contains("Zone Rules")) return "rule object";
            if (n.StartsWith("RM_")) return "rule mockup";
            if (n.Contains("Stall")) return "stall door (toilet rules)";
            if (n.Contains("AllNeon")) return "neon sign";
            if (p != model.transform && p.GetComponent<NetworkIdentity>() != null && p.GetComponent<SlidingDoor>() == null)
                return "inside a networked object";
        }
        Bounds b = BoundsOf(model);
        foreach (Vector3 e in exits)
        {
            Vector3 d = b.center - e;
            d.y = 0f;
            if (d.magnitude <= ExitRadius) return "next to an exit";
        }
        if (model.GetComponentsInChildren<Renderer>(true).Length == 0) return "no mesh";
        // A door that already has its own script (Tutorial door, a rule door...) keeps it.
        foreach (MonoBehaviour mb in model.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && !(mb is SlidingDoor) && (mb is IInteractable || mb.GetType().Name.Contains("Door")))
                return "has its own script (" + mb.GetType().Name + ")";
        return null;
    }

    private static bool Build(Scene scene, GameObject model, StringBuilder log, StringBuilder skipped)
    {
        // Parts = every mesh of the model (a mesh under another part moves with it).
        var renderers = new List<Renderer>(model.GetComponentsInChildren<Renderer>(true));
        var partSet = new HashSet<Transform>();
        foreach (Renderer r in renderers) partSet.Add(r.transform);
        var bodies = new List<Renderer>();
        var handles = new List<Renderer>();
        foreach (Renderer r in renderers)
        {
            bool underPart = false;
            for (Transform p = r.transform.parent; p != null && p != model.transform.parent; p = p.parent)
                if (partSet.Contains(p)) { underPart = true; break; }
            if (underPart) continue;
            if (r.name.ToLowerInvariant().Contains("handle")) handles.Add(r);
            else bodies.Add(r);
        }
        if (bodies.Count == 0) { skipped.AppendLine($"- {Path(model.transform)}: no door leaf found"); return false; }

        Bounds all = BoundsOf(model);
        Vector3 axis = SlideAxis(bodies);

        // The root: at the doorway, on the floor. The model goes under it.
        Transform rootT = model.transform.parent;
        SlidingDoor door = rootT != null ? rootT.GetComponent<SlidingDoor>() : null;
        if (door == null)
        {
            var root = new GameObject("Sliding Door (" + model.name + ")");
            Undo.RegisterCreatedObjectUndo(root, Title);
            if (model.transform.parent != null) Undo.SetTransformParent(root.transform, model.transform.parent, Title);
            else SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetPositionAndRotation(new Vector3(all.center.x, all.min.y, all.center.z), Quaternion.identity);
            root.transform.SetSiblingIndex(model.transform.GetSiblingIndex());
            Undo.SetTransformParent(model.transform, root.transform, Title);
            Undo.AddComponent<NetworkIdentity>(root);
            door = Undo.AddComponent<SlidingDoor>(root);
        }

        var parts = new List<SlidingDoor.Part>();
        var bodyOffsets = new List<Vector3>();
        foreach (Renderer b in bodies)
        {
            Vector3 centre = b.bounds.center;
            float side = Vector3.Dot(centre - all.center, axis);
            float dir = bodies.Count == 1 || Mathf.Abs(side) < 0.01f ? 1f : Mathf.Sign(side);
            Vector3 size = b.bounds.size;
            float width = Mathf.Abs(size.x * axis.x) + Mathf.Abs(size.z * axis.z);
            Vector3 offset = axis * dir * width * 0.95f;
            bodyOffsets.Add(offset);
            parts.Add(new SlidingDoor.Part { part = b.transform, closedLocal = b.transform.localPosition, openOffset = offset });
            if (b.GetComponent<Collider>() == null)
            {
                BoxCollider box = Undo.AddComponent<BoxCollider>(b.gameObject);
                box.isTrigger = false;
            }
        }
        foreach (Renderer h in handles)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < bodies.Count; i++)
            {
                float d = (bodies[i].bounds.center - h.bounds.center).sqrMagnitude;
                if (d < best) { best = d; nearest = i; }
            }
            parts.Add(new SlidingDoor.Part { part = h.transform, closedLocal = h.transform.localPosition, openOffset = bodyOffsets[nearest] });
        }

        Undo.RecordObject(door, Title);
        door.EditorSetup(parts.ToArray());
        EditorUtility.SetDirty(door);

        int animators = 0;
        foreach (Animator a in model.GetComponentsInChildren<Animator>(true))
        {
            if (!a.enabled) continue;
            Undo.RecordObject(a, Title);
            a.enabled = false;
            animators++;
        }
        log.AppendLine($"+ {Path(model.transform)}: {bodies.Count} leaf / leaves, {handles.Count} handle(s)" +
                       (animators > 0 ? $", old Animator off" : ""));
        return true;
    }

    /// <summary>Flat direction the leaves slide along: across the two halves, or the width of one leaf.</summary>
    private static Vector3 SlideAxis(List<Renderer> bodies)
    {
        if (bodies.Count >= 2)
        {
            float best = -1f;
            Vector3 axis = Vector3.right;
            for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Vector3 d = bodies[j].bounds.center - bodies[i].bounds.center;
                    d.y = 0f;
                    if (d.sqrMagnitude > best) { best = d.sqrMagnitude; axis = d; }
                }
            if (best > 0.0001f) return Snap(axis.normalized);
        }
        Vector3 s = bodies[0].bounds.size;
        return s.x >= s.z ? Vector3.right : Vector3.forward;
    }

    /// <summary>Doors stand along X or Z: snap a nearly straight direction to that axis.</summary>
    private static Vector3 Snap(Vector3 v)
    {
        if (Mathf.Abs(v.x) > 0.9f) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
        if (Mathf.Abs(v.z) > 0.9f) return new Vector3(0f, 0f, Mathf.Sign(v.z));
        return v;
    }

    private static Bounds BoundsOf(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        for (Transform q = t.parent; q != null; q = q.parent) p = q.name + "/" + p;
        return p;
    }
}
