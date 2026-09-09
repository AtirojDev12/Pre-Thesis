using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse look for the local player.
///
/// This stays a plain MonoBehaviour on purpose. It usually lives on a child
/// object (a camera pivot), and Mirror expects NetworkBehaviours to sit on the
/// same GameObject as the NetworkIdentity -- so instead of converting it, it
/// looks up the owning player's NetworkIdentity in its parents and asks that.
///
/// The remote-player handling below matters more than it looks: every client
/// spawns a copy of the player prefab for EVERY connected player. Six players
/// meant six cameras all rendering, six AudioListeners fighting over the audio,
/// and six scripts all locking the cursor and turning their own bodies from one
/// keyboard and mouse.
/// </summary>
public class FirstPersonCamera : MonoBehaviour
{
    [Header("Player")]
    public Transform playerBody;

    [Header("Mouse Settings")]
    public float mouseSensitivity = 0.1f;

    [Header("Camera Limits")]
    public float minLookAngle = -80f;
    public float maxLookAngle = 80f;

    private float verticalRotation = 0f;
    private NetworkIdentity _ownerIdentity;
    private bool _initialised;

    // No NetworkIdentity in the parents means this isn't a networked player at
    // all (a camera rig dropped straight into a test scene), so it belongs to
    // whoever is sitting at this machine.
    private bool IsLocal =>
        NetworkMode.IsOffline || _ownerIdentity == null || _ownerIdentity.isLocalPlayer;

    private void Awake()
    {
        _ownerIdentity = GetComponentInParent<NetworkIdentity>();
    }

    private void Start()
    {
        // In a networked match isLocalPlayer isn't reliable until the object has
        // been spawned and assigned, so this is finished lazily in Update. Offline
        // there is nothing to wait for.
        if (NetworkMode.IsOffline) Initialise();
    }

    private void Initialise()
    {
        if (_initialised) return;
        _initialised = true;

        if (IsLocal)
        {
            // ล็อกเมาส์ไว้กลางหน้าจอ
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            DisableRemoteViewpoint();
        }
    }

    /// <summary>
    /// Turns off the parts of another player's prefab that would otherwise take
    /// over this machine's screen and speakers.
    /// </summary>
    private void DisableRemoteViewpoint()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.enabled = false;

        AudioListener listener = GetComponent<AudioListener>();
        if (listener == null) listener = GetComponentInChildren<AudioListener>(true);
        if (listener != null) listener.enabled = false;

        enabled = false;
    }

    private void Update()
    {
        if (!_initialised)
        {
            // Wait until Mirror has actually spawned this object, because
            // isLocalPlayer is not meaningful before then. netId stays 0 until
            // the spawn message has been processed.
            bool readyToDecide = _ownerIdentity == null || _ownerIdentity.netId != 0;
            if (readyToDecide) Initialise();
            if (!_initialised) return;
        }

        if (!IsLocal) return;
        if (Mouse.current == null || playerBody == null) return;

        // รับค่าการขยับเมาส์
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // มองขึ้น / ลง
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, minLookAngle, maxLookAngle);
        transform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);

        // หันซ้าย / ขวา
        playerBody.Rotate(Vector3.up * mouseX);
    }
}
