using System.Collections;
using UnityEngine;
using Mirror; // ดึงระเบียบเน็ตเวิร์กออนไลน์ของ Mirror มาคุมระบบเสกสปอว์น

public class GhostManager : MonoBehaviour
{
    // ประกาศโครงสร้างตามที่สคริปต์ GhostFavorRecovery ของเพื่อนเรียกหาใช้งานเป๊ะๆ 
    public static GhostManager Instance;

    // เปิดช่องตัวแปรให้เป็น public เพื่อให้สคริปต์เพื่อนดึงค่าไปใช้ได้ปกติ
    public bool areLightsOnCurrently = true;

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

    void Awake()
    {
        // ทำการล็อกล็อกพิกัดผูกข้อมูลตั้งแต่วินาทีแรกที่เปิดระบบขึ้นมาในแผนที่
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        timer = 0f;
        SetAllLightsEnabled(true);
        areLightsOnCurrently = true;
    }

    void Update()
    {
        // 💡 [จุดแก้ไขปลดล็อกบั๊กผีไม่เกิด] 💡
        // ปรับเงื่อนไขใหม่: ถ้าอยู่ในระบบ Mirror และเครื่องนี้ "ไม่ใช่ Server" ค่อยกดข้ามไป 
        // แต่ถ้าเรากำลังกดปุ่ม Play เทสเกมแบบออฟไลน์คนเดียวเฉยๆ ตัวนับเวลาจะทำงานผ่านฉลุยปกติครับ!
        if (NetworkServer.active && !NetworkServer.activeHost) return; 

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

    // ❌ ลบคำสั่งชิงปิดไฟอัตโนมัติออกไป ❌
    // SetAllLightsEnabled(false); 
    // areLightsOnCurrently = false; 

    if (ghostPrefab != null && spawnPoint != null)
    {
        currentGhostInstance = Instantiate(ghostPrefab, spawnPoint.position, Quaternion.identity);
        currentGhostInstance.transform.localScale = ghostPrefab.transform.localScale;
        
        TimedGhost ghostScript = currentGhostInstance.GetComponent<TimedGhost>();
        if (ghostScript != null)
        {
            // 💡 สั่งให้ผีอ่านค่าจากสวิตช์ไฟ ณ วินาทีนั้นเลยว่าผู้เล่นวิ่งไปกดปิดทันไหม 💡
            // ถ้าผู้เล่นปิดไฟทัน ค่าจะเป็น false ผีจะตาบอด / ถ้าลืมปิด ค่าจะเป็น true ไฟจะสว่างและผีจะเห็นตัวคุณ!
            ghostScript.isSceneLightOn = areLightsOnCurrently; 
        }

        if (NetworkServer.active)
        {
            NetworkServer.Spawn(currentGhostInstance);
        }
    }
    
    Invoke("DespawnGhost", ghostDuration);
}


    void DespawnGhost()
    {
        isGhostActive = false;
        isSequenceRunning = false; 
        
        if (currentGhostInstance != null)
        {
            // ถ้าอยู่ในระบบออนไลน์ ให้ทำลายวัตถุออนไลน์ออกจากเน็ตเวิร์ก
            if (NetworkServer.active)
            {
                NetworkServer.Destroy(currentGhostInstance);
            }
            else
            {
                // ถ้าเทสคนเดียวปกติ ใช้คำสั่งลบอ็อบเจกต์ธรรมดา
                Destroy(currentGhostInstance);
            }
        }
        
        SetAllLightsEnabled(true);
        areLightsOnCurrently = true;
    }

    public void CancelDespawnTimer()
    {
        CancelInvoke("DespawnGhost"); 
    }

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
