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

/// <summary>
/// The popcorn network object owns replication.
///
/// 28 Nov 2026 (Atiroj, agreed with Mr.k): the Ghost Flavor vanishes ONLY when a rule says so
/// (Ticket Zone rule 3 GhostFavorTimer, or the timed ghost of rule 4 arriving in GhostManager),
/// through ServerOnGhostAppeared. Switching the lights off is just a light switch: it no longer
/// makes it vanish (it used to, which also punished players who turned the lights off during
/// the rule 4 warning as the rule tells them to).
///
/// 17 Nov 2026 (Mr.k, Task 6 #46): the displaced Ghost Flavor is a HOTBAR item now. Picking it
/// up needs a free hotbar slot and puts the "Ghost Flavor" token there (ItemCatalog.GhostFlavor).
/// The bottle shows only while that slot is in the carrier's hand. R drops it only while it is
/// in the hand. Bringing it home, dropping it, or losing the token ends the carry.
/// </summary>
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
    private int dropFrame = -1;
    private GhostFavorTrail trail;
    private GhostFavorDisappearVfx disappearVfx;
    private uint disappearanceId;
    private Renderer[] favorRenderers;   // 17 Nov: hidden while it is in the carrier's hotbar but not the hand
    // 28 Nov (Atiroj): carried at the same spot as a held cup / popcorn bucket.
    private PopcornItemHoldingSystem handSource;
    private Vector3 favorCentreLocal;      // bounds centre in the prop's own space (pivot may be at its base)
    private Transform chestOwner;
    // 28 Nov: the jar sits under "PopcornTasteSelect", which is squashed (scale y 0.226; the jar
    // makes up for it with scale y 60). Turning it under that parent shears it (looks stretched),
    // so while it is away from home it has no parent. At home it goes back exactly as authored.
    private Transform homeParent;
    private Vector3 homeLocalPosition;
    private Quaternion homeLocalRotation;
    private Vector3 homeLocalScale;
    private bool detached;
    private Transform chestBone;
    private static readonly Vector3 FallbackHandPosition = new Vector3(-0.3f, -0.24f, 0.65f);
    private static readonly Vector3 FallbackHandRotation = new Vector3(-10f, 0f, -8f);
    private static readonly Vector3 FallbackChestOffset = new Vector3(-.18f, -.1f, .3f);
    // Cups / buckets are in the LEFT hand and can be held at the same time as the Ghost Flavor
    // (separate systems), so it uses the same height and distance on the RIGHT side.
    // false = exactly the cup spot (left side).
    private const bool MirrorToRightHand = true;
    private bool favorShown = true;
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
        homeParent = target.parent;
        homeLocalPosition = target.localPosition;
        homeLocalRotation = target.localRotation;
        homeLocalScale = target.localScale;
        detached = false;
        state = new GhostFavorState { position = homePosition, rotation = homeRotation };
        colliders = target.GetComponentsInChildren<Collider>();
        favorRenderers = target.GetComponentsInChildren<Renderer>(true);
        handSource = GetComponent<PopcornItemHoldingSystem>();
        favorCentreLocal = Vector3.zero;
        if (favorRenderers.Length > 0)
        {
            Bounds bounds = favorRenderers[0].bounds;
            foreach (Renderer r in favorRenderers) if (r != null) bounds.Encapsulate(r.bounds);
            favorCentreLocal = target.InverseTransformPoint(bounds.center);
        }
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
            // 28 Nov: no light watching here any more. Only rules make it vanish (ServerOnGhostAppeared).
            // 17 Nov: the carry ends when the token left the hotbar (thrown away, lost on death ...).
            if (carrier != null && carrier.TryGetComponent(out PlayerInventory carrierInventory) && !carrierInventory.Has(ItemCatalog.GhostFlavor))
                Drop();
            if (carrier != null && !carrier.IsDead && !carrier.IsDowned)
            {
                // 28 Nov: same spot as a held cup / bucket (first person for the host's own carry).
                Vector3 carryPosition;
                Quaternion carryRotation;
                if (carrier != PlayerHealth.LocalInstance || !TryFirstPersonPose(carrier, out carryPosition, out carryRotation))
                    ThirdPersonPose(carrier.transform, out carryPosition, out carryRotation);
                state.position = carryPosition;
                state.rotation = carryRotation;
                if (Vector3.Distance(carrier.transform.position, homePosition) <= 1.8f)
                {
                    TakeToken(carrier);
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
        if (IsLocalHolding && !GameplayInput.Blocked && Keybinds.WasPressed(GameAction.ReloadOrDiscard) &&
            !BlackBoxCarry.UsesLocalDiscardInput) // 17 Nov: only while it is in the hand
        {
            dropFrame = Time.frameCount;
            RequestInteraction(true);
        }
    }

    private void LateUpdate()
    {
        if (favor == null) return;
        GhostFavorState visible = VisibleState;
        SetDetached(visible.displaced); // 28 Nov: before it is turned (no stretching)
        // Observe the replicated event before moving the prop. The smoke stays at home.
        if (!NetworkMode.SessionEnding && disappearVfx != null) disappearVfx.Observe(visible);
        // Only the authority simulates gravity. Clients follow the replicated pose.
        if (!HasAuthority || !falling)
            favor.SetPositionAndRotation(visible.displaced ? visible.position : homePosition,
                visible.displaced ? visible.rotation : homeRotation);
        bool carried = NetworkMode.IsOffline ? carrier != null : visible.carrierId != 0;
        // 28 Nov (Atiroj): held at the same spot as a cup / popcorn bucket.
        // The carrier sees it in front of the camera; others see it at the chest.
        if (carried && IsLocalCarrier && TryFirstPersonPose(PlayerHealth.LocalInstance, out Vector3 handPos, out Quaternion handRot))
            favor.SetPositionAndRotation(handPos, handRot);
        else if (carried && !IsLocalCarrier && NetworkClient.spawned.TryGetValue(visible.carrierId, out NetworkIdentity remote))
        {
            ThirdPersonPose(remote.transform, out Vector3 chestPos, out Quaternion chestRot);
            favor.SetPositionAndRotation(chestPos, chestRot);
        }
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = colliderEnabled[i] && !carried;

        // 17 Nov: in the carrier's hotbar but not in the hand = put away (not drawn).
        bool show = !carried || CarrierHoldsInHand(visible.carrierId);
        if (show != favorShown && favorRenderers != null)
        {
            favorShown = show;
            foreach (Renderer r in favorRenderers) if (r != null) r.enabled = show;
        }
    }

    // ---- 28 Nov (Atiroj): carry pose = the held cup / popcorn bucket pose ----------------

    /// <summary>First person: in front of the carrier's own camera, where a cup / bucket is held.</summary>
    private bool TryFirstPersonPose(PlayerHealth body, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = default;
        Camera view = body != null ? body.GetComponentInChildren<Camera>() : null;
        if (view == null) return false;
        Vector3 hand = handSource != null ? handSource.HandPosition : FallbackHandPosition;
        Vector3 turn = handSource != null ? handSource.HandRotation : FallbackHandRotation;
        if (MirrorToRightHand) { hand.x = Mathf.Abs(hand.x); turn.y = -turn.y; turn.z = -turn.z; }
        rotation = view.transform.rotation * Quaternion.Euler(turn);
        position = Centred(view.transform.TransformPoint(hand), rotation);
        return true;
    }

    /// <summary>What other players see: at the chest, same offset as a held cup / bucket (PlayerHeldItems).</summary>
    private void ThirdPersonPose(Transform body, out Vector3 position, out Quaternion rotation)
    {
        PlayerHeldItems held = body.GetComponent<PlayerHeldItems>();
        Vector3 offset = held != null ? held.TaskOffset : FallbackChestOffset;
        if (MirrorToRightHand) offset.x = Mathf.Abs(offset.x);
        if (chestOwner != body)
        {
            chestOwner = body;
            Animator animator = body.GetComponentInChildren<Animator>();
            chestBone = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
        }
        Vector3 centre = chestBone != null ? chestBone.position : body.position + Vector3.up * 1.3f;
        rotation = Quaternion.LookRotation(body.forward, Vector3.up);
        position = Centred(centre + body.right * offset.x + Vector3.up * offset.y + body.forward * offset.z, rotation);
    }

    /// <summary>28 Nov: away from home = no parent (no shear from the squashed parent); home = as authored.</summary>
    private void SetDetached(bool on)
    {
        if (favor == null || detached == on) return;
        detached = on;
        if (on)
        {
            // Only ever detached from the home pose, where the world scale is still exact.
            favor.SetParent(null, true);
            return;
        }
        favor.SetParent(homeParent, false);
        favor.localPosition = homeLocalPosition;
        favor.localRotation = homeLocalRotation;
        favor.localScale = homeLocalScale;
    }

    /// <summary>Puts the prop's visual centre (not its pivot) on the grip point, like the cups.</summary>
    private Vector3 Centred(Vector3 grip, Quaternion rotation) =>
        grip - rotation * Vector3.Scale(favorCentreLocal, favor.lossyScale);

    /// <summary>17 Nov: the local player carries it AND has its hotbar slot in the hand.</summary>
    public bool IsLocalHolding => IsLocalCarrier && PlayerInventory.Local != null && PlayerInventory.Local.IsHolding(ItemCatalog.GhostFlavor);

    private bool CarrierHoldsInHand(uint carrierNetId)
    {
        PlayerHealth body = NetworkMode.IsOffline ? carrier
            : NetworkClient.spawned.TryGetValue(carrierNetId, out NetworkIdentity id) && id != null ? id.GetComponent<PlayerHealth>()
            : NetworkServer.active && NetworkServer.spawned.TryGetValue(carrierNetId, out id) && id != null ? id.GetComponent<PlayerHealth>() : null;
        PlayerInventory inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
        return inventory == null || inventory.IsHolding(ItemCatalog.GhostFlavor);
    }

    /// <summary>17 Nov, authority: the carrier's hotbar token goes.</summary>
    private static void TakeToken(PlayerHealth player)
    {
        if (player != null && player.TryGetComponent(out PlayerInventory inventory)) inventory.ServerRemoveAll(ItemCatalog.GhostFlavor);
    }

    private void Drop()
    {
        TakeToken(carrier);
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

    /// <summary>
    /// Server/offline. The ONLY way the Ghost Flavor vanishes: called by the rules
    /// (GhostFavorTimer = rule 3, GhostManager.SpawnGhost = rule 4). Light switches never call it.
    /// </summary>
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
        // Leave a recovery already in progress alone, including on later rule triggers.
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
        ? "Ghost Flavor · Carry back to the popcorn station · [R] Drop (in hand)"
        : VisibleState.carrierId != 0 ? "Ghost Flavor · Another player is carrying it"
        : PlayerInventory.Local != null && !PlayerInventory.Local.HasEmptySlot ? "Ghost Flavor · Hotbar full: make room first"
        : "Ghost Flavor · [E] Pick up (takes a hotbar slot) and return to the popcorn station";

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
            // 17 Nov: the Ghost Flavor takes a hotbar slot, so it needs a free one.
            if (!player.TryGetComponent(out PlayerInventory inventory) || !inventory.ServerAddToken(ItemCatalog.GhostFlavor)) return false;
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
