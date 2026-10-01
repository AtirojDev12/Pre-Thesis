using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// ผีที่ GhostManager ปล่อยออกมาเป็นช่วงเวลา
///
/// MULTIPLAYER NOTES
/// -----------------
/// The AI runs on the server only (see Enemy_Abstract_Class). Two things here
/// had to change beyond that:
///
///   1. The jumpscare UI. It used to switch on a GameObject in the scene, which
///      on six machines means everybody's screen gets the scare at once — even
///      the four players on the other side of the cinema. It is now a TargetRpc
///      to the victim alone.
///
///   2. "Did the player move" used to compare against one cached player. It now
///      tracks the position of each candidate separately, because in the dark
///      this ghost hunts by MOVEMENT, and six players means six things that
///      might be moving.
///
/// 1 Oct 2026: restored from Mr.k's Week 4 multiplayer commit (c1a5c769) and
/// merged with Nonnapat's newer features, all checked for EVERY player:
///   - Dark: a player within 3 m is noticed at once.
///   - Dark: a player who is TOO LOUD (PlayerNoise.ServerMic over the threshold,
///     or ServerTooLoud) is noticed and chased. The server already holds every
///     player's loudness, so this works in multiplayer.
///   - Optional: hear loud players in the light too (hearMicInLight).
///   - Jumpscare overlay lookup also finds an INACTIVE tagged object.
/// </summary>
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

    [Header("Movement Detection Settings")]
    [SerializeField] private float movementThreshold = 0.02f;

    [Header("Dark rules (Nonnapat)")]
    [Tooltip("In the dark, a player this close is noticed even standing still.")]
    [SerializeField] private float closeRange = 3f;

    [Header("Mic Settings (Nonnapat)")]
    [Tooltip("ServerMic (0..1) above this = too loud. ServerTooLoud (the red line on the noise meter) always counts too.")]
    [SerializeField] private float micVolumeThreshold = 0.3f;

    [Tooltip("OFF = the ghost only listens in the dark (Nonnapat's rule). ON = it also chases loud players while the lights are on.")]
    [SerializeField] private bool hearMicInLight = false;

    [Header("Patrol Settings (ความมืด)")]
    [SerializeField] private float randomPatrolRadius = 15f;

    [Tooltip("How close the ghost must get before the jumpscare fires.")]
    [SerializeField] private float jumpscareRange = 1.5f;

    /// <summary>
    /// Whether the lights are on. Pushed by GhostManager, which owns it as a
    /// SyncVar — this ghost never reads Light components itself, because on a
    /// client those may not match the server's idea of the switch.
    /// </summary>
    [HideInInspector] public bool isSceneLightOn = true;

    private bool isJumpscareTriggered = false;

    // Per-player last-known position, for the dark-hunting movement check. A
    // single lastPlayerPosition could only ever watch one person.
    private readonly System.Collections.Generic.Dictionary<uint, Vector3> lastSeenPositions =
        new System.Collections.Generic.Dictionary<uint, Vector3>();

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
        if (isJumpscareTriggered) return;

        if (isSceneLightOn)
        {
            // Lit: normal sight. The base class already picks the nearest player
            // it can actually see, across everyone in the map.
            base.CheckForPlayer();

            // Optional: also hear loud players while the lights are on.
            if (hearMicInLight && canDetectMic && currentState != EnemyState.Chase)
            {
                PlayerHealth loud = FindNearestNoticedPlayer(false, false, true);
                if (loud != null)
                {
                    targetPlayer = loud;
                    OnPlayerSpotted();
                }
            }
        }
        else
        {
            // Dark: it cannot see. It notices a player who is very close, who
            // MOVES, or who is TOO LOUD — checked for every player, not one
            // cached transform.
            if (currentState != EnemyState.Chase)
            {
                PlayerHealth noticed = FindNearestNoticedPlayer(true, true, canDetectMic);
                if (noticed != null)
                {
                    targetPlayer = noticed;
                    OnPlayerSpotted();
                }
            }
        }

        RememberPositions();
    }

    /// <summary>
    /// The closest player inside view distance who has moved more than the
    /// threshold since the last check. Stand still in the dark and this ghost
    /// walks past you — which is the whole point of the mechanic, and it only
    /// works if it is evaluated per player.
    /// </summary>
    private PlayerHealth FindNearestNoticedPlayer(bool useCloseRange, bool useMovement, bool useMic)
    {
        PlayerHealth best = null;
        float bestSqr = viewDistance * viewDistance;

        var players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth candidate = players[i];
            if (candidate == null || candidate.IsDead || candidate.netIdentity == null) continue;
            if (!PlayerRegistry.IsInRound(candidate)) continue; // escaped / finished players are out

            Vector3 position = candidate.transform.position;
            float sqr = (position - transform.position).sqrMagnitude;
            if (sqr > bestSqr) continue;

            bool noticed = false;

            // 1. Very close (Nonnapat).
            if (useCloseRange && sqr <= closeRange * closeRange) noticed = true;

            // 2. Moved since the last check.
            if (!noticed && useMovement &&
                lastSeenPositions.TryGetValue(candidate.netIdentity.netId, out Vector3 previous) &&
                Vector3.Distance(position, previous) > movementThreshold)
                noticed = true;

            // 3. Too loud on the microphone (Nonnapat). PlayerNoise holds the
            //    SERVER copy of every player's loudness, so the host hears everyone.
            if (!noticed && useMic && IsTooLoud(candidate)) noticed = true;

            if (!noticed) continue;
            bestSqr = sqr;
            best = candidate;
        }

        if (best != null)
            Debug.Log($"[Ghost AI] noticed {best.name} in the {(isSceneLightOn ? "light" : "dark")}.", this);

        return best;
    }

    private bool IsTooLoud(PlayerHealth candidate)
    {
        if (!candidate.TryGetComponent(out PlayerNoise noise)) return false;
        return noise.ServerMic > micVolumeThreshold || noise.ServerTooLoud;
    }

    private void RememberPositions()
    {
        var players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth candidate = players[i];
            if (candidate == null || candidate.netIdentity == null) continue;

            lastSeenPositions[candidate.netIdentity.netId] = candidate.transform.position;
        }
    }

    protected override void PatrolBehavior()
    {
        if (isJumpscareTriggered) return;
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        if (isSceneLightOn)
        {
            // Lit: drift toward whoever is nearest, even without line of sight.
            PlayerHealth nearest = PlayerRegistry.ClosestTo(transform.position);
            if (nearest != null) agent.SetDestination(nearest.transform.position);
        }
        else if (!agent.pathPending && agent.remainingDistance < 1f)
        {
            agent.SetDestination(GetRandomNavMeshLocation());
        }
    }

    private Vector3 GetRandomNavMeshLocation()
    {
        Vector3 randomDirection = Random.insideUnitSphere * randomPatrolRadius + transform.position;

        return NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, randomPatrolRadius, NavMesh.AllAreas)
            ? hit.position
            : transform.position;
    }

    protected override void ChaseBehavior()
    {
        if (isJumpscareTriggered) return;
        if (targetPlayer == null || targetPlayer.IsDead || !PlayerRegistry.IsInRound(targetPlayer))
        {
            // The person we were chasing died or left. Do not freeze — go back
            // to patrol and let CheckForPlayer pick somebody else.
            currentState = EnemyState.Patrol;
            return;
        }

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            agent.SetDestination(targetPlayer.transform.position);

        if (Vector3.Distance(transform.position, targetPlayer.transform.position) <= jumpscareRange)
            TriggerJumpscare(targetPlayer);
    }

    protected override void SearchBehavior()
    {
        currentState = EnemyState.Patrol;
    }

    /// <summary>
    /// SERVER ONLY. Hurts one named victim and shows the scare on THAT player's
    /// screen alone.
    /// </summary>
    public override void TriggerJumpscare(PlayerHealth victim)
    {
        if (!HasAiAuthority || isJumpscareTriggered) return;

        isJumpscareTriggered = true;

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
