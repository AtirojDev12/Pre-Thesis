using System;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// 23 Nov 2026 (Mr.k): ONE walking NPC for the map rules and map events
/// (film ghost, toilet ghosts, VIP guest, flashlight hunter, foreign things).
///
/// A scene object (made by Tools > Pre-Thesis > One Step > Map Rules), hidden until a
/// controller calls it, so nothing has to be registered as a spawnable prefab.
/// The body is the NPC ghost model (CustomerModels.asset, Ghost Models list):
///   female = the "F..." model, male = the first "M..." model.
///
/// What a controller can ask (SERVER): appear / hide, walk to a point, roam the whole
/// map, follow a player, stand still, say a line (speech bubble), carry a flashlight,
/// be pressed with E (prompt), be hit with the Bat or the mop, and report who looks at it.
///
/// SERVER AUTHORITY: movement runs on the server only. Clients get position + facing
/// through SyncVars and smooth it. Look reports and hits come from each player's own PC
/// and are checked again on the server (distance).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapWalker : InteractableBase
{
    // 23 Nov: Guard = the flashlight hunter (security guard ghost), Vip = the VIP guest ghost.
    // Both are MOCKUP bodies (MapMockups), not the NPC ghost models.
    public enum Body : byte { None = 0, Female = 1, Male = 2, AnyGhost = 3, Guard = 4, Vip = 5 }

    [Tooltip("Which controller uses this walker (set by the setup tool). Example: VIP, Hunter, Foreign.")]
    [SerializeField] private string role = "";
    [SerializeField, Min(0.1f)] private float defaultSpeed = 2.2f;

    // Server writes, everyone reads.
    [SyncVar(hook = nameof(OnOutChanged))] private bool isOut;
    [SyncVar] private Vector3 netPosition;
    [SyncVar] private float netYaw;
    [SyncVar(hook = nameof(OnBodyChanged))] private Body body;
    [SyncVar(hook = nameof(OnSeedChanged))] private uint modelSeed; // 23 Nov: AnyGhost with this seed (0 = its netId)
    [SyncVar] private string bubble = "";
    [SyncVar] private bool lightOn;
    [SyncVar] private bool interactable;
    [SyncVar] private string prompt = "";
    [SyncVar] private bool hittable;
    [SyncVar] private float lookAngle;      // 0 = nobody reports looking at it
    [SyncVar] private float lookDistance = 20f;
    [SyncVar(hook = nameof(OnSeatedChanged))] private bool seatedLook; // 23 Nov: mockup "Seated" pose (VIP)

    public static readonly List<MapWalker> All = new List<MapWalker>();

    /// <summary>SERVER. Someone pressed E on it (while it allows that).</summary>
    public event Action<MapWalker, GameObject> ServerInteracted;
    /// <summary>SERVER. Someone hit it with the Bat or the mop (while it allows that).</summary>
    public event Action<MapWalker, GameObject> ServerHit;

    public string Role => role;
    public bool IsOut => isOut;
    public bool Reserved { get; set; }

    private enum Mode { Idle, GoTo, Roam, Follow }

    // Server only.
    private Mode mode;
    private float speed;
    private Vector3 goal;
    private Transform follow;
    private float followStop = 1.6f;
    private float nextPath, nextSync, nextPick;
    private NavMeshPath path;
    private Vector3[] corners = new Vector3[16];
    private int cornerCount, cornerIndex;
    private readonly Dictionary<uint, double> lookingSince = new Dictionary<uint, double>();

    // Client only.
    private CustomerModelVisual visual;
    private GameObject fallback;
    private GameObject mockup;   // 23 Nov: Guard / VIP mockup body (also kept in fallback)
    private Transform bobPart;
    private Body builtBody = (Body)255;
    private uint builtSeed;
    private Light torch;
    private Canvas bubbleCanvas;
    private TMP_Text bubbleText;
    private Vector3 lastPos;
    private bool walkingShown;
    private float lookTimer;
    private bool lookSent;
    private float lookSentAt = -99f;
    private CapsuleCollider pressCollider;

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    private const double LookReportLife = 1.2;

    // ---- Lifetime ------------------------------------------------------------------------

    protected override void Awake()
    {
        base.Awake();
        path = new NavMeshPath();
        speed = defaultSpeed;
        pressCollider = GetComponent<CapsuleCollider>();
        if (pressCollider == null)
        {
            pressCollider = gameObject.AddComponent<CapsuleCollider>();
            pressCollider.radius = 0.35f;
            pressCollider.height = 1.8f;
            pressCollider.center = new Vector3(0f, 0.9f, 0f);
        }
        pressCollider.enabled = false;
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (isOut) transform.SetPositionAndRotation(netPosition, Quaternion.Euler(0f, netYaw, 0f));
        ApplyLook();
    }

    /// <summary>SERVER. A free walker of this role (not out, not taken), or null.</summary>
    public static MapWalker ServerTake(string role)
    {
        foreach (MapWalker w in All)
            if (w != null && w.role == role && !w.Reserved && !w.isOut && NetworkMode.HasServerAuthority(w))
            {
                w.Reserved = true;
                return w;
            }
        return null;
    }

    /// <summary>Every walker of this role (for counting / the setup tool).</summary>
    public static int Count(string role)
    {
        int n = 0;
        foreach (MapWalker w in All) if (w != null && w.role == role) n++;
        return n;
    }

    // ---- Server API -----------------------------------------------------------------------

    /// <summary>SERVER. Appear standing at a point (moved onto the NavMesh when one is near).</summary>
    public void ServerAppear(Vector3 at, Body look, float yaw = 0f)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        if (NavMesh.SamplePosition(at, out NavMeshHit hit, 6f, NavMesh.AllAreas)) at = hit.position;
        transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        netPosition = at;
        netYaw = yaw;
        body = look;
        modelSeed = 0;
        seatedLook = false;
        mode = Mode.Idle;
        follow = null;
        speed = defaultSpeed;
        cornerCount = 0;
        isOut = true;
        ApplyLook();
    }

    /// <summary>SERVER. Go away (hidden, free for its controller again).</summary>
    public void ServerHide()
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        isOut = false;
        bubble = "";
        lightOn = false;
        interactable = false;
        prompt = "";
        hittable = false;
        lookAngle = 0f;
        seatedLook = false;
        mode = Mode.Idle;
        follow = null;
        lookingSince.Clear();
        Reserved = false;
        ApplyLook();
    }

    /// <summary>SERVER. Stand exactly here, facing <paramref name="yaw"/> (no NavMesh snap: a seat, a booth).</summary>
    public void ServerPlaceAt(Vector3 at, float yaw)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        mode = Mode.Idle;
        follow = null;
        transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        netPosition = at;
        netYaw = yaw;
    }

    /// <summary>SERVER. 23 Nov: this exact ghost model (AnyGhost body), e.g. the same one as an audience member.</summary>
    public void ServerSetModelSeed(uint seed)
    {
        if (!NetworkMode.HasServerAuthority(this) || modelSeed == seed) return;
        modelSeed = seed;
        ApplyLook();
    }

    /// <summary>SERVER. 23 Nov: the mockup body sits (VIP on its seat) or stands.</summary>
    public void ServerSetSeated(bool seated)
    {
        if (!NetworkMode.HasServerAuthority(this) || seatedLook == seated) return;
        seatedLook = seated;
        MapMockups.SetSeated(mockup, seated);
    }

    public void ServerSetSpeed(float metresPerSecond) => speed = Mathf.Max(0.1f, metresPerSecond);
    public void ServerStop() { mode = Mode.Idle; follow = null; }
    public void ServerGoTo(Vector3 point) { mode = Mode.GoTo; goal = point; follow = null; nextPath = 0f; }
    public void ServerRoam() { mode = Mode.Roam; follow = null; goal = MapNav.RandomPoint(transform.position); nextPath = 0f; }
    public void ServerFollow(Transform target, float stopDistance) { mode = Mode.Follow; follow = target; followStop = stopDistance; nextPath = 0f; }
    public void ServerSay(string line) => bubble = line ?? "";
    public void ServerSetLight(bool on) => lightOn = on;
    public void ServerSetPress(string text) { prompt = text ?? ""; interactable = !string.IsNullOrEmpty(text); }
    public void ServerSetHittable(bool on) => hittable = on;
    public void ServerSetLookReports(float angle, float distance) { lookAngle = Mathf.Max(0f, angle); lookDistance = Mathf.Max(1f, distance); }

    /// <summary>SERVER. Reached the GoTo point (flat distance).</summary>
    public bool ServerArrived(float within = 0.6f) => FlatDistance(transform.position, goal) <= within;

    /// <summary>Head height point (for looks, beams and bubbles).</summary>
    public Vector3 Head => transform.position + Vector3.up * 1.6f;

    /// <summary>SERVER. Did this player report "I am looking at it" just now?</summary>
    public bool ServerIsLooking(PlayerHealth player)
    {
        if (player == null) return false;
        uint id = NetworkMode.IsOffline ? 1u : player.netId;
        return lookingSince.TryGetValue(id, out double at) && Now - at <= LookReportLife;
    }

    // ---- E press (InteractableBase) -----------------------------------------------------

    public override string GetInteractionPrompt() => prompt;
    public override bool CanInteract() => isOut && interactable && base.CanInteract();

    protected override void OnInteracted(GameObject interactor)
    {
        if (isOut && interactable) ServerInteracted?.Invoke(this, interactor);
    }

    // ---- Every frame ------------------------------------------------------------------------

    private void Update()
    {
        bool server = NetworkMode.HasServerAuthority(this);
        if (server && isOut) ServerTick();
        if (!server && isOut) ClientSmooth();
        if (isOut) LocalTick();
        bool press = isOut && interactable;
        if (pressCollider != null && pressCollider.enabled != press) pressCollider.enabled = press;
    }

    private void ServerTick()
    {
        switch (mode)
        {
            case Mode.GoTo: Move(goal); break;
            case Mode.Roam:
                if (FlatDistance(transform.position, goal) < 1f && Time.time >= nextPick)
                {
                    nextPick = Time.time + 0.5f; // a failed pick (stays put) is not retried every frame
                    goal = MapNav.RandomPoint(transform.position);
                }
                Move(goal);
                break;
            case Mode.Follow:
                if (follow == null) { mode = Mode.Idle; break; }
                if (FlatDistance(transform.position, follow.position) > followStop) Move(follow.position);
                else Face(follow.position);
                break;
        }
        if (Time.time >= nextSync)
        {
            nextSync = Time.time + 0.05f;
            netPosition = transform.position;
            netYaw = transform.eulerAngles.y;
        }
    }

    private void Move(Vector3 to)
    {
        // Follow the NavMesh when there is one (no walking through walls), else straight.
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 0.4f;
            cornerCount = 0;
            cornerIndex = 0;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(to, out NavMeshHit end, 3f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(from.position, end.position, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid)
            {
                cornerCount = path.GetCornersNonAlloc(corners);
                if (cornerCount == corners.Length) { corners = new Vector3[corners.Length * 2]; cornerCount = path.GetCornersNonAlloc(corners); }
                cornerIndex = cornerCount > 1 ? 1 : 0;
            }
        }
        Vector3 next = to;
        if (cornerCount > 0)
        {
            while (cornerIndex < cornerCount - 1 && FlatDistance(transform.position, corners[cornerIndex]) < 0.3f) cornerIndex++;
            next = corners[Mathf.Clamp(cornerIndex, 0, cornerCount - 1)];
        }
        Vector3 step = Vector3.MoveTowards(transform.position, next, speed * Time.deltaTime);
        // 23 Nov fix: always on the walkable floor. Before, a ghost with no path (or between two far path
        // corners on stairs) walked in a straight line through the air.
        if (NavMesh.SamplePosition(step, out NavMeshHit onFloor, 1.2f, NavMesh.AllAreas)) step = onFloor.position;
        else if (cornerCount == 0) return; // off the NavMesh and no path: do not float away
        Face(step);
        transform.position = step;
    }

    private void Face(Vector3 point)
    {
        Vector3 dir = point - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void ClientSmooth()
    {
        transform.position = Vector3.Lerp(transform.position, netPosition, 1f - Mathf.Exp(-12f * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    // ---- Local (every PC): looks, walk animation, bubble, light, hits ---------------------------

    private void LocalTick()
    {
        // Walk animation from how far the body moved.
        float moved = FlatDistance(transform.position, lastPos) / Mathf.Max(0.0001f, Time.deltaTime);
        lastPos = transform.position;
        bool walking = moved > 0.3f;
        if (walking != walkingShown)
        {
            walkingShown = walking;
            if (visual != null) visual.SetWalking(walking);
        }
        // 23 Nov: the mockup bodies have no animation: a small bob while walking.
        if (bobPart != null)
        {
            float y = walking && !seatedLook ? Mathf.Abs(Mathf.Sin(Time.time * 7f)) * 0.035f : 0f;
            Vector3 lp = bobPart.localPosition;
            lp.y = Mathf.Lerp(lp.y, y, 1f - Mathf.Exp(-20f * Time.deltaTime));
            bobPart.localPosition = lp;
        }

        if (torch != null && torch.enabled != lightOn) torch.enabled = lightOn;
        UpdateBubble();

        if (lookAngle > 0f)
        {
            lookTimer -= Time.deltaTime;
            if (lookTimer <= 0f)
            {
                lookTimer = 0.15f;
                ReportLooking(LocalLooks());
            }
            else if (lookSent) ReportLooking(true); // keep the "yes" alive
        }
        else if (lookSent) ReportLooking(false);

        if (hittable) LocalHitCheck();
    }

    private bool LocalLooks()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || me.IsDead) return false;
        Camera cam = RuleFx.LocalCamera();
        Transform model = visual != null ? visual.transform : fallback != null ? fallback.transform : transform;
        return RuleFx.IsLookingAt(cam != null ? cam.transform : null, Head, lookAngle, lookDistance, model, me.transform);
    }

    private void ReportLooking(bool looking)
    {
        if (looking == lookSent && (!looking || Time.unscaledTime - lookSentAt < 0.5f)) return;
        lookSent = looking;
        lookSentAt = Time.unscaledTime;
        if (NetworkMode.IsOffline) ServerSetLooking(1u, looking);
        else if (NetworkClient.isConnected) CmdLooking(looking);
    }

    [Command(requiresAuthority = false, channel = Channels.Unreliable)]
    private void CmdLooking(bool looking, NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) ServerSetLooking(sender.identity.netId, looking);
    }

    private void ServerSetLooking(uint id, bool looking)
    {
        if (looking) lookingSince[id] = Now;
        else lookingSince.Remove(id);
    }

    /// <summary>LOCAL. Left Click with the Bat in hand (or the mop) while close and aiming at it.</summary>
    private void LocalHitCheck()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (GameplayInput.Blocked || Cursor.lockState != CursorLockMode.Locked) return;
        if (!LocalHoldsHitter()) return;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || me.IsDead || me.IsDowned) return;
        Camera cam = RuleFx.LocalCamera();
        if (cam == null) return;
        Vector3 chest = transform.position + Vector3.up * 1.1f;
        Vector3 to = chest - cam.transform.position;
        if (to.magnitude > MapRuleTuning.Current.hitReach || Vector3.Angle(cam.transform.forward, to) > 30f) return;
        if (NetworkMode.IsOffline) ServerTakeHit(me.gameObject);
        else if (NetworkClient.isConnected) CmdHit();
        RuleFx.Toast(MapRuleTuning.Current.hitLine, 1.2f);
    }

    /// <summary>LOCAL. The Bat is the selected hotbar item, or the mop is in your hands.</summary>
    public static bool LocalHoldsHitter()
    {
        PlayerInventory inventory = PlayerInventory.Local;
        bool bat = inventory != null && inventory.HeldSlot.itemId == ItemCatalog.Bat;
        return bat || CarryProp.LocalHolds(CarryProp.Mop);
    }

    [Command(requiresAuthority = false)]
    private void CmdHit(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) ServerTakeHit(sender.identity.gameObject);
    }

    private void ServerTakeHit(GameObject player)
    {
        if (!isOut || !hittable || player == null) return;
        if ((player.transform.position - transform.position).sqrMagnitude > Mathf.Pow(MapRuleTuning.Current.hitReach + 1.5f, 2f)) return;
        ServerHit?.Invoke(this, player);
    }

    // ---- Look: body, light, bubble ----------------------------------------------------------

    private void OnOutChanged(bool oldValue, bool newValue)
    {
        if (newValue) { transform.position = netPosition; lastPos = netPosition; }
        ApplyLook();
    }

    private void OnBodyChanged(Body oldValue, Body newValue) => ApplyLook();
    private void OnSeedChanged(uint oldValue, uint newValue) => ApplyLook();
    private void OnSeatedChanged(bool oldValue, bool newValue) => MapMockups.SetSeated(mockup, newValue);

    private void ApplyLook()
    {
        if (isOut && (builtBody != body || builtSeed != modelSeed)) BuildBody();
        MapMockups.SetSeated(mockup, seatedLook);
        if (visual != null && visual.gameObject.activeSelf != isOut) visual.gameObject.SetActive(isOut);
        if (fallback != null && fallback.activeSelf != isOut) fallback.SetActive(isOut);
        if (torch == null) MakeTorch();
        torch.enabled = isOut && lightOn;
        if (!isOut && bubbleCanvas != null) bubbleCanvas.gameObject.SetActive(false);
    }

    private void BuildBody()
    {
        builtBody = body;
        builtSeed = modelSeed;
        if (torch != null && torch.transform.parent != transform)
        {
            // 23 Nov: the torch sat in the old mockup's hand: back on the walker before that body goes.
            torch.transform.SetParent(transform, false);
            torch.transform.localPosition = new Vector3(0.25f, 1.35f, 0.3f);
            torch.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        }
        if (visual != null) Destroy(visual.gameObject);
        if (fallback != null) Destroy(fallback);
        visual = null;
        fallback = null;
        mockup = null;
        bobPart = null;
        if (body == Body.None) return;

        // 23 Nov: Guard / VIP = mockup bodies (feet at the root, no capsule-centre offset).
        if (MapMockups.Handles(body))
        {
            mockup = MapMockups.Build(body, transform);
            fallback = mockup;
            bobPart = MapMockups.BobPart(mockup);
            MapMockups.SetSeated(mockup, seatedLook);
            Transform lens = mockup.transform.Find(MapMockups.LensName);
            if (lens == null) lens = mockup.transform.Find(MapMockups.StandingName + "/" + MapMockups.LensName);
            if (lens != null)
            {
                if (torch == null) MakeTorch();
                torch.transform.SetParent(lens.parent, true);
                torch.transform.position = lens.position;
                torch.transform.rotation = Quaternion.LookRotation(lens.up, Vector3.up); // the lens cylinder points along its up
            }
            return;
        }

        uint seed = body == Body.Female ? GhostSeed(true) : body == Body.Male ? GhostSeed(false)
            : modelSeed != 0 ? modelSeed : (netId != 0 ? netId : 7u);
        visual = CustomerModelVisual.TryCreate("Body (" + body + ")", transform.position + Vector3.up, transform.rotation,
            seed, false, 0.25f, false, true);
        if (visual != null)
        {
            visual.transform.SetParent(transform, true);
            visual.transform.localPosition = Vector3.up; // capsule-centre convention of the customer bodies
            visual.transform.localRotation = Quaternion.identity;
            return;
        }
        // No ghost model set up: a dark capsule.
        fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        fallback.name = "Body (capsule)";
        Destroy(fallback.GetComponent<Collider>());
        fallback.transform.SetParent(transform, false);
        fallback.transform.localPosition = Vector3.up;
        Renderer r = fallback.GetComponent<Renderer>();
        if (r != null) r.material.color = new Color(0.12f, 0.12f, 0.14f);
    }

    private static readonly Dictionary<bool, uint> seeds = new Dictionary<bool, uint>();

    /// <summary>A seed whose ghost model is female ("F...") or male ("M..."). Same on every PC.</summary>
    private static uint GhostSeed(bool female)
    {
        if (seeds.TryGetValue(female, out uint known)) return known;
        uint pick = 1;
        CustomerModelLibrary library = CustomerModelLibrary.Instance;
        if (library != null)
            for (uint s = 1; s < 400; s++)
            {
                if (!library.TryPick(s, true, out CustomerModelLibrary.Entry e) || e.model == null) continue;
                bool isFemale = e.model.name.StartsWith("F", StringComparison.OrdinalIgnoreCase);
                if (isFemale == female) { pick = s; break; }
            }
        seeds[female] = pick;
        return pick;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        seeds.Clear();
    }

    private void MakeTorch()
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0.25f, 1.35f, 0.3f);
        go.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        torch = go.AddComponent<Light>();
        torch.type = LightType.Spot;
        MapRuleTuning t = MapRuleTuning.Current;
        torch.range = t.hunterBeamRange + 2f;
        torch.spotAngle = t.hunterBeamAngle * 2f;
        torch.intensity = t.hunterLightIntensity;
        torch.color = new Color(1f, 0.95f, 0.8f);
        torch.shadows = LightShadows.Soft;
        torch.enabled = false;
    }

    private void UpdateBubble()
    {
        bool show = !string.IsNullOrEmpty(bubble);
        if (!show) { if (bubbleCanvas != null && bubbleCanvas.gameObject.activeSelf) bubbleCanvas.gameObject.SetActive(false); return; }
        if (bubbleCanvas == null) MakeBubble();
        if (!bubbleCanvas.gameObject.activeSelf) bubbleCanvas.gameObject.SetActive(true);
        if (bubbleText.text != bubble) bubbleText.text = bubble;
        Transform anchor = visual != null ? visual.LabelAnchor : null;
        bubbleCanvas.transform.position = anchor != null ? anchor.position : transform.position + Vector3.up * 2.2f;
        Camera cam = RuleFx.LocalCamera();
        if (cam != null) bubbleCanvas.transform.rotation = Quaternion.LookRotation(bubbleCanvas.transform.position - cam.transform.position, Vector3.up);
    }

    private void MakeBubble()
    {
        var go = new GameObject("Speech Bubble", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        bubbleCanvas = go.AddComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(420f, 110f);
        rect.localScale = Vector3.one * 0.0045f;
        var bg = new GameObject("Back", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
        bg.transform.SetParent(go.transform, false);
        bg.color = new Color(0.05f, 0.03f, 0.03f, 0.82f);
        var bgRect = bg.rectTransform;
        bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one; bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        bubbleText = textGo.AddComponent<TextMeshProUGUI>();
        bubbleText.fontSize = 34f;
        bubbleText.alignment = TextAlignmentOptions.Center;
        bubbleText.color = new Color(1f, 0.9f, 0.85f);
        bubbleText.textWrappingMode = TextWrappingModes.Normal;
        var tr = bubbleText.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(14f, 8f); tr.offsetMax = new Vector2(-14f, -8f);
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetRole(string value) => role = value;
}

/// <summary>
/// 23 Nov 2026 (Mr.k): random walkable points on the map's NavMesh (cached per scene).
/// Fix (23 Nov, clip): ONLY points INSIDE the cinema building. Before, ghosts walked out to the
/// street. A point counts as inside when:
///   1. something is above it (roof / ceiling, up to 15 m), and
///   2. walls are around it (at least 3 of 4 flat rays hit something within 40 m;
///      this drops the entrance canopy outside the front door), and
///   3. the walker can really walk there from where it is (complete NavMesh path;
///      this drops table tops and other islands).
/// </summary>
public static class MapNav
{
    private const float RoofHeight = 15f;
    private const float WallReach = 40f;
    private const int Tries = 24;

    private static Vector3[] vertices;
    private static int[] indices;
    private static float[] cumulative;
    private static UnityEngine.SceneManagement.Scene builtFor;
    private static NavMeshPath testPath;
    private static readonly Vector3[] flat = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { vertices = null; indices = null; cumulative = null; builtFor = default; testPath = null; }

    /// <summary>A random point INSIDE the building that a walker at <paramref name="near"/> can walk to.</summary>
    public static Vector3 RandomPoint(Vector3 near)
    {
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (vertices == null || scene != builtFor) Build(scene);
        if (testPath == null) testPath = new NavMeshPath();
        bool haveFrom = NavMesh.SamplePosition(near, out NavMeshHit from, 2f, NavMesh.AllAreas);
        if (!haveFrom || !Inside(from.position))
        {
            // Asked from outside / off the NavMesh (a controller object): test reachability from a task spot.
            Vector3 anchor = Anchor();
            haveFrom = NavMesh.SamplePosition(anchor, out from, 3f, NavMesh.AllAreas);
        }
        bool table = cumulative != null && cumulative.Length > 0 && cumulative[cumulative.Length - 1] > 0f;
        for (int t = 0; t < Tries; t++)
        {
            Vector3 p;
            if (table) p = PickOnTable();
            else
            {
                Vector3 q = near + new Vector3(UnityEngine.Random.Range(-12f, 12f), 0f, UnityEngine.Random.Range(-12f, 12f));
                if (!NavMesh.SamplePosition(q, out NavMeshHit h, 4f, NavMesh.AllAreas)) continue;
                p = h.position;
            }
            if (!Inside(p)) continue;
            if (haveFrom && !(NavMesh.CalculatePath(from.position, p, NavMesh.AllAreas, testPath)
                              && testPath.status == NavMeshPathStatus.PathComplete)) continue;
            return p;
        }
        return haveFrom ? from.position : near; // nothing good: stay put (never walk outside)
    }

    /// <summary>A walkable point surely inside: the first task spot that passes <see cref="Inside"/>.</summary>
    private static Vector3 Anchor()
    {
        foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
            if (spot != null && NavMesh.SamplePosition(spot.transform.position, out NavMeshHit h, 3f, NavMesh.AllAreas) && Inside(h.position))
                return h.position;
        return Vector3.zero;
    }

    /// <summary>True when the point is inside the building (roof above + walls around).</summary>
    public static bool Inside(Vector3 point)
    {
        Vector3 up = point + Vector3.up * 0.3f;
        if (!Physics.Raycast(up, Vector3.up, RoofHeight, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        Vector3 chest = point + Vector3.up * 1.2f;
        int walls = 0;
        for (int i = 0; i < flat.Length; i++)
            if (Physics.Raycast(chest, flat[i], WallReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) walls++;
        return walls >= 3;
    }

    private static Vector3 PickOnTable()
    {
        float r = UnityEngine.Random.value * cumulative[cumulative.Length - 1];
        int lo = 0, hi = cumulative.Length - 1;
        while (lo < hi) { int mid = (lo + hi) / 2; if (cumulative[mid] < r) lo = mid + 1; else hi = mid; }
        Vector3 a = vertices[indices[lo * 3]], b = vertices[indices[lo * 3 + 1]], c = vertices[indices[lo * 3 + 2]];
        float u = UnityEngine.Random.value, v = UnityEngine.Random.value;
        if (u + v > 1f) { u = 1f - u; v = 1f - v; }
        return a + (b - a) * u + (c - a) * v;
    }

    private static void Build(UnityEngine.SceneManagement.Scene scene)
    {
        builtFor = scene;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        vertices = tri.vertices;
        indices = tri.indices;
        int count = indices.Length / 3;
        cumulative = new float[count];
        float total = 0f, all = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = vertices[indices[i * 3]], b = vertices[indices[i * 3 + 1]], c = vertices[indices[i * 3 + 2]];
            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            all += area;
            // Outside triangles (street, canopy, roof top) never get picked.
            if (Inside((a + b + c) / 3f)) total += area;
            cumulative[i] = total;
        }
        Debug.Log($"[MapNav] Walk area inside the building: {total:0} of {all:0} m2 ({count} triangles).");
    }
}
