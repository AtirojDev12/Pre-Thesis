using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;

    [Header("Camera")]
    public Transform playerCamera;

    private Rigidbody rb;
    private Vector3 movement;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (Keyboard.current == null)
            return;

        // รับ Input
        float horizontal = 0f;
        float vertical = 0f;

        // A / D
        if (Keyboard.current.aKey.isPressed)
            horizontal = -1f;

        if (Keyboard.current.dKey.isPressed)
            horizontal = 1f;

        // W / S
        if (Keyboard.current.wKey.isPressed)
            vertical = 1f;

        if (Keyboard.current.sKey.isPressed)
            vertical = -1f;

        // --------------------------------
        // ทิศทางของกล้อง
        // --------------------------------

        Vector3 forward = playerCamera.forward;
        Vector3 right = playerCamera.right;

        // ไม่ให้มุมกล้องขึ้น/ลงมีผลกับการเดิน
        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        // --------------------------------
        // คำนวณทิศทางการเดิน
        // --------------------------------

        movement =
            forward * vertical +
            right * horizontal;

        // ป้องกันเดินเฉียงเร็วเกินไป
        movement = Vector3.ClampMagnitude(movement, 1f);
    }

    private void FixedUpdate()
    {
        Vector3 newPosition =
            rb.position +
            movement * moveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(newPosition);
    }
}