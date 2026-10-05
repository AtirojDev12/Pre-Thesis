using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>One cancellable local action; online tank changes are committed by the server.</summary>
public sealed class PopcornPreparation : MonoBehaviour
{
    public static PopcornPreparation Instance { get; private set; }
    public const float PreparationSeconds = 3f;
    public ItemHoldingSystem Holder { get; private set; }
    public PopcornStationAudio Audio { get; private set; }
    public bool IsPreparing => activeStation != null;
    public int Capacity { get; private set; } = 20;
    public int RefillServings { get; private set; } = 10;
    public int TankRemaining => NetworkMode.IsOffline ? offlineRemaining :
        PopcornNetSync.Instance != null ? PopcornNetSync.Instance.TankRemaining : 0;
    public int TankCapacity => NetworkMode.IsOffline ? Capacity :
        PopcornNetSync.Instance != null ? PopcornNetSync.Instance.TankCapacity : Capacity;
    private int offlineRemaining = 20;
    private readonly List<PopcornStation> stations = new List<PopcornStation>();
    private PopcornStation activeStation;
    private PlayerHealth preparingPlayer;
    private float elapsed;
    private int requestId;
    private bool awaitingResult;
    private bool awaitingStart;
    private bool refilling;
    private GameObject progressPanel;
    private RectTransform progressFill;
    private TMP_Text progressText;
    private TMP_Text statusText;
    private Canvas tankCanvas;
    private TMP_Text tankText;
    private RectTransform tankFill;
    private float statusUntil;

    public void Configure(ItemHoldingSystem holder)
    {
        Instance = this;
        Holder = holder;
        Audio = gameObject.AddComponent<PopcornStationAudio>();
        Audio.Configure(this);
        Holder.RefillDiscarded += DiscardRefill;
        BuildUi();
    }

    public void ConfigureSupply(int capacity, int refill)
    {
        Capacity = Mathf.Max(1, capacity);
        RefillServings = Mathf.Max(1, refill);
        offlineRemaining = Capacity;
    }

    public PopcornStation StationAt(int id) => id >= 0 && id < stations.Count ? stations[id] : null;

    public void AddStation(Transform target, PopcornStationKind kind, PopcornFlavor flavor = PopcornFlavor.None)
    {
        if (target == null)
        {
            Debug.LogWarning("[Popcorn] Assign the " + kind + " station on Popcorn Minigame Systems.", this);
            return;
        }
        PopcornStation station = target.GetComponent<PopcornStation>();
        if (station == null) station = target.gameObject.AddComponent<PopcornStation>();
        station.Configure(this, kind, flavor, stations.Count);
        stations.Add(station);
        if (kind == PopcornStationKind.Tank) BuildTankUi(target);
    }

    private bool CanPrepare(PopcornStation station)
    {
        if (station.Kind == PopcornStationKind.Maker) return Holder.CanMakeRefill;
        if (station.Kind == PopcornStationKind.Tank) return Holder.CanScoop && TankRemaining > 0;
        return station.Kind == PopcornStationKind.Water && Holder.HasItem && Holder.IsCup && !Holder.IsReady;
    }

    public void Interact(PopcornStation station)
    {
        if (Holder == null || Holder.SubmissionPending || IsPreparing || GameplayInput.Blocked) return;
        if (GhostFavorRecovery.Instance != null && GhostFavorRecovery.Instance.IsLocalCarrier) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        if (player == null || player.IsDead || player.IsDowned) return;
        if (station.Kind == PopcornStationKind.Bucket || station.Kind == PopcornStationKind.Cup)
        {
            bool cup = station.Kind == PopcornStationKind.Cup;
            bool pickedUp = Holder.PickUp(cup);
            if (pickedUp) Audio.OneShot(station, Holder.StateRevision);
            ShowStatus(pickedUp ? (cup ? "Cup picked up · Choose a drink dispenser" : "Bucket picked up · Scoop from PopCornTank") :
                Holder.HasItem ? "Hands full · R to discard" : "Cannot pick up container · Check prefab setup");
            return;
        }
        if (station.Kind == PopcornStationKind.Ghost)
        {
            if (GhostFavorRecovery.Instance != null) GhostFavorRecovery.Instance.RequestInteraction();
            else MixGhostAtHome();
            return;
        }
        if (station.Kind == PopcornStationKind.Scoop)
        {
            bool flavored = Holder.Hold(station.Flavor);
            if (flavored) Audio.OneShot(station, Holder.StateRevision);
            ShowStatus(flavored ? UiFactory.ItemName(station.Flavor) + " ready · Ghost customer? Add Ghost Flavor" : station.GetInteractionPrompt());
            return;
        }
        bool refill = station.Kind == PopcornStationKind.Tank && Holder.IsRefill;
        if (refill && TankRemaining >= TankCapacity)
        {
            ShowStatus("Tank full · Keep NewPopcorn for later");
            return;
        }
        if (!refill && !CanPrepare(station))
        {
            ShowStatus(station.GetInteractionPrompt());
            return;
        }
        if (!NetworkMode.IsOffline && (PopcornNetSync.Instance == null || !Mirror.NetworkClient.ready))
        {
            ShowStatus("Waiting for the server");
            return;
        }
        activeStation = station;
        preparingPlayer = player;
        elapsed = 0f;
        requestId++;
        awaitingResult = refill;
        refilling = refill;
        statusText.text = string.Empty;
        if (refill)
        {
            if (NetworkMode.IsOffline)
            {
                offlineRemaining = Mathf.Min(Capacity, offlineRemaining + RefillServings);
                ReceiveSupplyResult(requestId, true, "Tank refilled");
            }
            else PopcornNetSync.Instance.RequestSupply(station.Id, requestId, PopcornSupplyAction.Refill);
            return;
        }
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressPanel.SetActive(true);
        UpdateProgress();
        if (!NetworkMode.IsOffline)
        {
            awaitingStart = true;
            PopcornNetSync.Instance.RequestSupply(station.Id, requestId, PopcornSupplyAction.Begin);
        }
        else Audio.Begin(0, new PopcornSoundAction { station = station.Id, request = requestId, started = Time.timeAsDouble });
    }

    private void Update()
    {
        if (Holder == null) return;
        if (GhostFavorRecovery.Instance != null && GhostFavorRecovery.Instance.IsLocalCarrier)
        {
            if (!awaitingResult) Cancel(false);
            ShowStatus(GhostFavorRecovery.Instance.Prompt);
            return;
        }
        Keyboard keyboard = Keyboard.current;
        if (!Holder.SubmissionPending && !awaitingResult && !GameplayInput.Blocked && keyboard != null && keyboard.rKey.wasPressedThisFrame && Holder.HasItem &&
            (GhostFavorRecovery.Instance == null || !GhostFavorRecovery.Instance.DroppedThisFrame))
        {
            Cancel(false);
            Holder.Consume();
            ShowStatus("Item discarded · Pick up a new bucket or cup");
        }
        if (statusText != null && Time.time >= statusUntil) statusText.text = string.Empty;
        if (!IsPreparing || awaitingResult) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        PlayerInteractor interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (GameplayInput.Blocked || keyboard == null || !keyboard.eKey.isPressed || player == null || player != preparingPlayer ||
            player.IsDead || player.IsDowned || !CanPrepare(activeStation) ||
            interactor == null || !interactor.isActiveAndEnabled || camera == null ||
            !ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), activeStation))
        {
            Cancel(true);
            return;
        }
        // Start the local timer after server acknowledgement. Packet timing then
        // cannot make an honest three-second hold look too short on the server.
        if (awaitingStart) return;
        elapsed += Time.deltaTime;
        UpdateProgress();
        if (elapsed < PreparationSeconds) return;
        if (NetworkMode.IsOffline && activeStation.Kind == PopcornStationKind.Water)
        {
            PopcornFlavor flavor = activeStation.Flavor == PopcornFlavor.None ? PopcornFlavor.Drink : activeStation.Flavor;
            bool completed = Holder.Hold(flavor);
            Cancel(false);
            ShowStatus(completed ? UiFactory.ItemName(flavor) + " ready · Ghost customer? Add Ghost Flavor" : "Unable to fill · Check held prefab setup");
        }
        else if (NetworkMode.IsOffline)
        {
            bool success = activeStation.Kind == PopcornStationKind.Maker || offlineRemaining > 0;
            if (success && activeStation.Kind == PopcornStationKind.Tank) offlineRemaining--;
            ReceiveSupplyResult(requestId, success, success ? "" : "Tank empty · Make NewPopcorn first");
        }
        else
        {
            Audio.StopLocal(requestId);
            awaitingResult = true;
            progressText.text = "FINISHING · Waiting for server";
            PopcornNetSync.Instance.RequestSupply(activeStation.Id, requestId, PopcornSupplyAction.Complete);
        }
    }

    public void ReceiveSupplyResult(int id, bool success, string message)
    {
        if (id != requestId || activeStation == null) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        bool alive = player != null && player == preparingPlayer && !player.IsDead && !player.IsDowned;
        if (success && refilling) Holder.Consume();
        else if (success && alive)
        {
            success = activeStation.Kind == PopcornStationKind.Maker ? Holder.HoldRefill() :
                activeStation.Kind == PopcornStationKind.Water ? Holder.Hold(activeStation.Flavor == PopcornFlavor.None ? PopcornFlavor.Drink : activeStation.Flavor) : Holder.Scoop();
            message = success ? (Holder.IsRefill ? "NewPopcorn ready · Press E at PopCornTank to refill" :
                activeStation.Kind == PopcornStationKind.Water ? UiFactory.ItemName(Holder.HeldFlavor) + " ready · Ghost customer? Add Ghost Flavor" : "Popcorn scooped · Choose a flavor") : "Unable to hold item";
        }
        else if (success && activeStation.Kind == PopcornStationKind.Maker) DiscardRefill();
        Cancel(false, false);
        ShowStatus(message);
    }

    public void RejectSupplyBegin(int id, string message)
    {
        if (id != requestId || activeStation == null) return;
        Cancel(false, false);
        ShowStatus(message);
    }

    public void AcceptSupplyBegin(int id)
    {
        if (id == requestId && activeStation != null) awaitingStart = false;
    }

    private void DiscardRefill()
    {
        if (!NetworkMode.IsOffline && PopcornNetSync.Instance != null)
            PopcornNetSync.Instance.RequestDiscardRefill();
    }

    private void UpdateProgress()
    {
        float fraction = Mathf.Clamp01(elapsed / PreparationSeconds);
        progressFill.anchorMax = new Vector2(fraction, 1f);
        string action = activeStation.Kind == PopcornStationKind.Maker ? "MAKING NEW POPCORN" :
            activeStation.Kind == PopcornStationKind.Tank ? "SCOOPING POPCORN" : "FILLING " + UiFactory.FlavorName(activeStation.Flavor).ToUpperInvariant();
        progressText.text = action + $"  {Mathf.CeilToInt(fraction * 100f)}%\nHold E and keep aiming · {Mathf.Max(0f, PreparationSeconds - elapsed):0.0}s";
    }

    private void LateUpdate()
    {
        if (tankText == null) return;
        bool ready = NetworkMode.IsOffline || PopcornNetSync.Instance != null;
        tankText.text = ready ? $"POPCORN TANK  {TankRemaining} / {TankCapacity}\n" +
            (TankRemaining == 0 ? "EMPTY · Make NewPopcorn to refill" : "servings remaining") : "POPCORN TANK · Connecting...";
        tankFill.anchorMax = new Vector2(Mathf.Clamp01((float)TankRemaining / Mathf.Max(1, TankCapacity)), 1f);
        PlayerHealth player = PlayerHealth.LocalInstance;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (camera != null && tankCanvas != null)
        {
            tankCanvas.worldCamera = camera;
            tankCanvas.transform.rotation = camera.transform.rotation;
        }
    }

    public void MixGhostAtHome()
    {
        bool mixed = Holder.MixGhost();
        if (mixed)
        {
            var station = stations.Find(s => s.Kind == PopcornStationKind.Ghost);
            if (station != null) Audio.OneShot(station, Holder.StateRevision);
        }
        ShowStatus(mixed ? "Ghost flavor added · Ready to serve" : "Fill and flavor popcorn or fill a drink before mixing");
    }

    private void Cancel(bool notify, bool send = true)
    {
        if (activeStation != null) Audio?.StopLocal(requestId);
        if (send && activeStation != null && !awaitingResult && !NetworkMode.IsOffline && PopcornNetSync.Instance != null)
            PopcornNetSync.Instance.RequestSupply(activeStation.Id, requestId, PopcornSupplyAction.Cancel);
        activeStation = null;
        preparingPlayer = null;
        elapsed = 0f;
        awaitingResult = false;
        awaitingStart = false;
        refilling = false;
        if (progressPanel != null) progressPanel.SetActive(false);
        if (notify) ShowStatus("Cancelled · Aim at the station and hold E to restart");
    }

    private void ShowStatus(string message)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusUntil = Time.time + 4f;
    }

    private void OnDisable() => Cancel(false);
    private void OnDestroy()
    {
        if (Holder != null) Holder.RefillDiscarded -= DiscardRefill;
        if (tankCanvas != null) Destroy(tankCanvas.gameObject);
        if (Instance == this) Instance = null;
    }

    private void BuildTankUi(Transform target)
    {
        GameObject display = new GameObject("Tank Supply Display", typeof(RectTransform), typeof(Canvas));
        display.transform.SetParent(target, false);
        tankCanvas = display.GetComponent<Canvas>();
        tankCanvas.renderMode = RenderMode.WorldSpace;
        display.GetComponent<RectTransform>().sizeDelta = new Vector2(360f, 125f);
        Vector3 scale = target.lossyScale;
        display.transform.localScale = new Vector3(
            0.002f / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            0.002f / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
            0.002f / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool foundBounds = false;
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            if (!foundBounds) { bounds = collider.bounds; foundBounds = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        // Position only the new label above the tank, leaving all authored props intact.
        display.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.18f, bounds.center.z);
        GameObject tank = UiFactory.CreatePanel("Tank Supply", display.transform, new Color(0.025f, 0.035f, 0.05f, 0.92f));
        UiFactory.Stretch(tank.GetComponent<RectTransform>());
        tankText = UiFactory.CreateText("Remaining Popcorn", tank.transform, "", 25f, Color.white);
        tankText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(tankText.rectTransform, new Vector2(0.04f, 0.3f), new Vector2(0.96f, 0.95f));
        GameObject tankTrack = UiFactory.CreatePanel("Supply Track", tank.transform, new Color(0.2f, 0.23f, 0.28f));
        UiFactory.SetRect(tankTrack.GetComponent<RectTransform>(), new Vector2(0.05f, 0.1f), new Vector2(0.95f, 0.23f));
        tankFill = UiFactory.CreatePanel("Supply Fill", tankTrack.transform, new Color(1f, 0.78f, 0.18f)).GetComponent<RectTransform>();
        UiFactory.Stretch(tankFill);
    }

    private void BuildUi()
    {
        GameObject root = new GameObject("Preparation HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 60;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        progressPanel = UiFactory.CreatePanel("Preparation Progress", root.transform, new Color(0.025f, 0.035f, 0.05f, 0.96f));
        RectTransform rect = progressPanel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.28f);
        rect.sizeDelta = new Vector2(640f, 125f);
        progressText = UiFactory.CreateText("Action", progressPanel.transform, string.Empty, 28f, Color.white);
        progressText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(progressText.rectTransform, new Vector2(0.03f, 0.33f), new Vector2(0.97f, 0.96f));
        GameObject track = UiFactory.CreatePanel("Track", progressPanel.transform, new Color(0.2f, 0.23f, 0.28f));
        UiFactory.SetRect(track.GetComponent<RectTransform>(), new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.26f));
        GameObject fill = UiFactory.CreatePanel("Fill", track.transform, Color.white);
        progressFill = fill.GetComponent<RectTransform>();
        UiFactory.Stretch(progressFill);
        progressPanel.SetActive(false);

        GameObject status = UiFactory.CreatePanel("Preparation Feedback", root.transform, new Color(0.025f, 0.035f, 0.05f, 0.92f));
        rect = status.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.1f);
        rect.sizeDelta = new Vector2(820f, 65f);
        statusText = UiFactory.CreateText("Status", status.transform, string.Empty, 28f, Color.white);
        statusText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(statusText.rectTransform, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f));
        // Hide the background along with expired feedback.
        status.AddComponent<PopcornStatusBackdrop>().Configure(statusText);
    }
}

public sealed class PopcornStatusBackdrop : MonoBehaviour
{
    private TMP_Text text;
    private Image background;
    public void Configure(TMP_Text label) { text = label; background = GetComponent<Image>(); }
    private void LateUpdate() { if (background != null) background.enabled = text != null && !string.IsNullOrEmpty(text.text); }
}
