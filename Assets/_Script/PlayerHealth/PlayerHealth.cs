using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ระบบเลือด (Health) ของผู้เล่น
/// - ใส่สคริปต์นี้ไว้ที่ตัวผู้เล่น (Player GameObject)
/// - เรียก TakeDamage() เพื่อลดเลือด, Heal() เพื่อเพิ่มเลือด
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("ค่าพลังชีวิต")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("การป้องกันโดนดาเมจรัว (กันชนแล้วเลือดหมดทันที)")]
    [SerializeField] private float invincibilityDuration = 0.5f;
    private float invincibleTimer = 0f;
    private bool isInvincible = false;

    [Header("Events (ผูกกับ UI หรือ effect อื่นๆ ได้)")]
    public UnityEvent<float, float> OnHealthChanged; // ส่ง (currentHealth, maxHealth)
    public UnityEvent OnDamaged;
    public UnityEvent OnDeath;

    private bool isDead = false;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Update()
    {
        // นับเวลาหมดสถานะไร้เทียมทาน
        if (isInvincible)
        {
            invincibleTimer -= Time.deltaTime;
            if (invincibleTimer <= 0f)
            {
                isInvincible = false;
            }
        }
    }

    public void TakeDamage(float amount)
    {
        if (isDead || isInvincible || amount <= 0f) return;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke();

        Debug.Log($"[PlayerHealth] โดนดาเมจ {amount} คะแนน เหลือเลือด {currentHealth}/{maxHealth}");

        // เปิดสถานะไร้เทียมทานชั่วคราว กันโดนดาเมจซ้ำในเฟรมเดียวกัน
        isInvincible = true;
        invincibleTimer = invincibilityDuration;

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;

        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        Debug.Log($"[PlayerHealth] ฮีลเลือด {amount} คะแนน เลือดตอนนี้ {currentHealth}/{maxHealth}");
    }

    private void Die()
    {
        isDead = true;
        Debug.Log("[PlayerHealth] ผู้เล่นตายแล้ว");
        OnDeath?.Invoke();
        // ใส่ logic เกมโอเวอร์ / รีสตาร์ท ที่นี่ได้
    }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;
}
