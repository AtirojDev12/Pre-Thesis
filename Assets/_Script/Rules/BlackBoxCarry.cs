using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): the "black box" for the toilet rule "Ghost food".
///
/// FINAL GAME: a real item kept in a Staff Room.
/// FOR NOW: a box place in each toilet room. Press E on the box to carry it
/// (it floats in front of you), then press E on the drop spot to throw it in.
/// Press the discard key (default R) to return it to its original place.
/// Not an inventory item, so the save system is not touched.
///
/// Lives on the box place (with a <see cref="RuleInteractable"/>: [E] Pick up black box).
/// SERVER AUTHORITY: who carries it is a SyncVar.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RuleInteractable))]
public sealed class BlackBoxCarry : NetworkBehaviour
{
    [Tooltip("The box mockup (prefab \"RM_BlackBox\"). Moves to the carrier's hands.")]
    [SerializeField] private Transform box;

    [SyncVar] private uint carrierId;
    [SyncVar(hook = nameof(OnGoneChanged))] private bool gone;

    private RuleInteractable pickup;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Collider[] boxColliders;
    private GameObject offlineCarrier;
    private static int localReturnFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => localReturnFrame = -1;

    private bool IsLocalCarrier => !gone && PlayerHealth.LocalInstance != null &&
        ServerIsCarrier(PlayerHealth.LocalInstance.gameObject);

    // Reserve this press even if another component updates after the box was returned.
    public static bool UsesLocalDiscardInput
    {
        get
        {
            if (localReturnFrame == Time.frameCount) return true;
            foreach (var carry in FindObjectsByType<BlackBoxCarry>(FindObjectsSortMode.None))
                if (carry.IsLocalCarrier) return true;
            return false;
        }
    }

    /// <summary>Every machine: netId of the carrier (1 offline), 0 = on its place.</summary>
    public uint CarrierId => carrierId;

    private void Awake()
    {
        pickup = GetComponent<RuleInteractable>();
        if (box != null)
        {
            homePosition = box.position;
            homeRotation = box.rotation;
            boxColliders = box.GetComponentsInChildren<Collider>(true);
        }
    }

    private void Start()
    {
        if (NetworkMode.HasServerAuthority(this))
        {
            pickup.ServerUsed += OnPickedUp;
            pickup.ServerCanUse = who => carrierId == 0 && !gone;
        }
    }

    private void OnDestroy()
    {
        if (pickup != null) pickup.ServerUsed -= OnPickedUp;
    }

    // ---- Server --------------------------------------------------------------------------

    private void OnPickedUp(GameObject who)
    {
        carrierId = IdOf(who);
        offlineCarrier = NetworkMode.IsOffline ? who : null;
        pickup.ServerSetUsable(false);
    }

    /// <summary>SERVER. Is this body the one carrying the box?</summary>
    public bool ServerIsCarrier(GameObject who) => who != null && carrierId != 0 &&
        (NetworkMode.IsOffline ? offlineCarrier == who : IdOf(who) == carrierId);

    /// <summary>SERVER. The box was thrown in: it disappears until <see cref="ServerReturnHome"/>.</summary>
    public void ServerConsume()
    {
        carrierId = 0;
        offlineCarrier = null;
        gone = true;
        ApplyGone(true);
    }

    /// <summary>SERVER. Put a new box on the place (next night event).</summary>
    public void ServerReturnHome()
    {
        carrierId = 0;
        offlineCarrier = null;
        gone = false;
        ApplyGone(false);
        if (box != null) box.SetPositionAndRotation(homePosition, homeRotation);
        pickup.ServerSetUsable(true);
    }

    private void RequestReturnHome()
    {
        if (!IsLocalCarrier) return;
        localReturnFrame = Time.frameCount;
        if (NetworkMode.IsOffline) TryReturnHome(PlayerHealth.LocalInstance.gameObject);
        else if (isClient) CmdReturnHome();
    }

    [Command(requiresAuthority = false)]
    private void CmdReturnHome(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) TryReturnHome(sender.identity.gameObject);
    }

    private bool TryReturnHome(GameObject who)
    {
        if (!NetworkMode.HasServerAuthority(this) || gone || !ServerIsCarrier(who)) return false;
        PlayerHealth health = who.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead || health.IsDowned || !PlayerRegistry.IsInRound(health)) return false;
        ServerReturnHome();
        return true;
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && carrierId != 0)
        {
            // The carrier died, went down, left the game or escaped: the box goes back.
            PlayerHealth carrier = CarrierBody();
            if (carrier == null || carrier.IsDead || carrier.IsDowned || !PlayerRegistry.IsInRound(carrier)) ServerReturnHome();
        }
        if (IsLocalCarrier && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked &&
            Keybinds.WasPressed(GameAction.ReloadOrDiscard)) RequestReturnHome();
    }

    private void LateUpdate()
    {
        if (box == null || gone) return;
        bool carried = carrierId != 0;
        if (boxColliders != null)
            foreach (Collider c in boxColliders) if (c != null && c.enabled == carried) c.enabled = !carried && pickup.Usable;

        if (!carried)
        {
            box.SetPositionAndRotation(homePosition, homeRotation);
            return;
        }

        PlayerHealth body = CarrierBody();
        if (body == null) return;
        Transform hold = body.transform;
        Vector3 forward = hold.forward;
        Vector3 at = hold.position + Vector3.up * 1.05f + forward * 0.45f;
        if (body == PlayerHealth.LocalInstance)
        {
            Camera cam = RuleFx.LocalCamera();
            if (cam != null)
            {
                forward = cam.transform.forward;
                at = cam.transform.position + cam.transform.forward * 0.55f - cam.transform.up * 0.3f + cam.transform.right * 0.2f;
            }
        }
        box.SetPositionAndRotation(at, Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized + Vector3.forward * 0.0001f));
    }

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

    private void OnGoneChanged(bool oldValue, bool newValue) => ApplyGone(newValue);

    private void ApplyGone(bool isGone)
    {
        if (box != null && box.gameObject.activeSelf == isGone) box.gameObject.SetActive(!isGone);
    }

    /// <summary>Editor tool.</summary>
    public void SetBox(Transform boxTransform) => box = boxTransform;
}
