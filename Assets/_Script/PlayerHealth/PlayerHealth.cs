using Mirror;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ระบบเลือด (Health) ของผู้เล่น -- server-authoritative.
///
/// The server is the only machine that may change health. Clients receive the
/// result through [SyncVar]s and raise local events for UI and effects. This
/// matters for two reasons: teammates need to actually see each other's health,
/// and a modified client must not be able to simply decide it took no damage.
///
/// Damage sources should call TakeDamage() from server-side code (see
/// DamageOnCollision, which only runs its logic on the server).
/// </summary>
public class PlayerHealth : NetworkBehaviour
{
    [Header("ค่าพลังชีวิต")]
    // SyncVar as well as serialized: difficulty may scale max health per match,
    // and clients must agree on the denominator their health bar is drawing.
    [SyncVar] [SerializeField] private float maxHealth = 100f;

    [SyncVar(hook = nameof(OnHealthSynced))] [SerializeField] private float currentHealth = 100f;

    [SyncVar(hook = nameof(OnDeadSynced))] [SerializeField] private bool isDead;

    [SyncVar(hook = nameof(OnDownedSynced))] [SerializeField] private bool isDowned;

    [Header("การป้องกันโดนดาเมจรัว (กันชนแล้วเลือดหมดทันที)")]
    [SerializeField] private float invincibilityDuration = 0.5f;

    [Header("ระบบล้ม (Downed State)")]
    [SerializeField] private float downedDuration = 30f;
    [SerializeField] private float timerSyncInterval = 0.1f;
    [SyncVar] [SerializeField] private float downedTimer;

    // Server-only clock. Comparing a timestamp removes the need for an Update()
    // that would otherwise run once per player per client, every frame.
    private float _invincibleUntil;
    private float _downedStartTime;
    private float _lastTimerSyncTime;

    [Header("Events (ผูกกับ UI หรือ effect อื่นๆ ได้)")]
    public UnityEvent<float, float> OnHealthChanged; // (currentHealth, maxHealth)
    public UnityEvent OnDamaged;
    public UnityEvent OnDeath;
    public UnityEvent OnDowned;
    public UnityEvent<float> OnDownedTimerChanged; // (remainingTime)

    /// <summary>
    /// The PlayerHealth belonging to the player at THIS machine, or null before
    /// they spawn. UI binds to this instead of being pre-wired in the Inspector,
    /// because a runtime-spawned prefab cannot reference a scene object.
    /// </summary>
    public static PlayerHealth LocalInstance { get; private set; }

    public static event System.Action<PlayerHealth> LocalInstanceChanged;

    private float _lastRaisedHealth = float.NaN;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
    public bool IsDowned => isDowned;
    public float DownedTimer => downedTimer;
    private bool IsInvincible => Time.time < _invincibleUntil;

    // Statics survive between Play sessions when Unity 6's domain reload is
    // disabled, so a stale LocalInstance from the last run must be cleared.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        LocalInstance = null;
        LocalInstanceChanged = null;
    }

    public override void OnStartServer()
    {
        currentHealth = maxHealth;
        isDead = false;
        isDowned = false;
        downedTimer = 0f;
    }

    private void Start()
    {
        // A scene with no NetworkManager never fires OnStartServer or
        // OnStartLocalPlayer, so set the same state up directly.
        if (NetworkMode.IsOffline)
        {
            currentHealth = maxHealth;
            isDead = false;
            isDowned = false;
            downedTimer = 0f;
            SetLocalInstance(this);
            RaiseHealthChanged(currentHealth);
        }
    }

    public override void OnStartLocalPlayer() => SetLocalInstance(this);

    public override void OnStopLocalPlayer()
    {
        if (LocalInstance == this) SetLocalInstance(null);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Late joiners must draw the health this player actually has, not the
        // prefab default.
        RaiseHealthChanged(currentHealth);
    }

    private void Update()
    {
        // Only server/host should update the downed timer
        if (!NetworkMode.HasServerAuthority(this)) return;

        if (isDowned && !isDead)
        {
            downedTimer = downedDuration - (Time.time - _downedStartTime);

            if (downedTimer <= 0f)
            {
                downedTimer = 0f;
                ServerDie();
            }
            else
            {
                // Sync timer to clients at intervals to avoid network spam
                if (Time.time - _lastTimerSyncTime >= timerSyncInterval)
                {
                    _lastTimerSyncTime = Time.time;

                    if (NetworkMode.IsOffline)
                    {
                        OnDownedTimerChanged?.Invoke(downedTimer);
                    }
                    else
                    {
                        RpcUpdateDownedTimer(downedTimer);
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (LocalInstance == this) SetLocalInstance(null);
    }

    private static void SetLocalInstance(PlayerHealth instance)
    {
        LocalInstance = instance;
        LocalInstanceChanged?.Invoke(instance);
    }

    /// <summary>
    /// SERVER-SIDE ONLY. Call this from server code (or from anywhere in a solo
    /// test scene). A client calling it directly changes nothing and is warned.
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning(
                $"[PlayerHealth] TakeDamage was called on '{name}' from a machine with no server authority and was ignored. " +
                "Damage must be applied on the server.", this);
            return;
        }

        if (isDead || isDowned || amount <= 0f || IsInvincible) return;

        currentHealth = Mathf.Clamp(currentHealth - amount, 0f, maxHealth);
        _invincibleUntil = Time.time + invincibilityDuration;

        // Mirror does not call SyncVar hooks on the machine that made the
        // change, so the server/host raises its own local event here.
        RaiseHealthChanged(currentHealth);

        if (NetworkMode.IsOffline) RaiseDamaged();
        else RpcDamaged();

        Debug.Log($"[PlayerHealth] โดนดาเมจ {amount} คะแนน เหลือเลือด {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f) ServerDowned();
    }

    private void ServerDowned()
    {
        isDowned = true;
        _downedStartTime = Time.time;
        downedTimer = downedDuration;
        _lastTimerSyncTime = Time.time;
        Debug.Log($"[PlayerHealth] ผู้เล่นล้มแล้ว ({name}) เริ่มนับถอยหลัง {downedDuration} วินาที");

        RaiseDowned();

        if (NetworkMode.IsOffline)
        {
            OnDownedTimerChanged?.Invoke(downedTimer);
        }
        else
        {
            RpcDowned();
            RpcUpdateDownedTimer(downedTimer);
        }
    }

    /// <summary>SERVER-SIDE ONLY.</summary>
    public void Heal(float amount)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning(
                $"[PlayerHealth] Heal was called on '{name}' from a machine with no server authority and was ignored.", this);
            return;
        }

        if (isDead || amount <= 0f) return;

        // If downed, healing revives the player
        if (isDowned)
        {
            Revive(amount);
            return;
        }

        currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        RaiseHealthChanged(currentHealth);

        Debug.Log($"[PlayerHealth] ฮีลเลือด {amount} คะแนน เลือดตอนนี้ {currentHealth}/{maxHealth}");
    }

    /// <summary>SERVER-SIDE ONLY. Revive player from downed state.</summary>
    public void Revive(float healAmount = 0f)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning(
                $"[PlayerHealth] Revive was called on '{name}' from a machine with no server authority and was ignored.", this);
            return;
        }

        if (!isDowned || isDead) return;

        isDowned = false;
        downedTimer = 0f;
        currentHealth = Mathf.Clamp(healAmount > 0f ? healAmount : maxHealth * 0.3f, 1f, maxHealth);

        Debug.Log($"[PlayerHealth] ผู้เล่นถูกคืนชีพ ({name}) เลือด {currentHealth}/{maxHealth}");

        RaiseHealthChanged(currentHealth);

        if (NetworkMode.IsOffline)
        {
            // Offline mode handled
        }
        else
        {
            RpcRevived();
        }
    }

    [ClientRpc] private void RpcRevived()
    {
        // Client-side revive effects can be added here
    }

    private void ServerDie()
    {
        isDead = true;
        isDowned = false;
        downedTimer = 0f;
        Debug.Log($"[PlayerHealth] ผู้เล่นตายแล้ว ({name})");

        // Clients learn about the death from the isDead SyncVar hook. The hook
        // does not fire on the machine that made the change, so raise it here
        // for the server/host. Deliberately NOT also sending a ClientRpc --
        // that would fire the death event twice on every client.
        RaiseDeath();

        if (NetworkMode.IsOffline)
        {
            HandleLocalDeathConsequences();
            return;
        }

        // Only the machine that owns this player may touch its save file, so the
        // inventory consequence is sent to that one client -- never broadcast.
        TargetHandleDeathConsequences(connectionToClient);
    }

    [ClientRpc] private void RpcDamaged() => RaiseDamaged();
    [ClientRpc] private void RpcDowned() => RaiseDowned();
    [ClientRpc] private void RpcUpdateDownedTimer(float timer) => OnDownedTimerChanged?.Invoke(timer);

    /// <summary>
    /// Runs ONLY on the machine that owns this player. SaveManager.Current is
    /// local, per-machine save data -- the server neither has it nor may write it.
    /// </summary>
    [TargetRpc]
    private void TargetHandleDeathConsequences(NetworkConnectionToClient target) => HandleLocalDeathConsequences();

    private void HandleLocalDeathConsequences()
    {
        // TODO (design doc: "dying in a match removes the item from inventory"):
        // once the loadout system exists and we know which permanent items were
        // actually carried into this match, remove them here --
        //     foreach (string itemID in carriedPermanentItemIDs)
        //         SaveManager.RemovePermanentItem(itemID);
        //     SaveManager.SaveToDisk();
        // Deliberately not guessed at yet: which items count as "carried" is a
        // loadout decision that hasn't been made. The delivery path is wired so
        // only the owning client will run it when that day comes.
    }

    private void OnHealthSynced(float oldValue, float newValue) => RaiseHealthChanged(newValue);

    private void OnDeadSynced(bool oldValue, bool newValue)
    {
        if (newValue) RaiseDeath();
    }

    private void OnDownedSynced(bool oldValue, bool newValue)
    {
        if (newValue) RaiseDowned();
    }

    /// <summary>
    /// Idempotent on purpose: on a host the value can arrive both from the
    /// server-side call and from the client path, and firing UI events twice for
    /// one change causes double-flashes and double-counted effects.
    /// </summary>
    private void RaiseHealthChanged(float value)
    {
        if (!float.IsNaN(_lastRaisedHealth) && Mathf.Approximately(_lastRaisedHealth, value)) return;
        _lastRaisedHealth = value;
        OnHealthChanged?.Invoke(value, maxHealth);
    }

    private void RaiseDamaged() => OnDamaged?.Invoke();

    private void RaiseDeath() => OnDeath?.Invoke();

    private void RaiseDowned() => OnDowned?.Invoke();
}
