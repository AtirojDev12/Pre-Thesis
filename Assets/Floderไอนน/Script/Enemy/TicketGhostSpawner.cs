using UnityEngine;
using Mirror;

public class TicketGhostSpawner : NetworkBehaviour
{
    [Header("Ghost Setup")]
    [Tooltip("ลากไฟล์ Prefab สีฟ้าของผี TicketPunisherGhost มาใส่ที่ช่องนี้")]
    [SerializeField] private GameObject ticketGhostPrefab;

    [Header("Spawn Position")]
    [Tooltip("ลากวัตถุจุดเกิดหลังตู้ตั๋วที่จมดินอยู่มาใส่ช่องนี้ (ถ้าปล่อยว่าง มันจะเกิด ณ ตำแหน่งของวัตถุนี้เอง)")]
    [SerializeField] private Transform spawnPoint;

    [Header("Respawn Delay")]
    [Tooltip("ระยะเวลาหน่วงก่อนจะเสกผีตัวใหม่ขึ้นมาแทนที่ตัวเดิมที่เพิ่งทำลายไป (วินาที)")]
    [SerializeField] private float respawnDelay = 2.0f;

    private void Start()
    {
        // 💡 [จุดแก้ไขปลดล็อกบั๊กผีไม่ยอมเกิด] 💡
        // สั่งให้ผีสปอนออกมาได้ทันที ถ้าเครื่องนี้เปิดรันออนไลน์เป็น Server หรือเป็นการกด Play เล่นเกมแบบออฟไลน์คนเดียวปกติใน Unity Editor
        if (NetworkServer.active || Application.isEditor)
        {
            SpawnNewGhost();
        }
    }

    // ฟังก์ชันสั่งเสกผีแดงตัวใหม่โผล่มาสถิตใต้ดิน
    public void SpawnNewGhost()
    {
        if (ticketGhostPrefab == null) return;

        // ดึงพิกัดจุดเกิดที่ตั้งไว้ (ถ้าไม่ได้ลากใส่ ให้เกิดตรงตัวสปอว์นเนอร์ชิ้นนี้เลย)
        Vector3 pos = (spawnPoint != null) ? spawnPoint.position : transform.position;
        Quaternion rot = (spawnPoint != null) ? spawnPoint.rotation : transform.rotation;

        // 1. สั่ง Instantiate เสกโครงร่างผีตัวใหม่ขึ้นมาในด่าน
        GameObject newGhost = Instantiate(ticketGhostPrefab, pos, rot);
        
        // 2. ตรวจเช็คระบบเน็ตเวิร์ก: ถ้ามีการเปิดห้องออนไลน์รันอยู่จริงๆ ค่อยใช้คำสั่งซิงค์ข้ามจอของ Mirror
        if (NetworkServer.active)
        {
            NetworkServer.Spawn(newGhost);
            Debug.Log("[Ticket Spawner] สั่งกระจายวัตถุผีลงทัณฑ์เข้าสู่ระบบออนไลน์ของ Mirror สำเร็จ!");
        }
        else
        {
            Debug.Log("[Ticket Spawner] โหมดเทสออฟไลน์: เสกผีแดงลงทัณฑ์มาสแตนด์บายหลังตู้ตั๋วสำเร็จ!");
        }
    }

    // ฟังก์ชันรอรับสัญญาณแจ้งตายจากตัวผี เพื่อเริ่มนับเวลาชุบชีวิตตัวใหม่
    public void NotifyGhostDestroyed()
    {
        // ใช้คำสั่งหน่วงเวลาตามวินาทีที่ตั้งไว้ แล้วค่อยเสกตัวใหม่ขึ้นมาสแตนด์บายทดแทนตัวเก่าถาวร
        Invoke(nameof(SpawnNewGhost), respawnDelay);
    }
}
