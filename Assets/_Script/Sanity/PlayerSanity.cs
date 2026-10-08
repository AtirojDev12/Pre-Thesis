using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>The three sanity levels of the GDD.</summary>
public enum SanityLevel
{
    Calm = 0,      // above shakenBelow
    Shaken = 1,    // vision harder, more anomalies, harder tasks
    Breaking = 2,  // friends look like ghosts, no sanity gain, no healing
}

/// <summary>
/// 8 Oct (Mr.k, GDD "Sanity (หลอดสติ)"). One per player, on Player.prefab
/// (Tools > Pre-Thesis > Setup Sanity adds it).
///
/// SERVER AUTHORITY. Only the server changes sanity (SyncVar 0..100). The owner
/// only reports what the server cannot know cheaply (is a ghost on my screen?)
/// and asks (chant on/off, eat, feed a friend). Every number is in SanitySettings.
///
///   DRAIN                                    GAIN
///   - a ghost on your screen                 - chant: hold H and stand still
///   - alone (no teammate near) in the dark       alone = slow, holy item carried = fast,
///   - rules / anomalies call the server API      each friend chanting near you = faster
///   x difficulty (MatchDirector)             - Snack: eat it, or feed a friend (fast)
///
///   LEVELS: Calm / Shaken (60) / Breaking (30). At 0 for a while: jumpscare -5 HP, repeats.
///
/// FOR OTHER PROGRAMMERS (rules, anomalies, minigames):
///   PlayerSanity.ServerRuleFailed(player)  /  ServerAnomaly(player, amount)  /  ServerDrain(...)
///   sanity.Level, sanity.AnomalyChanceMultiplier, sanity.TaskDifficultyMultiplier
///   PlayerSanity.Local (your own) on any machine.
///
/// Runs only during a match round (not in the lobby), and pauses while you are
/// downed, dead or finished (escaped).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSanity : NetworkBehaviour
{
    public const float Max = 100f;
    private const float TickSeconds = 0.25f;
    private const float FirstSightCooldown = 10f;
    private const float SightReportInterval = 0.25f;

    /// <summary>Every player body with sanity in the scene (lobby bodies included, they just stay inactive).</summary>
    public static readonly List<PlayerSanity> All = new List<PlayerSanity>();
    /// <summary>Your own sanity (owner machine). Null before you spawn.</summary>
    public static PlayerSanity Local { get; private set; }

    [SyncVar(hook = nameof(OnSanitySynced))] private float sanity = Max;
    [SyncVar] private bool chanting;

    /// <summary>Every machine: the value changed (0..100).</summary>
    public event System.Action<float> Changed;
    /// <summary>Every machine: the level changed.</summary>
    public event System.Action<SanityLevel> LevelChanged;
    /// <summary>Owner only: the sanity-0 jumpscare just hit you.</summary>
    public event System.Action JumpscareReceived;

    public float Value => sanity;
    public float Fraction => Mathf.Clamp01(sanity / Max);
    public SanityLevel Level => LevelOf(sanity);
    public bool IsChanting => chanting;

    /// <summary>GDD: no healing in the lowest level (PlayerHealth.Heal asks this).</summary>
    public bool BlocksHealing => Level == SanityLevel.Breaking && Settings.blockHealWhenBreaking;
    /// <summary>GDD: no sanity gain in the lowest level.</summary>
    public bool BlocksGain => Level == SanityLevel.Breaking && Settings.blockGainWhenBreaking;

    /// <summary>For the anomaly system: multiply your anomaly chance by this (1 when calm).</summary>
    public float AnomalyChanceMultiplier => Level == SanityLevel.Breaking ? Settings.anomalyMultiplierBreaking
        : Level == SanityLevel.Shaken ? Settings.anomalyMultiplierShaken : 1f;
    /// <summary>For minigames: make the task harder by this (1 when calm).</summary>
    public float TaskDifficultyMultiplier => Level == SanityLevel.Breaking ? Settings.taskDifficultyBreaking
        : Level == SanityLevel.Shaken ? Settings.taskDifficultyShaken : 1f;

    private static SanitySettings Settings => SanitySettings.Current;

    public static SanityLevel LevelOf(float value)
    {
        SanitySettings s = Settings;
        if (value <= s.breakingBelow) return SanityLevel.Breaking;
        if (value <= s.shakenBelow) return SanityLevel.Shaken;
        return SanityLevel.Calm;
    }

    private PlayerHealth health;
    private PlayerInventory inventory;
    private SanityLevel shownLevel = SanityLevel.Calm;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        Local = null;
    }

    private void Awake()
    {
        health = GetComponent<PlayerHealth>();
        inventory = GetComponent<PlayerInventory>();
    }

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }

    private void OnDisable()
    {
        All.Remove(this);
        if (Local == this) Local = null;
    }

    public override void OnStartLocalPlayer() => BecomeLocal();

    private void Start()
    {
        // Offline test scenes: no Mirror callbacks, the only body is yours.
        if (NetworkMode.IsOffline && NetworkMode.IsLocalController(this)) BecomeLocal();
    }

    private void BecomeLocal()
    {
        Local = this;
        if (GetComponent<SanityEffects>() == null) gameObject.AddComponent<SanityEffects>();
    }

    private bool IsOwner => NetworkMode.IsLocalController(this);

    // ---- Every machine -------------------------------------------------------

    private void OnSanitySynced(float oldValue, float newValue) => Raise(newValue);

    private void Raise(float value)
    {
        Changed?.Invoke(value);
        SanityLevel level = LevelOf(value);
        if (level == shownLevel) return;
        shownLevel = level;
        LevelChanged?.Invoke(level);
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerTick();
        if (IsOwner) OwnerTick();
    }

    // ---- Is sanity running for this body right now? --------------------------

    /// <summary>Every machine (best guess on clients): match running, not lobby, alive, standing, still inside.</summary>
    public bool InPlay
    {
        get
        {
            MatchDirector match = MatchDirector.Instance;
            if (match == null || !match.RoundRunning || PlayerInventory.InLobby) return false;
            if (health != null && (health.IsDead || health.IsDowned)) return false;
            if (NetworkMode.HasServerAuthority(this))
                return NetworkMode.IsOffline || !match.ServerIsFinished(netIdentity);
            return !match.IsFinished(netId);
        }
    }

    // ---- Server --------------------------------------------------------------

    private float tickTimer;
    private bool reportedSeeingGhost;
    private float lastGhostSeen = -999f;
    private float darkAloneTimer;
    private float zeroTimer;
    private float nextZeroHit;
    private Vector3 lastPosition;
    private bool hasLastPosition;
    private float lastSnack = -999f;

    private void ServerTick()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer < TickSeconds) return;
        float dt = tickTimer;
        tickTimer = 0f;

        Vector3 position = transform.position;
        float speed = hasLastPosition ? Vector3.Distance(position, lastPosition) / dt : 0f;
        lastPosition = position;
        hasLastPosition = true;

        if (!InPlay)
        {
            if (chanting) chanting = false;
            darkAloneTimer = 0f;
            zeroTimer = 0f;
            return;
        }

        SanitySettings s = Settings;
        float difficulty = MatchDirector.Instance != null ? MatchDirector.Instance.SanityDrainMultiplier : 1f;
        float delta = 0f;

        // Drain 1: a ghost on my screen (reported by the owner).
        if (reportedSeeingGhost)
        {
            if (Time.time - lastGhostSeen > FirstSightCooldown) delta -= s.ghostFirstSight * difficulty;
            delta -= s.ghostSightPerSecond * dt * difficulty;
            lastGhostSeen = Time.time;
        }

        // Drain 2: alone in the dark for too long.
        bool dark = GhostManager.Instance != null && !GhostManager.Instance.areLightsOnCurrently;
        if (dark && !TeammateWithin(s.aloneRadius, false))
        {
            darkAloneTimer += dt;
            if (darkAloneTimer > s.darkAloneGraceSeconds) delta -= s.darkAlonePerSecond * dt * difficulty;
        }
        else darkAloneTimer = 0f;

        // Gain: chanting, standing still.
        if (chanting && speed <= s.chantMaxMoveSpeed && !BlocksGain)
        {
            float rate = CarriesHolyItem() ? s.chantWithHolyItemPerSecond : s.chantAlonePerSecond;
            rate += s.chantPerFriend * ChantingFriendsNear(s.chantFriendRadius);
            delta += Mathf.Min(rate, s.chantMaxPerSecond) * dt;
        }

        if (delta != 0f) ServerSet(sanity + delta);

        // At 0 for too long: jumpscare + damage, repeating.
        if (sanity <= 0f)
        {
            zeroTimer += dt;
            if (zeroTimer >= s.zeroGraceSeconds && Time.time >= nextZeroHit)
            {
                nextZeroHit = Time.time + s.zeroRepeatSeconds;
                ServerJumpscare();
            }
        }
        else
        {
            zeroTimer = 0f;
            nextZeroHit = 0f;
        }
    }

    private void ServerSet(float value)
    {
        value = Mathf.Clamp(value, 0f, Max);
        if (Mathf.Approximately(value, sanity)) return;
        sanity = value;
        Raise(value); // SyncVar hooks do not run on the machine that changed the value
    }

    private void ServerJumpscare()
    {
        Debug.Log($"[PlayerSanity] {name}: sanity at 0 -> jumpscare, -{Settings.zeroDamage} HP");
        if (health != null) health.TakeDamage(Settings.zeroDamage);
        if (NetworkMode.IsOffline || isLocalPlayer) JumpscareReceived?.Invoke();
        else TargetJumpscare();
    }

    [TargetRpc]
    private void TargetJumpscare() => JumpscareReceived?.Invoke();

    private bool CarriesHolyItem()
    {
        if (inventory == null) return false;
        return inventory.CountOf(ItemCatalog.HolyBook) > 0
            || inventory.CountOf(ItemCatalog.Amulet) > 0
            || inventory.CountOf(ItemCatalog.HolyCross) > 0;
    }

    private bool TeammateWithin(float radius, bool mustBeChanting)
    {
        float r2 = radius * radius;
        for (int i = 0; i < All.Count; i++)
        {
            PlayerSanity other = All[i];
            if (other == null || other == this || !other.InPlay) continue;
            if (mustBeChanting && !other.chanting) continue;
            if ((other.transform.position - transform.position).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    private int ChantingFriendsNear(float radius)
    {
        float r2 = radius * radius;
        int count = 0;
        for (int i = 0; i < All.Count; i++)
        {
            PlayerSanity other = All[i];
            if (other == null || other == this || !other.chanting || !other.InPlay) continue;
            if ((other.transform.position - transform.position).sqrMagnitude <= r2) count++;
        }
        return count;
    }

    // ---- Server API for other systems -----------------------------------------

    /// <summary>SERVER. Lose sanity. applyDifficulty = multiply by the round's sanity-drain difficulty.</summary>
    public void ServerDrain(float amount, string reason, bool applyDifficulty = true)
    {
        if (!NetworkMode.HasServerAuthority(this) || amount <= 0f || !InPlay) return;
        if (applyDifficulty && MatchDirector.Instance != null) amount *= MatchDirector.Instance.SanityDrainMultiplier;
        ServerSet(sanity - amount);
        if (!string.IsNullOrEmpty(reason)) Debug.Log($"[PlayerSanity] {name}: -{amount:0.#} ({reason}) -> {sanity:0}");
    }

    /// <summary>SERVER. Gain sanity (ignored while BREAKING if the settings block it). True if it was applied.</summary>
    public bool ServerGain(float amount, string reason)
    {
        if (!NetworkMode.HasServerAuthority(this) || amount <= 0f || !InPlay || BlocksGain) return false;
        ServerSet(sanity + amount);
        if (!string.IsNullOrEmpty(reason)) Debug.Log($"[PlayerSanity] {name}: +{amount:0.#} ({reason}) -> {sanity:0}");
        return true;
    }

    /// <summary>SERVER. A rule's task failed for this player. amount &lt; 0 = the default in SanitySettings.</summary>
    public static void ServerRuleFailed(GameObject player, float amount = -1f)
    {
        if (player != null && player.TryGetComponent(out PlayerSanity s))
            s.ServerDrain(amount < 0f ? Settings.ruleFailedDefault : amount, "rule failed");
    }

    /// <summary>SERVER. An anomaly hit this player. Each anomaly can pass its own amount (GDD: they differ).</summary>
    public static void ServerAnomaly(GameObject player, float amount = -1f)
    {
        if (player != null && player.TryGetComponent(out PlayerSanity s))
            s.ServerDrain(amount < 0f ? Settings.anomalyDefault : amount, "anomaly");
    }

    /// <summary>SERVER. Dev cheat: set the value directly.</summary>
    public void ServerDevSet(float value) { if (NetworkMode.HasServerAuthority(this)) ServerSet(value); }

    // ---- Commands --------------------------------------------------------------

    [Command(channel = Channels.Unreliable)]
    private void CmdReportGhostSight(bool seeing) => reportedSeeingGhost = seeing;

    [Command]
    private void CmdSetChanting(bool on) => ServerSetChanting(on);

    private void ServerSetChanting(bool on) => chanting = on && InPlay;

    [Command]
    private void CmdEatSnack() => ServerEatSnack();

    private void ServerEatSnack()
    {
        if (!HoldsSnack() || Time.time - lastSnack < Settings.snackEatSeconds * 0.8f) return;
        if (!InPlay || BlocksGain) return;
        lastSnack = Time.time;
        if (inventory.ServerTakeOne(inventory.SelectedSlot)) ServerGain(Settings.snackGain, "ate a snack");
    }

    [Command]
    private void CmdFeedFriend(NetworkIdentity target) => ServerFeedFriend(target != null ? target.GetComponent<PlayerSanity>() : null);

    private void ServerFeedFriend(PlayerSanity friend)
    {
        if (friend == null || friend == this || !HoldsSnack() || !InPlay || !friend.InPlay || friend.BlocksGain) return;
        if (Time.time - lastSnack < Settings.snackFeedSeconds * 0.8f) return;
        float range = Settings.snackFeedRange + 1f; // a little slack for movement lag
        if ((friend.transform.position - transform.position).sqrMagnitude > range * range) return;
        lastSnack = Time.time;
        if (inventory.ServerTakeOne(inventory.SelectedSlot)) friend.ServerGain(Settings.snackGain, "fed by " + name);
    }

    private bool HoldsSnack() =>
        inventory != null && !inventory.HeldSlot.IsEmpty && !inventory.HeldSlot.spare
        && inventory.HeldSlot.itemId == ItemCatalog.Snack;

    // ---- Owner -----------------------------------------------------------------

    private Camera ownCamera;
    private float sightTimer;
    private bool lastSeeing;
    private float lastSightSent;
    private readonly List<Enemy_Abstract_Class> ghosts = new List<Enemy_Abstract_Class>();
    private float ghostListTimer;
    private bool wantChant;

    /// <summary>Owner: 0..1 progress of eating / feeding, for the HUD. 0 = not using.</summary>
    public float UseProgress { get; private set; }
    /// <summary>Owner: true while the progress is for feeding a friend.</summary>
    public bool UseIsFeeding { get; private set; }
    private float useTimer;
    private PlayerSanity feedTarget;

    private void OwnerTick()
    {
        if (ownCamera == null) ownCamera = GetComponentInChildren<Camera>();
        bool inPlay = InPlay;

        // Ghost on screen, a few times a second.
        sightTimer += Time.deltaTime;
        if (sightTimer >= SightReportInterval)
        {
            sightTimer = 0f;
            bool seeing = inPlay && SeesGhost();
            if (seeing != lastSeeing || (seeing && Time.time - lastSightSent > 1f))
            {
                lastSeeing = seeing;
                lastSightSent = Time.time;
                if (NetworkMode.IsOffline) reportedSeeingGhost = seeing;
                else CmdReportGhostSight(seeing);
            }
        }

        bool canAct = inPlay && !GameplayInput.Blocked && Cursor.lockState == CursorLockMode.Locked;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        // Chant: hold the key.
        bool chant = canAct && keyboard != null && keyboard[Settings.chantKey].isPressed;
        if (chant != wantChant)
        {
            wantChant = chant;
            if (NetworkMode.IsOffline) ServerSetChanting(chant);
            else CmdSetChanting(chant);
        }

        // Snack: hold Left Click = eat, hold Right Click on a friend = feed.
        bool holding = canAct && HoldsSnack() && mouse != null;
        bool eat = holding && mouse.leftButton.isPressed;
        PlayerSanity target = holding && mouse.rightButton.isPressed ? FriendInFront() : null;
        if (target != null && target != feedTarget) useTimer = 0f;
        feedTarget = target;

        if (eat || target != null)
        {
            UseIsFeeding = target != null;
            float needed = UseIsFeeding ? Settings.snackFeedSeconds : Settings.snackEatSeconds;
            useTimer += Time.deltaTime;
            UseProgress = Mathf.Clamp01(useTimer / needed);
            if (useTimer >= needed)
            {
                useTimer = 0f;
                UseProgress = 0f;
                if (UseIsFeeding)
                {
                    if (NetworkMode.IsOffline) ServerFeedFriend(target);
                    else CmdFeedFriend(target.netIdentity);
                }
                else if (NetworkMode.IsOffline) ServerEatSnack();
                else CmdEatSnack();
            }
        }
        else
        {
            useTimer = 0f;
            UseProgress = 0f;
        }
    }

    private bool SeesGhost()
    {
        if (ownCamera == null) return false;
        ghostListTimer -= SightReportInterval;
        if (ghostListTimer <= 0f)
        {
            ghostListTimer = 1f;
            ghosts.Clear();
            ghosts.AddRange(FindObjectsByType<Enemy_Abstract_Class>(FindObjectsSortMode.None));
        }

        SanitySettings s = Settings;
        Vector3 eye = ownCamera.transform.position;
        Vector3 forward = ownCamera.transform.forward;
        float cos = Mathf.Cos(s.ghostSightAngle * Mathf.Deg2Rad);

        for (int i = 0; i < ghosts.Count; i++)
        {
            Enemy_Abstract_Class ghost = ghosts[i];
            if (ghost == null || !ghost.isActiveAndEnabled) continue;
            Vector3 target = GhostCentre(ghost);
            Vector3 to = target - eye;
            float distance = to.magnitude;
            if (distance > s.ghostSightRange || distance < 0.01f) continue;
            if (Vector3.Dot(forward, to / distance) < cos) continue;

            // Something solid in between? (the ghost itself does not count)
            if (Physics.Raycast(eye, to / distance, out RaycastHit hit, distance - 0.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(ghost.transform)
                && !hit.transform.IsChildOf(transform))
                continue;
            return true;
        }
        return false;
    }

    private static Vector3 GhostCentre(Enemy_Abstract_Class ghost)
    {
        Renderer r = ghost.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : ghost.transform.position + Vector3.up;
    }

    private PlayerSanity FriendInFront()
    {
        if (ownCamera == null) return null;
        float range = Settings.snackFeedRange;
        Ray ray = new Ray(ownCamera.transform.position, ownCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        PlayerSanity found = null;
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerSanity other = hits[i].collider.GetComponentInParent<PlayerSanity>();
            if (other == null || other == this || hits[i].distance >= best) continue;
            best = hits[i].distance;
            found = other;
        }
        return found;
    }

    /// <summary>Hotbar hint while a sanity item is in your hand (HotbarHUD calls this).</summary>
    public static string HotbarHint(PlayerInventory inventory)
    {
        if (inventory == null || inventory.HeldSlot.IsEmpty || inventory.HeldSlot.spare) return string.Empty;
        string id = inventory.HeldSlot.itemId;
        if (id == ItemCatalog.Snack) return "Hold [Left Click] eat   ·   Hold [Right Click] on a friend: feed them";
        if (ItemCatalog.IsHolyItem(id)) return $"Carry it: hold [{Settings.chantKey}] to chant faster";
        return string.Empty;
    }
}
