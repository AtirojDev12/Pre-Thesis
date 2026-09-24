using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Single cancellable preparation action per local inventory.</summary>
public sealed class PopcornPreparation : MonoBehaviour
{
    public const float PreparationSeconds = 3f;
    public ItemHoldingSystem Holder { get; private set; }
    public bool IsPreparing => activeStation != null;
    private PopcornStation activeStation;
    private PlayerHealth preparingPlayer;
    private float elapsed;
    private GameObject progressPanel;
    private RectTransform progressFill;
    private TMP_Text progressText;
    private TMP_Text statusText;
    private float statusUntil;

    public void Configure(ItemHoldingSystem holder)
    {
        Holder = holder;
        BuildUi();
    }

    public void AddStation(Transform target, PopcornStationKind kind, PopcornFlavor flavor = PopcornFlavor.None)
    {
        if (target == null)
        {
            Debug.LogWarning("[Popcorn] Assign the " + kind + " station on Popcorn Minigame Systems.", this);
            return;
        }
        PopcornStation station = target.GetComponent<PopcornStation>();
        if (station == null) station = target.gameObject.AddComponent<PopcornStation>();
        station.Configure(this, kind, flavor);
    }

    public void Interact(PopcornStation station)
    {
        if (Holder == null || IsPreparing) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        if (player == null || player.IsDead || player.IsDowned) return;
        if (station.Kind == PopcornStationKind.Bucket || station.Kind == PopcornStationKind.Cup)
        {
            bool cup = station.Kind == PopcornStationKind.Cup;
            ShowStatus(Holder.PickUp(cup) ? (cup ? "Cup picked up · Find water dispenser" : "Bucket picked up · Choose a flavor station") :
                Holder.HasItem ? "Hands full · R to discard" : "Cannot pick up container · Check prefab setup");
            return;
        }
        if (station.Kind == PopcornStationKind.Ghost)
        {
            ShowStatus(Holder.MixGhost() ? "Ghost flavor added · Ready to serve" : station.GetInteractionPrompt());
            return;
        }
        bool water = station.Kind == PopcornStationKind.Water;
        if (!Holder.HasItem || Holder.IsReady || Holder.IsCup != water)
        {
            ShowStatus(station.GetInteractionPrompt());
            return;
        }
        activeStation = station;
        preparingPlayer = player;
        elapsed = 0f;
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressPanel.SetActive(true);
        statusText.text = string.Empty;
        UpdateProgress();
    }

    private void Update()
    {
        if (Holder == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame && Holder.HasItem)
        {
            Cancel(false);
            Holder.Consume();
            ShowStatus("Item discarded · Pick up a new bucket or cup");
        }
        if (statusText != null && Time.time >= statusUntil) statusText.text = string.Empty;
        if (!IsPreparing) return;

        PlayerHealth player = PlayerHealth.LocalInstance;
        PlayerInteractor interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (keyboard == null || !keyboard.eKey.isPressed || player == null || player != preparingPlayer ||
            player.IsDead || player.IsDowned || !Holder.HasItem || Holder.IsReady ||
            interactor == null || !interactor.isActiveAndEnabled || camera == null ||
            !ReferenceEquals(interactor.GetInteractableAlongRay(new Ray(camera.transform.position, camera.transform.forward)), activeStation))
        {
            Cancel(true);
            return;
        }
        elapsed += Time.deltaTime;
        UpdateProgress();
        if (elapsed < PreparationSeconds) return;
        PopcornFlavor flavor = activeStation.Kind == PopcornStationKind.Water ? PopcornFlavor.Drink : activeStation.Flavor;
        bool completed = Holder.Hold(flavor);
        Cancel(false);
        ShowStatus(completed ? UiFactory.ItemName(flavor) + " ready · Ghost customer? Add Ghost Flavor" : "Unable to fill · Check held prefab setup");
    }

    private void UpdateProgress()
    {
        float fraction = Mathf.Clamp01(elapsed / PreparationSeconds);
        progressFill.anchorMax = new Vector2(fraction, 1f);
        progressText.text = (activeStation.Kind == PopcornStationKind.Water ? "FILLING WATER" :
            "SCOOPING " + UiFactory.FlavorName(activeStation.Flavor).ToUpperInvariant()) +
            $"  {Mathf.CeilToInt(fraction * 100f)}%\nHold E and keep aiming · {Mathf.Max(0f, PreparationSeconds - elapsed):0.0}s";
    }

    private void Cancel(bool notify)
    {
        activeStation = null;
        preparingPlayer = null;
        elapsed = 0f;
        if (progressPanel != null) progressPanel.SetActive(false);
        if (notify) ShowStatus("Cancelled · Aim at the station and hold E to restart");
    }

    private void ShowStatus(string message)
    {
        statusText.text = message;
        statusUntil = Time.time + 4f;
    }

    private void OnDisable() => Cancel(false);

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
