using UnityEngine;
using UnityEngine.InputSystem; // เพิ่มบรรทัดนี้เข้ามา

public class DoorController : MonoBehaviour
{
    public Animator doorAnimator;
    private bool isPlayerNear = false;
    private bool isOpen = false;

    void Update()
    {
        // เปลี่ยนมาเช็กการกดปุ่ม E ผ่านระบบ New Input System
        if (isPlayerNear && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            isOpen = !isOpen;
            doorAnimator.SetBool("isOpen", isOpen);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerNear = false;
        }
    }
}
