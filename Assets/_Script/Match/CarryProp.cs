using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 17 Nov 2026 (Mr.k, Task 5): a scene object you pick up with E and carry: the MOP and the FILM REELS.
///
///   HAND item (no Hotbar Token): mop, normal-room film reel. Shown in your hand all the time.
///     Only ONE hand item at a time (mop, reel, popcorn item, RESTROOM BREAK sign).
///     Hotbar items still work while you hold it (it is not two-handed).
///   HOTBAR item (Hotbar Token set): ghost-room film reel. Takes a hotbar slot (ItemCatalog token)
///     and shows only while that slot is in your hand.
///
///   R = put it down. Mop: on the floor in front of you. Reel: back on its shelf.
///   Dying / going down / leaving = the same. A used reel goes back on its shelf (a new reel).
///   Chores ask for an item by <see cref="CarryId"/> (ZoneChoreSpot "Needs Item").
///
/// SERVER AUTHORITY: who carries it and where it rests are SyncVars. Not an inventory item for the
/// save file: the hotbar token is match-only.
/// </summary>
[DisallowMultipleComponent]
public sealed class CarryProp : InteractableBase
{
    public const string Mop = "mop";
    public const string FilmReelGhost = "film_reel_ghost";
    public const string FilmReelNormal = "film_reel_normal";

    [Header("What it is")]
    [Tooltip("What chores ask for: \"mop\", \"film_reel_ghost\" or \"film_reel_normal\".")]
    [SerializeField] private string carryId = Mop;
    [SerializeField] private string displayName = "Mop";
    [Tooltip("17 Nov: film reels: the film name written on it, e.g. \"Room 13\". The projector only takes the film that is due now.")]
    [SerializeField] private string label = "";
    [Tooltip("Empty = carried in the HAND (no hotbar slot). Else the ItemCatalog token it puts in the hotbar (\"film_reel_ghost\").")]
    [SerializeField] private string hotbarToken = "";
    [Tooltip("On = R / dying puts it back where it started (reel shelf). Off = it is put on the floor (mop).")]
    [SerializeField] private bool goHomeWhenDropped;

    [Header("In your hand (first person)")]
    [SerializeField] private Vector3 firstPersonOffset = new Vector3(0.32f, -0.42f, 0.62f);
    [SerializeField] private Vector3 firstPersonEuler = new Vector3(-20f, 0f, -12f);
    [SerializeField, Range(0.1f, 2f)] private float firstPersonScale = 0.8f;
    [Header("In a friend's hand")]
    [SerializeField] private Vector3 carriedOffset = new Vector3(0.28f, 0.95f, 0.35f);
    [SerializeField] private Vector3 carriedEuler = new Vector3(-15f, 0f, 0f);

    private const float ServerRange = 4.5f;

    [SyncVar] private uint carrierId;
    [SyncVar] private Vector3 restPosition;
    [SyncVar] private Quaternion restRotation = Quaternion.identity;
    [SyncVar] private bool restSet; // 17 Nov fix: until the host sets a rest place, stay where the scene put it

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 baseScale;
    private Renderer[] renderers;
    private Collider[] colliders;
    private GameObject offlineCarrier;
    private bool shown = true;
    private static int localDiscardFrame = -1;

    public static readonly List<CarryProp> All = new List<CarryProp>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        localDiscardFrame = -1;
    }

    public string CarryId => carryId;
    public string DisplayName => displayName;
    public string Label => label;
    /// <summary>Ghost Film Reel "Room 13" (name + label).</summary>
    public string FullName => string.IsNullOrEmpty(label) ? displayName : displayName + " \"" + label + "\"";
    public bool UsesHotbar => !string.IsNullOrEmpty(hotbarToken);
    public bool IsCarried => carrierId != 0;

    // ---- Life ---------------------------------------------------------------------------

    protected override void Awake()
    {
        base.Awake();
        homePosition = transform.position;
        homeRotation = transform.rotation;
        baseScale = transform.localScale;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public override void OnStartServer()
    {
        base.OnStartServer();
        ServerGoHome();
    }

    private void Start()
    {
        if (NetworkMode.IsOffline) ServerGoHome();
    }

    // ---- Interaction ----------------------------------------------------------------------

    public override string GetInteractionPrompt() =>
        "[E] Pick up " + FullName + (UsesHotbar ? " (takes a hotbar slot)" : "");

    public override bool CanInteract() => isActiveAndEnabled && carrierId == 0 && RoundRunning;

    protected override void OnInteracted(GameObject who)
    {
        if (carrierId != 0 || who == null || !RoundRunning) return;
        if ((who.transform.position - transform.position).sqrMagnitude > ServerRange * ServerRange) return;
        PlayerHealth health = who.GetComponent<PlayerHealth>();
        if (health == null || health.IsDead || health.IsDowned || !PlayerRegistry.IsInRound(health)) return;
        PlayerInventory inventory = who.GetComponent<PlayerInventory>();

        if (UsesHotbar)
        {
            if (ServerCarriesId(who, carryId)) { Say(who, "You already carry a " + displayName + ". Put it back first."); return; }
            if (inventory == null || !inventory.ServerAddToken(hotbarToken)) { Say(who, "Hotbar full: free a slot first."); return; }
        }
        else if (ServerHandBusy(who, out string busy))
        {
            Say(who, "Hands full: put the " + busy + " down first.");
            return;
        }

        carrierId = IdOf(who);
        offlineCarrier = NetworkMode.IsOffline ? who : null;
        Say(who, FullName + ": [" + Keybinds.KeyName(GameAction.ReloadOrDiscard) + "] = put it " + (goHomeWhenDropped ? "back" : "down") +
            (UsesHotbar ? " (while it is in your hand)." : "."));
    }

    // ---- Server API ------------------------------------------------------------------------

    /// <summary>SERVER. Is this body the carrier?</summary>
    public bool ServerIsCarrier(GameObject who) => who != null && carrierId != 0 &&
        (NetworkMode.IsOffline ? offlineCarrier == who || offlineCarrier == null : IdOf(who) == carrierId);

    /// <summary>SERVER. The prop of this kind that this player holds IN THE HAND (hotbar ones: slot selected), or null.</summary>
    public static CarryProp ServerHeld(GameObject who, string id)
    {
        if (who == null || string.IsNullOrEmpty(id)) return null;
        foreach (CarryProp prop in All)
            if (prop != null && prop.carryId == id && prop.ServerIsCarrier(who) && prop.InHandOf(who)) return prop;
        return null;
    }

    /// <summary>SERVER. Does this player carry one of this kind (in the hand or put away)?</summary>
    public static bool ServerCarriesId(GameObject who, string id)
    {
        foreach (CarryProp prop in All)
            if (prop != null && prop.carryId == id && prop.ServerIsCarrier(who)) return true;
        return false;
    }

    /// <summary>
    /// SERVER. Is the player's free hand taken? Hand props, the RESTROOM BREAK sign, a popcorn item.
    /// Hotbar items do not count (only one hand item at a time, Task 6).
    /// </summary>
    public static bool ServerHandBusy(GameObject who, out string what)
    {
        what = null;
        if (who == null) return false;
        foreach (CarryProp prop in All)
            if (prop != null && !prop.UsesHotbar && prop.ServerIsCarrier(who)) { what = prop.displayName.ToLowerInvariant(); return true; }
        if (RestBreakSign.ServerCarriesAnySign(who)) { what = "sign"; return true; }
        if (who.TryGetComponent(out PlayerHeldItems held) && held.TaskItem.IsInHand) { what = "popcorn item"; return true; }
        return false;
    }

    /// <summary>SERVER. Used by a chore (a reel went into the projector): it goes home (a new reel on the shelf).</summary>
    public void ServerConsume()
    {
        ServerTakeToken();
        carrierId = 0;
        offlineCarrier = null;
        ServerGoHome();
    }

    private void ServerGoHome()
    {
        ServerTakeToken();
        carrierId = 0;
        offlineCarrier = null;
        ServerSetRest(homePosition, homeRotation);
    }

    /// <summary>SERVER. R / down / left: home (reel) or on the floor in front of the carrier (mop).</summary>
    private void ServerDrop(Transform carrier)
    {
        if (goHomeWhenDropped || carrier == null)
        {
            ServerGoHome();
            return;
        }
        ServerTakeToken();
        carrierId = 0;
        offlineCarrier = null;
        Vector3 forward = Vector3.ProjectOnPlane(carrier.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 at = carrier.position + forward * 0.6f + Vector3.up * 1.2f;
        if (Physics.Raycast(at, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            at = hit.point;
        else at.y = carrier.position.y;
        // Lying on the floor: the model's up axis (the stick) points away from the player.
        ServerSetRest(at + Vector3.up * 0.04f, Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(90f, 0f, 0f));
    }

    private void ServerTakeToken()
    {
        if (!UsesHotbar) return;
        PlayerHealth body = CarrierBody();
        PlayerInventory inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
        if (inventory == null) return;
        int carried = 0;
        foreach (CarryProp other in All)
            if (other != null && other.hotbarToken == hotbarToken && other.carrierId != 0 && other.CarrierBody() == body) carried++;
        if (carried > 1)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                if (inventory.GetSlot(i).itemId == hotbarToken) { inventory.ServerRemoveAt(i); return; }
        }
        else inventory.ServerRemoveAll(hotbarToken);
    }

    private void ServerSetRest(Vector3 position, Quaternion rotation)
    {
        restPosition = position;
        restRotation = rotation;
        restSet = true;
        if (carrierId == 0) transform.SetPositionAndRotation(position, rotation);
    }

    private void Say(GameObject who, string text)
    {
        if (who == null || string.IsNullOrEmpty(text)) return;
        if (NetworkMode.IsOffline) { RuleFx.Toast(text, 3.5f); return; }
        NetworkIdentity identity = who.GetComponent<NetworkIdentity>();
        if (identity != null && identity.connectionToClient != null) TargetSay(identity.connectionToClient, text);
    }

    [TargetRpc]
    private void TargetSay(NetworkConnectionToClient target, string text) => RuleFx.Toast(text, 3.5f);

    // ---- Every frame ----------------------------------------------------------------------

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this) && carrierId != 0)
        {
            PlayerHealth carrier = CarrierBody();
            if (carrier == null || carrier.IsDead || carrier.IsDowned || !PlayerRegistry.IsInRound(carrier))
                ServerDrop(carrier != null ? carrier.transform : null);
            else if (UsesHotbar && carrier.TryGetComponent(out PlayerInventory inv) && !inv.Has(hotbarToken))
                ServerGoHome(); // the token left the hotbar some other way
        }

        if (LocalOwnsDiscardKey && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked &&
            Keybinds.WasPressed(GameAction.ReloadOrDiscard))
        {
            localDiscardFrame = Time.frameCount;
            if (NetworkMode.IsOffline) TryDrop(PlayerHealth.LocalInstance.gameObject);
            else if (isClient) CmdDrop();
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdDrop(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) TryDrop(sender.identity.gameObject);
    }

    private void TryDrop(GameObject who)
    {
        if (!NetworkMode.HasServerAuthority(this) || !ServerIsCarrier(who)) return;
        ServerDrop(who.transform);
    }

    private void LateUpdate()
    {
        bool carried = carrierId != 0;
        if (colliders != null)
            foreach (Collider c in colliders) if (c != null && c.enabled == carried) c.enabled = !carried;

        if (!carried)
        {
            Show(true);
            transform.localScale = baseScale;
            if (restSet) transform.SetPositionAndRotation(restPosition, restRotation);
            else transform.SetPositionAndRotation(homePosition, homeRotation);
            return;
        }
        PlayerHealth body = CarrierBody();
        if (body == null) return;
        bool inHand = InHandOf(body.gameObject);
        Show(inHand);
        if (!inHand) return;

        Camera cam = body == PlayerHealth.LocalInstance ? RuleFx.LocalCamera() : null;
        if (cam != null)
        {
            Transform c = cam.transform;
            transform.localScale = baseScale * firstPersonScale;
            Vector3 at = c.position + c.right * firstPersonOffset.x + c.up * firstPersonOffset.y + c.forward * firstPersonOffset.z;
            transform.SetPositionAndRotation(at, c.rotation * Quaternion.Euler(firstPersonEuler));
            return;
        }
        transform.localScale = baseScale;
        Transform hold = body.transform;
        Vector3 flat = Vector3.ProjectOnPlane(hold.forward, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
        Quaternion facing = Quaternion.LookRotation(flat.normalized, Vector3.up);
        transform.SetPositionAndRotation(hold.position + facing * carriedOffset, facing * Quaternion.Euler(carriedEuler));
    }

    private void Show(bool show)
    {
        if (show == shown || renderers == null) return;
        shown = show;
        foreach (Renderer r in renderers) if (r != null) r.enabled = show;
    }

    // ---- Local ----------------------------------------------------------------------------

    private bool IsLocalCarrier => carrierId != 0 && PlayerHealth.LocalInstance != null &&
        (NetworkMode.IsOffline ? true : carrierId == PlayerHealth.LocalInstance.netId);

    /// <summary>Carried by this body and out (hand item, or its hotbar slot is selected).</summary>
    private bool InHandOf(GameObject who)
    {
        if (!UsesHotbar) return true;
        PlayerInventory inventory = who != null ? who.GetComponent<PlayerInventory>() : null;
        return inventory == null || inventory.IsHolding(hotbarToken);
    }

    /// <summary>R belongs to this prop now. Hand props give R to a selected flashlight or carried token first.</summary>
    private bool LocalOwnsDiscardKey
    {
        get
        {
            if (!IsLocalCarrier || PlayerInventory.Local == null) return false;
            PlayerInventory inv = PlayerInventory.Local;
            if (UsesHotbar) return inv.IsHolding(hotbarToken);
            InventorySlot held = inv.HeldSlot;
            if (held.IsEmpty) return true;
            ItemCatalog.ItemInfo info = ItemCatalog.Find(held.itemId);
            if (info == null) return true;
            return !(info.carryToken || (info.usesBatteries && !held.spare));
        }
    }

    /// <summary>The local player's carried prop uses the discard key this frame.</summary>
    public static bool LocalUsesDiscardInput
    {
        get
        {
            if (localDiscardFrame == Time.frameCount) return true;
            foreach (CarryProp prop in All) if (prop != null && prop.LocalOwnsDiscardKey) return true;
            return false;
        }
    }

    /// <summary>Local: the hand item the local player carries (mop, normal reel), or null.</summary>
    public static CarryProp LocalHandProp
    {
        get
        {
            foreach (CarryProp prop in All) if (prop != null && !prop.UsesHotbar && prop.IsLocalCarrier) return prop;
            return null;
        }
    }

    /// <summary>Local: the label of the one of this kind in the local player's hand ("02:00"), or null.</summary>
    public static string LocalHeldLabel(string id)
    {
        if (PlayerHealth.LocalInstance == null) return null;
        foreach (CarryProp prop in All)
            if (prop != null && prop.carryId == id && prop.IsLocalCarrier && prop.InHandOf(PlayerHealth.LocalInstance.gameObject)) return prop.label ?? "";
        return null;
    }

    /// <summary>Local: the hotbar-type prop the local player carries with this token, or null (hint text).</summary>
    public static CarryProp LocalCarriedByToken(string token)
    {
        foreach (CarryProp prop in All)
            if (prop != null && prop.hotbarToken == token && prop.IsLocalCarrier) return prop;
        return null;
    }

    /// <summary>Local: does the local player hold one of this kind in the hand right now?</summary>
    public static bool LocalHolds(string id)
    {
        if (PlayerHealth.LocalInstance == null) return false;
        foreach (CarryProp prop in All)
            if (prop != null && prop.carryId == id && prop.IsLocalCarrier && prop.InHandOf(PlayerHealth.LocalInstance.gameObject)) return true;
        return false;
    }

    // ---- Helpers -------------------------------------------------------------------------

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

    private static bool RoundRunning => MatchDirector.Instance == null || MatchDirector.Instance.RoundRunning;

    /// <summary>Editor tool.</summary>
    public void EditorSetup(string id, string nameText, string token, bool goHome, string labelText = "")
    {
        carryId = id;
        displayName = nameText;
        label = labelText ?? "";
        hotbarToken = token ?? "";
        goHomeWhenDropped = goHome;
        interactionPrompt = "[E] Pick up " + nameText;
    }

    /// <summary>Editor tool: how it sits in the hand.</summary>
    public void EditorSetHold(Vector3 fpOffset, Vector3 fpEuler, float fpScale, Vector3 offset, Vector3 euler)
    {
        firstPersonOffset = fpOffset;
        firstPersonEuler = fpEuler;
        firstPersonScale = fpScale;
        carriedOffset = offset;
        carriedEuler = euler;
    }
}
