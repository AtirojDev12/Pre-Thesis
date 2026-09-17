using Mirror;
using UnityEngine;

/// <summary>
/// Local sprint resource for the owning player. PlayerMovement asks this
/// component whether a sprint request can be honoured, while StaminaBarUI binds
/// to LocalInstance so a scene HUD never displays another player's stamina.
/// </summary>
[DisallowMultipleComponent]
public class PlayerStamina : NetworkBehaviour
{
    [Header("Stamina")]
    [Min(1f)] [SerializeField] private float maxStamina = 100f;
    [Min(0f)] [SerializeField] private float sprintDrainPerSecond = 25f;
    [Min(0f)] [SerializeField] private float recoveryPerSecond = 20f;
    [Min(0f)] [SerializeField] private float recoveryDelay = 1f;

    [Tooltip("After stamina reaches zero, it must recover to this amount before sprinting can resume.")]
    [Min(0f)] [SerializeField] private float staminaRequiredAfterExhaustion = 20f;

    public static PlayerStamina LocalInstance { get; private set; }
    public static event System.Action<PlayerStamina> LocalInstanceChanged;

    public event System.Action<float, float> StaminaChanged;
    public event System.Action<bool> SprintingChanged;

    public float CurrentStamina { get; private set; }
    public float MaxStamina => maxStamina;
    public bool IsSprinting { get; private set; }
    public bool IsExhausted { get; private set; }

    private float _lastSprintTime = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        LocalInstance = null;
        LocalInstanceChanged = null;
    }

    private void Awake() => ResetStamina();

    private void Start()
    {
        if (NetworkMode.IsOffline) SetLocalInstance(this);
    }

    public override void OnStartLocalPlayer() => SetLocalInstance(this);

    public override void OnStopLocalPlayer()
    {
        if (LocalInstance == this) SetLocalInstance(null);
    }

    /// <summary>
    /// Advances stamina for one frame and returns whether sprinting is allowed.
    /// Only PlayerMovement on the owning client should call this.
    /// </summary>
    public bool UpdateSprint(bool requested, float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);

        if (IsExhausted && CurrentStamina >= Mathf.Min(staminaRequiredAfterExhaustion, maxStamina))
            IsExhausted = false;

        bool canSprint = requested && !IsExhausted && CurrentStamina > 0f;

        if (canSprint)
        {
            _lastSprintTime = Time.time;
            SetStamina(CurrentStamina - sprintDrainPerSecond * deltaTime);

            if (CurrentStamina <= 0f)
            {
                IsExhausted = true;
                canSprint = false;
            }
        }
        else if (Time.time >= _lastSprintTime + recoveryDelay)
        {
            SetStamina(CurrentStamina + recoveryPerSecond * deltaTime);
        }

        SetSprinting(canSprint);
        return canSprint;
    }

    public void ResetStamina()
    {
        IsExhausted = false;
        SetSprinting(false);
        SetStamina(maxStamina, true);
    }

    private void SetStamina(float value, bool forceNotify = false)
    {
        float next = Mathf.Clamp(value, 0f, maxStamina);
        if (!forceNotify && Mathf.Approximately(CurrentStamina, next)) return;

        CurrentStamina = next;
        StaminaChanged?.Invoke(CurrentStamina, maxStamina);
    }

    private void SetSprinting(bool value)
    {
        if (IsSprinting == value) return;
        IsSprinting = value;
        SprintingChanged?.Invoke(value);
    }

    private static void SetLocalInstance(PlayerStamina stamina)
    {
        if (LocalInstance == stamina) return;
        LocalInstance = stamina;
        LocalInstanceChanged?.Invoke(stamina);
    }

    private void OnDestroy()
    {
        if (LocalInstance == this) SetLocalInstance(null);
    }

    private void OnValidate()
    {
        maxStamina = Mathf.Max(1f, maxStamina);
        staminaRequiredAfterExhaustion = Mathf.Clamp(staminaRequiredAfterExhaustion, 0f, maxStamina);
    }
}
