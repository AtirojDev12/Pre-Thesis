using Mirror;
using UnityEngine;

/// <summary>
/// 7 Nov 2026 (Mr.k): a RESTROOM BREAK sign for the Ticket Zone rule "The one who returns".
///
/// There are two (Popcorn seller, Ticket seller).
///
/// 15 Nov 2026 (Mr.k): the signs are ALWAYS in the map. Each one stands on its ORANGE spot
/// (its home) from the start of the round and never vanishes.
///   [E] on the sign = pick it up (it floats in front of you)
///   [R] (discard key) = put it down. Within 2.5 m of its BLUE spot it snaps onto it = PLACED.
///                       Within 2.5 m of its ORANGE spot it snaps back home. Else it stands on the floor.
/// When the timed ghost's warning flicker starts, players have 10 s to put each sign on its
/// blue spot. The ghost walks to each sign. Not placed + a player inside the GREEN seller area =
/// it attacks that player.
/// While a sign stands on its BLUE spot its counter is CLOSED: no new customer comes to that
/// station (GhostCounterGate). So carry it back home after the ghost leaves.
///
/// Editor colours (Scene view only): orange = home, blue = where it must go for the ghost,
/// green = the seller area the ghost checks. Move the child objects to change them.
///
/// SERVER AUTHORITY: shown / carrier / placed / position are SyncVars. Scene object.
/// Made by Tools > Pre-Thesis > Scenes > Map: Ticket Ghost Restroom Break Signs.
/// </summary>
[DisallowMultipleComponent]
public sealed class RestBreakSign : InteractableBase
{
    [Header("Spots (move these child objects)")]
    [Tooltip("ORANGE: the sign's home. It stands here from the start of the round.")]
    [SerializeField] private Transform appearSpot;
    [Tooltip("BLUE: where the sign must be put.")]
    [SerializeField] private Transform placeSpot;
    [Tooltip("GREEN: this seller's area (behind the counter). 1) The ghost never ROAMS in here. 2) When the ghost reaches the red spot and the sign is not on the blue spot, a player inside this box is attacked (only then may it go in). Its scale is the box size.")]
    [SerializeField] private Transform sellerArea;
    [Tooltip("RED (7 Nov): where the ghost walks to and stops to check this sign (put it in front of the counter, on the floor). Empty = 1 m in front of the blue spot.")]
    [SerializeField] private Transform ghostStopSpot;

    [Header("Facing (7 Nov) - the arrow in the Scene view shows the FRONT")]
    [Tooltip("Turn the sign where it APPEARS (degrees around up). 90 = quarter turn. Rotating the orange spot object works too.")]
    [SerializeField, Range(-180f, 180f)] private float appearTurn;
    [Tooltip("Turn the sign where it is PUT (degrees around up). Also turns the faint place hint. Rotating the blue spot object works too.")]
    [SerializeField, Range(-180f, 180f)] private float placeTurn;

    [Header("Rules")]
    [Tooltip("Who this sign is for, shown in messages (\"Popcorn\" / \"Ticket\").")]
    [SerializeField] private string sellerName = "Popcorn";
    // 15 Nov: new field name on purpose, so the scene's old saved 1.6 m is not used.
    [Tooltip("Put the sign down within this distance (metres) of its blue spot (or its orange home spot) and it snaps onto it.")]
    [SerializeField, Min(0.3f)] private float snapRadius = 2.5f;
    public enum Station { Auto, Popcorn, Ticket }
    [Tooltip("Which counter this sign closes while it stands on its blue spot. Auto = from the seller name.")]
    [SerializeField] private Station station = Station.Auto;

    [Header("Carried (7 Nov: it covered the whole screen)")]
    [Tooltip("Size of the sign in YOUR hands (first person). 0.3 = 30% of its real size.")]
    [SerializeField, Range(0.1f, 1f)] private float firstPersonScale = 0.3f;
    [Tooltip("Where it sits in your view: x = right, y = up, z = forward (metres from the camera).")]
    [SerializeField] private Vector3 firstPersonOffset = new Vector3(0.3f, -0.38f, 0.65f);
    [Tooltip("Size of the sign in a FRIEND's hands (what other players see).")]
    [SerializeField, Range(0.1f, 1f)] private float carriedScale = 0.6f;

    [Header("Place hint (7 Nov)")]
    [Tooltip("While YOU carry this sign, a faint see-through copy shows its blue spot. Only the carrier sees it.")]
    [SerializeField] private Color placeHintColor = new Color(0.35f, 0.65f, 1f, 0.3f);
    [Tooltip("15 Nov: the same faint copy on its orange home spot (where it goes back after the ghost leaves).")]
    [SerializeField] private Color homeHintColor = new Color(1f, 0.6f, 0.15f, 0.25f);

    private const float ServerRange = 4.5f;

    [SyncVar] private bool shown;
    [SyncVar] private bool placed;
    [SyncVar] private uint carrierId;
    [SyncVar] private Vector3 restPosition;
    [SyncVar] private Quaternion restRotation = Quaternion.identity;

    private GameObject offlineCarrier;
    private Renderer[] renderers;
    private Collider[] colliders;
    private bool appliedShown = true;
    private Vector3 baseScale = Vector3.one;
    private GameObject placeHint;
    private Material placeHintMaterial;
    private GameObject homeHint;
    private Material homeHintMaterial;
    private bool closedCounter; // server: this sign currently closes its counter
    private static int localDiscardFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => localDiscardFrame = -1;

    public string SellerName => sellerName;
    public bool Shown => shown;
    /// <summary>Every machine: the sign stands on its blue spot.</summary>
    public bool IsPlaced => shown && placed && carrierId == 0;
    /// <summary>Where the sign is now (carried = in front of the carrier).</summary>
    public Vector3 CurrentPosition => transform.position;

    /// <summary>7 Nov: where the ghost stops to check this sign (red spot, else 1 m in front of the blue spot).</summary>
    public Vector3 GhostStopPosition => ghostStopSpot != null ? ghostStopSpot.position
        : placeSpot != null ? placeSpot.position + PlaceRotation * Vector3.back : transform.position;

    /// <summary>Editor tool: has a red spot been set?</summary>
    public bool HasGhostStopSpot => ghostStopSpot != null;
    public Transform PlaceSpotTransform => placeSpot;

    private bool IsLocalCarrier => shown && carrierId != 0 && PlayerHealth.LocalInstance != null &&
        IsCarrier(PlayerHealth.LocalInstance.gameObject);

    /// <summary>The local player carries a sign (the discard key belongs to it this frame).</summary>
    public static bool LocalUsesDiscardInput
    {
        get
        {
            if (localDiscardFrame == Time.frameCount) return true;
            foreach (RestBreakSign sign in FindObjectsByType<RestBreakSign>())
                if (sign.IsLocalCarrier) return true;
            return false;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        baseScale = transform.localScale;
        // Built now, while the sign stands at its real size (not while it is small in someone's hands).
        placeHint = BuildHint(" (place hint)", placeHintColor, out placeHintMaterial);
        homeHint = BuildHint(" (home hint)", homeHintColor, out homeHintMaterial);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        ServerGoHome();
    }

    private void Start()
    {
        if (NetworkMode.IsOffline) ServerGoHome();
    }

    /// <summary>SERVER. 15 Nov: the sign is always in the map; a round starts with it on its orange home spot.</summary>
    private void ServerGoHome()
    {
        shown = true;
        placed = false;
        carrierId = 0;
        offlineCarrier = null;
        ServerSetRest(AppearPosition, AppearRotation);
    }

    private bool IsTicketStation =>
        station == Station.Ticket || (station == Station.Auto && sellerName.ToLowerInvariant().Contains("ticket"));

    /// <summary>SERVER. A sign on its blue spot closes its counter (no new customer).</summary>
    private void ServerSetCounterClosed(bool closed)
    {
        if (closedCounter == closed) return;
        closedCounter = closed;
        if (IsTicketStation) GhostCounterGate.TicketClosedBySign = closed;
        else GhostCounterGate.PopcornClosedBySign = closed;
    }

    private void OnDisable()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerSetCounterClosed(false);
    }

    // ---- Interaction ------------------------------------------------------------------------

    public override string GetInteractionPrompt() => "[E] Pick up the RESTROOM BREAK sign (" + sellerName + ")";

    public override bool CanInteract() => isActiveAndEnabled && shown && carrierId == 0 && !LocalUsesDiscardInput;

    /// <summary>SERVER. [E]: pick it up.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        if (!shown || carrierId != 0 || interactor == null) return;
        if ((interactor.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return;
        PlayerHealth health = interactor.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead || health.IsDowned || !PlayerRegistry.IsInRound(health)) return;
        if (ServerCarriesAnySign(interactor)) return; // one sign at a time
        carrierId = IdOf(interactor);
        offlineCarrier = NetworkMode.IsOffline ? interactor : null;
        placed = false;
        Say(interactor, "Blue spot = for the ghost (the " + sellerName.ToLowerInvariant() + " counter is closed while it stands there). " +
            "Orange spot = its home. [" + Keybinds.KeyName(GameAction.ReloadOrDiscard) + "] = put it down.");
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this))
        {
            if (carrierId != 0)
            {
                PlayerHealth carrier = CarrierBody();
                if (carrier == null || carrier.IsDead || carrier.IsDowned || !PlayerRegistry.IsInRound(carrier))
                    ServerPutDown(carrier != null ? carrier.transform : null);
            }
            ServerSetCounterClosed(IsPlaced); // 15 Nov: on the blue spot = this counter takes no new customer
        }

        if (IsLocalCarrier && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked &&
            Keybinds.WasPressed(GameAction.ReloadOrDiscard))
        {
            localDiscardFrame = Time.frameCount;
            if (NetworkMode.IsOffline) TryPutDown(PlayerHealth.LocalInstance.gameObject);
            else if (isClient) CmdPutDown();
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdPutDown(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) TryPutDown(sender.identity.gameObject);
    }

    private void TryPutDown(GameObject who)
    {
        if (!NetworkMode.HasServerAuthority(this) || !IsCarrier(who)) return;
        ServerPutDown(who.transform);
    }

    /// <summary>
    /// SERVER. Near the blue spot = placed on it. Near the orange spot = back home. Else it stands on the
    /// floor in front of the carrier. Both spots in range: the nearer one wins.
    /// </summary>
    private void ServerPutDown(Transform carrier)
    {
        GameObject who = carrier != null ? carrier.gameObject : null;
        carrierId = 0;
        offlineCarrier = null;
        Vector3 from = carrier != null ? carrier.position : transform.position;
        float toBlue = placeSpot != null ? Flat(from - placeSpot.position) : float.MaxValue;
        float toHome = appearSpot != null ? Flat(from - appearSpot.position) : float.MaxValue;
        if (toBlue <= snapRadius && toBlue <= toHome)
        {
            placed = true;
            ServerSetRest(placeSpot.position, PlaceRotation);
            if (who != null) Say(who, "Sign placed: the " + sellerName.ToLowerInvariant() + " counter is CLOSED (no customers). Lights OFF, stay quiet. Take it home after the ghost leaves.");
            return;
        }
        placed = false;
        if (toHome <= snapRadius)
        {
            ServerSetRest(AppearPosition, AppearRotation);
            if (who != null) Say(who, "The sign is back on its place. The " + sellerName.ToLowerInvariant() + " counter is open.");
            return;
        }
        Vector3 forward = carrier != null ? Vector3.ProjectOnPlane(carrier.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 at = from + forward * 0.7f + Vector3.up * 1.5f;
        if (Physics.Raycast(at, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            at = hit.point;
        else at.y = from.y;
        ServerSetRest(at, Quaternion.LookRotation(-forward, Vector3.up));
        if (who != null) Say(who, "That is not one of its spots (blue = for the ghost, orange = home).");
    }

    // ---- Server API (TicketGhostVisit) ------------------------------------------------------

    // 15 Nov: ServerShow / ServerHide are gone. The sign no longer appears with the warning or
    // vanishes when the ghost leaves: it stays where the players left it.

    /// <summary>SERVER. A living, in-round player standing inside the green seller area (nearest to the sign), or null.</summary>
    public PlayerHealth ServerPlayerInSellerArea()
    {
        PlayerHealth best = null;
        float bestSqr = float.PositiveInfinity;
        foreach (PlayerHealth p in PlayerRegistry.All)
        {
            if (!PlayerRegistry.IsHuntable(p) || !InSellerArea(p.transform.position)) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = p; }
        }
        return best;
    }

    public bool InSellerArea(Vector3 point)
    {
        if (sellerArea == null) return false;
        Vector3 local = sellerArea.InverseTransformPoint(point);
        return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
    }

    /// <summary>SERVER. Does this player carry any restroom break sign?</summary>
    public static bool ServerCarriesAnySign(GameObject who)
    {
        foreach (RestBreakSign sign in FindObjectsByType<RestBreakSign>())
            if (sign.IsCarrier(who)) return true;
        return false;
    }

    // ---- Every machine: show + follow -------------------------------------------------------

    private void LateUpdate()
    {
        if (appliedShown != shown)
        {
            appliedShown = shown;
            foreach (Renderer r in renderers) if (r != null) r.enabled = shown;
        }
        bool carried = shown && carrierId != 0;
        foreach (Collider c in colliders) if (c != null) c.enabled = shown && !carried;
        bool mine = IsLocalCarrier;
        UpdateHint(placeHint, placeHintMaterial, placeHintColor, mine && placeSpot != null,
            placeSpot != null ? placeSpot.position : Vector3.zero, PlaceRotation);
        UpdateHint(homeHint, homeHintMaterial, homeHintColor, mine && appearSpot != null, AppearPosition, AppearRotation);
        if (!shown) return;

        if (!carried)
        {
            transform.localScale = baseScale;
            transform.SetPositionAndRotation(restPosition, restRotation);
            return;
        }

        PlayerHealth body = CarrierBody();
        if (body == null) return;
        Transform hold = body.transform;

        // 7 Nov: in YOUR hands = small, lower right of the view (it used to fill the screen).
        Camera cam = body == PlayerHealth.LocalInstance ? RuleFx.LocalCamera() : null;
        if (cam != null)
        {
            Transform c = cam.transform;
            transform.localScale = baseScale * firstPersonScale;
            Vector3 at = c.position + c.right * firstPersonOffset.x + c.up * firstPersonOffset.y + c.forward * firstPersonOffset.z;
            Quaternion face = Quaternion.LookRotation(-c.forward, c.up) * Quaternion.Euler(0f, 25f, 0f);
            transform.SetPositionAndRotation(at, face);
            return;
        }

        // A friend's hands: smaller, held low in front of the body.
        transform.localScale = baseScale * carriedScale;
        Vector3 flat = Vector3.ProjectOnPlane(hold.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        Vector3 held = hold.position + Vector3.up * 0.55f + flat.normalized * 0.5f;
        transform.SetPositionAndRotation(held, Quaternion.LookRotation(-flat.normalized, Vector3.up));
    }

    // ---- Place hint (local only) ------------------------------------------------------------

    /// <summary>Faint see-through copy of the sign on a spot (blue = place, orange = home), only for the player carrying it.</summary>
    private void UpdateHint(GameObject hint, Material material, Color color, bool show, Vector3 position, Quaternion rotation)
    {
        if (hint == null) return;
        if (!show)
        {
            if (hint.activeSelf) hint.SetActive(false);
            return;
        }
        if (!hint.activeSelf) hint.SetActive(true);
        hint.transform.SetPositionAndRotation(position, rotation);
        hint.transform.localScale = baseScale;
        color.a *= 0.7f + 0.3f * Mathf.Sin(Time.time * 3f); // slow pulse so it is easy to spot
        material.color = color;
    }

    /// <summary>Builds one hidden hint copy of the sign's meshes. Null when no suitable shader exists.</summary>
    private GameObject BuildHint(string suffix, Color color, out Material material)
    {
        material = null;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("UI/Default");
        if (shader == null) return null;
        material = new Material(shader) { color = color, renderQueue = 3100 };
        Material placeHintMaterial = material; // the parts below share this material
        var placeHint = new GameObject(name + suffix);
        // Copy every mesh of the sign (not the text), in the sign's own space, without its root scale.
        Matrix4x4 toRoot = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<TMPro.TMP_Text>() != null) continue;
            Matrix4x4 rel = toRoot * filter.transform.localToWorldMatrix;
            var part = new GameObject(filter.name);
            part.transform.SetParent(placeHint.transform, false);
            part.transform.localPosition = rel.GetColumn(3);
            part.transform.localRotation = rel.rotation;
            part.transform.localScale = Vector3.Scale(rel.lossyScale, new Vector3(1f / Mathf.Max(0.0001f, baseScale.x),
                1f / Mathf.Max(0.0001f, baseScale.y), 1f / Mathf.Max(0.0001f, baseScale.z)));
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer r = part.AddComponent<MeshRenderer>();
            r.sharedMaterial = placeHintMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        placeHint.SetActive(false);
        return placeHint;
    }

    private void OnDestroy()
    {
        if (placeHint != null) Destroy(placeHint);
        if (placeHintMaterial != null) Destroy(placeHintMaterial);
        if (homeHint != null) Destroy(homeHint);
        if (homeHintMaterial != null) Destroy(homeHintMaterial);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private Vector3 AppearPosition => appearSpot != null ? appearSpot.position : transform.position;
    private Quaternion AppearRotation => (appearSpot != null ? appearSpot.rotation : transform.rotation) * Quaternion.Euler(0f, appearTurn, 0f);
    private Quaternion PlaceRotation => (placeSpot != null ? placeSpot.rotation : transform.rotation) * Quaternion.Euler(0f, placeTurn, 0f);

#if UNITY_EDITOR
    // Edit mode: the sign stands on its orange spot with the chosen turn, so you see the result at once.
    private void OnValidate()
    {
        if (Application.isPlaying || appearSpot == null || !gameObject.scene.IsValid()) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && appearSpot != null && !Application.isPlaying)
                transform.SetPositionAndRotation(AppearPosition, AppearRotation);
        };
    }
#endif

    private void ServerSetRest(Vector3 position, Quaternion rotation)
    {
        restPosition = position;
        restRotation = rotation;
        if (!carriedNow) transform.SetPositionAndRotation(position, rotation);
    }

    private bool carriedNow => carrierId != 0;

    private bool IsCarrier(GameObject who) => who != null && carrierId != 0 &&
        (NetworkMode.IsOffline ? offlineCarrier == who || offlineCarrier == null : IdOf(who) == carrierId);

    private PlayerHealth CarrierBody()
    {
        if (carrierId == 0) return null;
        if (NetworkMode.IsOffline) return offlineCarrier != null ? offlineCarrier.GetComponent<PlayerHealth>() : PlayerHealth.LocalInstance;
        NetworkIdentity identity;
        if (NetworkServer.active ? NetworkServer.spawned.TryGetValue(carrierId, out identity) : NetworkClient.spawned.TryGetValue(carrierId, out identity))
            return identity != null ? identity.GetComponent<PlayerHealth>() : null;
        return null;
    }

    private static uint IdOf(GameObject player)
    {
        if (NetworkMode.IsOffline) return 1u;
        NetworkIdentity identity = player != null ? player.GetComponent<NetworkIdentity>() : null;
        return identity != null ? identity.netId : 0u;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    private void Say(GameObject player, string line)
    {
        NetworkIdentity id = player != null ? player.GetComponent<NetworkIdentity>() : null;
        if (NetworkMode.IsOffline || (id != null && id.isLocalPlayer && id.isClient)) RuleFx.Toast(line, 4f);
        else if (id != null && id.connectionToClient != null) TargetSay(id.connectionToClient, line);
    }

    [TargetRpc]
    private void TargetSay(NetworkConnectionToClient target, string line) => RuleFx.Toast(line, 4f);

    // ---- Editor colours ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (appearSpot != null) DrawSpot(appearSpot.position, AppearRotation, new Color(1f, 0.55f, 0.05f, 0.9f));
        if (placeSpot != null)
        {
            Color blue = new Color(0.15f, 0.45f, 1f, 0.9f);
            DrawSpot(placeSpot.position, PlaceRotation, blue);
            Gizmos.color = blue;
            Gizmos.DrawWireSphere(placeSpot.position, snapRadius);
        }
        if (placeSpot != null || ghostStopSpot != null)
        {
            Vector3 stop = GhostStopPosition;
            Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.9f);
            Gizmos.DrawSphere(stop + Vector3.up * 0.15f, 0.25f);
            Gizmos.DrawLine(stop, stop + Vector3.up * 1.8f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(stop + Vector3.up * 2f, sellerName + ": GHOST STOPS here (red)");
#endif
        }
        if (sellerArea != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.8f);
            Gizmos.matrix = sellerArea.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }
#if UNITY_EDITOR
        if (appearSpot != null) UnityEditor.Handles.Label(appearSpot.position + Vector3.up * 1.3f, sellerName + " sign: HOME (orange)");
        if (placeSpot != null) UnityEditor.Handles.Label(placeSpot.position + Vector3.up * 1.3f, sellerName + " sign: PUT here (blue)");
        if (sellerArea != null) UnityEditor.Handles.Label(sellerArea.position + Vector3.up * (sellerArea.lossyScale.y * 0.5f + 0.2f), sellerName + " seller area (green)");
#endif
    }

    /// <summary>A sign-shaped box turned like the sign, plus an arrow out of its FRONT (the text side).</summary>
    private static void DrawSpot(Vector3 at, Quaternion rotation, Color color)
    {
        Gizmos.color = color;
        Gizmos.matrix = Matrix4x4.TRS(at + Vector3.up * 0.5f, rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, new Vector3(0.55f, 1f, 0.12f));
        Gizmos.matrix = Matrix4x4.identity;
        Vector3 front = rotation * Vector3.back; // the sign's text faces its local -Z
        Vector3 from = at + Vector3.up * 0.5f;
        Vector3 to = from + front * 0.9f;
        Gizmos.color = Color.white;
        Gizmos.DrawLine(from, to);
        Vector3 side = Vector3.Cross(Vector3.up, front).normalized * 0.15f;
        Gizmos.DrawLine(to, to - front * 0.2f + side);
        Gizmos.DrawLine(to, to - front * 0.2f - side);
    }
}
