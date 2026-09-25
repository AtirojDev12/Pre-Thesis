using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public struct GhostFavorState
{
    public bool displaced;
    public uint carrierId;
    public Vector3 position;
    public Quaternion rotation;
}

/// <summary>Observes the existing light state; the popcorn network object owns replication.</summary>
public sealed class GhostFavorRecovery : MonoBehaviour
{
    public static GhostFavorRecovery Instance { get; private set; }
    private Transform favor;
    private Transform[] destinations;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private GhostFavorState state;
    private PlayerHealth carrier;
    private Collider[] colliders;
    private bool[] colliderEnabled;
    private Rigidbody favorBody;
    private bool falling;
    private bool wasCarried;
    private float darkSeconds;
    private bool blackoutHandled;
    private int dropFrame = -1;
    public bool DroppedThisFrame => dropFrame == Time.frameCount;
    public bool IsHome => !VisibleState.displaced;
    public GhostFavorState VisibleState => !NetworkMode.IsOffline && PopcornNetSync.Instance != null
        ? PopcornNetSync.Instance.GhostFavor : state;
    public bool IsLocalCarrier => PlayerHealth.LocalInstance != null && (NetworkMode.IsOffline
        ? carrier == PlayerHealth.LocalInstance : VisibleState.carrierId != 0 &&
          VisibleState.carrierId == PlayerHealth.LocalInstance.netId);

    public void Configure(Transform target, Transform[] relocationPoints)
    {
        Instance = this;
        favor = target;
        destinations = relocationPoints;
        homePosition = target.position;
        homeRotation = target.rotation;
        state = new GhostFavorState { position = homePosition, rotation = homeRotation };
        colliders = target.GetComponentsInChildren<Collider>();
        colliderEnabled = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderEnabled[i] = colliders[i].enabled;
        favorBody = target.GetComponent<Rigidbody>();
        if (favorBody == null) favorBody = target.gameObject.AddComponent<Rigidbody>();
        favorBody.isKinematic = true;
        favorBody.useGravity = false;
        favorBody.constraints = RigidbodyConstraints.FreezeRotation;
        favorBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void Update()
    {
        if (favor == null || NetworkMode.SessionEnding) return;
        if (NetworkMode.IsOffline || (PopcornNetSync.Instance != null && PopcornNetSync.Instance.isServer))
        {
            bool dark = GhostManager.Instance != null && !GhostManager.Instance.areLightsOnCurrently;
            darkSeconds = dark ? darkSeconds + Time.deltaTime : 0f;
            if (!dark) blackoutHandled = false;
            // Warning flickers last at most 0.2s; a sustained outage is a blackout.
            if (darkSeconds >= 0.5f && !blackoutHandled)
            {
                blackoutHandled = true;
                Relocate();
            }
            if (carrier != null && !carrier.IsDead && !carrier.IsDowned)
            {
                state.position = carrier.transform.TransformPoint(new Vector3(0.45f, 1f, 0.8f));
                state.rotation = carrier.transform.rotation;
                if (Vector3.Distance(carrier.transform.position, homePosition) <= 1.8f)
                {
                    carrier = null;
                    wasCarried = false;
                    state = new GhostFavorState { position = homePosition, rotation = homeRotation };
                }
            }
            else if (wasCarried)
            {
                Drop();
            }
            if (falling)
            {
                state.position = favorBody.position;
                state.rotation = favorBody.rotation;
            }
            if (PopcornNetSync.Instance != null && PopcornNetSync.Instance.isServer)
                PopcornNetSync.Instance.SetGhostFavorState(state);
        }
        if (IsLocalCarrier && !GameplayInput.Blocked && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            dropFrame = Time.frameCount;
            RequestInteraction(true);
        }
    }

    private void LateUpdate()
    {
        if (favor == null) return;
        GhostFavorState visible = VisibleState;
        // Only the authority simulates gravity. Clients follow the replicated pose.
        if (!falling)
            favor.SetPositionAndRotation(visible.displaced ? visible.position : homePosition,
                visible.displaced ? visible.rotation : homeRotation);
        bool carried = NetworkMode.IsOffline ? carrier != null : visible.carrierId != 0;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i] && !carried;
    }

    private void Drop()
    {
        carrier = null;
        wasCarried = false;
        state.carrierId = 0;
        favorBody.position = state.position;
        favorBody.rotation = state.rotation;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i];
        favorBody.isKinematic = false;
        favorBody.useGravity = true;
        falling = true;
    }

    private void Relocate()
    {
        // Leave a recovery already in progress alone, including on later blackouts.
        if (state.displaced) return;
        Transform destination = null;
        if (destinations != null && destinations.Length > 0)
            destination = destinations[Random.Range(0, destinations.Length)];
        if (destination == null)
        {
            TicketMinigame tickets = FindAnyObjectByType<TicketMinigame>();
            if (tickets != null) destination = tickets.GhostFavorDestination;
        }
        if (destination == null) return;
        state = new GhostFavorState { displaced = true, position = destination.position, rotation = destination.rotation };
    }

    public string Prompt => IsHome ? "Ghost Flavor" : IsLocalCarrier
        ? "Ghost Flavor · Carry back to the popcorn station · [R] Drop"
        : VisibleState.carrierId != 0 ? "Ghost Flavor · Another player is carrying it"
        : "Ghost Flavor · [E] Pick up and return to the popcorn station";

    public void RequestInteraction(bool drop = false)
    {
        if (NetworkMode.IsOffline)
        {
            if (ServerInteract(PlayerHealth.LocalInstance, drop)) MixLocally();
        }
        else if (PopcornNetSync.Instance != null) PopcornNetSync.Instance.RequestGhostFavor(drop);
    }

    // Returns true only when this request is allowed to mix seasoning at home.
    public bool ServerInteract(PlayerHealth player, bool drop)
    {
        if (!NetworkMode.IsOffline && (PopcornNetSync.Instance == null || !PopcornNetSync.Instance.isServer)) return false;
        if (player == null || player.IsDead || player.IsDowned) return false;
        if (drop)
        {
            if (carrier == player) Drop();
            return false;
        }
        Vector3 target = state.displaced ? state.position : homePosition;
        if (Vector3.Distance(player.transform.position, target) > 4.5f) return false;
        if (!state.displaced) return true;
        if (carrier == null)
        {
            if (falling)
            {
                favorBody.linearVelocity = Vector3.zero;
                favorBody.angularVelocity = Vector3.zero;
            }
            favorBody.isKinematic = true;
            favorBody.useGravity = false;
            falling = false;
            carrier = player;
            wasCarried = true;
            state.carrierId = NetworkMode.IsOffline ? 0 : player.netId;
        }
        return false;
    }

    public void MixLocally()
    {
        if (!IsHome) return;
        GetComponent<PopcornPreparation>().MixGhostAtHome();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
