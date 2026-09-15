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
public class PlayerMovement : NetworkBehaviour
{
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");
    private static readonly int IsDownedParameter = Animator.StringToHash("IsDowned");
    private static readonly int IsDeadParameter = Animator.StringToHash("IsDead");

    [Header("Movement")]
    public float moveSpeed = 5f;

    [Header("Camera")]
    public Transform playerCamera;

    [Header("Animation")]
    [Tooltip("Animator using the Player controller. Leave empty to find it automatically on this player.")]
    [SerializeField] private Animator playerAnimator;

    [Tooltip("How quickly Idle and Walking blend together.")]
    [Min(0f)] [SerializeField] private float animationDampTime = 0.1f;

    private Rigidbody rb;
    private Vector3 movement;
    private PlayerHealth playerHealth;
    private Vector3 lastObservedPosition;
    private bool hasObservedPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerHealth = GetComponent<PlayerHealth>();
        lastObservedPosition = transform.position;
        hasObservedPosition = true;

        if (playerAnimator == null)
            playerAnimator = GetComponent<Animator>();

        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>(true);

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
            UpdateWalkingAnimation();
            return;
        }

        if (Keyboard.current == null || playerCamera == null)
        {
            movement = Vector3.zero;
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

        float normalizedSpeed = isIncapacitated || moveSpeed <= 0f
            ? 0f
            : Mathf.Clamp01(displacement.magnitude / (Time.deltaTime * moveSpeed));

        SetAnimationSpeed(normalizedSpeed);
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

    private void FixedUpdate()
    {
        if (!NetworkMode.IsLocalController(this)) return;
        if (rb == null || rb.isKinematic) return;

        rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime);
    }
}
