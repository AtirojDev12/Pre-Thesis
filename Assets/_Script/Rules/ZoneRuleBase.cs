using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): base class for every ZONE RULE (Screen Room, Toilet, ...).
///
/// WHAT A ZONE RULE IS
///   - It works only inside its own <see cref="RuleZone"/>.
///   - It is NOT a Map Rule. Map Rules (Hard / 13 Rules) are a separate system
///     that runs across the whole map on top of the zone rules.
///   - For now EVERY zone rule is on at every difficulty (testing). Change that
///     later in <see cref="RulesActiveNow"/> only.
///
/// SERVER AUTHORITY
///   The server decides when a rule event starts, who broke it and the punishment.
///   Clients only show things (SyncVars, TargetRpcs) and report what only they can
///   see cheaply ("I am looking at X"), through Commands the server re-checks.
///
/// FOR DESIGNERS
///   Every number is in the Inspector of the rule object (Zone Rules root in the
///   scene). The looks are prefab instances from Assets/Prefab/RuleMockups:
///   change the prefab, every copy changes.
/// </summary>
public abstract class ZoneRuleBase : NetworkBehaviour
{
    [Header("Rule (designer text)")]
    [Tooltip("Short name, e.g. \"Don't look at the audience\".")]
    [SerializeField] protected string ruleTitle = "Rule";
    [Tooltip("What players must do. Notes for the team; not shown in game yet.")]
    [TextArea(2, 5)] [SerializeField] protected string ruleText = "";
    [Tooltip("Where this rule works. Players outside are never affected.")]
    [SerializeField] protected RuleZone zone;

    [Header("Feedback (testing)")]
    [Tooltip("Show a short red line when a player breaks this rule (good for testing, maybe off for the final game).")]
    [SerializeField] protected bool showBrokenMessage = true;
    [Tooltip("Log every step of this rule to the Console.")]
    [SerializeField] protected bool debugLog = true;

    /// <summary>Every zone rule in the scene (dev tools / Map Rules may read this).</summary>
    public static readonly List<ZoneRuleBase> All = new List<ZoneRuleBase>();

    public string RuleTitle => ruleTitle;
    public RuleZone Zone => zone;

    protected readonly List<PlayerHealth> inside = new List<PlayerHealth>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => All.Clear();

    protected virtual void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        ZoneChoreSpot.ServerBlocked = IsChoreLocked; // same method every time, safe to repeat
    }

    /// <summary>SERVER. Override to lock chores while a punishment runs (e.g. the smoking stall).</summary>
    protected virtual bool ServerLocksChore(ZoneChoreSpot spot) => false;

    private static bool IsChoreLocked(ZoneChoreSpot spot)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].ServerLocksChore(spot)) return true;
        return false;
    }
    protected virtual void OnDisable() => All.Remove(this);

    // ---- When is a rule allowed to run ------------------------------------------------

    /// <summary>
    /// Mr.k, 16 Oct: all zone rules are active regardless of difficulty, for testing.
    /// The only place to change that later.
    /// </summary>
    public static bool RulesActiveNow => true;

    protected bool IsServerSide => NetworkMode.HasServerAuthority(this);

    /// <summary>SERVER. The round is running and this rule may act.</summary>
    protected bool ServerRuleLive
    {
        get
        {
            if (!isActiveAndEnabled || zone == null || !RulesActiveNow) return false;
            MatchDirector match = MatchDirector.Instance;
            return match == null || match.RoundRunning;
        }
    }

    /// <summary>In-game hour 0..7 (0 when there is no match).</summary>
    protected static int CurrentHour
    {
        get
        {
            MatchDirector match = MatchDirector.Instance;
            if (match == null) return 0;
            match.GetInGameTime(out int hour, out _);
            return hour;
        }
    }

    protected static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;

    /// <summary>SERVER. Fills and returns <see cref="inside"/>.</summary>
    protected List<PlayerHealth> ServerPlayersInside() => zone != null ? zone.PlayersInside(inside) : inside;

    // ---- Local player helpers --------------------------------------------------------------

    protected static PlayerHealth LocalBody => PlayerHealth.LocalInstance;

    /// <summary>LOCAL. Is my own living body inside this rule's zone?</summary>
    protected bool LocalInside
    {
        get
        {
            PlayerHealth me = LocalBody;
            return me != null && !me.IsDead && !me.IsDowned && zone != null && zone.Contains(me.transform.position);
        }
    }

    // ---- Punishment (server) -------------------------------------------------------------

    /// <summary>
    /// SERVER. Punish one player for breaking this rule: damage, sanity (through
    /// PlayerSanity.ServerRuleFailed, so difficulty and god mode still apply), and
    /// optionally a jumpscare / message on THAT player's screen only.
    /// </summary>
    protected void ServerPunish(PlayerHealth player, float damage, float sanity, bool jumpscare, string message)
    {
        if (player == null || player.IsDead) return;
        if (damage > 0f) player.TakeDamage(damage);
        if (sanity > 0f) PlayerSanity.ServerRuleFailed(player.gameObject, sanity);
        if (debugLog) Debug.Log($"[Rule] {ruleTitle}: {player.name} -{damage:0.#} HP -{sanity:0.#} sanity ({message})", this);
        ServerShow(player, jumpscare, message);
    }

    /// <summary>SERVER. Jumpscare and/or message on one player's screen.</summary>
    protected void ServerShow(PlayerHealth player, bool jumpscare, string message)
    {
        if (player == null) return;
        string text = showBrokenMessage ? message : null;
        if (!jumpscare && string.IsNullOrEmpty(text)) return;
        if (NetworkMode.IsOffline || (player.isLocalPlayer && player.isClient)) LocalShow(jumpscare, text);
        else if (player.connectionToClient != null) TargetShow(player.connectionToClient, jumpscare, text);
    }

    [TargetRpc]
    private void TargetShow(NetworkConnectionToClient target, bool jumpscare, string message) => LocalShow(jumpscare, message);

    private static void LocalShow(bool jumpscare, string message)
    {
        if (jumpscare) RuleFx.ShowJumpscare(2.5f);
        if (!string.IsNullOrEmpty(message)) RuleFx.Toast(message, 3.5f);
    }

    protected void Log(string text)
    {
        if (debugLog) Debug.Log($"[Rule] {ruleTitle}: {text}", this);
    }

    /// <summary>
    /// SERVER. Force this rule's event to start now (for a future F1 dev button).
    /// Rules without an event do nothing.
    /// </summary>
    public virtual void ServerDevTrigger() { }

    // ---- "I am looking at it" reports (owner -> server) -----------------------------------

    private bool lookSent;
    private float lookSentAt = -99f;
    private readonly Dictionary<uint, double> lookingSince = new Dictionary<uint, double>();
    private const double LookReportLife = 1.2; // a "yes" counts this long without a new report

    /// <summary>
    /// LOCAL (owner). Call every frame with "am I looking at it now". Sends to the
    /// server only on change, and repeats "yes" twice a second (packets may drop).
    /// </summary>
    protected void LocalReportLooking(bool looking)
    {
        if (looking == lookSent && (!looking || Time.unscaledTime - lookSentAt < 0.5f)) return;
        lookSent = looking;
        lookSentAt = Time.unscaledTime;
        if (NetworkMode.IsOffline) ServerSetLooking(1u, looking);
        else if (NetworkClient.isConnected) CmdLooking(looking);
    }

    [Command(requiresAuthority = false, channel = Channels.Unreliable)]
    private void CmdLooking(bool looking, NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) ServerSetLooking(sender.identity.netId, looking);
    }

    private void ServerSetLooking(uint id, bool looking)
    {
        if (looking) lookingSince[id] = Now;
        else lookingSince.Remove(id);
    }

    /// <summary>SERVER. Did this player report "I am looking at it" just now?</summary>
    protected bool ServerIsLooking(PlayerHealth player)
    {
        if (player == null) return false;
        uint id = NetworkMode.IsOffline ? 1u : player.netId;
        return lookingSince.TryGetValue(id, out double at) && Now - at <= LookReportLife;
    }

    // ---- Stillness helper (server) -------------------------------------------------------

    /// <summary>
    /// Remembers where players stood, to ask later "did anyone move?".
    /// Small jitter (network smoothing, turning on the spot) is allowed.
    /// </summary>
    protected sealed class StillnessWatch
    {
        private readonly List<PlayerHealth> players = new List<PlayerHealth>();
        private readonly List<Vector3> positions = new List<Vector3>();

        public void Start(List<PlayerHealth> who)
        {
            players.Clear();
            positions.Clear();
            for (int i = 0; i < who.Count; i++)
            {
                if (who[i] == null) continue;
                players.Add(who[i]);
                positions.Add(who[i].transform.position);
            }
        }

        public void Clear()
        {
            players.Clear();
            positions.Clear();
        }

        /// <summary>The first watched player (still inside <paramref name="zone"/>) who moved more than <paramref name="tolerance"/> metres; else null.</summary>
        public PlayerHealth FirstMover(RuleZone zone, float tolerance)
        {
            float t2 = tolerance * tolerance;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerHealth p = players[i];
                if (p == null || p.IsDead || (zone != null && !zone.Contains(p))) continue;
                Vector3 d = p.transform.position - positions[i];
                d.y = 0f;
                if (d.sqrMagnitude > t2) return p;
            }
            return null;
        }

        /// <summary>Anyone who entered after the watch started counts as standing at their entry point.</summary>
        public void AddLateComers(List<PlayerHealth> nowInside)
        {
            for (int i = 0; i < nowInside.Count; i++)
                if (nowInside[i] != null && !players.Contains(nowInside[i]))
                {
                    players.Add(nowInside[i]);
                    positions.Add(nowInside[i].transform.position);
                }
        }
    }
}
