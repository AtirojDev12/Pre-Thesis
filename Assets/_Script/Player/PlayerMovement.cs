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
    [Header("Movement")]
    public float moveSpeed = 5f;

    [Header("Camera")]
    public Transform playerCamera;

    private Rigidbody rb;
    private Vector3 movement;
    private PlayerHealth playerHealth;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        playerHealth = GetComponent<PlayerHealth>();

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
        if (!NetworkMode.IsLocalController(this))
        {
            movement = Vector3.zero;
            return;
        }

        // Disable movement when downed or dead
        if (playerHealth != null && (playerHealth.IsDowned || playerHealth.IsDead))
        {
            movement = Vector3.zero;
            return;
        }

        if (Keyboard.current == null || playerCamera == null) return;

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
    }

    private void FixedUpdate()
    {
        if (!NetworkMode.IsLocalController(this)) return;
        if (rb == null || rb.isKinematic) return;

        rb.MovePosition(rb.position + movement * moveSpeed * Time.fixedDeltaTime);
    }
}
