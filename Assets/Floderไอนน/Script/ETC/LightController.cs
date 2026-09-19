using UnityEngine;
// เพิ่มการเรียกใช้งานระบบ Input System ใหม่
using UnityEngine.InputSystem; 

public class LightController : MonoBehaviour
{
    // ช่องสำหรับลากแสงไฟ (Light) หลายๆ ดวงมาใส่ใน Unity Inspector
    [SerializeField] private Light[] sceneLights;

    // ตัวแปรเก็บสถานะปัจจุบันว่าไฟเปิดหรือปิดอยู่
    private bool isLightOn = true;

    void Update()
    {
        // ใช้ระบบ Input System ใหม่: ตรวจสอบว่าปุ่ม L บนคีย์บอร์ดถูกกดในเฟรมนี้ไหม
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            ToggleLights();
        }
    }

    // ฟังก์ชันสำหรับสลับสถานะเปิด-ปิดไฟทั้งหมดในรายการ
    public void ToggleLights()
    {
        isLightOn = !isLightOn;

        foreach (Light light in sceneLights)
        {
            if (light != null)
            {
                light.enabled = isLightOn;
            }
        }

        // 💡 [เพิ่มโค้ดใหม่] ส่งค่าสถานะไฟ (true/false) ไปอัปเดตที่ GhostManager 💡
        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.PlayerToggleLights(isLightOn);
        }
    }

    public void TurnOffAllLights()
    {
        isLightOn = false;
        foreach (Light light in sceneLights)
        {
            if (light != null)
            {
                light.enabled = false;
            }
        }

        // 💡 [เพิ่มโค้ดใหม่] ส่งสัญญาณบอก GhostManager ว่าไฟดับหมดแล้ว 💡
        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.PlayerToggleLights(false);
        }
    }

    public void TurnOnAllLights()
    {
        isLightOn = true;
        foreach (Light light in sceneLights)
        {
            if (light != null)
            {
                light.enabled = true;
            }
        }

        // 💡 [เพิ่มโค้ดใหม่] ส่งสัญญาณบอก GhostManager ว่าไฟเปิดหมดแล้ว 💡
        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.PlayerToggleLights(true);
        }
    }
}
