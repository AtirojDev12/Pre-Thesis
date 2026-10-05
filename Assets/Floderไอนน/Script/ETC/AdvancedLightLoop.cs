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

    [Tooltip("One short clip per blink, in order (U1, U2, U3). Falls back to sparkSound.")]
    [SerializeField] private AudioClip[] flickerSounds;
    [SerializeField] private AudioClip runningSound;
    [SerializeField, Range(0, 1)] private float runningVolume = 0.35f;
    [SerializeField, Range(0, 1)] private float flickerVolume = 0.8f;
    [Tooltip("Seconds from each clip's start to its audible hit. Usually zero.")]
    [SerializeField] private float[] hitOffsets;
    private AudioSource runningSource;
    private SoundCategoryVolume runningCategory;
    private Coroutine routine;

    [Header("ตั้งค่าการกระพริบ")]
    [Tooltip("จำนวนครั้งที่ไฟจะดับ-เปิด (กระพริบ) ใน 1 รอบลูป")]
    [SerializeField] private int blinkCountPerLoop = 3;
    
    [Tooltip("ระยะเวลาที่ไฟ ดับ หรือ เปิด ในจังหวะกระพริบ (วินาที)")]
    [SerializeField] private float blinkDuration = 0.2f;

    [Tooltip("ระยะเวลาที่ไฟจะเปิดติดค้างไว้ตลอด ก่อนที่จะเริ่มกระพริบรอบใหม่ (วินาที)")]
    [SerializeField] private float delayBetweenLoops = 5f;

    private void OnEnable()
    {
        if (Application.isBatchMode) return;
        // ตรวจสอบเช็ค AudioSource อัตโนมัติถ้าไม่ได้ลากใส่ไว้
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if (runningSound != null)
        {
            if (runningSource == null)
            {
                var emitter = new GameObject("Lamp Hum");
                emitter.transform.SetParent(transform, false);
                runningSource = emitter.AddComponent<AudioSource>();
                runningSource.playOnAwake = false;
                runningSource.spatialBlend = 0; // Menu background, independent of camera distance.
                runningSource.loop = true;
                runningCategory = emitter.AddComponent<SoundCategoryVolume>();
                runningCategory.Category = SoundCategory.Ambient;
            }
            runningSource.clip = runningSound;
            runningCategory.SetBaseVolume(runningVolume);
            runningSource.Play();
        }

        // สั่งเปิดไฟทุกดวงเตรียมไว้ตั้งแต่เริ่มเกม
        SetAllLights(true);
        
        // เริ่มทำงานลูปไฟกระพริบ
        routine = StartCoroutine(BlinkLoopRoutine());
    }

    private void OnDisable()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (runningSource != null) runningSource.Stop();
        if (audioSource != null) audioSource.Stop();
    }

    private IEnumerator BlinkLoopRoutine()
    {
        while (true)
        {
            // 1. ช่วงเวลารอคอย: ไฟจะเปิดติดค้างไว้
            SetAllLights(true);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, delayBetweenLoops));

            // 2. ช่วงเวลากระพริบและเล่นเสียง
            for (int i = 0; i < blinkCountPerLoop; i++)
            {
                AudioClip clip = flickerSounds != null && flickerSounds.Length > 0
                    ? flickerSounds[i % flickerSounds.Length] : sparkSound;
                float offset = hitOffsets != null && hitOffsets.Length > 0
                    ? Mathf.Max(0, hitOffsets[i % hitOffsets.Length]) : 0;
                // Schedule ahead so source setup and the audio buffer do not shift the hit.
                double clipStart = AudioSettings.dspTime + (AudioManager.Instance != null ? 0.05 : 0);
                double hitTime = clipStart + offset;
                if (AudioManager.Instance != null)
                    AudioManager.Instance.PlayClip(clip, SoundCategory.Sfx, flickerVolume,
                        transform.position, false, 1, 15, gameObject.scene, clipStart);
                else if (audioSource != null && clip != null)
                {
                    audioSource.PlayOneShot(clip, flickerVolume);
                }

                // The lamp and hum follow the same hit clock; clip leading silence is configurable.
                while (AudioSettings.dspTime < hitTime) yield return null;
                SetAllLights(false);
                double onTime = hitTime + Mathf.Max(0.01f, blinkDuration);
                while (AudioSettings.dspTime < onTime) yield return null;

                // สั่งเปิดไฟขึ้นมา
                SetAllLights(true);
                // Let each short spark finish before the next one starts.
                double nextTime = System.Math.Max(onTime + Mathf.Max(0.01f, blinkDuration),
                    hitTime - offset + (clip != null ? clip.length : 0));
                while (AudioSettings.dspTime < nextTime) yield return null;
            }
        }
    }

    // ฟังก์ชันสำหรับเปิด/ปิดไฟทุกดวงใน List พร้อม ๆ กัน
    private void SetAllLights(bool state)
    {
        // Keep the loop position advancing silently while the lamp is off.
        if (runningCategory != null) runningCategory.SetBaseVolume(state ? runningVolume : 0);
        foreach (Light lightSource in lightGroup)
        {
            if (lightSource != null)
            {
                lightSource.enabled = state;
            }
        }
    }
}
