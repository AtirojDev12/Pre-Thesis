using UnityEngine;

/// <summary>
/// 24 Oct 2026 (Mr.k): the map in YOUR hand (local player only).
///
///   Map selected in the hotbar   = small map in the lower right of the screen
///   HOLD the read key            = the map comes up in front of your eyes
///                                  (Settings > Controls "Read map", default Left Mouse)
///
/// The model is the designer's Assets/Prefab/Map.prefab. It reaches the game through
/// Resources/Items/WorldMap.prefab (the dropped map), which holds it as a child, so
/// a material or shape change on Map.prefab shows up in the hand too.
/// The model is scaled to fit (holdSize / readSize), so its own size does not matter.
///
/// Purely visual: no network traffic. The item itself is server-owned (PlayerInventory).
/// Added by PlayerInventory.Awake, so the Player prefab needs no change.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInventory))]
public sealed class MapController : MonoBehaviour
{
    [Header("In hand (camera space, metres / degrees)")]
    [SerializeField] private Vector3 holdPosition = new Vector3(0.24f, -0.25f, 0.45f);
    [SerializeField] private Vector3 holdRotation = new Vector3(35f, -15f, 0f);
    [Tooltip("Longest side of the map in your hand (metres).")]
    [SerializeField, Min(0.01f)] private float holdSize = 0.22f;

    [Header("Reading (hold the read key)")]
    [SerializeField] private Vector3 readPosition = new Vector3(0f, -0.01f, 0.36f);
    [SerializeField] private Vector3 readRotation = Vector3.zero;
    [Tooltip("Longest side of the map while reading (metres). Bigger = fills more of the screen.")]
    [SerializeField, Min(0.01f)] private float readSize = 0.4f;
    [Tooltip("How fast the map moves up / down (1 / seconds).")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 8f;

    /// <summary>Local player is reading the map right now.</summary>
    public static bool IsReading { get; private set; }

    private PlayerInventory inventory;
    private PlayerHealth health;
    private Transform anchor;      // child of the camera: moves + scales
    private float modelSize = 1f;  // longest side of the model at scale 1
    private float blend;           // 0 = in hand, 1 = reading
    private bool missingWarned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsReading = false;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (!NetworkMode.IsLocalController(inventory)) return;

        bool alive = health == null || (!health.IsDead && !health.IsDowned);
        bool show = alive && inventory.IsHoldingMap;
        if (!show)
        {
            if (anchor != null && anchor.gameObject.activeSelf) anchor.gameObject.SetActive(false);
            blend = 0f;
            IsReading = false;
            return;
        }

        if (anchor == null && !Build()) return;
        if (!anchor.gameObject.activeSelf) anchor.gameObject.SetActive(true);

        bool canUseHands = !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked;
        bool read = canUseHands && Keybinds.IsPressed(GameAction.ReadMap);
        IsReading = read;

        blend = Mathf.MoveTowards(blend, read ? 1f : 0f, blendSpeed * Time.deltaTime);
        float e = blend * blend * (3f - 2f * blend); // smooth start and stop
        anchor.localPosition = Vector3.Lerp(holdPosition, readPosition, e);
        anchor.localRotation = Quaternion.Slerp(Quaternion.Euler(holdRotation), Quaternion.Euler(readRotation), e);
        anchor.localScale = Vector3.one * (Mathf.Lerp(holdSize, readSize, e) / modelSize);
    }

    /// <summary>Makes the hand model once: a copy of the Map inside WorldMap, centred on an anchor.</summary>
    private bool Build()
    {
        Camera cam = GetComponentInChildren<Camera>();
        GameObject world = Resources.Load<GameObject>(ItemCatalog.WorldMapPath);
        Transform source = world != null && world.transform.childCount > 0 ? world.transform.GetChild(0) : null;
        if (cam == null || source == null)
        {
            if (!missingWarned)
                Debug.LogWarning("[MapController] No map model. Run Tools > Pre-Thesis > One Step > Items: Map.", this);
            missingWarned = true;
            return false;
        }

        anchor = new GameObject("Held Map").transform;
        anchor.SetParent(cam.transform, false);
        GameObject model = Instantiate(source.gameObject, anchor, false);
        model.name = "Map";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (renderers.Length > 0)
        {
            // Centre the model on the anchor and remember its size, so any shape fits.
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            model.transform.position += anchor.position - bounds.center;
            Vector3 local = anchor.InverseTransformVector(bounds.size);
            modelSize = Mathf.Max(0.001f, Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        }
        return true;
    }

    private void OnDisable()
    {
        IsReading = false;
        if (anchor != null) anchor.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (anchor != null) Destroy(anchor.gameObject);
    }
}
