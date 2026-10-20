using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>Reusable, local-only document. Uses the player's existing interaction ray and key.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Interaction/Readable Paper")]
public sealed class ReadablePaper : MonoBehaviour, ILocalInteractable
{
    [Header("Document")]
    [SerializeField] private string interactionPrompt = "[E] Read paper";
    [Tooltip("Drag instruction images here in reading order. No Sprite import setting is required.")]
    [SerializeField] private Texture2D[] pages = new Texture2D[0];

    [Header("Screen layout")]
    [Tooltip("Maximum paper size on a 1920 x 1080 reference canvas. Image proportions are preserved.")]
    [SerializeField] private Vector2 maximumSize = new Vector2(1400, 900);
    [SerializeField] private Vector2 screenMargin = new Vector2(60, 90);
    [SerializeField] private Vector2 centerOffset = Vector2.zero;
    [SerializeField] private Color backdropColor = new Color(0, 0, 0, .7f);
    [SerializeField] private int sortingOrder = 2000;

    [Header("Slide animation")]
    [Min(0)] [SerializeField] private float openDuration = .45f;
    [Min(0)] [SerializeField] private float closeDuration = .25f;
    [Tooltip("0 = below screen, 1 = reading position. Overshoot is supported.")]
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Min(0)] [SerializeField] private float offscreenPadding = 30;

    [Header("Controls")]
    [Tooltip("Escape always closes. Set None to disable the additional close key.")]
    [SerializeField] private Key closeKey = Key.E;
    [SerializeField] private Key nextPageKey = Key.RightArrow;
    [SerializeField] private Key previousPageKey = Key.LeftArrow;

    [Header("Local events")]
    [SerializeField] private UnityEvent onOpened = new UnityEvent();
    [Tooltip("Invoked after closing, including player death, object removal and scene unload.")]
    [SerializeField] private UnityEvent onClosed = new UnityEvent();

    public Vector2 MaximumSize => maximumSize;
    public Vector2 ScreenMargin => screenMargin;
    public Vector2 CenterOffset => centerOffset;
    public Color BackdropColor => backdropColor;
    public int SortingOrder => sortingOrder;
    public float OpenDuration => Mathf.Max(0, openDuration);
    public float CloseDuration => Mathf.Max(0, closeDuration);
    public AnimationCurve SlideCurve => slideCurve;
    public float OffscreenPadding => Mathf.Max(0, offscreenPadding);
    public Key CloseKey => closeKey;
    public Key NextPageKey => nextPageKey;
    public Key PreviousPageKey => previousPageKey;
    public int PageCount => pages == null ? 0 : pages.Length;
    public Texture2D GetPage(int index) => index >= 0 && index < PageCount ? pages[index] : null;

    public string GetInteractionPrompt() => interactionPrompt;
    public Transform GetTransform() => transform;
    public bool CanInteract() => isActiveAndEnabled && GetPage(0) != null && !OverlayPanels.BlocksEscape;

    public void Interact(GameObject interactor)
    {
        if (!CanInteract() || interactor == null || GameplayInput.Blocked || OverlayPanels.AnyOpen) return;
        PlayerInteractor controller = interactor.GetComponent<PlayerInteractor>();
        if (controller == null || !NetworkMode.IsLocalController(controller)) return;
        PaperReaderUI reader = interactor.GetComponentInChildren<PaperReaderUI>(true);
        if (reader == null)
        {
            var root = new GameObject("Paper Reader", typeof(RectTransform));
            root.transform.SetParent(interactor.transform, false);
            reader = root.AddComponent<PaperReaderUI>();
        }
        if (reader.Open(this, interactor)) onOpened.Invoke();
    }

    public void NotifyClosed() => onClosed.Invoke();
}
