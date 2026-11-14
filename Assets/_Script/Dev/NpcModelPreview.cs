using UnityEngine;

/// <summary>
/// Preview helper for the ModelPreview scene: gives an NPC model its Idle/Walk
/// controller in Play Mode. Use the buttons in the Inspector (NpcModelPreviewEditor)
/// or tick "Walking" to switch between the Idle and Walk animation in place.
/// </summary>
[DisallowMultipleComponent]
public sealed class NpcModelPreview : MonoBehaviour
{
    [Tooltip("Controller with Idle/Walk states and a bool 'IsWalking'.")]
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private bool walking;
    [Tooltip("Animation playback speed (1 = normal). Works during Play Mode.")]
    [SerializeField, Range(0f, 2f)] private float playbackSpeed = 1f;
    [Tooltip("Same as 'Lock Head To Rest Pose' in Resources/CustomerModels: keeps a flipped Mixamo neck/head upright.")]
    [SerializeField] private bool lockHeadToRestPose;
    [Tooltip("Same as 'Arm Spread Degrees' in Resources/CustomerModels: swings the arms away from the body.")]
    [SerializeField, Range(0f, 30f)] private float armSpreadDegrees = 10f;

    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
    private static readonly int IdleStateHash = Animator.StringToHash("Idle");
    private static readonly int WalkStateHash = Animator.StringToHash("Walk");
    private Animator animator;
    private CustomerModelVisual.HeadRestPose headLock;
    private CustomerModelVisual.ArmSpread armSpread;
    private bool paused;

    public bool IsWalking => walking;
    public float PlaybackSpeed => playbackSpeed;
    public float ArmSpreadDegrees => armSpreadDegrees;
    public bool IsPaused => paused;
    public bool IsReady => animator != null && animator.runtimeAnimatorController != null;

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"[ModelPreview] '{name}' has no Animator.", this);
            return;
        }
        if (lockHeadToRestPose) headLock = CustomerModelVisual.HeadRestPose.Capture(animator);
        armSpread = CustomerModelVisual.ArmSpread.Capture(animator, armSpreadDegrees);
        if (controller != null) animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false; // walk in place, same as the customers in game
        Apply();
        NpcPreviewControls.EnsureExists(); // on-screen buttons + keyboard shortcuts in the Game view
    }

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Clamp(speed, 0f, 2f);
        Apply();
    }

    public void SetArmSpread(float degrees)
    {
        armSpreadDegrees = Mathf.Clamp(degrees, 0f, 30f);
        if (animator != null) armSpread = CustomerModelVisual.ArmSpread.Capture(animator, armSpreadDegrees);
    }

    private void LateUpdate()
    {
        headLock?.Apply();
        armSpread?.Apply();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        // Re-capture so the slider takes effect live during Play Mode.
        if (animator != null) armSpread = CustomerModelVisual.ArmSpread.Capture(animator, armSpreadDegrees);
        Apply();
    }

    // ---- Buttons (Inspector) ------------------------------------------------

    /// <summary>Plays the Idle animation from its first frame.</summary>
    [ContextMenu("Play Idle")]
    public void PlayIdle() => PlayState(false);

    /// <summary>Plays the Walk animation (in place) from its first frame.</summary>
    [ContextMenu("Play Walk")]
    public void PlayWalk() => PlayState(true);

    /// <summary>Restarts whichever animation is selected.</summary>
    [ContextMenu("Restart Animation")]
    public void Restart() => PlayState(walking);

    /// <summary>Freezes / resumes the animation on the current frame.</summary>
    [ContextMenu("Pause or Resume")]
    public void TogglePause()
    {
        paused = !paused;
        Apply();
    }

    private void PlayState(bool walk)
    {
        walking = walk;
        paused = false;
        if (!IsReady) return;
        Apply();
        int state = walk ? WalkStateHash : IdleStateHash;
        if (animator.HasState(0, state)) animator.Play(state, 0, 0f);
    }

    private void Apply()
    {
        if (!IsReady) return;
        animator.SetBool(IsWalkingHash, walking);
        animator.speed = paused ? 0f : playbackSpeed;
    }
}
