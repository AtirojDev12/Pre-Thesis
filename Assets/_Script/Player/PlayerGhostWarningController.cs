using System.Collections.Generic;
using Mirror;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(GhostWarningEffect))]
public sealed class PlayerGhostWarningController : MonoBehaviour
{
    [SerializeField] private bool warningsEnabled = true;
    [SerializeField, Min(0f)] private float cooldown = 3f;
    private readonly Dictionary<GhostWarningSource, bool> inside = new Dictionary<GhostWarningSource, bool>();
    private readonly List<GhostWarningSource> expired = new List<GhostWarningSource>();
    private NetworkIdentity owner;
    private PlayerHealth health;
    private Camera view;
    private FirstPersonCamera rig;
    private GhostWarningEffect effect;
    private GhostWarningSource pendingEvent, playingSource;
    private float nextWarningTime, suppressedUntil;
    public static PlayerGhostWarningController LocalInstance { get; private set; }
    public float FovReduction => CanDisplay ? effect.FovReduction : 0f;
    private bool IsLocal => !NetworkMode.SessionEnding && !SpectatorSession.Active
        && (NetworkMode.IsOffline || (NetworkClient.active && owner != null && owner.isLocalPlayer));
    private bool CanDisplay => IsLocal && warningsEnabled && rig != null && rig.enabled
        && view != null && view.isActiveAndEnabled && (health == null || (!health.IsDead && !health.IsDowned))
        && Time.time >= suppressedUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => LocalInstance = null;
    private void Awake()
    {
        owner = GetComponentInParent<NetworkIdentity>();
        health = GetComponentInParent<PlayerHealth>();
        rig = GetComponent<FirstPersonCamera>();
        view = GetComponentInChildren<Camera>(true);
        effect = GetComponent<GhostWarningEffect>();
    }
    private void Update()
    {
        if (IsLocal) LocalInstance = this;
        if (!IsLocal)
        {
            effect.Cancel();
            pendingEvent = playingSource = null;
            inside.Clear();
            return;
        }
        bool canDisplay = CanDisplay;
        if (!canDisplay) effect.Cancel();
        if (playingSource != null && !playingSource.Available) effect.Cancel();
        // A destroyed Unity object compares equal to null but still has a managed reference.
        if (!ReferenceEquals(playingSource, null) && playingSource == null) effect.Cancel();
        GhostWarningSource candidate = pendingEvent;
        pendingEvent = null;
        foreach (GhostWarningSource source in GhostWarningSource.Sources)
        {
            inside.TryGetValue(source, out bool wasInside);
            bool nowInside = source.Contains(health != null ? health.transform.position : transform.position, wasInside);
            inside[source] = nowInside;
            if (nowInside && !wasInside && Prefer(source, candidate)) candidate = source;
        }
        expired.Clear();
        foreach (GhostWarningSource source in inside.Keys)
            if (source == null || !GhostWarningSource.Sources.Contains(source)) expired.Add(source);
        foreach (GhostWarningSource source in expired) inside.Remove(source);
        // Entries during playback/cooldown are consumed, never queued for a surprise delayed pulse.
        if (canDisplay && candidate != null && candidate.Available && !effect.IsPlaying && Time.time >= nextWarningTime)
        {
            playingSource = candidate;
            effect.Play();
            nextWarningTime = Time.time + cooldown;
        }
    }
    private bool Prefer(GhostWarningSource source, GhostWarningSource other)
    {
        if (other == null) return true;
        if (source.Priority != other.Priority) return source.Priority > other.Priority;
        return (source.transform.position - transform.position).sqrMagnitude
            < (other.transform.position - transform.position).sqrMagnitude;
    }
    public void ReceiveEvent(GhostWarningSource source)
    {
        if (CanDisplay && source != null && source.Available && Prefer(source, pendingEvent)) pendingEvent = source;
    }
    public static void SuppressLocal(float duration)
    {
        var controller = LocalInstance;
        if (controller == null) return;
        controller.suppressedUntil = Mathf.Max(controller.suppressedUntil, Time.time + duration);
        controller.pendingEvent = controller.playingSource = null;
        controller.effect.Cancel();
    }
    private void OnDisable()
    {
        if (LocalInstance == this) LocalInstance = null;
        if (effect != null) effect.Cancel();
        pendingEvent = playingSource = null;
        inside.Clear();
    }
}
