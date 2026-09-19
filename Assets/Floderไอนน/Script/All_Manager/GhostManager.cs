using System.Collections;
using UnityEngine;

public class GhostManager : MonoBehaviour
{
    [Header("Prefab Settings")]
    [SerializeField] private GameObject ghostPrefab;   
    private GameObject currentGhostInstance; 

    [Header("Spawn Position")]
    [SerializeField] private Transform spawnPoint; 

    [Header("Lights Settings")]
    [SerializeField] private Light[] allLights;         

    [Header("Timing Settings")]
    [SerializeField] private float spawnInterval = 60f;   
    [SerializeField] private float flickerDuration = 3f;  
    [SerializeField] private float countdownDuration = 10f; 
    [SerializeField] private float ghostDuration = 15f;    

    private float timer = 0f;
    private bool isGhostActive = false;
    private bool isSequenceRunning = false; 

    public bool areLightsOnCurrently { get; private set; } = true;

    void Start()
    {
        timer = 0f;
        SetAllLightsEnabled(true);
        areLightsOnCurrently = true;
    }

    void Update()
    {
        if (isGhostActive || isSequenceRunning) return;

        timer += Time.deltaTime;

        if (timer >= (spawnInterval - flickerDuration))
        {
            StartCoroutine(GhostArrivalSequence());
        }
    }

    IEnumerator GhostArrivalSequence()
    {
        isSequenceRunning = true;
        timer = 0f;

        SetAllLightsEnabled(false); yield return new WaitForSeconds(0.2f);
        SetAllLightsEnabled(true); yield return new WaitForSeconds(0.3f);
        SetAllLightsEnabled(false); yield return new WaitForSeconds(0.2f);
        SetAllLightsEnabled(true); yield return new WaitForSeconds(0.3f);
        SetAllLightsEnabled(false); yield return new WaitForSeconds(0.1f);

        SetAllLightsEnabled(true);
        areLightsOnCurrently = true; 
        
        yield return new WaitForSeconds(countdownDuration);

        SpawnGhost();
    }

    void SpawnGhost()
    {
        isGhostActive = true;

        if (ghostPrefab != null && spawnPoint != null)
        {
            currentGhostInstance = Instantiate(ghostPrefab, spawnPoint.position, spawnPoint.rotation);
            
            TimedGhost ghostScript = currentGhostInstance.GetComponent<TimedGhost>();
            if (ghostScript != null)
            {
                ghostScript.isSceneLightOn = areLightsOnCurrently;
            }
        }

        // สั่งนับเวลาถอยหลัง 15 วิเพื่อให้ผีหายไปตามปกติ
        Invoke("DespawnGhost", ghostDuration);
    }

    void DespawnGhost()
    {
        isGhostActive = false;
        isSequenceRunning = false; 
        
        if (currentGhostInstance != null)
        {
            Destroy(currentGhostInstance);
        }
        
        SetAllLightsEnabled(true);
        areLightsOnCurrently = true;
    }

    // 💡 ฟังก์ชันใหม่: สั่งให้หยุดเวลาทำลายผีทันที (เมื่อผีทำ Jumpscare สำเร็จ)
    public void CancelDespawnTimer()
    {
        CancelInvoke("DespawnGhost"); // ยกเลิกการสั่ง Despawn ของ Manager
    }

    // 💡 ฟังก์ชันใหม่: สั่งเคลียร์สถานะตัวจัดการเพื่อให้เริ่มลูป 1 นาทีรอบใหม่ได้หลังจบ Jumpscare
    public void ResetManagerAfterJumpscare()
    {
        isGhostActive = false;
        isSequenceRunning = false;
        SetAllLightsEnabled(true);
        areLightsOnCurrently = true;
    }

    public void PlayerToggleLights(bool open)
    {
        SetAllLightsEnabled(open);
        areLightsOnCurrently = open;
    }

    private void SetAllLightsEnabled(bool state)
    {
        if (allLights == null) return;

        foreach (Light lightItem in allLights)
        {
            if (lightItem != null)
            {
                lightItem.enabled = state;
            }
        }
    }
}
