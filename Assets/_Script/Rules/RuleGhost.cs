using Mirror;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 16 Oct 2026 (Mr.k): MOCKUP rule ghost. Used by zone-rule punishments
/// (audience that chases you in the screen room, the crying-stall ghost, the
/// toilet swarm, the "roams all night" ghost).
///
/// It is NOT Nonnapat's ghost system (Enemy_Abstract_Class / TimedGhost /
/// GhostManager are untouched). It is a simple placeholder that will be replaced
/// when the real rule ghosts exist. The look is the "Visual" child (a prefab
/// instance from Assets/Prefab/RuleMockups) — swap the prefab to change it.
///
/// A scene object (placed by the setup tool) that is hidden until a rule calls it,
/// so nothing has to be registered as a spawnable prefab.
///
/// SERVER AUTHORITY: the AI runs on the server only. Clients get position + facing
/// through SyncVars and smooth it.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuleGhost : NetworkBehaviour
{
    [Header("Look")]
    [Tooltip("Shown only while the ghost is out.")]
    [SerializeField] private GameObject visual;

    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float speed = 2.6f;
    [Tooltip("Roaming: how far it notices a player.")]
    [SerializeField, Min(1f)] private float noticeRange = 9f;

    [Header("Touch (damage)")]
    [SerializeField, Min(0.3f)] private float touchRange = 1.1f;
    [SerializeField, Min(0f)] private float touchDamage = 15f;
    [SerializeField, Min(0f)] private float touchSanity = 10f;
    [SerializeField, Min(0.2f)] private float touchCooldown = 1.5f;
    [Tooltip("Show the jumpscare overlay to the player it touches.")]
    [SerializeField] private bool jumpscareOnTouch;

    // Server writes, everyone reads.
    [SyncVar(hook = nameof(OnOutChanged))] private bool isOut;
    [SyncVar] private Vector3 netPosition;
    [SyncVar] private float netYaw;

    private enum Mode { Hidden, Chase, Hunt, Roam }

    // Server only.
    private Mode mode;
    private PlayerHealth target;
    private RuleZone keepInside;
    private double until;
    private int untilHour = -1;
    private float nextHit;
    private float nextPath;
    private float nextSync;
    private Vector3 wanderPoint;
    private NavMeshPath path; // created in Awake: Unity forbids it in a field initializer
    private Vector3[] corners = new Vector3[16];
    private int cornerCount;
    private int cornerIndex;
    private string touchMessage = "";
    private readonly System.Collections.Generic.List<PlayerHealth> buffer = new System.Collections.Generic.List<PlayerHealth>();

    public bool IsOut => isOut;

    private static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    private void Awake()
    {
        path = new NavMeshPath();
        if (visual != null) visual.SetActive(false);
        // These objects have no colliders on purpose (a ghost never blocks a corridor).
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyOut(isOut);
        if (isOut) transform.SetPositionAndRotation(netPosition, Quaternion.Euler(0f, netYaw, 0f));
    }

    // ---- Server API ----------------------------------------------------------------------

    /// <summary>SERVER. Appear at <paramref name="from"/> and chase one player for some seconds.
    /// <paramref name="stayIn"/> = it cannot leave that zone (null = free).</summary>
    public void ServerChase(PlayerHealth victim, Vector3 from, RuleZone stayIn, float seconds, string message)
    {
        ServerAppear(from, stayIn, message);
        mode = Mode.Chase;
        target = victim;
        until = Now + seconds;
    }

    /// <summary>SERVER. Appear and attack ANY player inside <paramref name="stayIn"/> for some seconds (toilet swarm).</summary>
    public void ServerHunt(Vector3 from, RuleZone stayIn, float seconds, string message)
    {
        ServerAppear(from, stayIn, message);
        mode = Mode.Hunt;
        until = Now + seconds;
    }

    /// <summary>SERVER. Roam the whole map until the in-game hour <paramref name="endHour"/> (e.g. 6 = 06:00).</summary>
    public void ServerRoam(Vector3 from, int endHour, string message)
    {
        ServerAppear(from, null, message);
        mode = Mode.Roam;
        untilHour = endHour;
        until = double.MaxValue;
        wanderPoint = from;
    }

    /// <summary>SERVER. Go away now.</summary>
    public void ServerHide()
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        mode = Mode.Hidden;
        target = null;
        keepInside = null;
        untilHour = -1;
        SetOut(false);
    }

    private void ServerAppear(Vector3 from, RuleZone stayIn, string message)
    {
        keepInside = stayIn;
        touchMessage = message ?? "";
        cornerCount = 0;
        nextPath = 0f;
        // Start on the floor (NavMesh) when there is one near.
        if (NavMesh.SamplePosition(from, out NavMeshHit onFloor, 8f, NavMesh.AllAreas)) from = onFloor.position;
        transform.position = from;
        netPosition = from;
        SetOut(true);
    }

    private void SetOut(bool value)
    {
        isOut = value;
        ApplyOut(value); // hooks do not run on the machine that set the value
    }

    // ---- Server AI ---------------------------------------------------------------------------

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) { if (isOut) ServerTick(); }
        else if (isOut) ClientSmooth();
    }

    private void ServerTick()
    {
        MatchDirector match = MatchDirector.Instance;
        bool roundOver = match != null && !match.RoundRunning;
        if (roundOver || Now >= until || (untilHour >= 0 && CurrentHour() >= untilHour)) { ServerHide(); return; }

        PlayerHealth chase = PickTarget();
        Vector3 goal = chase != null ? chase.transform.position : WanderGoal();
        if (keepInside != null) goal = keepInside.Box.ClosestPoint(goal);
        MoveTowards(goal);

        if (chase != null && Time.time >= nextHit)
        {
            Vector3 d = chase.transform.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude <= touchRange * touchRange)
            {
                nextHit = Time.time + touchCooldown;
                Touch(chase);
            }
        }

        if (Time.time >= nextSync)
        {
            nextSync = Time.time + 0.05f;
            netPosition = transform.position;
            netYaw = transform.eulerAngles.y;
        }
    }

    private PlayerHealth PickTarget()
    {
        switch (mode)
        {
            case Mode.Chase:
                if (!PlayerRegistry.IsHuntable(target)) return null;
                if (keepInside != null && !keepInside.Contains(target)) return null; // it cannot follow out
                return target;
            case Mode.Hunt:
                return keepInside != null ? Nearest(keepInside.PlayersInside(buffer), float.MaxValue) : null;
            case Mode.Roam:
                return Nearest(PlayerRegistry.All, noticeRange);
            default:
                return null;
        }
    }

    private PlayerHealth Nearest(System.Collections.Generic.IReadOnlyList<PlayerHealth> list, float range)
    {
        PlayerHealth best = null;
        float bestSqr = range == float.MaxValue ? float.MaxValue : range * range;
        for (int i = 0; i < list.Count; i++)
        {
            PlayerHealth p = list[i];
            if (!PlayerRegistry.IsHuntable(p)) continue;
            float sqr = (p.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = p; }
        }
        return best;
    }

    private Vector3 WanderGoal()
    {
        Vector3 flat = wanderPoint - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1f || Random.value < 0.002f)
        {
            Vector3 pick = keepInside != null
                ? keepInside.RandomPointInside(transform.position.y)
                : transform.position + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
            wanderPoint = NavMesh.SamplePosition(pick, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : pick;
        }
        return wanderPoint;
    }

    private void MoveTowards(Vector3 goal)
    {
        // Follow the NavMesh when there is one (no walking through walls), else straight.
        if (Time.time >= nextPath)
        {
            nextPath = Time.time + 0.4f;
            cornerCount = 0;
            cornerIndex = 0;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(goal, out NavMeshHit to, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid)
            {
                cornerCount = path.GetCornersNonAlloc(corners);
                if (cornerCount == corners.Length) { corners = new Vector3[corners.Length * 2]; cornerCount = path.GetCornersNonAlloc(corners); }
                cornerIndex = cornerCount > 1 ? 1 : 0;
            }
        }

        Vector3 next = goal;
        if (cornerCount > 0)
        {
            while (cornerIndex < cornerCount - 1 && FlatDistance(transform.position, corners[cornerIndex]) < 0.3f) cornerIndex++;
            next = corners[Mathf.Clamp(cornerIndex, 0, cornerCount - 1)];
        }

        Vector3 step = Vector3.MoveTowards(transform.position, next, speed * Time.deltaTime);
        if (keepInside != null) step = keepInside.Box.ClosestPoint(step);
        Vector3 dir = step - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.position = step;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void Touch(PlayerHealth victim)
    {
        if (touchDamage > 0f) victim.TakeDamage(touchDamage);
        if (touchSanity > 0f) PlayerSanity.ServerRuleFailed(victim.gameObject, touchSanity);
        Debug.Log($"[RuleGhost] '{name}' touched {victim.name} (-{touchDamage} HP, -{touchSanity} sanity).", this);
        if (!jumpscareOnTouch && string.IsNullOrEmpty(touchMessage)) return;
        if (NetworkMode.IsOffline || victim.isLocalPlayer) LocalTouchFx(jumpscareOnTouch, touchMessage);
        else if (victim.connectionToClient != null) TargetTouch(victim.connectionToClient, jumpscareOnTouch, touchMessage);
    }

    [TargetRpc]
    private void TargetTouch(NetworkConnectionToClient conn, bool jumpscare, string message) => LocalTouchFx(jumpscare, message);

    private static void LocalTouchFx(bool jumpscare, string message)
    {
        if (jumpscare) RuleFx.ShowJumpscare(2f);
        if (!string.IsNullOrEmpty(message)) RuleFx.Toast(message, 2.5f);
    }

    private static int CurrentHour()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null) return 0;
        match.GetInGameTime(out int hour, out _);
        return hour;
    }

    // ---- Clients ---------------------------------------------------------------------------

    private void ClientSmooth()
    {
        transform.position = Vector3.Lerp(transform.position, netPosition, 1f - Mathf.Exp(-12f * Time.deltaTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    private void OnOutChanged(bool oldValue, bool newValue)
    {
        if (newValue) transform.position = netPosition; // appear where the server put it, no slide
        ApplyOut(newValue);
    }

    private void ApplyOut(bool on)
    {
        if (visual != null && visual.activeSelf != on) visual.SetActive(on);
    }

    /// <summary>Editor tool: the mockup look.</summary>
    public void SetVisual(GameObject look) => visual = look;
}
