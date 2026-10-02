using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// In-match hotbar: 4 slots. The SELECTED slot is the item in your hand; the
/// other 3 are your pockets. Keys 1–4 or the mouse wheel change the slot.
///
/// SERVER-AUTHORITATIVE. The slots are a SyncList and the selection a SyncVar,
/// both written only by the server. A client asks with a Command and the
/// server checks it. Everyone sees the same inventory for every player, which
/// is what the Walkie-Talkie needs ("does this player carry a radio that is on?").
///
/// Rules already enforced (project spec):
///   - An item ID can only be in the inventory once (1 copy per type).
///   - Items are identified by ItemCatalog string IDs (same IDs as the save).
///
/// Owned shop items come from the save/loadout. PlayerItemThrow handles dropping
/// (Q, and every item when the player DIES) and WorldInventoryItem handles pickup.
/// The save is settled on the results screen (MatchResultsUI): dead = lose what you
/// brought; survived = you own exactly the permanent items you carried out. Popcorn buckets are still held by Atiroj's ItemHoldingSystem, which
/// is separate from this hotbar.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
[DisallowMultipleComponent]
public class PlayerInventory : NetworkBehaviour
{
    public const int SlotCount = 4;

    [Tooltip("Demo: items every player spawns with. Replaced by the lobby loadout later.")]
    [SerializeField] private List<string> startingItems = new List<string>(); // 1 Oct: walkie is bought now, not free

    private readonly SyncList<InventorySlot> slots = new SyncList<InventorySlot>();

    [SyncVar(hook = nameof(OnSelectedSlotChanged))]
    private int selectedSlot;

    private PlayerVoice voice;
    private HotbarHUD hud;
    private bool loadoutApplied;

    /// <summary>This machine's own inventory (null outside a match).</summary>
    public static PlayerInventory Local { get; private set; }

    /// <summary>Raised on every machine when slots or the selection change.</summary>
    public event System.Action Changed;

    public int SelectedSlot => selectedSlot;
    public int Count => slots.Count;

    public InventorySlot GetSlot(int index) =>
        index >= 0 && index < slots.Count ? slots[index] : InventorySlot.Empty;

    /// <summary>The item in your hand.</summary>
    public InventorySlot HeldSlot => GetSlot(selectedSlot);

    public bool IsHoldingRadio => !HeldSlot.IsEmpty && ItemCatalog.IsRadio(HeldSlot.itemId);
    public bool IsHoldingPoweredRadio => IsHoldingRadio && HeldSlot.poweredOn;

    /// <summary>Carries a switched-on radio in ANY slot: hears Walkie-Talkie traffic.</summary>
    public bool HasPoweredRadio
    {
        get
        {
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty && slots[i].poweredOn && ItemCatalog.IsRadio(slots[i].itemId)) return true;
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Local = null;

    private void Awake()
    {
        voice = GetComponent<PlayerVoice>();
        // Mirror's default SyncList guard throws when neither server nor client is running.
        // Permit the project's offline test mode, preserving Mirror's guard in real sessions.
        System.Func<bool> mirrorCanWrite = slots.IsWritable;
        slots.IsWritable = () => NetworkMode.IsOffline || mirrorCanWrite();
    }

    // ---- Lifecycle ---------------------------------------------------------

    public override void OnStartServer()
    {
        loadoutApplied = false;
        slots.Clear();
        for (int i = 0; i < SlotCount; i++) slots.Add(InventorySlot.Empty);
        selectedSlot = 0;

        foreach (string id in startingItems) ServerAddItem(id);
    }

    public override void OnStartClient()
    {
        slots.OnChange += OnSlotsChanged;
        Changed?.Invoke();
    }

    public override void OnStopClient() => slots.OnChange -= OnSlotsChanged;

    public override void OnStartLocalPlayer()
    {
        Local = this;
        hud = HotbarHUD.Create(this);
        SendLoadout();
    }

    private void Start()
    {
        if (!NetworkMode.IsOffline) return;
        OnStartServer();
        Local = this;
        slots.OnChange += OnSlotsChanged;
        hud = HotbarHUD.Create(this);
        SendLoadout();
    }

    // ---- Loadout from the save (1 Oct) ---------------------------------------
    //
    // The save lives on each player's own PC (SaveManager.Current), so the
    // owner tells the server which permanent items it owns; the server checks
    // every ID against ItemCatalog and still enforces max 1 per type.
    // Co-op PvE: trusting the owner's save is fine (same as the noise values).

    /// <summary>Items this PC brought into the current body (a match loses these on death).</summary>
    public static readonly List<string> LastLoadout = new List<string>();

    /// <summary>Owner: put every owned permanent item from the save into the hotbar.</summary>
    public void SendLoadout()
    {
        if (!NetworkMode.IsLocalController(this) || SaveManager.Current == null || SaveManager.Current.permanentItems == null) return;

        LastLoadout.Clear();
        foreach (PermanentItemData item in SaveManager.Current.permanentItems)
        {
            if (item == null || !item.isOwned || !ItemCatalog.Exists(item.itemID)) continue;
            if (!LastLoadout.Contains(item.itemID)) LastLoadout.Add(item.itemID);
        }
        if (NetworkMode.IsOffline) ApplyLoadout(LastLoadout.ToArray());
        else CmdLoadout(LastLoadout.ToArray());
    }

    [Command]
    private void CmdLoadout(string[] itemIds)
    {
        ApplyLoadout(itemIds);
    }

    private void ApplyLoadout(string[] itemIds)
    {
        if (loadoutApplied || itemIds == null) return;
        loadoutApplied = true;
        int added = 0;
        foreach (string id in itemIds)
        {
            if (added >= SlotCount) break;
            ItemCatalog.ItemInfo info = ItemCatalog.Find(id);
            if (info == null || !info.permanent) continue;
            if (ServerAddItem(id)) added++;
        }
        if (added > 0 && voice != null) voice.ServerRefreshRadio();
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
        if (hud != null) Destroy(hud.gameObject);
    }

    private void OnSlotsChanged(SyncList<InventorySlot>.Operation op, int index, InventorySlot item) => Changed?.Invoke();

    private void OnSelectedSlotChanged(int oldValue, int newValue) => Changed?.Invoke();

    // ---- Local input -------------------------------------------------------

    private void Update()
    {
        if (!NetworkMode.IsLocalController(this) || GameplayInput.Blocked) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) RequestSelect(0);
            else if (keyboard.digit2Key.wasPressedThisFrame) RequestSelect(1);
            else if (keyboard.digit3Key.wasPressedThisFrame) RequestSelect(2);
            else if (keyboard.digit4Key.wasPressedThisFrame) RequestSelect(3);
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) RequestSelect((selectedSlot + SlotCount - 1) % SlotCount);
            else if (scroll < -0.01f) RequestSelect((selectedSlot + 1) % SlotCount);
        }
    }

    // ---- Requests (local player) --------------------------------------------

    public void RequestSelect(int index)
    {
        if (!NetworkMode.IsLocalController(this) || index < 0 || index >= SlotCount || index == selectedSlot) return;
        if (NetworkMode.IsOffline) SelectSlot(index);
        else CmdSelectSlot(index);
    }

    /// <summary>Switch the radio in the given slot on / off.</summary>
    public void RequestTogglePower(int index)
    {
        if (!NetworkMode.IsLocalController(this)) return;
        if (NetworkMode.IsOffline) TogglePower(index);
        else CmdTogglePower(index);
    }

    [Command]
    private void CmdSelectSlot(int index)
    {
        SelectSlot(index);
    }

    private void SelectSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        selectedSlot = index;
        Changed?.Invoke();
        if (voice != null) voice.ServerRefreshRadio();
    }

    [Command]
    private void CmdTogglePower(int index)
    {
        TogglePower(index);
    }

    private void TogglePower(int index)
    {
        if (index < 0 || index >= slots.Count) return;
        InventorySlot slot = slots[index];
        if (slot.IsEmpty || !ItemCatalog.IsRadio(slot.itemId)) return;

        slot.poweredOn = !slot.poweredOn;
        slots[index] = slot;
        if (voice != null) voice.ServerRefreshRadio();
    }

    // ---- Server API (shop, pickups, death) ---------------------------------

    /// <summary>SERVER. Puts an item in the first empty slot. False if full, unknown or already owned.</summary>
    public bool ServerAddItem(string itemId) => ServerAddItem(InventorySlot.Of(itemId));

    public bool ServerAddItem(InventorySlot item)
    {
        string itemId = item.itemId;
        if (!NetworkMode.HasServerAuthority(this) || !ItemCatalog.Exists(itemId)) return false;

        int empty = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].itemId == itemId) return false; // max 1 copy per item type
            if (empty < 0 && slots[i].IsEmpty) empty = i;
        }
        if (empty < 0) return false;

        slots[empty] = item;
        if (voice != null) voice.ServerRefreshRadio();
        return true;
    }

    /// <summary>SERVER. Empties a slot (drop, trade, death). Returns the item ID that was there.</summary>
    public string ServerRemoveAt(int index)
    {
        if (!NetworkMode.HasServerAuthority(this) || index < 0 || index >= slots.Count || slots[index].IsEmpty) return null;
        string removed = slots[index].itemId;
        slots[index] = InventorySlot.Empty;
        if (voice != null) voice.ServerRefreshRadio();
        return removed;
    }
}
