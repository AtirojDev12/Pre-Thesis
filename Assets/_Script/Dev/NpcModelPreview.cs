using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Preview helper for the ModelPreview scene: gives an NPC model its Idle/Walk
/// controller in Play Mode. Use the buttons in the Inspector (NpcModelPreviewEditor)
/// or tick "Walking" to switch between the Idle and Walk animation in place.
/// In the Game view, ModelPreviewHub lists it under its Category tab ("NPC" by default;
/// the ghost customers use "GhostNPC").
/// </summary>
[DisallowMultipleComponent]
public sealed class NpcModelPreview : MonoBehaviour, IModelPreview
{
    [Tooltip("Tab this model is listed under in the ModelPreview panel, e.g. NPC or GhostNPC.")]
    [SerializeField] private string category = "NPC";
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

    // ---- IModelPreview (ModelPreviewHub) ----------------------------------------
    private static readonly string[] AnimationNames = { "Idle", "Walk" };
    public string Category => string.IsNullOrEmpty(category) ? "NPC" : category;
    public string DisplayName => name;
    public IReadOnlyList<string> Animations => AnimationNames;
    public string CurrentAnimation => walking ? "Walk" : "Idle";
    public void PlayAnimation(string animationName) => PlayState(animationName == "Walk");

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
        ModelPreviewHub.EnsureExists(); // on-screen model tabs + animation buttons in the Game view
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
