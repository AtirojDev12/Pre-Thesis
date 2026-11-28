using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local first-person camera rig. The pivot stays on the stable player
/// controller rather than an animated bone, preventing authored head motion
/// from being transferred directly to the viewer.
/// </summary>
[RequireComponent(typeof(PlayerGhostWarningController))]
public class FirstPersonCamera : MonoBehaviour
{
    private const string PlayerBodyLayerName = "LocalPlayerBody";
    private const string FirstPersonArmsLayerName = "FirstPersonArms";

    [Header("Player")]
    public Transform playerBody;

    [Header("Stable Camera Mount")]
    [Tooltip("Camera-pivot position relative to the non-animated player controller.")]
    [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 0.9f, 0.05f);
    [SerializeField] private float crouchCameraDrop = 0.65f;
    [SerializeField] private float crouchCameraLerpSpeed = 9f;

    [Header("Mouse Settings")]
    public float mouseSensitivity = 0.1f;

    [Header("Camera Limits")]
    public float minLookAngle = -80f;
    public float maxLookAngle = 80f;

    [Tooltip("How far the camera may turn left/right while downed without rotating the body on the floor.")]
    [Range(0f, 180f)] public float downedHorizontalLookLimit = 80f;

    [Header("Procedural Camera Motion")]
    [Tooltip("Small vertical movement while walking, in metres.")]
    [Range(0f, 0.05f)] [SerializeField] private float walkBobAmplitude = 0.008f;

    [Tooltip("Walking bob animation speed.")]
    [Min(0f)] [SerializeField] private float walkBobSpeed = 7f;

    [Tooltip("Small left/right movement while sprinting, in metres.")]
    [Range(0f, 0.08f)] [SerializeField] private float sprintSwayAmplitude = 0.015f;

    [Tooltip("Sprint sway animation speed.")]
    [Min(0f)] [SerializeField] private float sprintSwaySpeed = 8.5f;

    [Tooltip("How smoothly procedural motion starts and settles back to centre.")]
    [Min(0f)] [SerializeField] private float motionLerpSpeed = 10f;

    [Header("Sprint FOV")]
    [Min(1f)] [SerializeField] private float defaultFOV = 60f;
    [Min(1f)] [SerializeField] private float sprintFOV = 75f;
    [Min(0f)] [SerializeField] private float fovLerpSpeed = 8f;

    [Header("Downed Fall Camera (28 Nov, Atiroj)")]
    [Tooltip("Animator state played when the player goes down / dies. The camera follows the head until this state ends.")]
    [SerializeField] private string fallStateName = "Dying";

    [Tooltip("How tightly the camera follows the head while falling (higher = tighter, lower = smoother).")]
    [Min(0f)] [SerializeField] private float fallFollowSharpness = 18f;

    [Tooltip("Eye position from the head bone, in the view's own space (x right, y up, z forward), metres.")]
    [SerializeField] private Vector3 fallEyeOffset = new Vector3(0f, 0.08f, 0.08f);

    [Tooltip("The camera never goes lower than this above the floor, so it cannot see through it.")]
    [Min(0f)] [SerializeField] private float fallMinFloorClearance = 0.2f;

    [Tooltip("Safety: lock the view on the floor after this many seconds even if the fall state never ends.")]
    [Min(0.5f)] [SerializeField] private float fallMaxSeconds = 8f;

    [Tooltip("Mouse look on the floor, around the view the fall ended on (degrees left/right).")]
    [Range(0f, 90f)] [SerializeField] private float floorLookYawLimit = 70f;

    [Tooltip("Mouse look on the floor, around the view the fall ended on (degrees up/down).")]
    [Range(0f, 90f)] [SerializeField] private float floorLookPitchLimit = 50f;

    [Tooltip("Seconds to rise back to eye height after a revive.")]
    [Min(0f)] [SerializeField] private float reviveRiseSeconds = 0.45f;

    [Header("First-Person Visibility")]
    [Tooltip("Renderers assigned here are shown only by this first-person camera. Leave empty when the character has no separate arms mesh.")]
    [SerializeField] private Renderer[] firstPersonArmRenderers;

    private float verticalRotation;
    private float downedHorizontalRotation;

    // ---- Spectator first-person view (3 Oct, Mr.k) --------------------------
    // Read-only. The owner's look angles are sent to everyone by PlayerVoice,
    // so a spectator can look through this player's eyes.
    /// <summary>Owner only: up/down look angle (degrees).</summary>
    public float LookPitch => verticalRotation;
    /// <summary>Owner only: extra left/right look while downed (degrees).</summary>
    public float LookYawOffset => downedHorizontalRotation;

    /// <summary>World position of this player's eyes, on any machine (crouch included).</summary>
    public Vector3 EyePosition(bool crouching)
    {
        if (playerBody == null) return transform.position;
        return playerBody.TransformPoint(cameraLocalPosition + Vector3.down * (crouching ? crouchCameraDrop : 0f));
    }
    private float motionPhase;
    private Vector3 currentMotionOffset;
    private NetworkIdentity ownerIdentity;
    private PlayerHealth playerHealth;
    private PlayerStamina playerStamina;
    private PlayerMovement playerMovement;
    private float crouchCameraOffset;
    private Camera viewCamera;
    private bool initialised;
    private bool wasIncapacitated;
    private bool isMoving;
    private bool isSprinting;
    private PlayerGhostWarningController ghostWarning;
    private float locomotionFov;

    // ---- Downed fall camera (28 Nov) -----------------------------------------
    // Standing: normal stable rig. Falling: follows the head bone through the fall
    // animation. OnFloor: the fall ended, the view stays still where it landed
    // (mouse can look around a little). Rising: smooth way back up after a revive.
    private enum DownView { Standing, Falling, OnFloor, Rising }
    private DownView downView = DownView.Standing;
    private Animator bodyAnimator;
    private Transform headBone;
    private int fallStateHash;
    private bool cullingOverridden;
    private AnimatorCullingMode savedCulling;
    private Quaternion fallHeadStart;
    private Quaternion fallViewStart;
    private float fallStartedAt;
    private Vector3 floorLocalPosition;
    private Quaternion floorLocalRotation;
    private float floorYaw;
    private float floorPitch;
    private Vector3 riseFromLocalPosition;
    private Quaternion riseFromLocalRotation;
    private float riseStartedAt;
    private bool warnedNoHead;
    private bool noHeadThisFall; // do not search the body again every frame
    private readonly RaycastHit[] floorHits = new RaycastHit[8];

    // A rig without a NetworkIdentity is a standalone test rig and therefore
    // belongs to this machine.
    private bool IsLocal =>
        NetworkMode.IsOffline || ownerIdentity == null || ownerIdentity.isLocalPlayer;

    private void Awake()
    {
        ownerIdentity = GetComponentInParent<NetworkIdentity>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        playerStamina = GetComponentInParent<PlayerStamina>();
        playerMovement = GetComponentInParent<PlayerMovement>();
        viewCamera = GetComponent<Camera>();
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);
        ghostWarning = GetComponent<PlayerGhostWarningController>();
        locomotionFov = defaultFOV;
        fallStateHash = Animator.StringToHash(fallStateName);
    }

    private void Start()
    {
        // In a networked match isLocalPlayer is not reliable until Mirror has
        // spawned and assigned this object. Offline has nothing to wait for.
        if (NetworkMode.IsOffline) Initialise();
    }

    private void Initialise()
    {
        if (initialised) return;
        initialised = true;

        if (!IsLocal)
        {
            DisableRemoteViewpoint();
            return;
        }

        ConfigureStableRig();
        ConfigureFirstPersonVisibility();

        if (viewCamera != null) viewCamera.fieldOfView = defaultFOV;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ConfigureStableRig()
    {
        if (playerBody == null)
        {
            Debug.LogWarning(
                $"{nameof(FirstPersonCamera)} on {name} needs a Player Body transform.",
                this);
            return;
        }

        // CameraPivot is a sibling of the visual model. Keeping it under the
        // non-animated controller makes it follow locomotion without inheriting
        // head, neck, idle, or running animation motion.
        transform.SetParent(playerBody, false);
        transform.localPosition = cameraLocalPosition;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // The Camera is normally a child of this pivot. Its old fixed-height
        // offset must be cleared now that the pivot itself sits at eye level.
        if (viewCamera != null && viewCamera.transform != transform)
        {
            viewCamera.transform.localPosition = Vector3.zero;
            viewCamera.transform.localRotation = Quaternion.identity;
            viewCamera.transform.localScale = Vector3.one;
        }

    }

    private void ConfigureFirstPersonVisibility()
    {
        if (viewCamera == null) return;

        int bodyLayer = LayerMask.NameToLayer(PlayerBodyLayerName);
        int armsLayer = LayerMask.NameToLayer(FirstPersonArmsLayerName);

        if (bodyLayer < 0)
        {
            Debug.LogWarning($"Layer '{PlayerBodyLayerName}' is missing; the local body cannot be camera-culled.", this);
            return;
        }

        HashSet<Renderer> arms = new HashSet<Renderer>();
        if (firstPersonArmRenderers != null)
        {
            foreach (Renderer armRenderer in firstPersonArmRenderers)
            {
                if (armRenderer == null) continue;
                arms.Add(armRenderer);
                if (armsLayer >= 0) armRenderer.gameObject.layer = armsLayer;
            }
        }

        Transform characterRoot = ownerIdentity != null
            ? ownerIdentity.transform
            : (playerBody != null ? playerBody : transform.root);

        foreach (Renderer characterRenderer in characterRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!arms.Contains(characterRenderer))
                characterRenderer.gameObject.layer = bodyLayer;
        }

        // Hide this client's full body, but retain explicitly separated arms.
        viewCamera.cullingMask &= ~(1 << bodyLayer);
        if (armsLayer >= 0)
        {
            // Keep the arms layer exclusive to the owning first-person view.
            foreach (Camera otherCamera in Camera.allCameras)
            {
                if (otherCamera != null && otherCamera != viewCamera)
                    otherCamera.cullingMask &= ~(1 << armsLayer);
            }

            viewCamera.cullingMask |= 1 << armsLayer;
        }
    }

    /// <summary>Disables cameras and listeners belonging to other players.</summary>
    private void DisableRemoteViewpoint()
    {
        if (viewCamera != null) viewCamera.enabled = false;

        AudioListener listener = GetComponent<AudioListener>();
        if (listener == null) listener = GetComponentInChildren<AudioListener>(true);
        if (listener != null) listener.enabled = false;

        enabled = false;
    }

    private void Update()
    {
        if (!initialised)
        {
            bool readyToDecide = ownerIdentity == null || ownerIdentity.netId != 0;
            if (readyToDecide) Initialise();
            if (!initialised) return;
        }

        if (!IsLocal) return;

        bool isIncapacitated = playerHealth != null
            && (playerHealth.IsDowned || playerHealth.IsDead);

        // The popcorn maker can temporarily release the cursor for direct
        // world-space UI clicks. Do not rotate the camera while using it.
        if (Mouse.current != null && playerBody != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            // GameSettings.MouseSensitivityScale is the player's own setting
            // (Settings menu). It multiplies the designer's value, so tuning
            // mouseSensitivity on the prefab still works.
            float sensitivity = mouseSensitivity * GameSettings.MouseSensitivityScale;
            float mouseX = mouseDelta.x * sensitivity;
            float mouseY = mouseDelta.y * sensitivity;

            // 28 Nov: on the floor the mouse moves the floor view instead (below), so the
            // standing pitch is kept for after the revive.
            if (!isIncapacitated || downView == DownView.Standing || downView == DownView.Rising)
                verticalRotation = Mathf.Clamp(
                    verticalRotation - mouseY,
                    minLookAngle,
                    maxLookAngle);

            if (isIncapacitated && downView == DownView.Falling)
            {
                // 28 Nov: the fall animation owns the view until it ends.
                wasIncapacitated = true;
            }
            else if (isIncapacitated && downView == DownView.OnFloor)
            {
                // 28 Nov: look around a little from where the head landed.
                floorYaw = Mathf.Clamp(floorYaw + mouseX, -floorLookYawLimit, floorLookYawLimit);
                floorPitch = Mathf.Clamp(floorPitch - mouseY, -floorLookPitchLimit, floorLookPitchLimit);
                wasIncapacitated = true;
            }
            else if (isIncapacitated)
            {
                downedHorizontalRotation = Mathf.Clamp(
                    downedHorizontalRotation + mouseX,
                    -downedHorizontalLookLimit,
                    downedHorizontalLookLimit);
                wasIncapacitated = true;
            }
            else
            {
                if (wasIncapacitated)
                {
                    playerBody.Rotate(Vector3.up * downedHorizontalRotation);
                    downedHorizontalRotation = 0f;
                    wasIncapacitated = false;
                }

                playerBody.Rotate(Vector3.up * mouseX);
            }
        }

        isMoving = false;
        if (!isIncapacitated && !GameplayInput.Blocked)
        {
            isMoving = Keybinds.IsPressed(GameAction.MoveForward)
                || Keybinds.IsPressed(GameAction.MoveLeft)
                || Keybinds.IsPressed(GameAction.MoveBack)
                || Keybinds.IsPressed(GameAction.MoveRight);
        }

        isSprinting = isMoving && playerStamina != null && playerStamina.IsSprinting;
        UpdateProceduralMotion();

        if (viewCamera != null)
        {
            float targetFOV = isSprinting ? sprintFOV : defaultFOV;
            float fovBlend = 1f - Mathf.Exp(-fovLerpSpeed * Time.deltaTime);
            locomotionFov = Mathf.Lerp(locomotionFov, targetFOV, fovBlend);
            viewCamera.fieldOfView = Mathf.Clamp(locomotionFov -
                (ghostWarning != null ? ghostWarning.FovReduction : 0f), 1f, 179f);
        }
    }

    private void UpdateProceduralMotion()
    {
        Vector3 targetOffset = Vector3.zero;

        if (isMoving)
        {
            float speed = isSprinting ? sprintSwaySpeed : walkBobSpeed;
            motionPhase += speed * Time.deltaTime;

            if (isSprinting)
            {
                // Sprinting uses only a restrained lateral sway. Avoiding a
                // vertical bounce makes the faster movement easier on the eyes.
                targetOffset.x = Mathf.Sin(motionPhase) * sprintSwayAmplitude;
            }
            else
            {
                targetOffset.y = Mathf.Sin(motionPhase) * walkBobAmplitude;
            }
        }

        float motionBlend = 1f - Mathf.Exp(-motionLerpSpeed * Time.deltaTime);
        currentMotionOffset = Vector3.Lerp(currentMotionOffset, targetOffset, motionBlend);
    }

    private void LateUpdate()
    {
        if (!initialised || !IsLocal) return;

        bool incapacitated = playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead);
        UpdateDownView(incapacitated);
        if (downView == DownView.Falling) { FollowFall(); return; }
        if (downView == DownView.OnFloor)
        {
            transform.localPosition = floorLocalPosition;
            transform.localRotation = floorLocalRotation * Quaternion.Euler(floorPitch, floorYaw, 0f);
            return;
        }

        // The neutral local rotation is exactly forward. Only explicit look
        // input changes pitch/yaw; animation can no longer tilt or roll it.
        float targetDrop = playerMovement != null && playerMovement.IsCrouching ? crouchCameraDrop : 0f;
        crouchCameraOffset = Mathf.Lerp(crouchCameraOffset, targetDrop,
            1f - Mathf.Exp(-crouchCameraLerpSpeed * Time.deltaTime));
        Vector3 standPosition = cameraLocalPosition + currentMotionOffset + Vector3.down * crouchCameraOffset;
        Quaternion standRotation = Quaternion.Euler(
            verticalRotation,
            downedHorizontalRotation,
            0f);

        if (downView == DownView.Rising)
        {
            // 28 Nov: smooth way back up to eye height after a revive.
            float t = reviveRiseSeconds <= 0f ? 1f : Mathf.Clamp01((Time.time - riseStartedAt) / reviveRiseSeconds);
            float s = Mathf.SmoothStep(0f, 1f, t);
            transform.localPosition = Vector3.Lerp(riseFromLocalPosition, standPosition, s);
            transform.localRotation = Quaternion.Slerp(riseFromLocalRotation, standRotation, s);
            if (t >= 1f) downView = DownView.Standing;
            return;
        }

        transform.localPosition = standPosition;
        transform.localRotation = standRotation;
    }

    // ---- Downed fall camera (28 Nov) -----------------------------------------

    private void UpdateDownView(bool incapacitated)
    {
        if (!incapacitated) noHeadThisFall = false;
        if (incapacitated)
        {
            if (downView == DownView.Standing || downView == DownView.Rising)
            {
                // No humanoid head found: keep the old still camera at eye height.
                if (noHeadThisFall) return;
                if (!FindHead()) { noHeadThisFall = true; return; }
                BeginFall();
            }
            if (downView == DownView.Falling && FallFinished()) LandOnFloor();
        }
        else if (downView == DownView.Falling || downView == DownView.OnFloor)
        {
            BeginRise();
        }
    }

    private bool FindHead()
    {
        // Looked up at every fall: the body model can be swapped during the game.
        bodyAnimator = null;
        headBone = null;
        Transform root = ownerIdentity != null
            ? ownerIdentity.transform
            : (playerBody != null ? playerBody : transform.root);
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            if (animator == null || !animator.isHuman || animator.avatar == null) continue;
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) continue;
            bodyAnimator = animator;
            headBone = head;
            return true;
        }
        if (!warnedNoHead)
        {
            warnedNoHead = true;
            Debug.LogWarning($"{nameof(FirstPersonCamera)}: no humanoid head bone on {root.name}; the downed camera stays at eye height.", this);
        }
        return false;
    }

    private void BeginFall()
    {
        downView = DownView.Falling;
        fallStartedAt = Time.time;
        fallHeadStart = headBone.rotation;
        fallViewStart = transform.rotation;
        downedHorizontalRotation = 0f; // no body turn to apply after the revive
        floorYaw = 0f;
        floorPitch = 0f;
        // The local body is hidden from this camera, so a culling Animator could stop
        // moving the bones. Keep it animating while we follow the head.
        if (bodyAnimator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
        {
            savedCulling = bodyAnimator.cullingMode;
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            cullingOverridden = true;
        }
    }

    private bool FallFinished()
    {
        if (bodyAnimator == null || headBone == null) return true;
        if (Time.time - fallStartedAt >= fallMaxSeconds) return true;
        if (bodyAnimator.IsInTransition(0)) return false;
        AnimatorStateInfo state = bodyAnimator.GetCurrentAnimatorStateInfo(0);
        return state.shortNameHash == fallStateHash && state.normalizedTime >= 1f;
    }

    /// <summary>The view follows how the head turns and moves during the fall animation.</summary>
    private void FollowFall()
    {
        if (headBone == null) { LandOnFloor(); return; }
        // Only the head's change since the fall began is applied, so the bone's own axes do not matter.
        Quaternion headTurn = headBone.rotation * Quaternion.Inverse(fallHeadStart);
        Quaternion wantedRotation = headTurn * fallViewStart;
        Vector3 wantedPosition = KeepAboveFloor(headBone.position + wantedRotation * fallEyeOffset);
        float blend = 1f - Mathf.Exp(-fallFollowSharpness * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, wantedPosition, blend),
            Quaternion.Slerp(transform.rotation, wantedRotation, blend));
    }

    /// <summary>The fall animation ended: the view stays still where it landed.</summary>
    private void LandOnFloor()
    {
        downView = DownView.OnFloor;
        floorLocalPosition = transform.localPosition;
        floorLocalRotation = transform.localRotation;
        floorYaw = 0f;
        floorPitch = 0f;
        RestoreAnimatorCulling();
    }

    private void BeginRise()
    {
        downView = DownView.Rising;
        riseFromLocalPosition = transform.localPosition;
        riseFromLocalRotation = transform.localRotation;
        riseStartedAt = Time.time;
        floorYaw = 0f;
        floorPitch = 0f;
        RestoreAnimatorCulling();
    }

    private void RestoreAnimatorCulling()
    {
        if (cullingOverridden && bodyAnimator != null) bodyAnimator.cullingMode = savedCulling;
        cullingOverridden = false;
    }

    private Vector3 KeepAboveFloor(Vector3 point)
    {
        Transform root = ownerIdentity != null
            ? ownerIdentity.transform
            : (playerBody != null ? playerBody : transform.root);
        Vector3 origin = point + Vector3.up * 0.4f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, floorHits, 2f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float floor = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider hit = floorHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(root)) continue; // own body / capsule
            floor = Mathf.Max(floor, floorHits[i].point.y);
        }
        if (!float.IsNegativeInfinity(floor) && point.y < floor + fallMinFloorClearance)
            point.y = floor + fallMinFloorClearance;
        return point;
    }

    private void OnDisable()
    {
        RestoreAnimatorCulling();
    }

    private void OnValidate()
    {
        if (maxLookAngle < minLookAngle)
            maxLookAngle = minLookAngle;

        defaultFOV = Mathf.Clamp(defaultFOV, 1f, 179f);
        sprintFOV = Mathf.Clamp(sprintFOV, 1f, 179f);
        fovLerpSpeed = Mathf.Max(0f, fovLerpSpeed);
        walkBobSpeed = Mathf.Max(0f, walkBobSpeed);
        sprintSwaySpeed = Mathf.Max(0f, sprintSwaySpeed);
        motionLerpSpeed = Mathf.Max(0f, motionLerpSpeed);
        fallStateHash = Animator.StringToHash(fallStateName);
    }
}
