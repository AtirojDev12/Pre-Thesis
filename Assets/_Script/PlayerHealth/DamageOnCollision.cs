using UnityEngine;

/// <summary>
/// สคริปต์ทดสอบ: เมื่อผู้เล่นเดินชนวัตถุที่มีสคริปต์นี้ จะทำให้เลือดผู้เล่นลด
/// วิธีใช้:
/// 1) ใส่สคริปต์นี้ไว้ที่วัตถุที่ต้องการให้เป็น "ตัวสร้างความเสียหาย" เช่น กับดัก, ศัตรู, หนาม
/// 2) วัตถุนั้นต้องมี Collider ติดอยู่ (ถ้าจะให้เดินทะลุผ่านได้ ให้ติ๊ก "Is Trigger" ที่ Collider ด้วย)
/// 3) ฝั่งผู้เล่นต้องมี Collider + Rigidbody (หรือ Rigidbody2D) และมีสคริปต์ PlayerHealth.cs ติดอยู่
/// 4) ตั้ง Tag ของผู้เล่นเป็น "Player" (Unity มี Tag นี้ให้อยู่แล้ว)
/// </summary>
[RequireComponent(typeof(Collider))]
public class DamageOnCollision : MonoBehaviour
{
    [Header("ตั้งค่าความเสียหาย")]
    [SerializeField] private float damageAmount = 10f;

    [Tooltip("ถ้าเปิดไว้ วัตถุนี้จะถูกทำลายหลังชนผู้เล่นครั้งเดียว (เหมาะกับกับดักแบบใช้ครั้งเดียว)")]
    [SerializeField] private bool destroyOnHit = false;

    // ---------- กรณีใช้ Collider ปกติ (ไม่ติ๊ก Is Trigger) ----------
    private void OnCollisionEnter(Collision collision)
    {
        TryDealDamage(collision.gameObject);
    }

    // ---------- กรณีใช้ Collider แบบ Trigger (ติ๊ก Is Trigger) ----------
    private void OnTriggerEnter(Collider other)
    {
        TryDealDamage(other.gameObject);
    }

    // ---------- รองรับกรณีเป็นเกม 2D ด้วย ----------
    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryDealDamage(collision.gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDealDamage(other.gameObject);
    }

    private void TryDealDamage(GameObject other)
    {
        // เช็คว่าสิ่งที่มาชนคือผู้เล่นหรือไม่ (เช็คจาก Tag)
        if (!other.CompareTag("Player")) return;

        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth == null)
        {
            Debug.LogWarning("[DamageOnCollision] ผู้เล่นไม่มีสคริปต์ PlayerHealth ติดอยู่");
            return;
        }

        playerHealth.TakeDamage(damageAmount);

        if (destroyOnHit)
        {
            Destroy(gameObject);
        }
    }
}
