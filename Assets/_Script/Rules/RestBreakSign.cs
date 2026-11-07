using Mirror;
using UnityEngine;

/// <summary>
/// 7 Nov 2026 (Mr.k): a RESTROOM BREAK sign for the Ticket Zone rule "The one who returns".
///
/// There are two (Popcorn seller, Ticket seller). When the timed ghost's warning flicker
/// starts, both signs appear at their ORANGE spot (TicketGhostVisit). Players have 10 s:
///   [E] on the sign = pick it up (it floats in front of you)
///   [R] (discard key) = put it down. Near its BLUE spot it snaps onto the spot = PLACED.
/// The ghost walks to each sign. Not placed + a player inside the GREEN seller area = it
/// attacks that player. When the ghost leaves, the signs vanish (back next visit).
///
/// Editor colours (Scene view only): orange = where it appears, blue = where it must go,
/// green = the seller area the ghost checks. Move the child objects to change them.
///
/// SERVER AUTHORITY: shown / carrier / placed / position are SyncVars. Scene object.
/// Made by Tools > Pre-Thesis > Scenes > Map: Ticket Ghost Restroom Break Signs.
/// </summary>
[DisallowMultipleComponent]
public sealed class RestBreakSign : InteractableBase
{
    [Header("Spots (move these child objects)")]
    [Tooltip("ORANGE: where the sign appears when the lights flicker.")]
    [SerializeField] private Transform appearSpot;
    [Tooltip("BLUE: where the sign must be put.")]
    [SerializeField] private Transform placeSpot;
    [Tooltip("GREEN: the seller area. Its scale is the box size. A player inside it gets attacked if the sign is not placed.")]
    [SerializeField] private Transform sellerArea;

    [Header("Rules")]
    [Tooltip("Who this sign is for, shown in messages (\"Popcorn\" / \"Ticket\").")]
    [SerializeField] private string sellerName = "Popcorn";
    [Tooltip("Put the sign down within this distance (metres) of the blue spot and it snaps onto it.")]
    [SerializeField, Min(0.3f)] private float snapDistance = 1.6f;

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
    private static int localDiscardFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => localDiscardFrame = -1;

    public string SellerName => sellerName;
    public bool Shown => shown;
    /// <summary>Every machine: the sign stands on its blue spot.</summary>
    public bool IsPlaced => shown && placed && carrierId == 0;
    /// <summary>Where the sign is now (carried = in front of the carrier).</summary>
    public Vector3 CurrentPosition => transform.position;

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
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        shown = false;
        placed = false;
        carrierId = 0;
        ServerSetRest(AppearPosition, AppearRotation);
    }

    private void Start()
    {
        if (NetworkMode.IsOffline)
        {
            shown = false;
            ServerSetRest(AppearPosition, AppearRotation);
        }
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
        Say(interactor, "Put it on its blue spot at the " + sellerName.ToLowerInvariant() + " counter. [" +
            Keybinds.KeyName(GameAction.ReloadOrDiscard) + "] = put it down.");
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && carrierId != 0)
        {
            PlayerHealth carrier = CarrierBody();
            if (carrier == null || carrier.IsDead || carrier.IsDowned || !PlayerRegistry.IsInRound(carrier))
                ServerPutDown(carrier != null ? carrier.transform : null);
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

    /// <summary>SERVER. Near the blue spot = placed on it; else it stands on the floor in front of the carrier.</summary>
    private void ServerPutDown(Transform carrier)
    {
        GameObject who = carrier != null ? carrier.gameObject : null;
        carrierId = 0;
        offlineCarrier = null;
        Vector3 from = carrier != null ? carrier.position : transform.position;
        if (placeSpot != null && Flat(from - placeSpot.position) <= snapDistance)
        {
            placed = true;
            ServerSetRest(placeSpot.position, placeSpot.rotation);
            if (who != null) Say(who, "Sign placed. Now switch the lights OFF and stay quiet.");
            return;
        }
        placed = false;
        Vector3 forward = carrier != null ? Vector3.ProjectOnPlane(carrier.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 at = from + forward * 0.7f + Vector3.up * 1.5f;
        if (Physics.Raycast(at, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            at = hit.point;
        else at.y = from.y;
        ServerSetRest(at, Quaternion.LookRotation(-forward, Vector3.up));
        if (who != null) Say(who, "That is not its spot. Take it to the blue spot at the " + sellerName.ToLowerInvariant() + " counter.");
    }

    // ---- Server API (TicketGhostVisit) ------------------------------------------------------

    /// <summary>SERVER. The warning started: the sign appears at its orange spot.</summary>
    public void ServerShow()
    {
        carrierId = 0;
        offlineCarrier = null;
        placed = false;
        ServerSetRest(AppearPosition, AppearRotation);
        shown = true;
    }

    /// <summary>SERVER. The ghost left: the sign vanishes.</summary>
    public void ServerHide()
    {
        carrierId = 0;
        offlineCarrier = null;
        placed = false;
        shown = false;
        ServerSetRest(AppearPosition, AppearRotation);
    }

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
        if (!shown) return;

        if (!carried)
        {
            transform.SetPositionAndRotation(restPosition, restRotation);
            return;
        }

        PlayerHealth body = CarrierBody();
        if (body == null) return;
        Transform hold = body.transform;
        Vector3 forward = hold.forward;
        Vector3 at = hold.position + Vector3.up * 0.9f + forward * 0.55f;
        if (body == PlayerHealth.LocalInstance)
        {
            Camera cam = RuleFx.LocalCamera();
            if (cam != null)
            {
                forward = cam.transform.forward;
                at = cam.transform.position + cam.transform.forward * 0.7f - cam.transform.up * 0.55f + cam.transform.right * 0.15f;
            }
        }
        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = hold.forward;
        transform.SetPositionAndRotation(at, Quaternion.LookRotation(-flat.normalized, Vector3.up));
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private Vector3 AppearPosition => appearSpot != null ? appearSpot.position : transform.position;
    private Quaternion AppearRotation => appearSpot != null ? appearSpot.rotation : transform.rotation;

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
        if (appearSpot != null)
        {
            Gizmos.color = new Color(1f, 0.55f, 0.05f, 0.9f);
            Gizmos.DrawCube(appearSpot.position + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.1f));
        }
        if (placeSpot != null)
        {
            Gizmos.color = new Color(0.15f, 0.45f, 1f, 0.9f);
            Gizmos.DrawCube(placeSpot.position + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.1f));
            Gizmos.DrawWireSphere(placeSpot.position, snapDistance);
        }
        if (sellerArea != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.8f);
            Gizmos.matrix = sellerArea.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }
#if UNITY_EDITOR
        if (appearSpot != null) UnityEditor.Handles.Label(appearSpot.position + Vector3.up * 1.3f, sellerName + " sign: APPEARS here (orange)");
        if (placeSpot != null) UnityEditor.Handles.Label(placeSpot.position + Vector3.up * 1.3f, sellerName + " sign: PUT here (blue)");
        if (sellerArea != null) UnityEditor.Handles.Label(sellerArea.position + Vector3.up * (sellerArea.lossyScale.y * 0.5f + 0.2f), sellerName + " seller area (green)");
#endif
    }
}
