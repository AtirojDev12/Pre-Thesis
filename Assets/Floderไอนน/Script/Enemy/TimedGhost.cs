using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Server-owned ghost: sees players in lit zones, hears them in dark zones, and searches only the last perceived position.</summary>
public class TimedGhost : Enemy_Abstract_Class
{
    [Header("Ghost Visual Settings")]
    [Tooltip("Jumpscare overlay INSIDE this ghost's prefab, or left empty to find one tagged 'JumpscareUI' in the scene. It is shown only on the victim's screen.")]
    [SerializeField] private GameObject jumpscareUI;
    [SerializeField] private float jumpscareDuration = 3.0f;

    [Header("Damage")]
    [Tooltip("Damage dealt to the victim. The GDD makes a Jump Scare instant death — tick killsOutright for that instead of raising this.")]
    [SerializeField] private float jumpscareDamage = 25f;

    [Tooltip("GDD rule: a Jump Scare kills outright, skipping the downed state. Leave OFF while testing so a mistake does not end the run.")]
    [SerializeField] private bool killsOutright = false;

    [Header("Mic Settings (Nonnapat)")]
    [Tooltip("ServerMic (0..1) above this = too loud. ServerTooLoud (the red line on the noise meter) always counts too.")]
    [SerializeField] private float micVolumeThreshold = 0.3f;

    [Tooltip("OFF = the ghost only listens in the dark (Nonnapat's rule). ON = it also chases loud players while the lights are on.")]
    [SerializeField] private bool hearMicInLight = false;

    [Header("Patrol Settings (ความมืด)")]
    [SerializeField] private float randomPatrolRadius = 15f;

    [Tooltip("How close the ghost must get before the jumpscare fires.")]
    [SerializeField] private float jumpscareRange = 1.5f;
    [Tooltip("Height above the ghost/player origin for wall occlusion checks.")]
    [SerializeField, Min(0f)] private float sightHeight = 1.2f;

    [Header("Search after losing a player")]
    [SerializeField, Min(0f)] private float searchWaitSeconds = 3f;
    [SerializeField, Min(1f)] private float searchTravelTimeout = 15f;
    private bool isJumpscareTriggered;
    private Vector3 lastKnownPosition;
    private float searchStartedAt;
    private float searchArrivedAt = -1f;

    protected override void Start()
    {
        base.Start();

        if (agent == null) agent = GetComponent<NavMeshAgent>();

        // Only the machine running the AI needs to sit on the NavMesh.
        // (Nonnapat: enable the agent and search 5 m so it never floats.)
        if (HasAiAuthority && agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        if (jumpscareUI == null)
        {
            // Nonnapat's lookup: the overlay is usually INACTIVE, which
            // FindGameObjectWithTag cannot see. The scene check skips prefab assets.
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj.CompareTag("JumpscareUI") && obj.scene.IsValid())
                {
                    jumpscareUI = obj;
                    break;
                }
            }
        }

        if (jumpscareUI != null) jumpscareUI.SetActive(false);
    }

    protected override void CheckForPlayer()
    {
        if (!HasAiAuthority || isJumpscareTriggered) return;
        PlayerHealth noticed = null;
        float nearest = float.PositiveInfinity;
        foreach (PlayerHealth candidate in PlayerRegistry.All)
        {
            if (!CanPerceive(candidate)) continue;
            float distance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            noticed = candidate;
        }
        if (noticed != null)
        {
            targetPlayer = noticed;
            lastKnownPosition = noticed.transform.position;
            OnPlayerSpotted();
        }
        else if (currentState == EnemyState.Chase)
        {
            // Never update this position from the unseen/silent player's Transform.
            targetPlayer = null;
            OnPlayerLost();
            searchStartedAt = Time.time;
            searchArrivedAt = -1f;
        }
    }

    private bool IsLit(PlayerHealth candidate) => BuildingLightManager.Instance == null ||
        BuildingLightManager.Instance.LightsOnAt(candidate.transform.position);

    private bool CanPerceive(PlayerHealth candidate)
    {
        if (!PlayerRegistry.IsHuntable(candidate)) return false;
        bool lit = IsLit(candidate);
        if (lit && CanSee(candidate)) return true;
        if (!canDetectMic || (lit && !hearMicInLight)) return false;
        if ((candidate.transform.position - transform.position).sqrMagnitude > hearingRadius * hearingRadius) return false;
        return candidate.TryGetComponent(out PlayerNoise noise) &&
            (noise.ServerMic > micVolumeThreshold || noise.ServerTooLoud);
    }

    protected override bool CanSee(PlayerHealth candidate)
    {
        if (candidate == null || !IsLit(candidate)) return false;
        Vector3 offset = candidate.transform.position - transform.position;
        float distance = offset.magnitude;
        if (distance > viewDistance || Vector3.Angle(transform.forward, offset) >= viewAngle) return false;
        // Existing prefabs have an empty obstacle mask. Treat that as normal solid geometry,
        // rather than letting the ghost see through every wall.
        int mask = obstacleLayer.value == 0 ? Physics.DefaultRaycastLayers : obstacleLayer.value;
        Vector3 origin = transform.position + Vector3.up * sightHeight;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, offset.normalized, distance, mask, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(candidate.transform)) return false;
        return true;
    }

    protected override void PatrolBehavior()
    {
        if (isJumpscareTriggered || !AgentReady) return;
        // Patrol independently of where silent/hidden players are standing.
        if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance <= Mathf.Max(1f, agent.stoppingDistance)))
            agent.SetDestination(GetRandomNavMeshLocation());
    }

    private bool AgentReady => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

    private Vector3 GetRandomNavMeshLocation()
    {
        Vector3 randomDirection = Random.insideUnitSphere * randomPatrolRadius + transform.position;
        return NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, randomPatrolRadius, NavMesh.AllAreas)
            ? hit.position : transform.position;
    }

    protected override void ChaseBehavior()
    {
        if (isJumpscareTriggered || !AgentReady || !isPlayerDetected) return;
        agent.SetDestination(lastKnownPosition);
        if (PlayerRegistry.IsHuntable(targetPlayer) &&
            Vector3.Distance(transform.position, lastKnownPosition) <= jumpscareRange)
            TriggerJumpscare(targetPlayer);
    }

    protected override void SearchBehavior()
    {
        if (isJumpscareTriggered || !AgentReady) return;
        agent.speed = patrolSpeed;
        float arrivalDistance = Mathf.Max(0.5f, agent.stoppingDistance);
        bool searchingPath = agent.hasPath && (agent.destination - lastKnownPosition).sqrMagnitude <= 0.01f;
        bool arrived = Vector3.Distance(transform.position, lastKnownPosition) <= arrivalDistance ||
            (!agent.pathPending && searchingPath && agent.remainingDistance <= arrivalDistance);
        if (!arrived && !agent.pathPending && !searchingPath)
            agent.SetDestination(lastKnownPosition);
        if (arrived)
        {
            if (searchArrivedAt < 0f) searchArrivedAt = Time.time;
        }
        bool waited = searchArrivedAt >= 0f && Time.time - searchArrivedAt >= searchWaitSeconds;
        if (!waited && Time.time - searchStartedAt < searchTravelTimeout) return;
        agent.ResetPath();
        targetPlayer = null;
        currentState = EnemyState.Patrol;
    }

    public override void HearSound(Vector3 soundPosition, float soundIntensity)
    {
        if (!HasAiAuthority || isJumpscareTriggered || soundIntensity <= 0f) return;
        if (Vector3.Distance(transform.position, soundPosition) > hearingRadius * soundIntensity) return;
        // External noise supplies a fixed location, never a live player target.
        if (currentState == EnemyState.Chase) return;
        lastKnownPosition = soundPosition;
        targetPlayer = null;
        isPlayerDetected = false;
        currentState = EnemyState.Search;
        searchStartedAt = Time.time;
        searchArrivedAt = -1f;
    }

    /// <summary>
    /// SERVER ONLY. Hurts one named victim and shows the scare on THAT player's
    /// screen alone.
    /// </summary>
    public override void TriggerJumpscare(PlayerHealth victim)
    {
        if (!HasAiAuthority || isJumpscareTriggered) return;

        isJumpscareTriggered = true;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            agent.isStopped = true;

        if (victim != null)
        {
            if (killsOutright) victim.ServerKill("jump scare");
            else victim.TakeDamage(jumpscareDamage);
        }

        if (NetworkMode.IsOffline)
        {
            ShowJumpscare();
        }
        else if (victim != null && victim.connectionToClient != null)
        {
            // Only the victim sees it. A ClientRpc here would scare all six
            // players at once, including the four in another room.
            TargetShowJumpscare(victim.connectionToClient);
        }

        // Tell the manager not to despawn us mid-scare.
        if (GhostManager.Instance != null) GhostManager.Instance.CancelDespawnTimer();

        Invoke(nameof(EndJumpscareEffect), jumpscareDuration);
    }

    [TargetRpc]
    private void TargetShowJumpscare(NetworkConnectionToClient target) => ShowJumpscare();

    private void ShowJumpscare()
    {
        PlayerGhostWarningController.SuppressLocal(jumpscareDuration);
        if (jumpscareUI != null) jumpscareUI.SetActive(true);
        Invoke(nameof(HideJumpscare), jumpscareDuration);
    }

    private void HideJumpscare()
    {
        if (jumpscareUI != null) jumpscareUI.SetActive(false);
    }

    /// <summary>SERVER ONLY — despawning is the server's call, same as spawning.</summary>
    private void EndJumpscareEffect()
    {
        if (!HasAiAuthority) return;

        if (GhostManager.Instance != null) GhostManager.Instance.ResetManagerAfterJumpscare();

        if (NetworkServer.active) NetworkServer.Destroy(gameObject);
        else Destroy(gameObject);
    }
}
