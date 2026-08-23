using UnityEngine;
using UnityEngine.InputSystem;

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

    private void Start()
    {
        // ล็อกเมาส์ไว้กลางหน้าจอ
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (Mouse.current == null)
            return;

        // รับค่าการขยับเมาส์
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX = mouseDelta.x * mouseSensitivity;
        float mouseY = mouseDelta.y * mouseSensitivity;

        // -------------------------
        // มองขึ้น / ลง
        // -------------------------

        verticalRotation -= mouseY;

        verticalRotation = Mathf.Clamp(
            verticalRotation,
            minLookAngle,
            maxLookAngle
        );

        transform.localRotation =
            Quaternion.Euler(verticalRotation, 0f, 0f);

        // -------------------------
        // หันซ้าย / ขวา
        // -------------------------

        playerBody.Rotate(
            Vector3.up * mouseX
        );
    }
}