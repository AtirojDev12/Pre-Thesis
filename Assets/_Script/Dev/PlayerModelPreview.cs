using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Preview helper for the ModelPreview scene: plays the player model's actions
/// (idle, walk, run, crouch, strafe) through the real PlayerAnimatrCon controller,
/// using the same Animator parameters PlayerMovement sends in game, so what you
/// see here is what the blend/transition looks like in play.
///
/// In the Game view, ModelPreviewHub lists it under the "Player" tab.
/// The Inspector (PlayerModelPreviewEditor) has the same buttons.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerModelPreview : MonoBehaviour, IModelPreview
{
    public enum PreviewAction
    {
        Idle = 0,
        Walk = 1,
        StrafeLeft = 2,
        StrafeRight = 3,
        Run = 6,
        CrouchWalk = 7,
        CrouchIdle = 8,
    }

    [Tooltip("Player Animator Controller (PlayerAnimatrCon). Leave empty to keep the one already on the model.")]
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private PreviewAction action = PreviewAction.Idle;
    [Tooltip("Animation playback speed (1 = normal). Works during Play Mode.")]
    [SerializeField, Range(0f, 2f)] private float playbackSpeed = 1f;
    [Tooltip("Go through every action automatically, this many seconds each.")]
    [SerializeField] private bool autoCycle;
    [SerializeField, Min(0.5f)] private float autoCycleSeconds = 3f;

    // Same parameter names PlayerMovement writes in game.
    private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int HorizontalHash = Animator.StringToHash("Horizontal");
    private static readonly int VerticalHash = Animator.StringToHash("Vertical");
    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");
    private static readonly int IsCrouchingHash = Animator.StringToHash("IsCrouching");

    public static readonly PreviewAction[] Actions =
    {
        PreviewAction.Idle, PreviewAction.Walk, PreviewAction.Run, PreviewAction.CrouchIdle,
        PreviewAction.CrouchWalk, PreviewAction.StrafeLeft, PreviewAction.StrafeRight,
    };
    private static readonly string[] ActionNames = System.Array.ConvertAll(Actions, Label);

    private Animator animator;
    private bool paused;
    private float cycleTimer;

    public PreviewAction CurrentAction => action;
    public bool IsPaused => paused;
    public bool AutoCycle => autoCycle;
    public bool IsReady => animator != null && animator.runtimeAnimatorController != null;

    // ---- IModelPreview (ModelPreviewHub) ----------------------------------------
    public string Category => "Player";
    public string DisplayName => name;
    public IReadOnlyList<string> Animations => ActionNames;
    public string CurrentAnimation => Label(action);
    public void PlayAnimation(string animationName)
    {
        int index = System.Array.IndexOf(ActionNames, animationName);
        if (index >= 0) Play(Actions[index]);
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        ModelPreviewHub.EnsureExists(); // on-screen model tabs + animation buttons in the Game view
        if (animator == null)
        {
            Debug.LogWarning($"[ModelPreview] '{name}' has no Animator.", this);
            return;
        }
        if (controller != null) animator.runtimeAnimatorController = controller;
        if (animator.runtimeAnimatorController == null)
            Debug.LogWarning($"[ModelPreview] '{name}' has no Animator Controller. Assign PlayerAnimatrCon.", this);
        animator.applyRootMotion = false; // move in place, same as in game
        Play(action);
    }

    private void Update()
    {
        if (autoCycle && !paused)
        {
            cycleTimer += Time.deltaTime;
            if (cycleTimer >= autoCycleSeconds) Play(Next(action));
        }
        ApplyParameters();
    }

    // ---- Controls (hub, Inspector buttons) --------------------------------------

    /// <summary>Switches to an action using the controller's normal transition (blend).</summary>
    public void Play(PreviewAction next)
    {
        action = next;
        paused = false;
        cycleTimer = 0f;
        ApplyParameters();
    }

    /// <summary>Plays the current action again from its first frame.</summary>
    [ContextMenu("Restart Animation")]
    public void Restart()
    {
        paused = false;
        cycleTimer = 0f;
        ApplyParameters();
        if (!IsReady) return;
        animator.Update(0f); // let the Any State transition pick the target state first
        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        animator.Play(state.fullPathHash, 0, 0f);
    }

    [ContextMenu("Pause or Resume")]
    public void TogglePause()
    {
        paused = !paused;
        ApplyParameters();
    }

    public void SetAutoCycle(bool value)
    {
        autoCycle = value;
        cycleTimer = 0f;
    }

    public void SetPlaybackSpeed(float value) => playbackSpeed = Mathf.Clamp(value, 0f, 2f);

    [ContextMenu("Play Idle")] private void MenuIdle() => Play(PreviewAction.Idle);
    [ContextMenu("Play Walk")] private void MenuWalk() => Play(PreviewAction.Walk);
    [ContextMenu("Play Run")] private void MenuRun() => Play(PreviewAction.Run);
    [ContextMenu("Play Crouch Idle")] private void MenuCrouchIdle() => Play(PreviewAction.CrouchIdle);
    [ContextMenu("Play Crouch Walk")] private void MenuCrouchWalk() => Play(PreviewAction.CrouchWalk);

    public static string Label(PreviewAction a) => a switch
    {
        PreviewAction.Idle => "Idle",
        PreviewAction.Walk => "Walk",
        PreviewAction.Run => "Run",
        PreviewAction.CrouchIdle => "Crouch Idle",
        PreviewAction.CrouchWalk => "Crouch Walk",
        PreviewAction.StrafeLeft => "Strafe Left",
        PreviewAction.StrafeRight => "Strafe Right",
        _ => a.ToString(),
    };

    // ---- Animator -----------------------------------------------------------

    private void ApplyParameters()
    {
        if (!IsReady) return;
        bool crouch = action == PreviewAction.CrouchIdle || action == PreviewAction.CrouchWalk;
        bool sprint = action == PreviewAction.Run;
        Vector2 direction = action switch
        {
            PreviewAction.Walk or PreviewAction.Run or PreviewAction.CrouchWalk => Vector2.up,
            PreviewAction.StrafeLeft => Vector2.left,
            PreviewAction.StrafeRight => Vector2.right,
            _ => Vector2.zero,
        };
        float speed = direction == Vector2.zero ? 0f : sprint ? 8f : crouch ? 2.25f : 5f; // PlayerMovement defaults

        animator.SetInteger(LocomotionHash, (int)action);
        animator.SetBool(IsCrouchingHash, crouch);
        animator.SetBool(IsSprintingHash, sprint);
        animator.SetFloat(HorizontalHash, direction.x);
        animator.SetFloat(VerticalHash, direction.y);
        animator.SetFloat(SpeedHash, speed);
        animator.speed = paused ? 0f : playbackSpeed;
    }

    private static PreviewAction Next(PreviewAction current)
    {
        int index = System.Array.IndexOf(Actions, current);
        return Actions[(index + 1) % Actions.Length];
    }
}
