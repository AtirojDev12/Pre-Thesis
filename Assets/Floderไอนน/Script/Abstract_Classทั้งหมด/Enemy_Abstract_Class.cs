using Mirror;
using UnityEngine;
using UnityEngine.AI; // เรียกใช้งานระบบ NavMesh ของ Unity เพื่อควบคุมการเดินหลบสิ่งกีดขวาง

/// <summary>
/// คลาสแม่ของผีทุกตัว — โครงสร้าง AI ที่ผีตัวอื่นสืบทอดไปใช้ต่อ
///
/// WHAT CHANGED FOR MULTIPLAYER
/// ----------------------------
/// The AI now runs on the SERVER ONLY. Every client holds a copy of the ghost,
/// but only the server decides where it walks and who it is chasing; a
/// NetworkTransform on the prefab replicates the result. Without that, six
/// machines would each run their own copy of this AI, each pick a different
/// target, and the ghost would be standing somewhere different on every screen.
///
/// Targeting no longer caches one player at Start. It asks PlayerRegistry every
/// check for the nearest player it can actually see, so a six-player team gets
/// a ghost that switches to whoever steps into view — and a player who spawns
/// late is not invisible to it forever.
///
/// OFFLINE STILL WORKS. Every network guard goes through NetworkMode, so
/// pressing Play in a test scene with no host runs the whole AI locally exactly
/// as before. That is deliberate — the enemy test scenes must not need a host.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public abstract class Enemy_Abstract_Class : NetworkBehaviour
{
    // กำหนดกลุ่มสถานะ (State) ของผี: ลาดตระเวน, ไล่ล่า, ค้นหา, โดนดึงความสนใจจากเสียง
    public enum EnemyState { Patrol, Chase, Search, Distracted }

    [Header("AI State")]
    /// <summary>
    /// Replicated so clients can drive animation and audio from it. The SERVER
    /// writes it; a client that changes its own copy changes nothing real.
    /// </summary>
    [SyncVar(hook = nameof(OnStateChanged))]
    [SerializeField] protected EnemyState currentState = EnemyState.Patrol;

    [Header("Movement Settings")]
    [SerializeField] protected float patrolSpeed = 2f;  // ความเร็วตอนผีเดินตรวจตราปกติ
    [SerializeField] protected float chaseSpeed = 5f;   // ความเร็วตอนผีวิ่งไล่ล่าผู้เล่น
    protected NavMeshAgent agent;                       // ตัวควบคุมการเคลื่อนที่บน NavMesh

    [Header("Detection Settings")]
    [SerializeField] protected float viewDistance = 10f;    // ระยะสายตาของผี
    [SerializeField] protected float viewAngle = 45f;       // องศากรอบสายตาของผี
    [SerializeField] protected float hearingRadius = 8f;    // ระยะรัศมีการได้ยินเสียงของผู้เล่น
    [SerializeField] protected LayerMask playerLayer;       // เลเยอร์ของผู้เล่น
    [SerializeField] protected LayerMask obstacleLayer;     // เลเยอร์กำแพง/สิ่งกีดขวาง

    /// <summary>
    /// The player this ghost is currently after. Server-side truth. It is
    /// re-chosen on every check rather than cached once, because in a six-player
    /// match "the player" is not a fixed thing.
    /// </summary>
    protected PlayerHealth targetPlayer;

    /// <summary>Convenience for subclasses written against the old single-target field.</summary>
    protected Transform playerTransform => targetPlayer != null ? targetPlayer.transform : null;

    protected bool isPlayerDetected = false;

    /// <summary>
    /// True on the machine allowed to run AI: the server, or anybody when there
    /// is no server at all (offline test scene).
    /// </summary>
    protected bool HasAiAuthority => NetworkMode.HasServerAuthority(this);

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    protected virtual void Start()
    {
        // Deliberately NOT looking the player up here. See PlayerRegistry for
        // why a one-shot lookup is the wrong shape. Targeting happens per check.

        // A ghost on a client is a puppet: its NavMeshAgent must not fight the
        // positions arriving from the server, the same way a remote player's
        // rigidbody is made kinematic.
        if (!HasAiAuthority && agent != null) agent.enabled = false;
    }

    protected virtual void Update()
    {
        // Clients run no AI at all. They see the ghost move because the
        // NetworkTransform on the prefab is replicating the server's result.
        if (!HasAiAuthority) return;

        CheckForPlayer();
        SwitchStateBehavior();
    }

    // --- LOGIC การตรวจจับ ---

    /// <summary>
    /// Picks the nearest player this ghost can actually see, and chases them.
    ///
    /// The old version tested one cached player. This tests every living player
    /// and takes the closest one with line of sight, so a team cannot park five
    /// people in front of a ghost that is only looking at the sixth.
    /// </summary>
    protected virtual void CheckForPlayer()
    {
        PlayerHealth seen = PlayerRegistry.ClosestVisibleTo(transform.position, viewDistance, CanSee);

        if (seen != null)
        {
            targetPlayer = seen;
            OnPlayerSpotted();
            return;
        }

        // Nobody visible. If we were chasing, we have just lost them.
        if (currentState == EnemyState.Chase)
        {
            // Give up only once they are genuinely out of range, not merely
            // behind a pillar for one frame.
            bool targetStillClose = targetPlayer != null
                && !targetPlayer.IsDead
                && Vector3.Distance(transform.position, targetPlayer.transform.position) <= viewDistance;

            if (!targetStillClose) OnPlayerLost();
        }
    }

    /// <summary>
    /// Vision test for one candidate: inside the view cone and not behind a wall.
    /// Pulled out of CheckForPlayer so PlayerRegistry can apply it while picking
    /// the nearest, instead of us testing only whoever happened to be cached.
    /// </summary>
    protected virtual bool CanSee(PlayerHealth candidate)
    {
        if (candidate == null) return false;

        Vector3 toPlayer = candidate.transform.position - transform.position;
        float distance = toPlayer.magnitude;
        if (distance > viewDistance) return false;

        Vector3 direction = toPlayer / Mathf.Max(0.0001f, distance);
        if (Vector3.Angle(transform.forward, direction) >= viewAngle) return false;

        return !Physics.Raycast(transform.position, direction, distance, obstacleLayer);
    }

    /// <summary>
    /// ฟังก์ชันรับสัญญาณเสียงภายนอก. SERVER ONLY — a client reporting a noise it
    /// made must do it through a Command on the player, not by calling this.
    /// </summary>
    public virtual void HearSound(Vector3 soundPosition, float soundIntensity)
    {
        if (!HasAiAuthority) return;
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        float distanceToSound = Vector3.Distance(transform.position, soundPosition);

        if (distanceToSound <= hearingRadius * soundIntensity)
        {
            agent.SetDestination(soundPosition);
            currentState = EnemyState.Distracted;
        }
    }

    // --- ABSTRACT METHODS (คลาสลูกไปเขียนเอง) ---
    protected abstract void PatrolBehavior();
    protected abstract void ChaseBehavior();
    protected abstract void SearchBehavior();

    /// <summary>
    /// SERVER ONLY. Implementations decide what happens to <paramref name="victim"/> —
    /// damage, instant death, a scare — and must send any visual to that one
    /// player with a TargetRpc, never a broadcast.
    ///
    /// Takes the victim explicitly: with six players in the map, "the player"
    /// is not a thing, and a jumpscare that plays on everyone's screen at once
    /// is a bug the old single-player version could not express.
    /// </summary>
    public abstract void TriggerJumpscare(PlayerHealth victim);

    // --- STATE MANAGER ---
    private void SwitchStateBehavior()
    {
        if (agent == null || !agent.isActiveAndEnabled) return;

        switch (currentState)
        {
            case EnemyState.Patrol:
                agent.speed = patrolSpeed;
                PatrolBehavior();
                break;

            case EnemyState.Chase:
                agent.speed = chaseSpeed;
                ChaseBehavior();
                break;

            case EnemyState.Search:
                SearchBehavior();
                break;
        }
    }

    protected virtual void OnPlayerSpotted()
    {
        isPlayerDetected = true;

        if (currentState != EnemyState.Chase)
        {
            currentState = EnemyState.Chase;
            Debug.Log($"[{name}] เห็นผู้เล่นแล้ว! เริ่มไล่ล่า ({targetPlayer?.name}).", this);
        }
    }

    protected virtual void OnPlayerLost()
    {
        isPlayerDetected = false;
        currentState = EnemyState.Search;
        Debug.Log($"[{name}] ผู้เล่นคลาดสายตา กำลังเดินหาแถวนี้...", this);
    }

    /// <summary>
    /// Runs on remote clients when the server changes state. Override it to
    /// drive animation or audio; do NOT put AI decisions here.
    /// </summary>
    protected virtual void OnStateChanged(EnemyState oldState, EnemyState newState) { }
}
