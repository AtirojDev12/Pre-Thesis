using Mirror;
using UnityEngine;

/// <summary>
/// สคริปต์ทดสอบ: เมื่อผู้เล่นเดินชนวัตถุที่มีสคริปต์นี้ จะทำให้เลือดผู้เล่นลด
///
/// วิธีใช้:
/// 1) ใส่สคริปต์นี้ไว้ที่วัตถุที่ต้องการให้เป็น "ตัวสร้างความเสียหาย" เช่น กับดัก, ศัตรู, หนาม
/// 2) วัตถุนั้นต้องมี Collider ติดอยู่ (ถ้าจะให้เดินทะลุผ่านได้ ให้ติ๊ก "Is Trigger" ที่ Collider ด้วย)
/// 3) ฝั่งผู้เล่นต้องมี Collider + Rigidbody และมีสคริปต์ PlayerHealth.cs ติดอยู่
/// 4) ตั้ง Tag ของผู้เล่นเป็น "Player"
///
/// IMPORTANT: physics runs independently on every machine, so this deals damage
/// on the SERVER only. Without that guard, six clients would each detect the
/// same collision and apply the same hit, or a client could simply decide it
/// never touched the trap.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DamageOnCollision : MonoBehaviour
{
    [Header("ตั้งค่าความเสียหาย")]
    [SerializeField] private float damageAmount = 10f;

    [Tooltip("ถ้าเปิดไว้ วัตถุนี้จะถูกทำลายหลังชนผู้เล่นครั้งเดียว (เหมาะกับกับดักแบบใช้ครั้งเดียว)")]
    [SerializeField] private bool destroyOnHit = false;

    // ---------- Collider ปกติ (ไม่ติ๊ก Is Trigger) ----------
    private void OnCollisionEnter(Collision collision) => TryDealDamage(collision.gameObject);

    // ---------- Collider แบบ Trigger (ติ๊ก Is Trigger) ----------
    private void OnTriggerEnter(Collider other) => TryDealDamage(other.gameObject);

    // (The 2D physics callbacks that used to be here were removed -- this is a
    // 3D first-person project, so they could never fire.)

    private void TryDealDamage(GameObject other)
    {
        // Server-only, or a solo scene with no networking running at all.
        if (!NetworkMode.IsOffline && !NetworkServer.active) return;

        if (!other.CompareTag("Player")) return;

        // GetComponentInParent, not GetComponent: the collider that touched us
        // is often a child of the player root that owns PlayerHealth.
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
        {
            Debug.LogWarning("[DamageOnCollision] ผู้เล่นไม่มีสคริปต์ PlayerHealth ติดอยู่", other);
            return;
        }

        playerHealth.TakeDamage(damageAmount);

        if (destroyOnHit) DestroySelf();
    }

    private void DestroySelf()
    {
        if (NetworkMode.IsOffline)
        {
            Destroy(gameObject);
            return;
        }

        // A plain Destroy() on the server removes the trap on the server only --
        // every client would still see (and collide with) a ghost of it.
        if (TryGetComponent(out NetworkIdentity _))
        {
            NetworkServer.Destroy(gameObject);
        }
        else
        {
            Debug.LogWarning(
                $"[DamageOnCollision] '{name}' has destroyOnHit enabled but no NetworkIdentity, so it can only be " +
                "destroyed on the server and will still exist for every client. Add a NetworkIdentity.", this);
            Destroy(gameObject);
        }
    }
}
