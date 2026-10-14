using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Popcorn recipes, HUD and Mirror publication over the reusable holding component.</summary>
[RequireComponent(typeof(ItemHoldingSystem))]
public sealed class PopcornItemHoldingSystem : MonoBehaviour
{
    private ItemHoldingSystem holding;
    private uint submissionRevision;
    public ItemHoldingSystem Holding => holding;
    public bool HasItem => holding != null && holding.HasItem;
    public PopcornFlavor HeldFlavor { get; private set; }
    public bool IsCup { get; private set; }
    public bool IsReady => HasItem && PopcornRecipe.IsOrder(HeldFlavor);
    public bool HasPopcorn { get; private set; }
    public bool IsRefill { get; private set; }
    public bool SubmissionPending => holding != null && holding.SubmissionPending;
    public uint StateRevision { get; private set; }
    public bool IsInHand => holding != null && holding.IsInHand;
    public bool CanServe => IsReady && IsInHand;
    public TaskHeldState State => new TaskHeldState { revision = StateRevision, hasItem = HasItem, isCup = IsCup,
        hasPopcorn = HasPopcorn, isRefill = IsRefill, flavor = HeldFlavor, ghostMixed = GhostMixed,
        placementStation = 0 };
    public event System.Action RefillDiscarded;
    private GameObject refillPrefab, fantaPrefab, orangeJuicePrefab, pepsiPrefab;

    public void ConfigureSupply(GameObject refill, GameObject fanta, GameObject orangeJuice, GameObject pepsi)
    {
        refillPrefab = refill;
        fantaPrefab = fanta;
        orangeJuicePrefab = orangeJuice;
        pepsiPrefab = pepsi;
    }

    public bool CanScoop => !SubmissionPending && HasItem && !IsCup && !IsRefill && !HasPopcorn && heldPrefab != null;
    public bool CanMakeRefill => !SubmissionPending && !HasItem && refillPrefab != null;

    public bool Scoop()
    {
        if (!CanScoop || !ShowVisual(heldPrefab)) return false;
        HasPopcorn = true;
        RefreshLabel();
        return true;
    }

    public bool HoldRefill()
    {
        if (!CanMakeRefill || !ShowVisual(refillPrefab)) return false;
        IsRefill = true;
        RefreshLabel();
        return true;
    }
    public bool GhostMixed { get; private set; }
    private GameObject bucketPrefab, cupPrefab, waterPrefab, ghostWaterPrefab;

    public void ConfigureContainers(GameObject bucket, GameObject cup, GameObject water, GameObject ghostWater)
    {
        bucketPrefab = bucket;
        cupPrefab = cup;
        waterPrefab = water;
        ghostWaterPrefab = ghostWater;
    }

    public bool PickUp(bool cup)
    {
        if (SubmissionPending || HasItem || !ShowVisual(cup ? cupPrefab : bucketPrefab)) return false;
        IsCup = cup;
        HeldFlavor = PopcornFlavor.None;
        GhostMixed = false;
        RefreshLabel();
        return true;
    }

    public bool Place(PopcornStation station) => NetworkMode.IsOffline && PopcornPreparation.Instance != null &&
        PopcornPreparation.Instance.PlaceOfflineBottle(station);

    public bool ReceiveMachineBottle(PopcornFlavor flavor)
    {
        if (HasItem || SubmissionPending || !ShowVisual(PrefabFor(new TaskHeldState { hasItem = true, isCup = true, flavor = flavor }))) return false;
        IsCup = true;
        HeldFlavor = flavor;
        GhostMixed = false;
        RefreshLabel();
        return true;
    }

    // The anchor marks the bottom centre, independent of prefab pivot and machine scale.
    public static void PlaceBottleVisual(GameObject visual, Transform anchor)
    {
        ItemHoldingSystem.PlaceVisualAtAnchor(visual, anchor, .224f);
    }

    public bool MixGhost()
    {
        if (GhostFavorRecovery.Instance != null && !GhostFavorRecovery.Instance.IsHome) return false;
        if (SubmissionPending || !CanServe || GhostMixed) return false;
        if (IsCup && !ShowVisual(ghostWaterPrefab)) return false;
        GhostMixed = true;
        TintPopcorn();
        RefreshLabel();
        return true;
    }

    private void RefreshLabel()
    {
        if (heldItemText != null)
            heldItemText.text = IsRefill ? "NEW POPCORN\n[E] Refill PopCornTank" :
                !IsReady ? (IsCup ? "EMPTY PAPPERBOTTLE\nE to place at a dispenser" : HasPopcorn ?
                "POPCORN\nChoose a flavor station" : "EMPTY BUCKET\nScoop from PopCornTank") :
                UiFactory.ItemName(HeldFlavor) + (GhostMixed ? "\n+ GHOST FLAVOR" : "\nReady - Ghosts need mix");
        if (heldItemPanel != null) heldItemPanel.SetActive(IsInHand);
        PlayerHeldItems network = itemOwner != null ? itemOwner.GetComponent<PlayerHeldItems>() : null;
        if (network != null) StateRevision = network.Publish(State);
    }

    public bool BeginSubmission()
    {
        if (!CanServe || SubmissionPending) return false;
        return holding.TryBeginSubmission(out submissionRevision);
    }

    public void FinishSubmission(uint revision, bool consumed)
    {
        if (!SubmissionPending || StateRevision != revision) return;
        if (!holding.FinishSubmission(submissionRevision, false)) return;
        if (consumed) Consume();
    }

    public GameObject PrefabFor(TaskHeldState state)
    {
        if (!state.hasItem) return null;
        if (state.isRefill) return refillPrefab;
        if (!state.isCup) return state.hasPopcorn ? heldPrefab : bucketPrefab;
        if (!state.IsReady) return cupPrefab;
        if (state.ghostMixed) return ghostWaterPrefab;
        return state.flavor == PopcornFlavor.Pepsi ? pepsiPrefab : state.flavor == PopcornFlavor.Fanta ? fantaPrefab :
            state.flavor == PopcornFlavor.OrangeJuice ? orangeJuicePrefab : waterPrefab;
    }

    private GameObject heldItemPanel;
    private TMP_Text heldItemText;
    private GameObject heldPrefab;
    private GameObject heldVisual => holding != null ? holding.Visual : null;
    private Vector3 heldPosition;
    private Vector3 heldRotation;
    private PlayerHealth itemOwner;

    private void Awake() => holding = GetComponent<ItemHoldingSystem>();

    private void OnEnable() => PlayerHealth.LocalInstanceChanged += OnPlayerChanged;

    private void OnDisable()
    {
        PlayerHealth.LocalInstanceChanged -= OnPlayerChanged;
        Consume();
    }

    private void OnPlayerChanged(PlayerHealth player)
    {
        if (HasItem && player != itemOwner) Consume();
    }

    public void Configure(GameObject prefab, Vector3 position, Vector3 rotation)
    {
        heldPrefab = prefab;
        heldPosition = position;
        heldRotation = rotation;
    }

    public void BuildUi()
    {
        GameObject canvasObject = new GameObject("Held Popcorn HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        heldItemPanel = UiFactory.CreatePanel("Left Hand - Popcorn Bucket", canvasObject.transform, new Color(0.12f, 0.06f, 0.02f, 0.94f));
        RectTransform panelRect = heldItemPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(28f, -80f);
        panelRect.sizeDelta = new Vector2(340f, 190f);

        TMP_Text handLabel = UiFactory.CreateText("Hand Label", heldItemPanel.transform, "LEFT HAND", 26f, Color.white);
        UiFactory.SetRect(handLabel.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.96f));

        TMP_Text bucketIcon = UiFactory.CreateText("Bucket Icon", heldItemPanel.transform, "HELD ITEM", 30f, new Color(1f, 0.78f, 0.18f));
        bucketIcon.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(bucketIcon.rectTransform, new Vector2(0.08f, 0.55f), new Vector2(0.92f, 0.78f));

        heldItemText = UiFactory.CreateText("Held Flavor", heldItemPanel.transform, string.Empty, 30f, Color.white);
        heldItemText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(heldItemText.rectTransform, new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.55f));

        heldItemPanel.SetActive(false);
        foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    public bool Hold(PopcornFlavor flavor)
    {
        if (IsCup) return false;
        if (SubmissionPending || !HasItem || IsReady || IsRefill || !PopcornRecipe.IsOrder(flavor) || IsCup != PopcornRecipe.IsDrink(flavor)) return false;
        if (!HasPopcorn) return false;
        HeldFlavor = flavor;
        TintPopcorn();
        RefreshLabel();
        return true;
    }

    public bool TryDiscardInHand()
    {
        if (!IsInHand || SubmissionPending || (PopcornPreparation.Instance != null && PopcornPreparation.Instance.BottleTransferPending)) return false;
        Consume();
        return true;
    }

    private void TintPopcorn()
    {
        if (IsCup || heldVisual == null) return;
        TintVisual(heldVisual, HeldFlavor, GhostMixed);
    }

    public static void TintVisual(GameObject model, PopcornFlavor flavor, bool ghostMixed)
    {
        Color tint = ghostMixed ? new Color(0.35f, 0.95f, 1f) : flavor == PopcornFlavor.BBQ
            ? new Color(0.65f, 0.24f, 0.09f) : flavor == PopcornFlavor.Paprika
            ? new Color(1f, 0.35f, 0.12f) : new Color(1f, 0.8f, 0.2f);
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", tint);
        properties.SetColor("_Color", tint);
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            if (renderer.name.StartsWith("Popcorn ")) renderer.SetPropertyBlock(properties);
    }

    private bool ShowVisual(GameObject prefab)
    {
        PlayerHealth player = PlayerHealth.LocalInstance;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (prefab == null || camera == null || player.IsDead || player.IsDowned) return false;
        holding.ConfigureHandAnchor(camera.transform, heldPosition, heldRotation);
        bool shown = holding.HasItem ? holding.TryReplaceVisual(prefab) :
            holding.TryPickUp(new HeldItemDefinition(prefab.name, prefab));
        if (!shown) return false;
        itemOwner = player;
        return true;
    }

    private void Update()
    {
        if (itemOwner != null && !HasItem) Consume();
        else if (HasItem && (itemOwner == null || itemOwner.IsDead || itemOwner.IsDowned)) Consume();
    }

    public PopcornFlavor Consume()
    {
        PlayerHeldItems network = itemOwner != null ? itemOwner.GetComponent<PlayerHeldItems>() : null;
        PopcornFlavor result = HeldFlavor;
        if (IsRefill) RefillDiscarded?.Invoke();
        IsRefill = false;
        HasPopcorn = false;
        HeldFlavor = PopcornFlavor.None;
        GhostMixed = false;
        IsCup = false;
        itemOwner = null;
        holding.Clear();
        if (network != null) StateRevision = network.Publish(State);
        if (heldItemPanel != null) heldItemPanel.SetActive(false);
        return result;
    }

    private void OnDestroy() { if (holding != null) holding.Clear(); }
}

