using UnityEngine;

/// <summary>
/// Local player's Walkie-Talkie input and the walkie model in your hand.
///
///   Walkie in hand + HOLD the talk key   = talk on the radio
///   Walkie in hand + PRESS the power key = switch it on / off
///
/// Both keys are rebindable in Settings > Controls (any key or Mouse0..4).
/// Defaults: talk = Left Mouse, power = Right Mouse.
///
/// Input is ignored while a menu is open (GameplayInput.Blocked) or the cursor
/// is free (e.g. the popcorn screen), so clicking UI never keys the radio.
/// Everything that matters is decided on the server (PlayerVoice /
/// PlayerInventory); this script only asks.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory), typeof(PlayerVoice))]
public class WalkieTalkieController : MonoBehaviour
{
    [Header("Held walkie-talkie prefab")]
    [SerializeField] private GameObject heldPrefab;
    [SerializeField] private Vector3 heldPosition = new Vector3(0.28f, -0.28f, 0.5f);
    [SerializeField] private Vector3 heldRotation = new Vector3(-10f, -15f, 0f);

    private PlayerInventory inventory;
    private PlayerVoice voice;
    private PlayerHealth health;

    private GameObject heldModel;
    private string heldModelFor; // 17 Nov: free and paid walkies have their own model
    private WalkieTalkieVisual visual;
    private HudBar batteryBar;    // 17 Nov: the free walkie's battery (GAME HUD bar "walkie_battery")
    private bool batteryShown;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        voice = GetComponent<PlayerVoice>();
        health = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!NetworkMode.IsLocalController(inventory)) return;

        bool canUseHands = !GameplayInput.Blocked
            && Cursor.lockState == CursorLockMode.Locked
            && (health == null || (!health.IsDead && !health.IsDowned));

        // Power: only the walkie in your hand.
        if (canUseHands && inventory.IsHoldingRadio && Keybinds.WasPressed(GameAction.WalkiePower))
            inventory.RequestTogglePower(inventory.SelectedSlot);

        // Talk: held, powered walkie + key held down.
        bool wantTalk = canUseHands && inventory.IsHoldingPoweredRadio && Keybinds.IsPressed(GameAction.WalkieTalk);
        // 5 Nov (Mr.k): downed = any switched-on walkie in the hotbar works (no need to hold it).
        bool downed = health != null && health.IsDowned && !health.IsDead;
        if (downed && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked
            && inventory.HasPoweredRadio && Keybinds.IsPressed(GameAction.WalkieTalk))
            wantTalk = true;
        voice.RequestRadioTransmit(wantTalk);

        UpdateHeldModel();
        UpdateBatteryBar();
    }

    /// <summary>17 Nov: while the free walkie is in your hand, its battery shows in the GAME HUD.</summary>
    private void UpdateBatteryBar()
    {
        bool show = inventory.IsHoldingRadio && ItemCatalog.UsesWalkieBattery(inventory.HeldSlot.itemId)
            && (health == null || !health.IsDead);
        if (batteryBar == null) batteryBar = HudBar.Find(HudBar.WalkieBattery);
        if (batteryBar == null) return; // no bar in the GAME HUD: the hotbar still shows the %
        if (show != batteryShown) { batteryShown = show; batteryBar.Show(show); }
        if (!show) return;
        InventorySlot slot = inventory.HeldSlot;
        batteryBar.Set(slot.charge);
        bool low = slot.charge < 0.2f;
        batteryBar.SetFillColour(low ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.85f, 0.45f));
        batteryBar.SetLabel(slot.charge <= 0f
            ? ItemCatalog.DisplayName(slot.itemId) + "  EMPTY"
            : ItemCatalog.DisplayName(slot.itemId) + "  " + Mathf.CeilToInt(slot.charge * 100f) + "%" + (slot.poweredOn ? "" : "  (off)"));
    }

    // ---- Held prefab -------------------------------------------------------

    private void UpdateHeldModel()
    {
        bool show = inventory.IsHoldingRadio;
        if (show && heldModel != null && heldModelFor != inventory.HeldSlot.itemId) { Destroy(heldModel); heldModel = null; }
        if (show && heldModel == null) BuildHeldModel();
        if (heldModel == null) return;

        if (heldModel.activeSelf != show) heldModel.SetActive(show);
        if (!show) return;

        if (visual != null) visual.SetPower(inventory.HeldSlot.poweredOn, voice.RadioTransmitting);
    }

    private void BuildHeldModel()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        // 17 Nov: each walkie has its own model (ItemCatalog.heldPrefabPath). The Inspector slot is the paid one.
        string id = inventory.HeldSlot.itemId;
        ItemCatalog.ItemInfo info = ItemCatalog.Find(id);
        GameObject prefab = id == ItemCatalog.WalkieTalkie && heldPrefab != null ? heldPrefab
            : ItemCatalog.LoadPrefab(info != null ? info.heldPrefabPath : null, "Items/WalkieTalkie");
        if (prefab == null) { Debug.LogError("Missing held walkie-talkie prefab.", this); return; }
        heldModelFor = id;
        heldModel = Instantiate(prefab, cam.transform, false);
        heldModel.name = "Held Walkie-Talkie";
        heldModel.transform.localPosition = heldPosition;
        heldModel.transform.localRotation = Quaternion.Euler(heldRotation);
        visual = heldModel.GetComponent<WalkieTalkieVisual>();
        foreach (Renderer renderer in heldModel.GetComponentsInChildren<Renderer>())
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void OnDisable()
    {
        if (inventory != null && inventory.isLocalPlayer && voice != null) voice.RequestRadioTransmit(false);
    }

    private void OnDestroy()
    {
        if (heldModel != null) Destroy(heldModel);
        if (batteryBar != null && batteryShown) batteryBar.Show(false);
    }
}
