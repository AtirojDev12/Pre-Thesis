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
///
/// 17 Nov 2026 (Mr.k, Task 6): the black box is a HOTBAR item now. Picking it up needs a free
/// hotbar slot and puts the "Black Box" token there (ItemCatalog.BlackBox). The box shows in
/// front of you only while that slot is in your hand; in another slot it is put away.
/// R puts it back only while it is in your hand. The token goes when the box goes back
/// (thrown in, put back, carrier died / went down / left).
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

    /// <summary>17 Nov: the local player carries this box AND has its hotbar slot in the hand.</summary>
    private bool IsLocalHolding => IsLocalCarrier && PlayerInventory.Local != null && PlayerInventory.Local.IsHolding(ItemCatalog.BlackBox);

    private Renderer[] boxRenderers;
    private bool shownInHand = true;

    // Reserve this press even if another component updates after the box was returned.
    public static bool UsesLocalDiscardInput
    {
        get
        {
            if (localReturnFrame == Time.frameCount) return true;
            if (RestBreakSign.LocalUsesDiscardInput) return true; // 7 Nov: carrying a restroom break sign
            foreach (var carry in FindObjectsByType<BlackBoxCarry>(FindObjectsSortMode.None))
                if (carry.IsLocalHolding) return true; // 17 Nov: only while the box is in the hand
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
            boxRenderers = box.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void Start()
    {
        if (NetworkMode.HasServerAuthority(this))
        {
            pickup.ServerUsed += OnPickedUp;
            // 17 Nov: the box takes a hotbar slot, so it needs a free one.
            pickup.ServerCanUse = who => carrierId == 0 && !gone && who != null &&
                who.TryGetComponent(out PlayerInventory inv) && inv.HasEmptySlot;
        }
    }

    private void OnDestroy()
    {
        if (pickup != null) pickup.ServerUsed -= OnPickedUp;
    }

    // ---- Server --------------------------------------------------------------------------

    private void OnPickedUp(GameObject who)
    {
        PlayerInventory inventory = who != null ? who.GetComponent<PlayerInventory>() : null;
        if (inventory == null || !inventory.ServerAddToken(ItemCatalog.BlackBox)) return; // 17 Nov: hotbar full
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
        ServerTakeToken();
        carrierId = 0;
        offlineCarrier = null;
        gone = true;
        ApplyGone(true);
    }

    /// <summary>SERVER. Put a new box on the place (next night event).</summary>
    public void ServerReturnHome()
    {
        ServerTakeToken();
        carrierId = 0;
        offlineCarrier = null;
        gone = false;
        ApplyGone(false);
        if (box != null) box.SetPositionAndRotation(homePosition, homeRotation);
        pickup.ServerSetUsable(true);
    }

    /// <summary>SERVER. The carrier's hotbar token goes (unless they carry another black box too).</summary>
    private void ServerTakeToken()
    {
        PlayerHealth carrier = CarrierBody();
        PlayerInventory inventory = carrier != null ? carrier.GetComponent<PlayerInventory>() : null;
        if (inventory == null) return;
        int carried = 0;
        foreach (BlackBoxCarry other in FindObjectsByType<BlackBoxCarry>(FindObjectsSortMode.None))
            if (other.carrierId != 0 && other.CarrierBody() == carrier) carried++;
        if (carried > 1)
        {
            // Two boxes, two tokens: take one.
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                if (inventory.GetSlot(i).itemId == ItemCatalog.BlackBox) { inventory.ServerRemoveAt(i); return; }
        }
        else inventory.ServerRemoveAll(ItemCatalog.BlackBox);
    }

    private void RequestReturnHome()
    {
        if (!IsLocalHolding) return; // 17 Nov: only while it is in your hand
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
            // 17 Nov: the token left the hotbar some other way: the box goes back.
            else if (carrier.TryGetComponent(out PlayerInventory inv) && !inv.Has(ItemCatalog.BlackBox)) ServerReturnHome();
        }
        if (IsLocalHolding && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked &&
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
            ShowInHand(true);
            box.SetPositionAndRotation(homePosition, homeRotation);
            return;
        }
        PlayerHealth body = CarrierBody();
        if (body == null) return;
        // 17 Nov: in the hotbar but not in the hand = put away (not drawn).
        PlayerInventory carrierInventory = body.GetComponent<PlayerInventory>();
        bool inHand = carrierInventory == null || carrierInventory.IsHolding(ItemCatalog.BlackBox);
        ShowInHand(inHand);
        if (!inHand) return;
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

    private void ShowInHand(bool show)
    {
        if (show == shownInHand || boxRenderers == null) return;
        shownInHand = show;
        foreach (Renderer r in boxRenderers) if (r != null) r.enabled = show;
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
