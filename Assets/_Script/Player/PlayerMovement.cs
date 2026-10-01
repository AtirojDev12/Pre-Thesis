using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

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
    private static readonly int RunningState = Animator.StringToHash("Running");
    private static readonly int HorizontalParameter = Animator.StringToHash("Horizontal");
    private static readonly int VerticalParameter = Animator.StringToHash("Vertical");
    private static readonly int CrouchingParameter = Animator.StringToHash("IsCrouching");
    private static readonly int LocomotionParameter = Animator.StringToHash("Locomotion");

    [Header("Movement")]
    public float moveSpeed = 5f;

    [Tooltip("Movement speed while holding Left Shift and stamina is available.")]
    [Min(0f)] public float sprintSpeed = 8f;
    [Range(0.1f, 1f)] public float backwardSpeedMultiplier = 0.55f;
    [Range(0.1f, 1f)] public float strafeSpeedMultiplier = 0.8f;
    [Range(0.1f, 1f)] [SerializeField] private float crouchSpeedMultiplier = 0.45f;
    [SerializeField] private float crouchHeight = 1.2f;

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

    [Tooltip("Shoe mesh allowance below the foot/toe reference points, including toe-off, in world metres.")]
    [Min(0f)] [SerializeField] private float runningSoleClearance = 0.08f;

    private Rigidbody rb;
    private Vector3 movement;
    private PlayerHealth playerHealth;
    private PlayerStamina playerStamina;
    private bool isSprinting;
    private bool localCrouch;
    [SyncVar] private bool networkCrouch;
    private float standingHeight;
    private Vector3 standingCenter;
    public bool IsCrouching => NetworkMode.IsLocalController(this) ? localCrouch : networkCrouch;
    public bool IsSprinting => isSprinting;
    private Transform hips;
    private Transform leftFoot;
    private Transform rightFoot;
    private Transform leftToes;
    private Transform rightToes;
    private CapsuleCollider bodyCollider;
    private Vector3 hipsRestLocalPosition;
    // Replicate the owner's locomotion choice rather than guessing it from
    // interpolated transforms, which can pause/catch up between snapshots.
    [SyncVar] private bool animationMoving;
    [SyncVar] private bool animationSprinting;
    private bool sentAnimationState;
    private bool lastSentMoving;
    private bool lastSentSprinting;
    private bool lastSentCrouch;
    [SyncVar] private Vector2 animationDirection;
    private Vector2 lastSentDirection;
    private Vector2 localDirection;
    public Vector2 AnimationDirection => NetworkMode.IsLocalController(this) ? localDirection : animationDirection;
    public float CurrentMovementSpeed
    {
        get
        {
            Vector2 direction = AnimationDirection.normalized;
            float forwardScale = direction.y < 0f ? backwardSpeedMultiplier : 1f;
            float scale = new Vector2(direction.x * strafeSpeedMultiplier, direction.y * forwardScale).magnitude;
            return (isSprinting ? sprintSpeed : moveSpeed) * scale * (IsCrouching ? crouchSpeedMultiplier : 1f);
        }
    }

    [Header("Directional steps and grounding")]
    [Min(0.1f)] [SerializeField] private float stepLength = 0.7f;
    [Min(0f)] [SerializeField] private float stepHeight = 0.12f;
    [Min(0f)] [SerializeField] private float groundProbeDistance = 0.2f;
    [Range(0f, 80f)] [SerializeField] private float maximumGroundAngle = 50f;
    private readonly RaycastHit[] groundHits = new RaycastHit[24];
    private float stepPhase;
    private float directionalWeight;
    private Vector2 blendedDirection;
    private Vector3 leftFootRest;
    private Vector3 rightFootRest;
    private struct SolePoint
    {
        public Transform bone;
        public Vector3 position;
        public Transform bone1, bone2, bone3;
        public Vector3 position1, position2, position3;
        public Vector4 weights;
        public bool left;

        public Vector3 WorldPosition()
        {
            Vector3 point = bone.TransformPoint(position) * weights.x;
            if (weights.y > 0f) point += bone1.TransformPoint(position1) * weights.y;
            if (weights.z > 0f) point += bone2.TransformPoint(position2) * weights.z;
            if (weights.w > 0f) point += bone3.TransformPoint(position3) * weights.w;
            return point;
        }
    }
    private readonly List<SolePoint> solePoints = new List<SolePoint>();
    private Vector3 lastStepPosition;
    private bool hasStepPosition;
    private Transform modelRoot;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider != null) { standingHeight = bodyCollider.height; standingCenter = bodyCollider.center; }
        playerHealth = GetComponent<PlayerHealth>();
        playerStamina = GetComponent<PlayerStamina>();

        if (playerAnimator == null)
            playerAnimator = GetComponent<Animator>();

        if (playerAnimator == null)
            playerAnimator = GetComponentInChildren<Animator>(true);

        CacheRunningPoseReference();
        if (playerAnimator != null) playerAnimator.applyRootMotion = false;

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

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        sentAnimationState = false;
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
            ApplyCrouchShape(networkCrouch);
            UpdateRemoteWalkingAnimation(isDowned || isDead);
            return;
        }

        // Disable movement when downed or dead
        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            movement = Vector3.zero;
            localCrouch = false;
            ApplyCrouchShape(false);
            SetSprinting(playerStamina != null
                ? playerStamina.UpdateSprint(false, Time.deltaTime)
                : false);
            UpdateWalkingAnimation();
            return;
        }

        // GameplayInput.Blocked: the Esc menu is open, so W/A/S/D/Shift do nothing.
        if (playerCamera == null || GameplayInput.Blocked)
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

        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed) horizontal -= 1f;
            if (Keyboard.current.dKey.isPressed) horizontal += 1f;
            if (Keyboard.current.wKey.isPressed) vertical += 1f;
            if (Keyboard.current.sKey.isPressed) vertical -= 1f;
        }
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.04f) { horizontal = stick.x; vertical = stick.y; }
        }
        bool crouchHeld = (Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed) ||
                             (Gamepad.current != null && Gamepad.current.buttonEast.isPressed);
        if (crouchHeld && !localCrouch && IsGroundedForStance()) localCrouch = true;
        else if (!crouchHeld && localCrouch && CanStand()) localCrouch = false;
        ApplyCrouchShape(localCrouch);

        // ทิศทางของกล้อง
        Vector3 forward = playerCamera.forward;
        Vector3 right;

        // ไม่ให้มุมกล้องขึ้น/ลงมีผลกับการเดิน
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = transform.forward;
        forward.Normalize();
        right = Vector3.Cross(Vector3.up, forward);

        // คำนวณทิศทางการเดิน
        movement = forward * vertical + right * horizontal;

        // ป้องกันเดินเฉียงเร็วเกินไป
        movement = Vector3.ClampMagnitude(movement, 1f);

        bool shift = (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) ||
                     (Gamepad.current != null && Gamepad.current.leftStickButton.isPressed);
        bool wantsToSprint = !localCrouch && movement.sqrMagnitude > 0.01f && shift &&
                             (vertical > 0f || Mathf.Abs(horizontal) > 0.01f);
        SetSprinting(playerStamina != null
            ? playerStamina.UpdateSprint(wantsToSprint, Time.deltaTime)
            : wantsToSprint);

        UpdateWalkingAnimation();
    }

    private void UpdateWalkingAnimation()
    {
        Vector3 relative = transform.InverseTransformDirection(movement);
        localDirection = new Vector2(relative.x, relative.z);
        SetAnimationSpeed(movement.magnitude);
        if (NetworkMode.IsOffline || !isLocalPlayer) return;
        bool moving = movement.sqrMagnitude > 0.01f;
        if (sentAnimationState && moving == lastSentMoving && isSprinting == lastSentSprinting && localCrouch == lastSentCrouch &&
            (localDirection - lastSentDirection).sqrMagnitude < 0.0025f) return;
        sentAnimationState = true;
        lastSentMoving = moving;
        lastSentSprinting = isSprinting;
        lastSentCrouch = localCrouch;
        lastSentDirection = localDirection;
        CmdSetAnimationState(localDirection, isSprinting, localCrouch);
    }

    [Command]
    private void CmdSetAnimationState(Vector2 direction, bool sprinting, bool crouching)
    {
        bool incapacitated = playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead);
        if (float.IsNaN(direction.x) || float.IsNaN(direction.y) ||
            float.IsInfinity(direction.x) || float.IsInfinity(direction.y)) direction = Vector2.zero;
        animationDirection = incapacitated ? Vector2.zero : Vector2.ClampMagnitude(direction, 1f);
        animationMoving = animationDirection.sqrMagnitude > 0.01f;
        bool acceptedCrouch = !incapacitated && crouching;
        if (acceptedCrouch && !networkCrouch && !IsGroundedForStance()) acceptedCrouch = false;
        if (!incapacitated && !acceptedCrouch && networkCrouch && !CanStand()) acceptedCrouch = true;
        networkCrouch = acceptedCrouch;
        ApplyCrouchShape(networkCrouch);
        if (networkCrouch != crouching) TargetCorrectCrouch(connectionToClient, networkCrouch);
        animationSprinting = sprinting && animationMoving && !networkCrouch &&
                             (animationDirection.y > 0f || Mathf.Abs(animationDirection.x) > 0.01f);
    }

    [TargetRpc]
    private void TargetCorrectCrouch(NetworkConnectionToClient target, bool accepted)
    {
        localCrouch = accepted;
        ApplyCrouchShape(accepted);
        sentAnimationState = false;
    }

    private void UpdateRemoteWalkingAnimation(bool isIncapacitated)
    {
        SetSprinting(!isIncapacitated && animationSprinting);
        SetAnimationSpeed(!isIncapacitated && animationMoving ? 1f : 0f);
        SetDirectionalAnimation(isIncapacitated ? Vector2.zero : animationDirection, networkCrouch);
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
        SetDirectionalAnimation(localDirection, localCrouch);
    }

    private void SetDirectionalAnimation(Vector2 direction, bool crouching)
    {
        if (playerAnimator == null) return;
        playerAnimator.SetFloat(HorizontalParameter, direction.x, animationDampTime, Time.deltaTime);
        playerAnimator.SetFloat(VerticalParameter, direction.y, animationDampTime, Time.deltaTime);
        playerAnimator.SetBool(CrouchingParameter, crouching);
        int state = 0; // Idle
        if (direction.sqrMagnitude > 0.01f)
        {
            if (crouching) state = 7;
            else if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) && Mathf.Abs(direction.x) > 0.1f)
                state = direction.x < 0f ? (isSprinting ? 4 : 2) : (isSprinting ? 5 : 3);
            else state = isSprinting ? 6 : 1;
        }
        else if (crouching) state = 8;
        playerAnimator.SetInteger(LocomotionParameter, state);
    }

    private bool IsGroundedForStance() => rb != null && Mathf.Abs(rb.linearVelocity.y) < 1.5f &&
        TryGround(transform.position, groundProbeDistance, out _);

    private bool CanStand()
    {
        if (bodyCollider == null || standingHeight <= 0f) return true;
        float radius = bodyCollider.radius * 0.95f;
        Vector3 center = transform.TransformPoint(standingCenter);
        // Probe only the volume newly occupied by the head/shoulders. Testing
        // the full standing capsule would include the floor at the feet.
        float currentTop = bodyCollider.bounds.max.y;
        float standingTop = center.y + standingHeight * 0.5f;
        Vector3 bottom = new Vector3(center.x, currentTop + radius, center.z);
        Vector3 top = new Vector3(center.x, standingTop - radius, center.z);
        Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in hits)
            if (hit != bodyCollider && !hit.transform.IsChildOf(transform)) return false;
        return true;
    }

    private void ApplyCrouchShape(bool crouch)
    {
        if (bodyCollider == null || standingHeight <= 0f) return;
        float height = crouch ? Mathf.Clamp(crouchHeight, bodyCollider.radius * 2f, standingHeight) : standingHeight;
        bodyCollider.height = height;
        bodyCollider.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f);
    }

    private void CacheRunningPoseReference()
    {
        if (playerAnimator == null || !playerAnimator.isHuman) return;

        hips = playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
        modelRoot = hips;
        while (modelRoot != null && modelRoot.parent != playerAnimator.transform)
            modelRoot = modelRoot.parent;
        if (modelRoot == null || modelRoot == hips) modelRoot = playerAnimator.transform;
        leftFoot = playerAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = playerAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftToes = playerAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
        rightToes = playerAnimator.GetBoneTransform(HumanBodyBones.RightToes);
        if (hips != null) hipsRestLocalPosition = hips.localPosition;
        if (leftFoot != null && rightFoot != null)
        {
            leftFootRest = transform.InverseTransformPoint(leftFoot.position);
            rightFootRest = transform.InverseTransformPoint(rightFoot.position);
            CacheSoles();
        }
    }

    private void CacheSoles()
    {
        // Bake once, in the rest pose, to measure this model's shoes rather than
        // assuming ankle/toe pivots are the soles. Runtime work only transforms
        // these small foot patches with their skin weights; it never bakes the
        // full character each frame. Weights matter at the ankle/toe joint.
        solePoints.Clear();
        Transform[] feet = { leftFoot, rightFoot, leftToes, rightToes };
        var mesh = new Mesh();
        var vertices = new List<Vector3>();
        float ankleHeight = Mathf.Min(leftFoot.position.y, rightFoot.position.y);
        foreach (var renderer in playerAnimator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.BakeMesh(mesh);
            mesh.GetVertices(vertices);
            Mesh source = renderer.sharedMesh;
            bool readable = source != null && source.isReadable;
            Vector3[] sourceVertices = readable ? source.vertices : null;
            BoneWeight[] weights = readable ? source.boneWeights : null;
            Matrix4x4[] bindPoses = readable ? source.bindposes : null;
            Transform[] bones = renderer.bones;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                if (point.y > ankleHeight + 0.08f) continue;
                Transform nearest = leftFoot;
                float distance = float.PositiveInfinity;
                foreach (Transform foot in feet)
                {
                    if (foot == null) continue;
                    float candidate = (point - foot.position).sqrMagnitude;
                    if (candidate >= distance) continue;
                    distance = candidate;
                    nearest = foot;
                }
                if (distance < 0.09f)
                {
                    var sole = new SolePoint { bone = nearest, position = nearest.InverseTransformPoint(point),
                        weights = new Vector4(1f, 0f, 0f, 0f), left = nearest == leftFoot || nearest == leftToes };
                    if (readable && weights.Length == vertices.Count)
                    {
                        BoneWeight skin = weights[i];
                        Vector3 vertex = sourceVertices[i];
                        sole.bone = bones[skin.boneIndex0];
                        sole.bone1 = bones[skin.boneIndex1];
                        sole.bone2 = bones[skin.boneIndex2];
                        sole.bone3 = bones[skin.boneIndex3];
                        sole.position = bindPoses[skin.boneIndex0].MultiplyPoint3x4(vertex);
                        sole.position1 = bindPoses[skin.boneIndex1].MultiplyPoint3x4(vertex);
                        sole.position2 = bindPoses[skin.boneIndex2].MultiplyPoint3x4(vertex);
                        sole.position3 = bindPoses[skin.boneIndex3].MultiplyPoint3x4(vertex);
                        sole.weights = new Vector4(skin.weight0, skin.weight1, skin.weight2, skin.weight3);
                    }
                    solePoints.Add(sole);
                }
            }
        }
        Destroy(mesh);
    }

    private void LateUpdate()
    {
        if (playerAnimator == null || (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))) return;
        // Include the blend out of Running, even after Shift has been released.
        bool runningPose = playerAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == RunningState ||
            (playerAnimator.IsInTransition(0) && playerAnimator.GetNextAnimatorStateInfo(0).shortNameHash == RunningState);
        // The new FBXs contain metres of lateral/forward translation on Hips.
        // Physics owns displacement; otherwise the visible model slides away
        // from its collider and snaps back at each loop boundary.
        bool locomotionPose = playerAnimator.GetInteger(LocomotionParameter) != 0;

        if (hips == null)
        {
            CacheRunningPoseReference();
            if (hips == null) return;
        }

        // Mixamo clips often contain a small X/Z translation on the Hips bone.
        // With root motion disabled that movement does not steer the Rigidbody,
        // but it still shifts the entire rendered skeleton left/right. Remove
        // planar drift first; the grounding correction below preserves vertical
        // bounce wherever the animated shoes already clear the support plane.
        if (locomotionPose && stabilizeRunningHips && runningHipStability > 0f)
        {
            Vector3 animatedPosition = hips.localPosition;
            hips.localPosition = new Vector3(
                Mathf.Lerp(animatedPosition.x, hipsRestLocalPosition.x, runningHipStability),
                animatedPosition.y,
                Mathf.Lerp(animatedPosition.z, hipsRestLocalPosition.z, runningHipStability));
        }

        // Adjust only the visual skeleton, never the collider or camera.
        if (bodyCollider == null || !bodyCollider.enabled || leftFoot == null || rightFoot == null) return;
        float lowestSole = Mathf.Min(leftFoot.position.y - playerAnimator.leftFeetBottomHeight,
            rightFoot.position.y - playerAnimator.rightFeetBottomHeight);
        // At toe-off the toes can be lower than either ankle's sole estimate.
        if (leftToes != null) lowestSole = Mathf.Min(lowestSole, leftToes.position.y);
        if (rightToes != null) lowestSole = Mathf.Min(lowestSole, rightToes.position.y);
        if (!TryGround(bodyCollider.bounds.center, groundProbeDistance, out RaycastHit support)) return;
        float lift = support.point.y + runningSoleClearance - lowestSole;
        if (solePoints.Count > 0)
        {
            bool leftSupported = TryGround(leftFoot.position, groundProbeDistance + 0.15f, out RaycastHit leftSupport);
            bool rightSupported = TryGround(rightFoot.position, groundProbeDistance + 0.15f, out RaycastHit rightSupport);
            lift = float.NegativeInfinity;
            foreach (SolePoint point in solePoints)
            {
                if (point.left ? !leftSupported : !rightSupported) continue;
                RaycastHit footSupport = point.left ? leftSupport : rightSupport;
                float clearance = Vector3.Dot(footSupport.normal, point.WorldPosition() - footSupport.point);
                lift = Mathf.Max(lift, (0.003f - clearance) / footSupport.normal.y);
            }
            if (float.IsNegativeInfinity(lift)) return;
        }
        // Walking always has a support foot. Correct both floating and penetration;
        // the run retains its authored flight phase. Never ground an airborne body.
        if (lift > 0f || !runningPose)
            hips.position += Vector3.up * Mathf.Max(lift, -0.25f);
    }

    /// <summary>Final rendered sole clearance, after animation, IK and grounding.</summary>
    public bool TryGetFootGround(bool left, out Vector3 position, out float clearance)
    {
        position = transform.position;
        clearance = float.PositiveInfinity;
        Transform foot = left ? leftFoot : rightFoot;
        if (foot == null || !TryGround(foot.position, groundProbeDistance, out RaycastHit support)) return false;
        position = support.point;
        foreach (SolePoint point in solePoints)
            if (point.left == left)
                clearance = Mathf.Min(clearance, Vector3.Dot(support.normal, point.WorldPosition() - support.point));
        if (float.IsPositiveInfinity(clearance))
            clearance = foot.position.y - (left ? playerAnimator.leftFeetBottomHeight : playerAnimator.rightFeetBottomHeight) - support.point.y;
        return true;
    }

    private bool TryGround(Vector3 point, float reach, out RaycastHit support)
    {
        support = default;
        if (bodyCollider == null) return false;
        float bottom = bodyCollider.bounds.min.y;
        Vector3 origin = new Vector3(point.x, bottom + 0.3f, point.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 0.3f + reach,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.collider == bodyCollider ||
                hit.normal.y < Mathf.Cos(maximumGroundAngle * Mathf.Deg2Rad) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            support = hit;
        }
        return nearest < float.PositiveInfinity;
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || playerAnimator == null || !playerAnimator.isHuman || leftFoot == null || rightFoot == null) return;
        bool incapacitated = playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead);
        Vector2 direction = incapacitated ? Vector2.zero : AnimationDirection;
        // The authored lateral and crouch clips supply their own leg motion.
        if (IsCrouching || Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) && Mathf.Abs(direction.x) > 0.1f)
        {
            playerAnimator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            playerAnimator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            playerAnimator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            playerAnimator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            directionalWeight = 0f;
            return;
        }
        Vector3 stepDelta = transform.position - lastStepPosition;
        stepDelta.y = 0f;
        float distance = hasStepPosition ? Mathf.Min(stepDelta.magnitude, 1f) : 0f;
        lastStepPosition = transform.position;
        hasStepPosition = true;
        float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, animationDampTime));
        blendedDirection = Vector2.Lerp(blendedDirection, direction, blend);
        // Forward uses the authored walk/run; sideways and backward use an
        // alternating support/swing gait, never a forward clip played in reverse.
        float targetWeight = direction.sqrMagnitude > 0.01f
            ? Mathf.Clamp01((1f - direction.normalized.y) * 2f) : 0f;
        directionalWeight = Mathf.Lerp(directionalWeight, targetWeight, blend);
        if (incapacitated || !TryGround(transform.position, groundProbeDistance, out _))
        {
            playerAnimator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            playerAnimator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            playerAnimator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            playerAnimator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            return;
        }
        // Advance by actual travel, so blocked players and interpolated remote
        // players cannot keep sliding their planted feet at the requested speed.
        stepPhase = Mathf.Repeat(stepPhase + distance / (2f * Mathf.Max(0.1f, stepLength)), 1f);
        // A small knee bend gives the solver room to lift/reach a stepping foot.
        playerAnimator.bodyPosition -= Vector3.up * (0.08f * directionalWeight);
        PlaceFoot(AvatarIKGoal.LeftFoot, leftFootRest, stepPhase, playerAnimator.leftFeetBottomHeight);
        PlaceFoot(AvatarIKGoal.RightFoot, rightFootRest, Mathf.Repeat(stepPhase + 0.5f, 1f), playerAnimator.rightFeetBottomHeight);
    }

    private void PlaceFoot(AvatarIKGoal goal, Vector3 rest, float phase, float soleHeight)
    {
        bool stance = phase < 0.5f;
        float swing = (phase - 0.5f) * 2f;
        float along = stance ? 0.5f - phase * 2f : Mathf.Lerp(-0.5f, 0.5f, Mathf.SmoothStep(0f, 1f, swing));
        Vector3 direction = transform.TransformDirection(new Vector3(blendedDirection.x, 0f, blendedDirection.y)).normalized;
        Vector3 target = transform.TransformPoint(rest) + direction * (along * stepLength);
        Vector3 authored = modelRoot.TransformPoint(playerAnimator.transform.InverseTransformPoint(playerAnimator.GetIKPosition(goal)));
        if (!TryGround(Vector3.Lerp(authored, target, directionalWeight), groundProbeDistance + 0.15f, out RaycastHit hit))
        {
            playerAnimator.SetIKPositionWeight(goal, 0f);
            playerAnimator.SetIKRotationWeight(goal, 0f);
            return;
        }
        float lift = stance ? 0f : Mathf.Sin(swing * Mathf.PI) * stepHeight;
        target.y = hit.point.y + soleHeight + 0.025f + lift;
        // This prefab's visual model is offset below the Animator root. Unity's
        // humanoid goals omit that container offset; convert both ways so the
        // solver targets the rendered floor rather than a point a metre below it.
        // Keep the forward clip's swing height, lifting it only for an obstacle.
        authored.y = Mathf.Max(authored.y, hit.point.y + soleHeight + 0.025f);
        playerAnimator.SetIKPositionWeight(goal, 1f);
        Vector3 renderedTarget = Vector3.Lerp(authored, target, directionalWeight);
        playerAnimator.SetIKPosition(goal, playerAnimator.transform.TransformPoint(modelRoot.InverseTransformPoint(renderedTarget)));
        playerAnimator.SetIKRotationWeight(goal, directionalWeight);
        // Humanoid IK goals use sole-up/character-forward space, not the FBX
        // foot bone's bind rotation (which points this rig's toes downward).
        playerAnimator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, hit.normal) * transform.rotation);
    }

    private void FixedUpdate()
    {
        if (!NetworkMode.IsLocalController(this)) return;
        if (rb == null || rb.isKinematic) return;

        float currentSpeed = CurrentMovementSpeed;
        // Let the solver resolve walls and gravity instead of teleporting the
        // dynamic capsule through collisions with MovePosition every step.
        Vector3 velocity = movement * currentSpeed;
        float verticalVelocity = rb.linearVelocity.y;
        if (TryGround(rb.position, groundProbeDistance, out RaycastHit ground) && Vector3.Dot(rb.linearVelocity, ground.normal) <= 0.5f)
        {
            velocity = Vector3.ProjectOnPlane(velocity, ground.normal).normalized * velocity.magnitude;
            float gap = Mathf.Max(0f, bodyCollider.bounds.min.y - ground.point.y);
            verticalVelocity = velocity.y - Mathf.Max(1f, gap / Time.fixedDeltaTime);
        }
        rb.linearVelocity = new Vector3(velocity.x, verticalVelocity, velocity.z);
    }
}
