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
    private readonly BoundButton talkButton = new BoundButton();
    private readonly BoundButton powerButton = new BoundButton();

    private GameObject heldModel;
    private WalkieTalkieVisual visual;

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
        if (canUseHands && inventory.IsHoldingRadio && powerButton.WasPressedThisFrame(GameSettings.WalkiePowerBinding))
            inventory.RequestTogglePower(inventory.SelectedSlot);

        // Talk: held, powered walkie + key held down.
        bool wantTalk = canUseHands && inventory.IsHoldingPoweredRadio && talkButton.IsPressed(GameSettings.WalkieTalkBinding);
        voice.RequestRadioTransmit(wantTalk);

        UpdateHeldModel();
    }

    // ---- Held prefab -------------------------------------------------------

    private void UpdateHeldModel()
    {
        bool show = inventory.IsHoldingRadio;
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

        if (heldPrefab == null) heldPrefab = Resources.Load<GameObject>("Items/WalkieTalkie");
        if (heldPrefab == null) { Debug.LogError("Missing held walkie-talkie prefab.", this); return; }
        heldModel = Instantiate(heldPrefab, cam.transform, false);
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
    }
}
