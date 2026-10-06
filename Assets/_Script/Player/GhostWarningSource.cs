using System.Collections.Generic;
using Mirror;
using UnityEngine;

public enum GhostWarningCategory { None, Proximity, DangerZone, Event }

/// <summary>Author the danger here; visuals belong exclusively to the local player.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(NetworkIdentity))]
public sealed class GhostWarningSource : NetworkBehaviour
{
    internal static readonly HashSet<GhostWarningSource> Sources = new HashSet<GhostWarningSource>();
    [SerializeField] private GhostWarningCategory category = GhostWarningCategory.Proximity;
    [Tooltip("Server-controlled gate. Call SetWarningActive when the ghost becomes dangerous/safe.")]
    [SyncVar, SerializeField] private bool warningActive = true;
    [SerializeField, Min(0.1f)] private float enterRadius = 5f;
    [SerializeField, Min(0.1f)] private float exitRadius = 6f;
    [Tooltip("A dedicated zone collider, preferably a trigger. Required for DangerZone.")]
    [SerializeField] private Collider dangerZone;
    [Tooltip("Distance outside the zone required to rearm the warning.")]
    [SerializeField, Min(0.01f)] private float zoneExitMargin = 1f;
    [SerializeField] private int priority;
    public GhostWarningCategory Category => category;
    public int Priority => priority;
    public bool Available => isActiveAndEnabled && warningActive
        && (NetworkMode.IsOffline || (NetworkClient.active && netId != 0));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Sources.Clear();
    private void OnEnable() => Sources.Add(this);
    private void OnDisable() => Sources.Remove(this);
    protected override void OnValidate()
    {
        base.OnValidate();
        exitRadius = Mathf.Max(exitRadius, enterRadius + 0.01f);
    }

    public bool Contains(Vector3 position, bool previouslyInside)
    {
        if (!Available) return false;
        if (category == GhostWarningCategory.Proximity)
        {
            float radius = previouslyInside ? Mathf.Max(exitRadius, enterRadius + 0.01f) : enterRadius;
            return (position - transform.position).sqrMagnitude <= radius * radius;
        }
        if (category == GhostWarningCategory.DangerZone && dangerZone != null && dangerZone.enabled
            && dangerZone.gameObject.activeInHierarchy)
        {
            float margin = previouslyInside ? zoneExitMargin : 0.001f;
            return (dangerZone.ClosestPoint(position) - position).sqrMagnitude <= margin * margin;
        }
        return false;
    }

    public void SetWarningActive(bool active)
    {
        if (NetworkMode.HasServerAuthority(this)) warningActive = active;
    }

    /// <summary>Call on the server for a new danger event, with the affected player explicitly.</summary>
    public void WarnPlayer(PlayerHealth player)
    {
        if (!NetworkMode.HasServerAuthority(this) || !warningActive || !isActiveAndEnabled
            || category != GhostWarningCategory.Event || player == null || player.IsDead || player.IsDowned) return;
        if (NetworkMode.IsOffline)
        {
            if (PlayerHealth.LocalInstance == player) PlayerGhostWarningController.LocalInstance?.ReceiveEvent(this);
        }
        else if (player.connectionToClient != null) TargetWarn(player.connectionToClient);
    }

    [TargetRpc]
    private void TargetWarn(NetworkConnectionToClient recipient)
        => PlayerGhostWarningController.LocalInstance?.ReceiveEvent(this);

    private void OnDrawGizmosSelected()
    {
        if (category != GhostWarningCategory.Proximity) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, enterRadius);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(exitRadius, enterRadius + 0.01f));
    }
}
