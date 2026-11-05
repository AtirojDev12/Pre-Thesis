using UnityEngine;

/// <summary>
/// 5 Nov 2026 (Mr.k): a door in the Lobby scene. HOLD E to walk through.
///   - Kind = ToTutorial (in the lobby): takes you to the tutorial room of the map
///     chosen when the room was created (<see cref="TutorialRoom.For"/>).
///   - Kind = BackToLobby (inside a tutorial room): takes you back to the lobby door
///     you came in through.
///
/// LOCAL ONLY, like the shop / storage terminals: only your own body moves. Player
/// movement is owner-authoritative (NetworkTransform client -> server), so the move
/// reaches everyone with no extra network code. Placed by the Lobby tutorial tools.
/// </summary>
[DisallowMultipleComponent]
public sealed class TutorialDoor : MonoBehaviour, ILocalInteractable, IInteractionSurfaceTint
{
    public enum Kind { ToTutorial, BackToLobby }

    [SerializeField] private Kind kind = Kind.ToTutorial;
    [Tooltip("Hold E this long.")]
    [SerializeField, Min(0.2f)] private float holdSeconds = 1.5f;
    [Tooltip("ToTutorial door only: where you appear when you come back (in front of this door). Empty = 1.5 m in front.")]
    [SerializeField] private Transform returnPoint;

    private const float MaxDistance = 4f; // interaction range 3 m + slack

    // The lobby door this PC's player came in through (local: one player per PC).
    private static TutorialDoor lastEntrance;

    private bool holding;
    private float holdStart;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private bool highlighted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastEntrance = null;

    public string GetInteractionPrompt()
    {
        if (kind == Kind.BackToLobby) return "[Hold E] Back to the lobby";
        return "[Hold E] Tutorial: " + RoomDisplay.MapName(TutorialRoom.CurrentMapID());
    }

    public bool CanInteract() => isActiveAndEnabled && !holding;
    public Transform GetTransform() => transform;

    /// <summary>Local player pressed E on this door: start the hold.</summary>
    public void Interact(GameObject interactor)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || interactor != me.gameObject) return;
        holding = true;
        holdStart = Time.unscaledTime;
    }

    private void Update()
    {
        if (!holding) return;
        PlayerHealth me = PlayerHealth.LocalInstance;
        bool keep = me != null && !me.IsDead && !me.IsDowned
            && Keybinds.IsPressed(GameAction.Interact) && !GameplayInput.Blocked
            && (me.transform.position - transform.position).sqrMagnitude <= MaxDistance * MaxDistance;
        if (!keep) { Cancel(); return; }

        float t = (Time.unscaledTime - holdStart) / holdSeconds;
        ChoreProgressHUD.Show(this, kind == Kind.ToTutorial ? "Entering tutorial" : "Going back", t);
        if (t < 1f) return;
        Cancel();
        Go(me);
    }

    private void Go(PlayerHealth me)
    {
        if (kind == Kind.ToTutorial)
        {
            string map = TutorialRoom.CurrentMapID();
            TutorialRoom room = TutorialRoom.For(map);
            if (room == null)
            {
                RuleFx.Toast("No tutorial for " + RoomDisplay.MapName(map) + " yet.", 3f);
                return;
            }
            lastEntrance = this;
            Transform arrival = room.ArrivalPoint;
            Teleport(me, arrival.position, arrival.forward);
            return;
        }

        TutorialDoor back = lastEntrance != null ? lastEntrance : AnyEntrance();
        if (back == null) { RuleFx.Toast("No lobby door found.", 3f); return; }
        back.ReturnPose(out Vector3 position, out Vector3 forward);
        Teleport(me, position, forward);
        lastEntrance = null;
    }

    /// <summary>Where you appear back in the lobby: the return point, or 1.5 m in front of the door facing away from it.</summary>
    private void ReturnPose(out Vector3 position, out Vector3 forward)
    {
        if (returnPoint != null) { position = returnPoint.position; forward = returnPoint.forward; return; }
        position = transform.position + transform.forward * 1.5f;
        position.y = transform.position.y;
        forward = transform.forward;
    }

    private static TutorialDoor AnyEntrance()
    {
        foreach (TutorialDoor d in FindObjectsByType<TutorialDoor>())
            if (d.kind == Kind.ToTutorial) return d;
        return null;
    }

    /// <summary>Moves my own body (same way as the F1 teleport). Keeps only the facing (yaw).</summary>
    private static void Teleport(PlayerHealth me, Vector3 position, Vector3 forward)
    {
        // Points sit on the floor; the body's pivot is not at its feet. Keep the same
        // height above the floor the body has now (+ a little), so it never lands in the floor.
        float lift = 1f;
        if (Physics.Raycast(me.transform.position, Vector3.down, out RaycastHit hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            lift = Mathf.Clamp(me.transform.position.y - hit.point.y, 0f, 2.5f);
        position.y += lift + 0.05f;
        forward.y = 0f;
        Quaternion facing = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : me.transform.rotation;
        Rigidbody body = me.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = facing;
        }
        me.transform.SetPositionAndRotation(position, facing);
        Physics.SyncTransforms();
    }

    private void Cancel()
    {
        holding = false;
        ChoreProgressHUD.Hide(this);
    }

    public void SetHighlighted(bool on)
    {
        if (on == highlighted) return;
        highlighted = on;
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!on) { r.SetPropertyBlock(null); continue; }
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f));
            block.SetColor("_Color", new Color(0.55f, 0.85f, 1f));
            r.SetPropertyBlock(block);
        }
    }

    private void OnDisable()
    {
        Cancel();
        SetHighlighted(false);
    }

    /// <summary>Editor tool.</summary>
    public void Set(Kind doorKind, Transform comeBackPoint)
    {
        kind = doorKind;
        returnPoint = comeBackPoint;
    }
}
