using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public enum PopcornFlavor
{
    None,
    Cheese,
    BBQ,
    Ghost
}

public enum PopcornCustomerType
{
    Human,
    Ghost
}

/// <summary>
/// Scene entry point for the Zone 1 popcorn prototype. The authored counter and
/// machine meshes remain untouched; this component mounts the gameplay UI onto
/// them and coordinates the deliberately modular single CounterSlot.
/// </summary>
[DisallowMultipleComponent]
public sealed class PopcornMinigameBootstrap : MonoBehaviour
{
    [Header("Scene objects")]
    [SerializeField] private string cashierObjectName = "Cashier (1)";
    [SerializeField] private string popcornMakerObjectName = "Popcorn Maker (1)";

    [Header("Held popcorn")]
    [SerializeField] private GameObject heldPopcornPrefab;
    [SerializeField] private Vector3 heldPopcornPosition = new Vector3(-0.3f, -0.24f, 0.65f);
    [SerializeField] private Vector3 heldPopcornRotation = new Vector3(-10f, 0f, -8f);

    [Header("Scene-editable UI anchors")]
    [Tooltip("Move/rotate this Transform in the scene to place the cashier order display.")]
    [SerializeField] private Transform cashierUiAnchor;
    [Tooltip("Move/rotate this Transform in the scene to place the popcorn maker controls.")]
    [SerializeField] private Transform popcornMakerUiAnchor;

    [Header("Scene-editable customer route")]
    [Tooltip("Where a new customer first appears.")]
    [SerializeField] private Transform customerSpawnPoint;
    [Tooltip("The single occupied position at the counter.")]
    [SerializeField] private Transform customerWaitPoint;
    [Tooltip("The destination used after the order is resolved.")]
    [SerializeField] private Transform customerExitPoint;
    [SerializeField, Min(0f)] private float delayBetweenCustomers = 2f;

    private void Awake()
    {
        EnsureEventSystem();

        Transform cashier = FindSceneObject(cashierObjectName);
        Transform maker = FindSceneObject(popcornMakerObjectName);
        if (cashier == null || maker == null || !HasAllAnchors())
        {
            Debug.LogError(
                $"[PopcornMinigame] Scene setup is incomplete. Check the cashier/maker names and assign all " +
                "UI/customer anchor Transforms on Popcorn Minigame Systems.", this);
            enabled = false;
            return;
        }

        ItemHoldingSystem holder = gameObject.AddComponent<ItemHoldingSystem>();
        CounterSlot counterSlot = gameObject.AddComponent<CounterSlot>();
        PopcornGameManager manager = gameObject.AddComponent<PopcornGameManager>();

        counterSlot.Configure(customerSpawnPoint.position, customerWaitPoint.position, customerExitPoint.position);
        holder.Configure(heldPopcornPrefab, heldPopcornPosition, heldPopcornRotation);
        holder.BuildUi();
        manager.Configure(holder, counterSlot, cashier, maker, cashierUiAnchor, popcornMakerUiAnchor,
            delayBetweenCustomers);

        if (GetComponent<PopcornUiCursorController>() == null)
            gameObject.AddComponent<PopcornUiCursorController>();
    }

    private void Start() => StartCoroutine(DisableAuthoringCameraAfterPlayerSpawns());

    private bool HasAllAnchors() =>
        cashierUiAnchor != null && popcornMakerUiAnchor != null &&
        customerSpawnPoint != null && customerWaitPoint != null && customerExitPoint != null;

    private void OnDrawGizmos()
    {
        DrawUiAnchor(cashierUiAnchor, new Color(0.2f, 1f, 0.85f), new Vector3(0.72f, 0.52f, 0.03f));
        DrawUiAnchor(popcornMakerUiAnchor, new Color(1f, 0.72f, 0.15f), new Vector3(0.62f, 0.85f, 0.03f));

        DrawRoutePoint(customerSpawnPoint, Color.cyan, 0.28f);
        DrawRoutePoint(customerWaitPoint, Color.yellow, 0.32f);
        DrawRoutePoint(customerExitPoint, new Color(1f, 0.35f, 0.2f), 0.28f);

        if (customerSpawnPoint != null && customerWaitPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(customerSpawnPoint.position, customerWaitPoint.position);
        }
        if (customerWaitPoint != null && customerExitPoint != null)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.15f);
            Gizmos.DrawLine(customerWaitPoint.position, customerExitPoint.position);
        }
    }

    private static void DrawUiAnchor(Transform anchor, Color color, Vector3 size)
    {
        if (anchor == null) return;
        Gizmos.color = color;
        Gizmos.matrix = Matrix4x4.TRS(anchor.position, anchor.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.DrawRay(Vector3.zero, Vector3.back * 0.5f);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static void DrawRoutePoint(Transform point, Color color, float radius)
    {
        if (point == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(point.position, radius);
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem (Popcorn Gameplay)");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
        }

        // An EventSystem alone cannot turn mouse input into pointer events. This
        // scene builds its UI at runtime, so repair an existing incomplete
        // EventSystem as well as configuring one created above.
        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

        inputModule.enabled = true;
        inputModule.AssignDefaultActions();
    }

    private static Transform FindSceneObject(string objectName)
    {
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (candidate.name == objectName) return candidate;
        }
        return null;
    }

    private static IEnumerator DisableAuthoringCameraAfterPlayerSpawns()
    {
        yield return null;
        PlayerHealth player = FindAnyObjectByType<PlayerHealth>();
        if (player == null) yield break;

        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (!camera.transform.IsChildOf(player.transform))
            {
                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
                camera.enabled = false;
            }
        }
    }
}

/// <summary>One-item inventory with a camera-mounted left-hand prefab and HUD label.</summary>
public sealed class ItemHoldingSystem : MonoBehaviour
{
    public bool HasItem { get; private set; }
    public PopcornFlavor HeldFlavor { get; private set; }

    private GameObject heldItemPanel;
    private TMP_Text heldItemText;
    private GameObject heldPrefab;
    private GameObject heldVisual;
    private Vector3 heldPosition;
    private Vector3 heldRotation;

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
        panelRect.sizeDelta = new Vector2(250f, 290f);

        TMP_Text handLabel = UiFactory.CreateText("Hand Label", heldItemPanel.transform, "LEFT HAND", 26f, Color.white);
        UiFactory.SetRect(handLabel.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.96f));

        TMP_Text bucketIcon = UiFactory.CreateText("Bucket Icon", heldItemPanel.transform, "POPCORN", 42f, new Color(1f, 0.78f, 0.18f));
        bucketIcon.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(bucketIcon.rectTransform, new Vector2(0.12f, 0.30f), new Vector2(0.88f, 0.76f));

        heldItemText = UiFactory.CreateText("Held Flavor", heldItemPanel.transform, string.Empty, 30f, Color.white);
        heldItemText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(heldItemText.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.30f));

        heldItemPanel.SetActive(false);
        foreach (Graphic graphic in canvasObject.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    public bool Hold(PopcornFlavor flavor)
    {
        PlayerHealth player = PlayerHealth.LocalInstance;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (flavor == PopcornFlavor.None || heldPrefab == null || camera == null)
        {
            Debug.LogWarning("[PopcornMinigame] Cannot make popcorn: assign Held Popcorn Prefab and wait for the local player camera.", this);
            return false;
        }

        ClearVisual();
        heldVisual = Instantiate(heldPrefab, camera.transform, false);
        heldVisual.name = $"Held {UiFactory.FlavorName(flavor)} Popcorn";
        heldVisual.transform.localPosition = heldPosition;
        heldVisual.transform.localRotation = Quaternion.Euler(heldRotation);
        // A held prop must never obstruct the interaction ray or collide with its owner.
        foreach (Transform child in heldVisual.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        foreach (Collider collider in heldVisual.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in heldVisual.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        Color tint = flavor == PopcornFlavor.Ghost ? new Color(0.25f, 0.9f, 1f)
            : flavor == PopcornFlavor.BBQ ? new Color(0.8f, 0.28f, 0.12f) : new Color(1f, 0.75f, 0.15f);
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", tint);
        properties.SetColor("_Color", tint);
        foreach (Renderer renderer in heldVisual.GetComponentsInChildren<Renderer>())
            renderer.SetPropertyBlock(properties);

        HeldFlavor = flavor;
        HasItem = true;
        if (heldItemText != null) heldItemText.text = $"{UiFactory.FlavorName(flavor)}\nPOPCORN";
        if (heldItemPanel != null) heldItemPanel.SetActive(HasItem);
        return true;
    }

    public PopcornFlavor Consume()
    {
        PopcornFlavor result = HeldFlavor;
        HeldFlavor = PopcornFlavor.None;
        HasItem = false;
        ClearVisual();
        if (heldItemPanel != null) heldItemPanel.SetActive(false);
        return result;
    }

    private void ClearVisual()
    {
        if (heldVisual == null) return;
        heldVisual.SetActive(false);
        Destroy(heldVisual);
        heldVisual = null;
    }

    private void OnDestroy() => ClearVisual();
}

/// <summary>A reusable occupancy point; add more instances later for queues/counters.</summary>
public sealed class CounterSlot : MonoBehaviour
{
    public bool IsOccupied => ActiveCustomer != null;
    public PopcornCustomer ActiveCustomer { get; private set; }

    private Vector3 spawnPosition;
    private Vector3 waitPosition;
    private Vector3 exitPosition;

    public void Configure(Vector3 spawn, Vector3 wait, Vector3 exit)
    {
        spawnPosition = spawn;
        waitPosition = wait;
        exitPosition = exit;
    }

    public PopcornCustomer Occupy(PopcornGameManager manager, PopcornCustomerType type, PopcornFlavor order)
    {
        if (IsOccupied) return null;

        GameObject customerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        customerObject.name = $"{type} Customer";
        customerObject.transform.position = spawnPosition;
        customerObject.transform.localScale = new Vector3(0.75f, 1f, 0.75f);

        Renderer renderer = customerObject.GetComponent<Renderer>();
        renderer.material.color = type == PopcornCustomerType.Ghost
            ? new Color(0.35f, 0.95f, 1f, 0.78f)
            : new Color(1f, 0.68f, 0.25f, 1f);

        ActiveCustomer = customerObject.AddComponent<PopcornCustomer>();
        ActiveCustomer.Configure(manager, this, type, order, waitPosition, exitPosition);
        return ActiveCustomer;
    }

    public void Vacate(PopcornCustomer customer)
    {
        if (ActiveCustomer == customer) ActiveCustomer = null;
    }
}

public sealed class PopcornCustomer : MonoBehaviour, IInteractable, IInteractionHighlight
{
    private enum CustomerState { WalkingIn, Waiting, WalkingOut }

    public PopcornCustomerType CustomerType { get; private set; }
    public PopcornFlavor Order { get; private set; }

    private PopcornGameManager manager;
    private CounterSlot slot;
    private Vector3 waitPosition;
    private Vector3 exitPosition;
    private CustomerState state;
    private Collider interactionCollider;
    private Canvas submitPrompt;
    private const float MoveSpeed = 2.2f;

    public void Configure(PopcornGameManager owner, CounterSlot ownerSlot, PopcornCustomerType type,
        PopcornFlavor order, Vector3 wait, Vector3 exit)
    {
        manager = owner;
        slot = ownerSlot;
        CustomerType = type;
        Order = order;
        waitPosition = wait;
        exitPosition = exit;
        state = CustomerState.WalkingIn;
        interactionCollider = GetComponent<Collider>();
        interactionCollider.enabled = false;
        BuildSubmitPrompt();
    }

    private void BuildSubmitPrompt()
    {
        GameObject promptObject = new GameObject("Customer Submit Prompt", typeof(RectTransform), typeof(Canvas));
        promptObject.transform.SetParent(transform, false);
        promptObject.transform.localPosition = Vector3.up * 1.45f;
        // Compensate for the capsule's scale so the label has the same world size on every customer.
        promptObject.transform.localScale = new Vector3(0.002f / transform.lossyScale.x,
            0.002f / transform.lossyScale.y, 0.002f / transform.lossyScale.z);
        promptObject.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 80f);
        submitPrompt = promptObject.GetComponent<Canvas>();
        submitPrompt.renderMode = RenderMode.WorldSpace;
        GameObject panel = UiFactory.CreatePanel("Backdrop", promptObject.transform, new Color(0.025f, 0.04f, 0.05f, 0.95f));
        panel.GetComponent<Image>().raycastTarget = false;
        UiFactory.Stretch(panel.GetComponent<RectTransform>());
        TMP_Text text = UiFactory.CreateText("Submit Label", panel.transform, "[E] Submit order", 34f, Color.white);
        text.alignment = TextAlignmentOptions.Center;
        UiFactory.Stretch(text.rectTransform);
        submitPrompt.enabled = false;
    }

    private void LateUpdate()
    {
        if (submitPrompt == null || !submitPrompt.enabled) return;
        PlayerHealth player = PlayerHealth.LocalInstance;
        Camera camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (camera != null) submitPrompt.transform.rotation = camera.transform.rotation;
    }

    public void SetHighlighted(bool highlighted)
    {
        if (submitPrompt != null) submitPrompt.enabled = highlighted && CanInteract();
    }

    private void Update()
    {
        if (state == CustomerState.Waiting) return;

        Vector3 target = state == CustomerState.WalkingIn ? waitPosition : exitPosition;
        transform.position = Vector3.MoveTowards(transform.position, target, MoveSpeed * Time.deltaTime);
        Vector3 flatDirection = target - transform.position;
        flatDirection.y = 0f;
        if (flatDirection.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDirection), 8f * Time.deltaTime);

        if ((transform.position - target).sqrMagnitude > 0.01f) return;

        if (state == CustomerState.WalkingIn)
        {
            state = CustomerState.Waiting;
            interactionCollider.enabled = true;
            // Customers wait on the negative-Z/Y-BOT side and face the player
            // across the counter on positive Z.
            transform.rotation = Quaternion.LookRotation(Vector3.forward);
            manager.CustomerReady(this);
        }
        else
        {
            slot.Vacate(this);
            manager.CustomerFinishedLeaving(this);
            Destroy(gameObject);
        }
    }

    public void BeginLeaving()
    {
        SetHighlighted(false);
        interactionCollider.enabled = false;
        state = CustomerState.WalkingOut;
    }

    // The customer owns its world prompt; avoid also showing a duplicate at the crosshair.
    public string GetInteractionPrompt() => string.Empty;
    public bool CanInteract() => state == CustomerState.Waiting;
    public void Interact(GameObject interactor)
    {
        if (CanInteract()) manager.TryServe(this, interactor);
    }
    public Transform GetTransform() => transform;

    private void OnDisable() => SetHighlighted(false);
}

public sealed class PopcornGameManager : MonoBehaviour
{
    private ItemHoldingSystem holder;
    private CounterSlot counterSlot;
    private TMP_Text orderText;
    private TMP_Text scoreText;
    private TMP_Text feedbackText;
    private TMP_Text selectionText;
    private PopcornFlavor selectedFlavor;
    private int score;
    private float delayBetweenCustomers;
    private Coroutine feedbackRoutine;
    private Canvas cashierCanvas;
    private Canvas makerCanvas;
    private readonly Dictionary<PopcornFlavor, WorldButtonInteractable> flavorButtons = new Dictionary<PopcornFlavor, WorldButtonInteractable>();

    public void Configure(ItemHoldingSystem itemHolder, CounterSlot slot, Transform cashier, Transform maker,
        Transform cashierPlacement, Transform makerPlacement, float nextDelay)
    {
        holder = itemHolder;
        counterSlot = slot;
        delayBetweenCustomers = nextDelay;
        BuildCashierScreen(cashier, cashierPlacement);
        BuildMakerScreen(maker, makerPlacement);
        PlayerHealth.LocalInstanceChanged += BindWorldUiCamera;
        BindWorldUiCamera(PlayerHealth.LocalInstance);
        StartCoroutine(SpawnNextCustomerAfter(0.8f));
    }

    private void BuildCashierScreen(Transform cashier, Transform placement)
    {
        cashierCanvas = UiFactory.CreateWorldCanvas("Order Screen (World Space)", cashier,
            placement, new Vector2(720f, 520f));

        GameObject panel = UiFactory.CreatePanel("Cashier Display", cashierCanvas.transform, new Color(0.025f, 0.08f, 0.085f, 0.97f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());

        orderText = UiFactory.CreateText("Order Text", panel.transform, "WAITING FOR CUSTOMER...", 56f, new Color(0.8f, 1f, 0.9f));
        orderText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(orderText.rectTransform, new Vector2(0.05f, 0.43f), new Vector2(0.95f, 0.94f));

        scoreText = UiFactory.CreateText("Score Text", panel.transform, "SCORE: 0", 46f, Color.white);
        scoreText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(scoreText.rectTransform, new Vector2(0.05f, 0.23f), new Vector2(0.95f, 0.44f));

        feedbackText = UiFactory.CreateText("Feedback Text", panel.transform, string.Empty, 44f, Color.white);
        feedbackText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(feedbackText.rectTransform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.23f));
    }

    private void BuildMakerScreen(Transform maker, Transform placement)
    {
        makerCanvas = UiFactory.CreateWorldCanvas("Popcorn Controls (World Space)", maker,
            placement, new Vector2(620f, 850f));

        GameObject panel = UiFactory.CreatePanel("Popcorn Maker Controls", makerCanvas.transform, new Color(0.09f, 0.045f, 0.015f, 0.97f));
        UiFactory.Stretch(panel.GetComponent<RectTransform>());

        TMP_Text title = UiFactory.CreateText("Title", panel.transform, "POPCORN MAKER", 46f, new Color(1f, 0.8f, 0.22f));
        title.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(title.rectTransform, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.98f));

        selectionText = UiFactory.CreateText("Selection", panel.transform, "SELECT ONE FLAVOR", 32f, Color.white);
        selectionText.alignment = TextAlignmentOptions.Center;
        UiFactory.SetRect(selectionText.rectTransform, new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.87f));

        CreateFlavorButton(panel.transform, "CHEESE", PopcornFlavor.Cheese, new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.74f), new Color(0.95f, 0.62f, 0.08f));
        CreateFlavorButton(panel.transform, "BBQ", PopcornFlavor.BBQ, new Vector2(0.08f, 0.39f), new Vector2(0.92f, 0.55f), new Color(0.65f, 0.18f, 0.08f));
        CreateFlavorButton(panel.transform, "GHOST FLAVOR", PopcornFlavor.Ghost, new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.36f), new Color(0.15f, 0.65f, 0.75f));

        Button makeButton = UiFactory.CreateButton("Make Button", panel.transform, "MAKE", new Color(0.16f, 0.68f, 0.25f));
        UiFactory.SetRect(makeButton.GetComponent<RectTransform>(), new Vector2(0.08f, 0.035f), new Vector2(0.92f, 0.17f));
        makeButton.onClick.AddListener(MakePopcorn);
        WorldButtonInteractable.Attach(makeButton, "Make popcorn");
    }

    private void BindWorldUiCamera(PlayerHealth player)
    {
        Camera localCamera = player != null ? player.GetComponentInChildren<Camera>(true) : null;
        if (cashierCanvas != null) cashierCanvas.worldCamera = localCamera;
        if (makerCanvas != null) makerCanvas.worldCamera = localCamera;
    }

    private void OnDestroy()
    {
        PlayerHealth.LocalInstanceChanged -= BindWorldUiCamera;
    }

    private void CreateFlavorButton(Transform parent, string label, PopcornFlavor flavor,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        Button button = UiFactory.CreateButton(label + " Button", parent, label, color);
        UiFactory.SetRect(button.GetComponent<RectTransform>(), anchorMin, anchorMax);
        button.onClick.AddListener(() => SelectFlavor(flavor));
        flavorButtons.Add(flavor, WorldButtonInteractable.Attach(button, $"Select {UiFactory.FlavorName(flavor)}"));
    }

    private void SelectFlavor(PopcornFlavor flavor)
    {
        selectedFlavor = flavor;
        foreach (var entry in flavorButtons)
            entry.Value.SetSelected(entry.Key == flavor);
        selectionText.text = $"SELECTED: {UiFactory.FlavorName(flavor).ToUpperInvariant()}";
    }

    private void MakePopcorn()
    {
        if (selectedFlavor == PopcornFlavor.None)
        {
            ShowFeedback("Select a flavor first", new Color(1f, 0.78f, 0.15f));
            return;
        }

        if (!holder.Hold(selectedFlavor))
        {
            ShowFeedback("Cannot make popcorn yet", new Color(1f, 0.78f, 0.15f));
            return;
        }
        ShowFeedback($"Made {UiFactory.FlavorName(selectedFlavor)}", new Color(0.65f, 0.9f, 1f));
    }

    private IEnumerator SpawnNextCustomerAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (counterSlot.IsOccupied) yield break;

        PopcornCustomerType type = Random.value < 0.5f ? PopcornCustomerType.Human : PopcornCustomerType.Ghost;
        PopcornFlavor order = type == PopcornCustomerType.Ghost
            ? PopcornFlavor.Ghost
            : (Random.value < 0.5f ? PopcornFlavor.Cheese : PopcornFlavor.BBQ);

        counterSlot.Occupy(this, type, order);
        orderText.text = $"{type.ToString().ToUpperInvariant()} ORDER\n{UiFactory.FlavorName(order).ToUpperInvariant()} POPCORN";
    }

    public void CustomerReady(PopcornCustomer customer)
    {
        orderText.text = $"{customer.CustomerType.ToString().ToUpperInvariant()} ORDER\n{UiFactory.FlavorName(customer.Order).ToUpperInvariant()} POPCORN";
    }

    public void TryServe(PopcornCustomer customer, GameObject interactor)
    {
        if (customer == null || customer != counterSlot.ActiveCustomer || !customer.CanInteract()) return;
        if (!holder.HasItem)
        {
            ShowFeedback("Make popcorn first", new Color(1f, 0.78f, 0.15f));
            return;
        }

        PopcornFlavor served = holder.Consume();
        bool correct = served == customer.Order;

        if (correct)
        {
            score++;
            scoreText.text = $"SCORE: {score}";
            ShowFeedback("Correct!  +1 Point", new Color(0.22f, 1f, 0.35f));
            Debug.Log("[PopcornMinigame] +1 Point");
        }
        else
        {
            ShowFeedback("Incorrect", new Color(1f, 0.2f, 0.18f));
            Debug.Log($"[PopcornMinigame] Incorrect order: served {served}, requested {customer.Order}.");

            if (customer.CustomerType == PopcornCustomerType.Ghost)
            {
                PlayerHealth health = interactor != null ? interactor.GetComponentInParent<PlayerHealth>() : PlayerHealth.LocalInstance;
                if (health == null) health = PlayerHealth.LocalInstance;
                if (health != null) health.TakeDamage(10f);
                else Debug.LogWarning("[PopcornMinigame] Ghost order failed, but no PlayerHealth was found to receive 10 damage.");
            }
        }

        orderText.text = "ORDER COMPLETE";
        customer.BeginLeaving();
    }

    public void CustomerFinishedLeaving(PopcornCustomer customer)
    {
        orderText.text = "WAITING FOR CUSTOMER...";
        StartCoroutine(SpawnNextCustomerAfter(delayBetweenCustomers));
    }

    private void ShowFeedback(string message, Color color)
    {
        if (feedbackRoutine != null) StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(ShowFeedbackForFiveSeconds(message, color));
    }

    private IEnumerator ShowFeedbackForFiveSeconds(string message, Color color)
    {
        feedbackText.text = message;
        feedbackText.color = color;
        yield return new WaitForSeconds(5f);
        feedbackText.text = string.Empty;
        feedbackRoutine = null;
    }
}

/// <summary>Lets the same world-space UI Button respond to mouse clicks or the existing E interaction ray.</summary>
public sealed class WorldButtonInteractable : MonoBehaviour, IInteractable, IInteractionHighlight,
    IPointerEnterHandler, IPointerExitHandler
{
    private Button button;
    private string prompt;
    private Outline hoverOutline;
    private bool gazeHighlighted;
    private bool pointerHighlighted;
    private bool selected;
    private BoxCollider interactionCollider;

    public static WorldButtonInteractable Attach(Button target, string interactionPrompt)
    {
        BoxCollider collider = target.gameObject.AddComponent<BoxCollider>();
        RectTransform rect = target.GetComponent<RectTransform>();
        collider.size = new Vector3(rect.rect.width, rect.rect.height, 10f);

        WorldButtonInteractable interactable = target.gameObject.AddComponent<WorldButtonInteractable>();
        interactable.button = target;
        interactable.prompt = interactionPrompt;
        interactable.interactionCollider = collider;
        interactable.ResizeCollider();

        Outline outline = target.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.94f, 0.45f, 1f);
        outline.effectDistance = new Vector2(6f, -6f);
        outline.useGraphicAlpha = true;
        outline.enabled = false;
        interactable.hoverOutline = outline;
        return interactable;
    }

    private void OnRectTransformDimensionsChange() => ResizeCollider();

    private void ResizeCollider()
    {
        if (interactionCollider == null) return;
        Rect rect = ((RectTransform)transform).rect;
        interactionCollider.size = new Vector3(rect.width, rect.height, 10f);
        interactionCollider.center = new Vector3(rect.center.x, rect.center.y, 0f);
    }

    public string GetInteractionPrompt() => prompt;
    public bool CanInteract() => button != null && button.IsInteractable();
    public void Interact(GameObject interactor)
    {
        if (CanInteract()) button.onClick.Invoke();
    }
    public Transform GetTransform() => transform;

    public void SetHighlighted(bool highlighted)
    {
        gazeHighlighted = highlighted;
        RefreshHighlight();
    }

    public void SetSelected(bool isSelected)
    {
        selected = isSelected;
        RefreshHighlight();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerHighlighted = true;
        RefreshHighlight();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerHighlighted = false;
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        if (hoverOutline != null)
        {
            hoverOutline.enabled = selected || gazeHighlighted || pointerHighlighted;
            hoverOutline.effectColor = selected ? new Color(1f, 0.85f, 0.1f) : Color.white;
            hoverOutline.effectDistance = selected ? new Vector2(8f, -8f) : new Vector2(3f, -3f);
        }
    }
}

/// <summary>Tab toggles between FPS look and a visible cursor for direct world-UI clicking.</summary>
public sealed class PopcornUiCursorController : MonoBehaviour
{
    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current == null ||
            !UnityEngine.InputSystem.Keyboard.current.tabKey.wasPressedThisFrame) return;

        bool unlock = Cursor.lockState == CursorLockMode.Locked;
        Cursor.lockState = unlock ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = unlock;
    }
}

internal static class UiFactory
{
    public static Canvas CreateWorldCanvas(string name, Transform physicalParent, Transform placement, Vector2 size)
    {
        GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(physicalParent, true);
        canvasObject.transform.SetPositionAndRotation(placement.position, placement.rotation);
        canvasObject.transform.localScale = Vector3.one * 0.001f;

        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;
        // The authored anchors may be rotated to fit either side of a prop.
        // If the controls are visible, they should be clickable from that side.
        canvasObject.GetComponent<GraphicRaycaster>().ignoreReversedGraphics = false;
        return canvas;
    }

    public static GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(parent, false);
        panel.GetComponent<Image>().color = color;
        return panel;
    }

    public static TMP_Text CreateText(string name, Transform parent, string value, float size, Color color)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.isRightToLeftText = false;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Max(12f, size * 0.55f);
        text.fontSizeMax = size;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(string name, Transform parent, string label, Color color)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;
        // Flavor selection is explicit state, independent of EventSystem keyboard focus.
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        TMP_Text text = CreateText("Label", buttonObject.transform, label, 38f, Color.white);
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
        return button;
    }

    public static string FlavorName(PopcornFlavor flavor) => flavor == PopcornFlavor.Ghost ? "Ghost Flavor" : flavor.ToString();

    public static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static void Stretch(RectTransform rect) => SetRect(rect, Vector2.zero, Vector2.one);
}
