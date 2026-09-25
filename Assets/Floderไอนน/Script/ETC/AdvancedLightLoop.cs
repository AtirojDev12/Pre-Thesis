using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AdvancedLightLoop : MonoBehaviour
{
    [Header("ใส่หลอดไฟที่ต้องการควบคุม (ใส่ได้หลายดวง)")]
    [SerializeField] private List<Light> lightGroup = new List<Light>();

    [Header("ตั้งค่าระบบเสียง")]
    [Tooltip("ลากคอมโพเนนต์ AudioSource ที่อยู่ในวัตถุมาใส่ที่นี่")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("ใส่ไฟล์เสียงเอฟเฟกต์ไฟช็อต/ไฟตก (.mp3, .wav)")]
    [SerializeField] private AudioClip sparkSound;

    [Header("ตั้งค่าการกระพริบ")]
    [Tooltip("จำนวนครั้งที่ไฟจะดับ-เปิด (กระพริบ) ใน 1 รอบลูป")]
    [SerializeField] private int blinkCountPerLoop = 3;
    
    [Tooltip("ระยะเวลาที่ไฟ ดับ หรือ เปิด ในจังหวะกระพริบ (วินาที)")]
    [SerializeField] private float blinkDuration = 0.2f;

    [Tooltip("ระยะเวลาที่ไฟจะเปิดติดค้างไว้ตลอด ก่อนที่จะเริ่มกระพริบรอบใหม่ (วินาที)")]
    [SerializeField] private float delayBetweenLoops = 5f;

    private void Start()
    {
        // ตรวจสอบเช็ค AudioSource อัตโนมัติถ้าไม่ได้ลากใส่ไว้
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        // สั่งเปิดไฟทุกดวงเตรียมไว้ตั้งแต่เริ่มเกม
        SetAllLights(true);
        
        // เริ่มทำงานลูปไฟกระพริบ
        StartCoroutine(BlinkLoopRoutine());
    }

    private IEnumerator BlinkLoopRoutine()
    {
        while (true)
        {
            // 1. ช่วงเวลารอคอย: ไฟจะเปิดติดค้างไว้
            SetAllLights(true);
            yield return new WaitForSeconds(delayBetweenLoops);

            // 2. ช่วงเวลากระพริบและเล่นเสียง
            for (int i = 0; i < blinkCountPerLoop; i++)
            {
                // สั่งดับไฟ
                SetAllLights(false);

                // เล่นเสียงไฟตก/ไฟช็อต ทันทีที่ไฟดับ (ถ้ามีไฟล์เสียง)
                if (audioSource != null && sparkSound != null)
                {
                    audioSource.PlayOneShot(sparkSound);
                }
                
                yield return new WaitForSeconds(blinkDuration);

                // สั่งเปิดไฟขึ้นมา
                SetAllLights(true);
                yield return new WaitForSeconds(blinkDuration);
            }
        }
    }

    // ฟังก์ชันสำหรับเปิด/ปิดไฟทุกดวงใน List พร้อม ๆ กัน
    private void SetAllLights(bool state)
    {
        foreach (Light lightSource in lightGroup)
        {
            if (lightSource != null)
            {
                lightSource.enabled = state;
            }
        }
    }
}
