using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person movement. Only the player that belongs to THIS machine reads
/// input and moves -- every client holds one copy of this prefab per connected
/// player, and without that guard one keyboard would drive all six of them.
///
/// Movement is client-authoritative (the owner moves itself, a NetworkTransform
/// replicates the result). That is the normal trade-off for co-op PvE: it keeps
/// movement responsive, at the cost of trusting the client's position. Revisit
/// it only if position-cheating ever becomes a concern for this game.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerStamina))]
public class PlayerMovement : NetworkBehaviour
{
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int IsSprintingParameter = Animator.StringToHash("IsSprinting");
    private static readonly int IsDownedParameter = Animator.StringToHash("IsDowned");
    private static readonly int IsDeadParameter = Animator.StringToHash("IsDead");

    [Header("Movement")]
    public float moveSpeed = 5f;

    [Tooltip("Movement speed while holding Left Shift and stamina is available.")]
    [Min(0f)] public float sprintSpeed = 8f;

    [Header("Camera")]
    public Transform playerCamera;

    [Header("Animation")]
    [Tooltip("Animator using the Player controller. Leave empty to find it automatically on this player.")]
    [SerializeField] private Animator playerAnimator;

    [Tooltip("How quickly Idle and Walking blend together.")]
    [Min(0f)] [SerializeField] private float animationDampTime = 0.1f;

    [Header("Running Pose Stabilization")]
    [Tooltip("Removes lateral/root drift authored into the Running clip while preserving vertical bounce and limb motion.")]
    [SerializeField] private bool stabilizeRunningHips = true;

    [Tooltip("0 keeps the animation's original hip drift; 1 keeps the model centred while running.")]
    [Range(0f, 1f)] [SerializeField] private float runningHipStability = 1f;

    private Rigidbody rb;
    private Vector3 movement;
    private PlayerHealth playerHealth;
    private PlayerStamina playerStamina;
    private bool isSprinting;
    private Transform hips;
    private Vector3 hipsRestLocalPosition;
    private Vector3 lastObservedPosition;
    private bool hasObservedPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerHealth = GetComponent<PlayerHealth>();
        playerStamina = GetComponent<PlayerStamina>();
        lastObservedPosition = transform.position;
        hasObservedPosition = true;

        if (playerAnimator == null)
            playerAnimator = GetComponent<Animator>();

        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>(true);

        CacheRunningPoseReference();

        if (playerCamera == null)
        {
            // This player's own camera -- not Camera.main, which would be the
            // same camera for every player instance on this client.
            Camera ownCamera = GetComponentInChildren<Camera>(true);
            if (ownCamera != null) playerCamera = ownCamera.transform;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Remote players are positioned by their NetworkTransform. Leaving their
        // rigidbody dynamic makes local physics fight the incoming positions and
        // produces jitter and phantom collisions.
        if (!isLocalPlayer && rb != null) rb.isKinematic = true;
    }

    private void Update()
    {
        bool isDowned = playerHealth != null && playerHealth.IsDowned;
        bool isDead = playerHealth != null && playerHealth.IsDead;
        if (playerAnimator != null)
        {
            playerAnimator.SetBool(IsDownedParameter, isDowned);
            playerAnimator.SetBool(IsDeadParameter, isDead);
        }

        if (!NetworkMode.IsLocalController(this))
        {
            movement = Vector3.zero;
            UpdateRemoteWalkingAnimation(isDowned || isDead);
            return;
        }

        // Disable movement when downed or dead
        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            movement = Vector3.zero;
            SetSprinting(playerStamina != null
                ? playerStamina.UpdateSprint(false, Time.deltaTime)
                : false);
            UpdateWalkingAnimation();
            return;
        }

        if (Keyboard.current == null || playerCamera == null)
        {
            movement = Vector3.zero;
            SetSprinting(playerStamina != null
                ? playerStamina.UpdateSprint(false, Time.deltaTime)
                : false);
            UpdateWalkingAnimation();
            return;
        }

        // รับ Input
        float horizontal = 0f;
        float vertical = 0f;

        // A / D
        if (Keyboard.current.aKey.isPressed) horizontal = -1f;
        if (Keyboard.current.dKey.isPressed) horizontal = 1f;

        // W / S
        if (Keyboard.current.wKey.isPressed) vertical = 1f;
        if (Keyboard.current.sKey.isPressed) vertical = -1f;

        // ทิศทางของกล้อง
        Vector3 forward = playerCamera.forward;
        Vector3 right = playerCamera.right;

        // ไม่ให้มุมกล้องขึ้น/ลงมีผลกับการเดิน
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        // คำนวณทิศทางการเดิน
        movement = forward * vertical + right * horizontal;

        // ป้องกันเดินเฉียงเร็วเกินไป
        movement = Vector3.ClampMagnitude(movement, 1f);

        bool wantsToSprint = movement.sqrMagnitude > 0.01f &&
                             Keyboard.current.leftShiftKey.isPressed;
        SetSprinting(playerStamina != null
            ? playerStamina.UpdateSprint(wantsToSprint, Time.deltaTime)
            : wantsToSprint);

        UpdateWalkingAnimation();
    }

    private void UpdateWalkingAnimation()
    {
        SetAnimationSpeed(movement.magnitude);
    }

    private void UpdateRemoteWalkingAnimation(bool isIncapacitated)
    {
        Vector3 currentPosition = transform.position;

        if (!hasObservedPosition || Time.deltaTime <= 0f)
        {
            lastObservedPosition = currentPosition;
            hasObservedPosition = true;
            SetAnimationSpeed(0f);
            return;
        }

        Vector3 displacement = currentPosition - lastObservedPosition;
        displacement.y = 0f;
        lastObservedPosition = currentPosition;

        float actualSpeed = displacement.magnitude / Time.deltaTime;
        float normalizedSpeed = isIncapacitated || moveSpeed <= 0f
            ? 0f
            : Mathf.Clamp01(actualSpeed / moveSpeed);

        float sprintThreshold = (moveSpeed + sprintSpeed) * 0.5f;
        SetSprinting(!isIncapacitated && actualSpeed > sprintThreshold);

        SetAnimationSpeed(normalizedSpeed);
    }

    private void SetSprinting(bool value)
    {
        isSprinting = value;
        if (playerAnimator != null)
            playerAnimator.SetBool(IsSprintingParameter, isSprinting);
    }

    private void SetAnimationSpeed(float speed)
    {
        if (playerAnimator == null) return;

        playerAnimator.SetFloat(
            SpeedParameter,
            speed,
            animationDampTime,
            Time.deltaTime);
    }

    private void CacheRunningPoseReference()
    {
        if (playerAnimator == null || !playerAnimator.isHuman) return;

        hips = playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) hipsRestLocalPosition = hips.localPosition;
    }

    private void LateUpdate()
    {
        if (!stabilizeRunningHips || !isSprinting || runningHipStability <= 0f) return;

        if (hips == null)
        {
            CacheRunningPoseReference();
            if (hips == null) return;
        }

        // Mixamo clips often contain a small X/Z translation on the Hips bone.
        // With root motion disabled that movement does not steer the Rigidbody,
        // but it still shifts the entire rendered skeleton left/right. Keep the
        // animated Y value (the useful running bounce) and only remove planar
        // drift, so feet and limbs retain their original motion.
        Vector3 animatedPosition = hips.localPosition;
        hips.localPosition = new Vector3(
            Mathf.Lerp(animatedPosition.x, hipsRestLocalPosition.x, runningHipStability),
            animatedPosition.y,
            Mathf.Lerp(animatedPosition.z, hipsRestLocalPosition.z, runningHipStability));
    }

    private void FixedUpdate()
    {
        if (!NetworkMode.IsLocalController(this)) return;
        if (rb == null || rb.isKinematic) return;

        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;
        rb.MovePosition(rb.position + movement * currentSpeed * Time.fixedDeltaTime);
    }
}
