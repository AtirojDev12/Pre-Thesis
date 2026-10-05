using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 5 Oct (Mr.k). The flashlight in a player's hand: the beam, the battery drain,
/// the input, and the held model. Added at runtime by PlayerInventory (no prefab edit).
///
///   Flashlight in hand + Left Click (or F) = switch on / off
///   BASIC flashlight + mash SPACE          = charge the battery by hand
///   PAID flashlight  + R                   = put in a new Battery item (5 Oct, Mr.k)
///
/// WHO DECIDES WHAT
///   - On/off and battery live in the hotbar slot (InventorySlot.poweredOn / .charge),
///     a server-written SyncList. So EVERY machine knows whose light is on and
///     draws that beam: teammates see your light.
///   - The SERVER drains the battery. The owner only asks (toggle, crank).
///   - The beam only shines while the flashlight is the SELECTED slot (in hand).
///     Pocketed = no light and no drain.
///
/// Light numbers (range, brightness, cone, battery) are per item in ItemCatalog.
/// </summary>
[DisallowMultipleComponent]
public sealed class FlashlightController : MonoBehaviour
{
    /// <summary>Shown in the hotbar hint.</summary>
    public const string ToggleHint = "Left Click / F";

    /// <summary>How often the server writes the drained battery into the SyncList (seconds).</summary>
    private const float DrainWriteInterval = 0.5f;

    // Where the beam starts, relative to the eyes: a little right and below,
    // like a light held in the right hand.
    private static readonly Vector3 BeamOffset = new Vector3(0.18f, -0.16f, 0.25f);
    private static readonly Vector3 HeldModelPosition = new Vector3(0.24f, -0.26f, 0.45f);

    private PlayerInventory inventory;
    private PlayerHealth health;
    private PlayerVoice voice;
    private PlayerMovement movement;
    private FirstPersonCamera view;
    private Camera ownCamera;

    private Light beam;
    private GameObject heldModel;
    private FlashlightVisual heldVisual;
    private FlashlightHUD hud;

    private float drainPending;
    private float drainTimer;
    private float flickerSeed;

    private bool IsLocal => NetworkMode.IsLocalController(inventory);

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();
        voice = GetComponent<PlayerVoice>();
        movement = GetComponent<PlayerMovement>();
        view = GetComponentInChildren<FirstPersonCamera>(true);
        flickerSeed = Random.value * 100f;
    }

    private void Update()
    {
        if (inventory == null) return;
        if (NetworkMode.HasServerAuthority(inventory)) ServerDrain();
        if (IsLocal) LocalInput();
    }

    private void LateUpdate()
    {
        if (inventory == null) return;
        UpdateBeam();
        if (IsLocal)
        {
            UpdateHeldModel();
            UpdateHud();
        }
    }

    // ---- Server: battery -----------------------------------------------------

    private void ServerDrain()
    {
        if (!inventory.IsFlashlightShining)
        {
            drainPending = 0f;
            drainTimer = 0f;
            return;
        }

        ItemCatalog.ItemInfo info = ItemCatalog.Find(inventory.HeldSlot.itemId);
        if (info == null || info.batterySeconds <= 0f) return;

        drainPending += Time.deltaTime / info.batterySeconds;
        drainTimer += Time.deltaTime;
        float charge = inventory.HeldSlot.charge;
        // Write twice a second (not every frame), or at once when it runs out.
        if (drainTimer >= DrainWriteInterval || drainPending >= charge)
        {
            inventory.ServerSetCharge(inventory.SelectedSlot, charge - drainPending);
            drainPending = 0f;
            drainTimer = 0f;
        }
    }

    // ---- Owner: input --------------------------------------------------------

    private void LocalInput()
    {
        if (!inventory.IsHoldingFlashlight) return;
        bool canUseHands = !GameplayInput.Blocked
            && Cursor.lockState == CursorLockMode.Locked
            && (health == null || !health.IsDead);
        if (!canUseHands) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool toggle = (keyboard != null && keyboard.fKey.wasPressedThisFrame)
            || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        if (toggle && (health == null || !health.IsDowned))
            inventory.RequestTogglePower(inventory.SelectedSlot);

        ItemCatalog.ItemInfo info = ItemCatalog.Find(inventory.HeldSlot.itemId);
        if (info == null || keyboard == null) return;

        // Basic: charging by hand works while downed too, something to do while you wait for help.
        if (info.secondsPerCrank > 0f && keyboard.spaceKey.wasPressedThisFrame)
            inventory.RequestCrank();

        // Paid: R swaps in a Battery from your hotbar. (R also drops the carried ghost
        // favor item; that one wins so a single press never does both.)
        if (info.usesBatteries && keyboard.rKey.wasPressedThisFrame && !CarryingGhostFavor())
            inventory.RequestReload();
    }

    private static bool CarryingGhostFavor()
    {
        GhostFavorRecovery favor = FindAnyObjectByType<GhostFavorRecovery>();
        return favor != null && favor.IsLocalCarrier;
    }

    // ---- Every machine: the beam --------------------------------------------

    private void UpdateBeam()
    {
        bool dead = health != null && health.IsDead;
        bool shine = !dead && inventory.IsFlashlightShining;
        if (!shine)
        {
            if (beam != null && beam.enabled) beam.enabled = false;
            return;
        }

        ItemCatalog.ItemInfo info = ItemCatalog.Find(inventory.HeldSlot.itemId);
        if (info == null) return;
        if (beam == null) CreateBeam();
        if (beam == null) return;

        PlaceBeam();
        beam.range = info.lightRange;
        beam.spotAngle = info.spotAngle;
        FlashlightTuning.Look look = FlashlightTuning.Current.look; // designer values, live
        beam.innerSpotAngle = info.spotAngle * look.brightCentre;
        beam.intensity = info.lightIntensity * FlickerFactor(inventory.HeldSlot.charge, look);
        beam.color = look.beamColor;
        LightShadows shadows = IsLocal && look.ownBeamShadows ? LightShadows.Soft : LightShadows.None;
        if (beam.shadows != shadows) beam.shadows = shadows;
        beam.enabled = true;
    }

    /// <summary>1 = steady. Below look.flickerBelow the light stutters, worse as it empties.</summary>
    private float FlickerFactor(float charge, FlashlightTuning.Look look)
    {
        if (look.flickerBelow <= 0f || charge >= look.flickerBelow) return 1f;
        float weak = 1f - charge / look.flickerBelow; // 0 -> 1 as the battery empties
        float noise = Mathf.PerlinNoise(flickerSeed, Time.time * 9f);
        float dip = noise < 0.25f * weak ? 0.15f : 1f; // short drop-outs
        return Mathf.Lerp(1f, look.dimWhenEmpty, weak) * dip;
    }

    private void CreateBeam()
    {
        var go = new GameObject("Flashlight Beam");
        if (IsLocal)
        {
            // Owner: hang the beam on the camera, so it follows the mouse exactly.
            ownCamera = GetComponentInChildren<Camera>();
            if (ownCamera == null) { Destroy(go); return; }
            go.transform.SetParent(ownCamera.transform, false);
            go.transform.localPosition = BeamOffset;
            go.transform.localRotation = Quaternion.identity;
        }
        else
        {
            go.transform.SetParent(transform, false);
        }

        beam = go.AddComponent<Light>();
        beam.type = LightType.Spot;
        // Colour and shadows are set every frame in UpdateBeam (designer can change them live).
        // Only YOUR beam may cast shadows: six shadowed spot lights would cost too much.
        beam.shadows = LightShadows.None;
    }

    /// <summary>Remote players: aim the beam with their synced look angles.</summary>
    private void PlaceBeam()
    {
        if (IsLocal) return; // parented to the camera
        bool crouching = movement != null && movement.IsCrouching;
        Vector3 eyes = view != null ? view.EyePosition(crouching) : transform.position + Vector3.up * 1.6f;
        float pitch = voice != null ? voice.LookPitch : 0f;
        float yaw = voice != null ? voice.LookYawOffset : 0f;
        Quaternion look = transform.rotation * Quaternion.Euler(pitch, yaw, 0f);
        // Look angles arrive ~10 times a second: ease toward them so the beam does not step.
        Quaternion shown = Quaternion.Slerp(beam.transform.rotation, look, 1f - Mathf.Exp(-15f * Time.deltaTime));
        beam.transform.SetPositionAndRotation(eyes + look * BeamOffset, shown);
    }

    // ---- Owner: model in hand + HUD ------------------------------------------

    private void UpdateHeldModel()
    {
        bool show = inventory.IsHoldingFlashlight && (health == null || !health.IsDead);
        if (show && heldModel == null) BuildHeldModel();
        if (heldModel == null) return;
        if (heldModel.activeSelf != show) heldModel.SetActive(show);
        if (show && heldVisual != null) heldVisual.Show(inventory.HeldSlot, false);
    }

    private void BuildHeldModel()
    {
        Camera cam = GetComponentInChildren<Camera>();
        GameObject prefab = Resources.Load<GameObject>("Items/Flashlight");
        if (cam == null || prefab == null) return; // no model yet: the beam still works
        heldModel = Instantiate(prefab, cam.transform, false);
        heldModel.name = "Held Flashlight";
        heldModel.transform.localPosition = HeldModelPosition;
        heldModel.transform.localRotation = Quaternion.identity;
        heldVisual = heldModel.GetComponent<FlashlightVisual>();
        foreach (Renderer r in heldModel.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (Collider c in heldModel.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    private void UpdateHud()
    {
        bool show = inventory.IsHoldingFlashlight && (health == null || !health.IsDead);
        if (!show)
        {
            if (hud != null) hud.Hide();
            return;
        }
        if (hud == null) hud = FlashlightHUD.Create();
        InventorySlot slot = inventory.HeldSlot;
        ItemCatalog.ItemInfo info = ItemCatalog.Find(slot.itemId);
        bool batteries = info != null && info.usesBatteries;
        hud.Show(ItemCatalog.DisplayName(slot.itemId), slot.charge, slot.poweredOn,
            batteries, batteries ? inventory.UnitsOf(ItemCatalog.Battery) : 0);
    }

    private void OnDestroy()
    {
        if (beam != null) Destroy(beam.gameObject);
        if (heldModel != null) Destroy(heldModel);
        if (hud != null) Destroy(hud.gameObject);
    }
}
