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
    [Header("Held model (placeholder box until there is a real model)")]
    [SerializeField] private Vector3 heldPosition = new Vector3(0.28f, -0.28f, 0.5f);
    [SerializeField] private Vector3 heldRotation = new Vector3(-10f, -15f, 0f);

    private PlayerInventory inventory;
    private PlayerVoice voice;
    private PlayerHealth health;
    private readonly BoundButton talkButton = new BoundButton();
    private readonly BoundButton powerButton = new BoundButton();

    private GameObject heldModel;
    private Renderer ledRenderer;
    private MaterialPropertyBlock ledProperties;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        voice = GetComponent<PlayerVoice>();
        health = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!inventory.isLocalPlayer) return;

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

    // ---- Placeholder model -------------------------------------------------

    private void UpdateHeldModel()
    {
        bool show = inventory.IsHoldingRadio;
        if (show && heldModel == null) BuildHeldModel();
        if (heldModel == null) return;

        if (heldModel.activeSelf != show) heldModel.SetActive(show);
        if (!show) return;

        Color led = !inventory.HeldSlot.poweredOn ? new Color(0.25f, 0.05f, 0.05f)
            : voice.RadioTransmitting ? new Color(1f, 0.2f, 0.15f) : new Color(0.2f, 1f, 0.3f);
        ledProperties.SetColor("_BaseColor", led);
        ledProperties.SetColor("_Color", led);
        ledRenderer.SetPropertyBlock(ledProperties);
    }

    private void BuildHeldModel()
    {
        Camera cam = GetComponentInChildren<Camera>();
        if (cam == null) return;

        heldModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        heldModel.name = "Held Walkie-Talkie";
        Destroy(heldModel.GetComponent<Collider>());
        heldModel.transform.SetParent(cam.transform, false);
        heldModel.transform.localPosition = heldPosition;
        heldModel.transform.localRotation = Quaternion.Euler(heldRotation);
        heldModel.transform.localScale = new Vector3(0.07f, 0.16f, 0.04f);
        var body = new MaterialPropertyBlock();
        body.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.13f));
        body.SetColor("_Color", new Color(0.12f, 0.12f, 0.13f));
        heldModel.GetComponent<Renderer>().SetPropertyBlock(body);

        GameObject antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(antenna.GetComponent<Collider>());
        antenna.transform.SetParent(heldModel.transform, false);
        antenna.transform.localPosition = new Vector3(0.3f, 0.75f, 0f);
        antenna.transform.localScale = new Vector3(0.15f, 0.35f, 0.3f);
        antenna.GetComponent<Renderer>().SetPropertyBlock(body);

        GameObject led = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(led.GetComponent<Collider>());
        led.transform.SetParent(heldModel.transform, false);
        led.transform.localPosition = new Vector3(-0.25f, 0.42f, -0.55f);
        led.transform.localScale = new Vector3(0.25f, 0.1f, 0.3f);
        ledRenderer = led.GetComponent<Renderer>();
        ledProperties = new MaterialPropertyBlock();

        // Seen only by this player (first-person); never casts a shadow into the world.
        foreach (Renderer r in heldModel.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
