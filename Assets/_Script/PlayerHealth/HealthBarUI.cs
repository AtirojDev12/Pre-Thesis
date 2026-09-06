using UnityEngine;
using UnityEngine.UI;
using TMPro; // ถ้าไม่ได้ใช้ TextMeshPro ให้ลบ using นี้ และลบส่วน healthText ด้านล่างออก

/// <summary>
/// สคริปต์แสดงหลอดเลือด (Health Bar) บน UI
/// วิธีใช้:
/// 1) สร้าง UI > Slider ใน Canvas (คลิกขวาใน Hierarchy > UI > Slider)
/// 2) ลบ Handle ของ Slider ออกได้ถ้าไม่ต้องการให้ลากได้ (เอาไว้โชว์อย่างเดียว)
/// 3) ใส่สคริปต์นี้ไว้ที่ Slider (หรือ GameObject ไหนก็ได้ที่จัดการ UI)
/// 4) ลาก Slider component และ PlayerHealth (จากตัวผู้เล่น) มาใส่ในช่องด้านล่าง
/// 5) (ถ้ามี) ลาก Text (TMP) มาใส่ช่อง Health Text เพื่อโชว์ตัวเลข เช่น 80/100
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [Header("อ้างอิงถึงระบบเลือดของผู้เล่น")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("UI Elements")]
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText; // optional, ลบได้ถ้าไม่ใช้

    [Header("สีของหลอดเลือด (optional)")]
    [SerializeField] private Image fillImage;
    [SerializeField] private Gradient healthGradient; // เขียว -> เหลือง -> แดง

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged.AddListener(UpdateHealthBar);
            // ตั้งค่าเริ่มต้นทันทีตอนเปิดฉาก
            UpdateHealthBar(playerHealth.CurrentHealth, playerHealth.MaxHealth);
        }
        else
        {
            Debug.LogWarning("[HealthBarUI] ยังไม่ได้ลาก PlayerHealth มาใส่ใน Inspector");
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged.RemoveListener(UpdateHealthBar);
        }
    }

    private void UpdateHealthBar(float current, float max)
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = max;
            healthSlider.value = current;
        }

        if (healthText != null)
        {
            healthText.text = $"{Mathf.Ceil(current)} / {Mathf.Ceil(max)}";
        }

        if (fillImage != null && healthGradient != null)
        {
            float ratio = max > 0 ? current / max : 0f;
            fillImage.color = healthGradient.Evaluate(ratio);
        }
    }
}
