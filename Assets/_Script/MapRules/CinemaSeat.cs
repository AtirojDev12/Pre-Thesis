using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): ONE cinema seat (the folding seat of a ScreenRoomChair model).
///   - Players: look at it and press E to sit, SPACE to stand up (SeatSystem does the rest).
///   - The seat cushion folds OPEN while someone (player or ghost) sits, and shuts again after.
///   - Ghost seats (audience members, the VIP seat) cannot be taken by players.
/// Made by Tools > Pre-Thesis > One Step > Map Rules (it finds every chair cushion).
/// The collider is the folded cushion only, so players still walk along the rows.
/// LOCAL interaction (no NetworkIdentity per seat): the sit request goes through SeatSystem.
/// </summary>
[DisallowMultipleComponent]
public sealed class CinemaSeat : MonoBehaviour, ILocalInteractable
{
    [Tooltip("Index in SeatSystem's seat list (set by the setup tool).")]
    [SerializeField] private int index = -1;
    [Tooltip("True = the ghost screen room.")]
    [SerializeField] private bool ghostRoom;
    [Tooltip("A ghost sits here (audience / VIP): players cannot take it.")]
    [SerializeField] private bool ghostOnly;
    [Tooltip("Always open (an audience ghost sits here all night).")]
    [SerializeField] private bool alwaysOpen;

    [Header("Folding cushion (set by the setup tool)")]
    [SerializeField] private Transform cushion;
    [SerializeField] private Vector3 closedLocalPosition, openLocalPosition;
    [SerializeField] private Quaternion closedLocalRotation = Quaternion.identity, openLocalRotation = Quaternion.identity;

    [Header("Places (world, set by the setup tool)")]
    [Tooltip("Floor point under the open cushion (a sitting body's feet / root).")]
    [SerializeField] private Vector3 floorPoint;
    [Tooltip("Where you stand after getting up (in front of the seat).")]
    [SerializeField] private Vector3 standPoint;
    [Tooltip("Facing while seated (towards the screen).")]
    [SerializeField] private float yaw;

    private float open01;
    private Collider ownCollider;

    public int Index => index;
    public bool GhostRoom => ghostRoom;
    public bool GhostOnly => ghostOnly;
    public Vector3 FloorPoint => floorPoint;
    /// <summary>23 Nov: the folding cushion part of the chair model (the VIP seat paints it gold).</summary>
    public Transform Cushion => cushion;
    public Vector3 StandPoint => standPoint;
    public float Yaw => yaw;
    public Quaternion Facing => Quaternion.Euler(0f, yaw, 0f);

    private void Awake() => ownCollider = GetComponent<Collider>();

    // ---- E press (local) ---------------------------------------------------------------------

    public string GetInteractionPrompt() => MapRuleTuning.Current.sitPrompt;

    public bool CanInteract()
    {
        SeatSystem seats = SeatSystem.Instance;
        PlayerHealth me = PlayerHealth.LocalInstance;
        return seats != null && !ghostOnly && !alwaysOpen && me != null && !me.IsDead && !me.IsDowned
               && seats.LocalSeat < 0 && !seats.IsTaken(index);
    }

    public void Interact(GameObject interactor)
    {
        if (CanInteract()) SeatSystem.Instance.LocalRequestSit(index);
    }

    public Transform GetTransform() => transform;

    // ---- Fold open / shut --------------------------------------------------------------------

    private void Update()
    {
        SeatSystem seats = SeatSystem.Instance;
        bool open = (alwaysOpen && AudienceShown()) || (seats != null && seats.IsTaken(index));
        float target = open ? 1f : 0f;
        if (Mathf.Approximately(open01, target)) return;
        open01 = Mathf.MoveTowards(open01, target, Time.deltaTime / MapRuleTuning.Current.seatFoldSeconds);
        ApplyFold();
    }

    private void Start()
    {
        open01 = alwaysOpen ? 1f : 0f;
        ApplyFold();
    }

    private static GhostAudience audience;
    private static bool audienceSearched;

    /// <summary>The audience is in its seats (Staff Time hides it: the seats shut).</summary>
    private static bool AudienceShown()
    {
        if (!audienceSearched || (audience == null && Time.frameCount % 120 == 0))
        {
            audienceSearched = true;
            audience = FindAnyObjectByType<GhostAudience>();
        }
        return audience == null || audience.Visible;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { audience = null; audienceSearched = false; }

    private void ApplyFold()
    {
        if (cushion != null)
        {
            cushion.localPosition = Vector3.Lerp(closedLocalPosition, openLocalPosition, open01);
            cushion.localRotation = Quaternion.Slerp(closedLocalRotation, openLocalRotation, open01);
        }
        // An open seat never blocks: the sitter is inside it.
        if (ownCollider != null) ownCollider.enabled = open01 < 0.5f;
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetup(int seatIndex, bool inGhostRoom, Transform cushionPart, Vector3 closedPos, Quaternion closedRot,
        Vector3 openPos, Quaternion openRot, Vector3 floor, Vector3 stand, float faceYaw)
    {
        index = seatIndex;
        ghostRoom = inGhostRoom;
        cushion = cushionPart;
        closedLocalPosition = closedPos;
        closedLocalRotation = closedRot;
        openLocalPosition = openPos;
        openLocalRotation = openRot;
        floorPoint = floor;
        standPoint = stand;
        yaw = faceYaw;
    }

    /// <summary>Editor tool: a ghost sits here (always open = an audience member all night).</summary>
    public void EditorSetGhost(bool onlyGhosts, bool openAllNight) { ghostOnly = onlyGhosts; alwaysOpen = openAllNight; }
    public void EditorSetIndex(int seatIndex) => index = seatIndex;
}
