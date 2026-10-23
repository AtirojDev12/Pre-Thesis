using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public struct GhostFavorState
{
    public bool displaced;
    public uint carrierId;
    public Vector3 position;
    public Quaternion rotation;
    public uint disappearanceId;
    public double disappearedAt;
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
    private GhostFavorTrail trail;
    private GhostFavorDisappearVfx disappearVfx;
    private uint disappearanceId;
    private bool HasAuthority => !NetworkMode.SessionEnding && (NetworkMode.IsOffline ||
        (PopcornNetSync.Instance != null && PopcornNetSync.Instance.isServer));
    public bool DroppedThisFrame => dropFrame == Time.frameCount;
    public bool IsHome => !VisibleState.displaced;
    public Vector3 HomePosition => homePosition;
    public Transform ItemTransform => favor;
    public Vector3 AuthorityPosition => state.position;
    public bool HasTrailAuthority => HasAuthority;
    public bool IsCarried => NetworkMode.IsOffline ? carrier != null : VisibleState.carrierId != 0;
    public bool IsMovingForTrail => falling && favorBody != null && !favorBody.IsSleeping() &&
        favorBody.linearVelocity.sqrMagnitude > .01f;
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
        trail = GetComponent<GhostFavorTrail>();
        if (trail != null) trail.Configure(this);
        disappearVfx = GetComponent<GhostFavorDisappearVfx>();
        if (disappearVfx == null) disappearVfx = gameObject.AddComponent<GhostFavorDisappearVfx>();
        disappearVfx.Configure(this);
    }

    private void Update()
    {
        if (favor == null || NetworkMode.SessionEnding) return;
        if (HasAuthority)
        {
            bool dark = BuildingLightManager.Instance != null && !BuildingLightManager.LightsOn;
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
                    state.displaced = false;
                    state.carrierId = 0;
                    state.position = homePosition;
                    state.rotation = homeRotation;
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
            PublishState();
        }
        if (IsLocalCarrier && !GameplayInput.Blocked && Keybinds.WasPressed(GameAction.ReloadOrDiscard) &&
            !BlackBoxCarry.UsesLocalDiscardInput)
        {
            dropFrame = Time.frameCount;
            RequestInteraction(true);
        }
    }

    private void LateUpdate()
    {
        if (favor == null) return;
        GhostFavorState visible = VisibleState;
        // Observe the replicated event before moving the prop. The smoke stays at home.
        if (!NetworkMode.SessionEnding && disappearVfx != null) disappearVfx.Observe(visible);
        // Only the authority simulates gravity. Clients follow the replicated pose.
        if (!HasAuthority || !falling)
            favor.SetPositionAndRotation(visible.displaced ? visible.position : homePosition,
                visible.displaced ? visible.rotation : homeRotation);
        bool carried = NetworkMode.IsOffline ? carrier != null : visible.carrierId != 0;
        // Keep the owner's recovery view; observers see the existing prop beside the chest.
        if (carried && !IsLocalCarrier && NetworkClient.spawned.TryGetValue(visible.carrierId, out NetworkIdentity remote))
        {
            Animator animator = remote.GetComponentInChildren<Animator>();
            Transform chest = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            Vector3 centre = chest != null ? chest.position : remote.transform.position + Vector3.up * 1.3f;
            favor.SetPositionAndRotation(centre + remote.transform.right * .42f + remote.transform.forward * .3f,
                remote.transform.rotation);
        }
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
        PublishState();
    }

    /// <summary>Server/offline arrival trigger, independent of warning flickers and light switches.</summary>
    public void ServerOnGhostAppeared()
    {
        if (favor == null || !HasAuthority) return;
        Relocate();
        PublishState();
    }

    private void PublishState()
    {
        if (PopcornNetSync.Instance != null && PopcornNetSync.Instance.isServer)
            PopcornNetSync.Instance.SetGhostFavorState(state);
    }

    private void Relocate()
    {
        // Leave a recovery already in progress alone, including on later blackouts.
        if (state.displaced) return;
        var candidates = new List<Transform>();
        if (destinations != null)
            foreach (Transform point in destinations)
                if (point != null && !candidates.Contains(point)) candidates.Add(point);
        if (candidates.Count == 0)
        {
            TicketMinigame tickets = FindAnyObjectByType<TicketMinigame>();
            if (tickets != null && tickets.GhostFavorDestination != null) candidates.Add(tickets.GhostFavorDestination);
        }
        // Shuffle once: each reachable authored point has an equal chance, and an
        // unreachable/null point cannot strand the item or disable all other points.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int other = Random.Range(0, i + 1);
            Transform swap = candidates[i]; candidates[i] = candidates[other]; candidates[other] = swap;
        }
        foreach (Transform destination in candidates)
        {
            Vector3[] route = null;
            if (trail != null && trail.RequiresReachableDestination && !trail.TryPlanDestination(destination, out route)) continue;
            state = new GhostFavorState
            {
                displaced = true, position = destination.position, rotation = destination.rotation,
                disappearanceId = ++disappearanceId,
                disappearedAt = NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time
            };
            if (trail != null) trail.AcceptRelocation(state.position, route);
            return;
        }
        if (trail != null && trail.RequiresReachableDestination) trail.ReportNoReachableDestination();
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
        if (!HasAuthority) return false;
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
            PublishState();
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
