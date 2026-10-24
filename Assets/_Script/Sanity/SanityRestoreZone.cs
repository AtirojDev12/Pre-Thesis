using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// พื้นที่ฟื้นฟูสติเมื่อผู้เล่นเดินเข้าไป (ไม่ต้องแก้ PlayerSanity.cs)
/// สามารถฟื้นฟูสติได้แม้ผู้เล่นจะติดสถานะ Breaking หรือสติเหลือ 0
/// </summary>
[RequireComponent(typeof(Collider))]
public class SanityRestoreZone : MonoBehaviour
{
    [Header("Sanity Settings")]
    [Tooltip("อัตราการฟื้นฟูสติต่อวินาที")]
    [SerializeField] private float sanityPerSecond = 5f;

    [Tooltip("ติ๊กถูก = รีสติได้แม้สติจะเหลือ 0 หรือติด Breaking / ไม่ติ๊ก = เคารพกฎ BlocksGain ของเกม")]
    [SerializeField] private bool bypassBreakingBlock = true;

    [Header("Light Condition (การเช็คไฟ)")]
    [Tooltip("ต้องเปิดไฟในพื้นที่นี้ถึงจะรีสติใช่หรือไม่?")]
    [SerializeField] private bool requireLight = true;

    [Tooltip("เช็คจากไฟหลักของตึก (BuildingLightManager.LightsOn) ด้วยหรือไม่?")]
    [SerializeField] private bool checkBuildingLight = true;

    [Tooltip("เช็คจาก Component Light เฉพาะจุด (ถ้าใส่ไว้ ต้องมีอย่างน้อย 1 ดวงที่เปิดอยู่)")]
    [SerializeField] private Light[] localLights;

    [Tooltip("เช็คจาก GameObject ของไฟ (เช่น หลอดไฟ หรือตะเกียงที่ถูกเปิด activeInHierarchy)")]
    [SerializeField] private GameObject[] lightObjects;

    private const float TickInterval = 0.25f;
    private float tickTimer;

    private readonly HashSet<PlayerSanity> playersInZone = new HashSet<PlayerSanity>();

    /// <summary>เช็คว่าตอนนี้พื้นที่นี้มีไฟเปิดอยู่หรือไม่</summary>
    public bool IsLit
    {
        get
        {
            if (!requireLight) return true;

            // 1. เช็คไฟรวมของตึก
            if (checkBuildingLight && BuildingLightManager.Instance != null && !BuildingLightManager.LightsOn)
            {
                return false;
            }

            // 2. เช็คจาก Component Light เฉพาะจุด
            if (localLights != null && localLights.Length > 0)
            {
                bool anyLightOn = false;
                for (int i = 0; i < localLights.Length; i++)
                {
                    Light l = localLights[i];
                    if (l != null && l.isActiveAndEnabled && l.intensity > 0.01f)
                    {
                        anyLightOn = true;
                        break;
                    }
                }
                if (!anyLightOn) return false;
            }

            // 3. เช็คจาก GameObject ของไฟ
            if (lightObjects != null && lightObjects.Length > 0)
            {
                bool anyObjActive = false;
                for (int i = 0; i < lightObjects.Length; i++)
                {
                    if (lightObjects[i] != null && lightObjects[i].activeInHierarchy)
                    {
                        anyObjActive = true;
                        break;
                    }
                }
                if (!anyObjActive) return false;
            }

            return true;
        }
    }

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerSanity sanity = other.GetComponentInParent<PlayerSanity>();
        if (sanity != null)
        {
            playersInZone.Add(sanity);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerSanity sanity = other.GetComponentInParent<PlayerSanity>();
        if (sanity != null)
        {
            playersInZone.Remove(sanity);
        }
    }

    private void Update()
    {
        if (playersInZone.Count == 0) return;

        tickTimer += Time.deltaTime;
        if (tickTimer < TickInterval) return;

        float dt = tickTimer;
        tickTimer = 0f;

        // ถ้าไฟดับอยู่ ไม่ต้องรีสติ
        if (!IsLit) return;

        float gainAmount = sanityPerSecond * dt;

        playersInZone.RemoveWhere(p => p == null || !p.isActiveAndEnabled);

        foreach (PlayerSanity player in playersInZone)
        {
            // เช็คสิทธิ์ Server และเช็คว่าผู้เล่นยังอยู่ในเกม (ไม่ตาย/ไม่ล้ม/ไม่อยู่ล็อบบี้)
            if (!NetworkMode.HasServerAuthority(player) || !player.InPlay) continue;

            if (bypassBreakingBlock)
            {
                // ใช้ ServerDevSet เพื่อบวกค่าสติเข้าไปตรงๆ โดยข้ามการเช็ค BlocksGain (ไม่แตะโค้ด PlayerSanity)
                if (player.Value < PlayerSanity.Max)
                {
                    player.ServerDevSet(player.Value + gainAmount);
                }
            }
            else
            {
                // ถ้าไม่ติ๊ก bypass ก็จะใช้ ServerGain ปกติ (ซึ่งจะติด BlocksGain ตอนสติต่ำกว่า 30)
                player.ServerGain(gainAmount, null);
            }
        }
    }
}