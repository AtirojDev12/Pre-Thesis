#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 31 Oct 2026 (Mr.k): DEV X-RAY (F1 > X-ray). Shows players, ghosts, objectives and
/// items THROUGH WALLS: a coloured see-through highlight on the object + a name and
/// distance label. Each kind can be switched on/off in the F1 panel.
///
/// This PC only, purely visual: it reads what this machine already knows (synced
/// positions) and changes nothing in the game. Editor + Development Builds only.
///
/// Highlight = the object's own meshes drawn again with a "draw on top" material.
/// Objects with no mesh get a small marker cube. Hidden objects (renderer off,
/// e.g. a ghost that has not come yet) are not shown.
/// </summary>
public sealed class DevXRayView : MonoBehaviour
{
    public enum Kind { Player, Ghost, Chore, Exit, TaskBoard, RuleObject, Customer, Item, GhostPart, MapBox }

    public static readonly Kind[] AllKinds =
        { Kind.Player, Kind.Ghost, Kind.Chore, Kind.Exit, Kind.TaskBoard, Kind.RuleObject, Kind.Customer, Kind.Item, Kind.GhostPart, Kind.MapBox };

    // ---- Settings (remembered on this PC) ----------------------------------------------

    private static bool enabledFlag;
    private static bool labels = true;
    private static readonly bool[] shown = { true, true, true, true, true, true, true, true, true, true };
    private static bool loaded;

    /// <summary>X-ray on/off (master switch).</summary>
    public static bool Enabled
    {
        get { Load(); return enabledFlag; }
        set { Load(); enabledFlag = value; Save(); if (value) Ensure(); }
    }

    public static bool Labels
    {
        get { Load(); return labels; }
        set { Load(); labels = value; Save(); }
    }

    public static bool IsShown(Kind kind) { Load(); return shown[(int)kind]; }

    public static void SetShown(Kind kind, bool on) { Load(); shown[(int)kind] = on; Save(); }

    public static string KindName(Kind kind)
    {
        switch (kind)
        {
            case Kind.Player: return "Players";
            case Kind.Ghost: return "Ghosts";
            case Kind.Chore: return "Chores (needed)";
            case Kind.Exit: return "Exits";
            case Kind.TaskBoard: return "Task boards";
            case Kind.RuleObject: return "Rule objects";
            case Kind.Customer: return "Customers";
            case Kind.Item: return "Items";
            case Kind.GhostPart: return "Ghost body parts";
            default: return "Map box";
        }
    }

    public static Color KindColor(Kind kind)
    {
        switch (kind)
        {
            case Kind.Player: return new Color(0.3f, 1f, 0.4f);
            case Kind.Ghost: return new Color(1f, 0.2f, 0.2f);
            case Kind.Chore: return new Color(1f, 0.9f, 0.2f);
            case Kind.Exit: return new Color(0.2f, 0.95f, 1f);
            case Kind.TaskBoard: return new Color(1f, 0.6f, 0.15f);
            case Kind.RuleObject: return new Color(0.75f, 0.4f, 1f);
            case Kind.Customer: return new Color(0.95f, 0.95f, 0.95f);
            case Kind.Item: return new Color(0.45f, 0.7f, 1f);
            case Kind.GhostPart: return new Color(1f, 0.35f, 0.85f);
            default: return new Color(1f, 0.8f, 0.3f);
        }
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            enabledFlag = PlayerPrefs.GetInt("DevXRay.on", 0) == 1;
            labels = PlayerPrefs.GetInt("DevXRay.labels", 1) == 1;
            for (int i = 0; i < shown.Length; i++) shown[i] = PlayerPrefs.GetInt("DevXRay.k" + i, 1) == 1;
        }
        catch { /* no prefs: defaults */ }
        if (enabledFlag) Ensure();
    }

    private static void Save()
    {
        try
        {
            PlayerPrefs.SetInt("DevXRay.on", enabledFlag ? 1 : 0);
            PlayerPrefs.SetInt("DevXRay.labels", labels ? 1 : 0);
            for (int i = 0; i < shown.Length; i++) PlayerPrefs.SetInt("DevXRay.k" + i, shown[i] ? 1 : 0);
        }
        catch { /* ignore */ }
    }

    // ---- The one object that draws ------------------------------------------------------

    private static DevXRayView instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        loaded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart() => Load();

    private static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("Dev X-Ray View");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevXRayView>();
    }

    private sealed class Target
    {
        public Component owner;
        public Kind kind;
        public Renderer[] renderers;
        public bool drawn;
        public Vector3 anchor;
    }

    private readonly List<Target> targets = new List<Target>();
    private readonly Dictionary<SkinnedMeshRenderer, Mesh> baked = new Dictionary<SkinnedMeshRenderer, Mesh>();
    private readonly List<Renderer> scratch = new List<Renderer>();
    private Material material;
    private MaterialPropertyBlock block;
    private Mesh cube;
    private Camera[] cameras = new Camera[8];
    private Camera viewCamera;
    private float nextScan;
    private GUIStyle labelStyle;

    private void Awake()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader != null)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // through walls
            material.renderQueue = 4000;
        }
        else Debug.LogWarning("[DevXRay] Highlight shader not found: labels only.");
        block = new MaterialPropertyBlock();
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
        foreach (Mesh m in baked.Values) if (m != null) Destroy(m);
        baked.Clear();
    }

    private void Update()
    {
        if (!Enabled) return;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            Scan();
        }
        viewCamera = ViewCamera();
        foreach (Target t in targets) Draw(t);
    }

    // ---- Finding things (twice a second) ---------------------------------------------------

    private void Scan()
    {
        targets.Clear();
        PlayerHealth me = PlayerHealth.LocalInstance;
        foreach (PlayerHealth p in PlayerRegistry.All) if (p != null && p != me) Add(p, Kind.Player);

        foreach (Enemy_Abstract_Class g in FindObjectsByType<Enemy_Abstract_Class>()) Add(g, Kind.Ghost);
        foreach (RuleGhost g in FindObjectsByType<RuleGhost>()) Add(g, Kind.Ghost);
        foreach (GhostAudience g in FindObjectsByType<GhostAudience>()) Add(g, Kind.Ghost);
        foreach (GhostManifestation g in FindObjectsByType<GhostManifestation>()) Add(g, Kind.Ghost);
        foreach (MissingPartsGhost g in FindObjectsByType<MissingPartsGhost>()) Add(g, Kind.Ghost);

        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>()) Add(c, Kind.Chore);
        foreach (ExitPoint e in FindObjectsByType<ExitPoint>()) Add(e, Kind.Exit);
        foreach (ZoneTaskList b in ZoneTaskList.All) if (b != null) Add(b, Kind.TaskBoard);
        foreach (RuleInteractable r in FindObjectsByType<RuleInteractable>()) Add(r, Kind.RuleObject);
        foreach (BlackBoxCarry r in FindObjectsByType<BlackBoxCarry>()) Add(r, Kind.RuleObject);

        foreach (PopcornCustomer c in FindObjectsByType<PopcornCustomer>()) Add(c, Kind.Customer);
        AddByName("Human Ticket Customer", Kind.Customer);
        AddByName("Ghost Ticket Customer", Kind.Customer);

        foreach (WorldInventoryItem i in FindObjectsByType<WorldInventoryItem>())
            if (i != null) Add(i, ItemCatalog.IsGhostPart(i.ItemId) ? Kind.GhostPart : Kind.Item);
        foreach (MapPickup m in FindObjectsByType<MapPickup>()) Add(m, Kind.MapBox);

        // Forget baked meshes of bodies that are gone.
        var gone = new List<SkinnedMeshRenderer>();
        foreach (var pair in baked) if (pair.Key == null) gone.Add(pair.Key);
        foreach (SkinnedMeshRenderer key in gone) { Destroy(baked[key]); baked.Remove(key); }
    }

    private void AddByName(string objectName, Kind kind)
    {
        GameObject go = GameObject.Find(objectName);
        if (go != null) Add(go.transform, kind);
    }

    private void Add(Component owner, Kind kind)
    {
        if (owner == null) return;
        scratch.Clear();
        foreach (Renderer r in owner.GetComponentsInChildren<Renderer>(true))
        {
            if (r is SkinnedMeshRenderer) scratch.Add(r);
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter f) && f.sharedMesh != null) scratch.Add(r);
            if (scratch.Count >= 48) break;
        }
        targets.Add(new Target { owner = owner, kind = kind, renderers = scratch.ToArray() });
    }

    // ---- Drawing the highlight (every frame) ------------------------------------------------

    private bool Relevant(Target t)
    {
        if (t.owner == null || !IsShown(t.kind) || !t.owner.gameObject.activeInHierarchy) return false;
        if (t.owner is ZoneChoreSpot chore) return chore.Needed;
        if (t.owner is MissingPartsGhost ghost) return ghost.IsActive;
        return true;
    }

    private void Draw(Target t)
    {
        t.drawn = false;
        if (!Relevant(t)) return;
        Color color = KindColor(t.kind);
        color.a = 0.42f;
        block.SetColor("_Color", color);

        bool any = false;
        Bounds bounds = new Bounds(t.owner.transform.position, Vector3.zero);
        foreach (Renderer r in t.renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            if (material == null) continue;
            if (r is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) continue;
                if (!baked.TryGetValue(skinned, out Mesh mesh) || mesh == null)
                {
                    mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    baked[skinned] = mesh;
                }
                skinned.BakeMesh(mesh, true);
                Matrix4x4 m = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                for (int s = 0; s < mesh.subMeshCount; s++) Graphics.DrawMesh(mesh, m, material, 0, viewCamera, s, block);
            }
            else
            {
                Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null) continue;
                for (int s = 0; s < mesh.subMeshCount; s++) Graphics.DrawMesh(mesh, r.localToWorldMatrix, material, 0, viewCamera, s, block);
            }
        }

        if (!any)
        {
            // Nothing visible. Objects that never had a mesh get a marker; hidden ones stay hidden.
            if (t.renderers.Length > 0) return;
            Vector3 p = t.owner.transform.position;
            if (material != null && cube != null)
                Graphics.DrawMesh(cube, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 0.5f), material, 0, viewCamera, 0, block);
            bounds = new Bounds(p, Vector3.one * 0.5f);
        }
        t.drawn = true;
        t.anchor = new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z);
    }

    private Camera ViewCamera()
    {
        int count = Camera.allCamerasCount;
        if (cameras.Length < count) cameras = new Camera[count + 4];
        count = Camera.GetAllCameras(cameras);
        Camera best = null;
        for (int i = 0; i < count; i++)
        {
            Camera c = cameras[i];
            if (c == null || !c.isActiveAndEnabled || c.targetTexture != null) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        return best;
    }

    // ---- Labels -----------------------------------------------------------------------------

    private void OnGUI()
    {
        if (!Enabled || !Labels || viewCamera == null || Event.current.type != EventType.Repaint) return;
        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter };
        labelStyle.fontSize = Mathf.RoundToInt(18f * Screen.height / 1080f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.identity;
        Vector3 eye = viewCamera.transform.position;
        foreach (Target t in targets)
        {
            if (!t.drawn || t.owner == null) continue;
            Vector3 s = viewCamera.WorldToScreenPoint(t.anchor);
            if (s.z <= 0f) continue; // behind the camera
            string hex = ColorUtility.ToHtmlStringRGB(KindColor(t.kind));
            float distance = Vector3.Distance(eye, t.anchor);
            string text = $"<color=#{hex}><b>{LabelOf(t)}</b>  {distance:F0} m</color>";
            var rect = new Rect(s.x - 200f, Screen.height - s.y - 14f, 400f, 28f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), "<color=#000000>" + StripColor(text) + "</color>", labelStyle);
            GUI.Label(rect, text, labelStyle);
        }
        GUI.matrix = old;
    }

    private static string StripColor(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, "<color=#[0-9A-Fa-f]+>|</color>", "");

    private static string LabelOf(Target t)
    {
        switch (t.kind)
        {
            case Kind.Player:
                var p = (PlayerHealth)t.owner;
                return PlayerName(p) + (p.IsDead ? " (DEAD)" : p.IsDowned ? " (DOWNED)" : "");
            case Kind.Ghost: return "GHOST: " + t.owner.GetType().Name;
            case Kind.Chore: return "CHORE: " + ((ZoneChoreSpot)t.owner).Kind;
            case Kind.Exit: return "EXIT";
            case Kind.TaskBoard: return "BOARD: " + ((ZoneTaskList)t.owner).Title;
            case Kind.RuleObject: return "RULE: " + t.owner.name;
            case Kind.Customer: return "CUSTOMER";
            case Kind.Item: return ItemCatalog.DisplayName(((WorldInventoryItem)t.owner).ItemId);
            case Kind.GhostPart: return "GHOST PART: " + ItemCatalog.DisplayName(((WorldInventoryItem)t.owner).ItemId);
            default: return "MAP BOX";
        }
    }

    private static string PlayerName(PlayerHealth p)
    {
        MatchDirector match = MatchDirector.Instance;
        if (match != null)
            for (int i = 0; i < match.RoundPlayerCount; i++)
                if (match.RoundPlayerAt(i).netId == p.netId && !string.IsNullOrEmpty(match.RoundPlayerAt(i).name))
                    return match.RoundPlayerAt(i).name;
        return p.name;
    }
}
#endif
