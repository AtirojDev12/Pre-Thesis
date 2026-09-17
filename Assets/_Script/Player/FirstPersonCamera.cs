using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Local first-person camera rig. The pivot stays on the stable player
/// controller rather than an animated bone, preventing authored head motion
/// from being transferred directly to the viewer.
/// </summary>
public class FirstPersonCamera : MonoBehaviour
{
    private const string PlayerBodyLayerName = "LocalPlayerBody";
    private const string FirstPersonArmsLayerName = "FirstPersonArms";

    [Header("Player")]
    public Transform playerBody;

    [Header("Stable Camera Mount")]
    [Tooltip("Camera-pivot position relative to the non-animated player controller.")]
    [SerializeField] private Vector3 cameraLocalPosition = new Vector3(0f, 0.81f, 0.05f);

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

    [Header("First-Person Visibility")]
    [Tooltip("Renderers assigned here are shown only by this first-person camera. Leave empty when the character has no separate arms mesh.")]
    [SerializeField] private Renderer[] firstPersonArmRenderers;

    private float verticalRotation;
    private float downedHorizontalRotation;
    private float motionPhase;
    private Vector3 currentMotionOffset;
    private NetworkIdentity ownerIdentity;
    private PlayerHealth playerHealth;
    private PlayerStamina playerStamina;
    private Camera viewCamera;
    private bool initialised;
    private bool wasIncapacitated;
    private bool isMoving;
    private bool isSprinting;

    // A rig without a NetworkIdentity is a standalone test rig and therefore
    // belongs to this machine.
    private bool IsLocal =>
        NetworkMode.IsOffline || ownerIdentity == null || ownerIdentity.isLocalPlayer;

    private void Awake()
    {
        ownerIdentity = GetComponentInParent<NetworkIdentity>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        playerStamina = GetComponentInParent<PlayerStamina>();
        viewCamera = GetComponent<Camera>();
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>(true);

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

        if (Mouse.current != null && playerBody != null)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float mouseX = mouseDelta.x * mouseSensitivity;
            float mouseY = mouseDelta.y * mouseSensitivity;

            verticalRotation = Mathf.Clamp(
                verticalRotation - mouseY,
                minLookAngle,
                maxLookAngle);

            if (isIncapacitated)
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
        if (!isIncapacitated && Keyboard.current != null)
        {
            isMoving = Keyboard.current.wKey.isPressed
                || Keyboard.current.aKey.isPressed
                || Keyboard.current.sKey.isPressed
                || Keyboard.current.dKey.isPressed;
        }

        isSprinting = isMoving && playerStamina != null && playerStamina.IsSprinting;
        UpdateProceduralMotion();

        if (viewCamera != null)
        {
            float targetFOV = isSprinting ? sprintFOV : defaultFOV;
            float fovBlend = 1f - Mathf.Exp(-fovLerpSpeed * Time.deltaTime);
            viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, targetFOV, fovBlend);
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

        // The neutral local rotation is exactly forward. Only explicit look
        // input changes pitch/yaw; animation can no longer tilt or roll it.
        transform.localPosition = cameraLocalPosition + currentMotionOffset;
        transform.localRotation = Quaternion.Euler(
            verticalRotation,
            downedHorizontalRotation,
            0f);
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
    }
}
