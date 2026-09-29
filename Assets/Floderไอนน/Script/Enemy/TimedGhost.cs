using UnityEngine;
using UnityEngine.AI;
using Mirror; 

public class TimedGhost : Enemy_Abstract_Class
{
    [Header("Ghost Visual Settings")]
    [SerializeField] private GameObject jumpscareUI;       
    [SerializeField] private float jumpscareDuration = 3.0f; 
    
    [Header("Movement Detection Settings")]
    [SerializeField] private float movementThreshold = 0.02f; 

    [Header("Patrol Settings (ความมืด)")]
    [SerializeField] private float randomPatrolRadius = 15f; 

    [Header("Mic Settings for Child Ghost")]
    [SerializeField] private float micVolumeThreshold = 0.3f; 

    [HideInInspector] public bool isSceneLightOn = true; 

    private Vector3 lastPlayerPosition;  
    private bool isJumpscareTriggered = false; 

    protected override void Start()
    {
        base.Start(); 
        
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        
        // 💡 [แก้บั๊กลอยค้างออนไลน์] สั่งเปิดรันระบบ Agent และวาร์ปลงล็อกติดแผ่นพื้นสีฟ้าทันทีในเฟรมแรกที่ขยับตัว
        if (agent != null)
        {
            agent.enabled = true;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                Debug.Log("[Ghost AI] วาร์ปผีลงมายืนบนพื้น NavMesh สำเร็จ!");
            }
        }

        if (jumpscareUI == null)
        {
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.CompareTag("JumpscareUI") && obj.scene.name != null)
                {
                    jumpscareUI = obj;
                    break;
                }
            }
        }

        if (playerTransform != null)
        {
            lastPlayerPosition = playerTransform.position;
        }
        
        if (jumpscareUI != null) jumpscareUI.SetActive(false);
    }

    protected override void checkForPlayer()
    {
        if (isJumpscareTriggered) return;
        if (playerTransform == null) return;

        if (isSceneLightOn)
        {
            // [ไฟเปิดอยู่] -> ผีใช้สายตาปกติ
            base.checkForPlayer();
        }
        else
        {
            // [ไฟดับอยู่] -> ผีตาบอด สัมผัสการขยับ + ดึงเสียงไมค์ออนไลน์จากระบบของเพื่อน
            if (currentState != EnemyState.Chase) 
            {
                float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

                if (distanceToPlayer <= viewDistance)
                {
                    // 1. ระบบชนระยะประชิดตัว 3 เมตร ผีจะตื่นทันที
                    if (distanceToPlayer <= 3f)
                    {
                        Debug.Log("[Ghost AI] ผีเดินสุ่มมาเจอผู้เล่นระยะประชิดตัว!");
                        OnPlayerSpotted();
                        return;
                    }

                    // 2. ตรวจจับจากการขยับตัวเคลื่อนที่ (ระบบพิกัดสำรอง)
                    float playerMovedDistance = Vector3.Distance(playerTransform.position, lastPlayerPosition);
                    if (playerMovedDistance > movementThreshold)
                    {
                        Debug.Log("[Ghost AI] ผีตรวจจับพิกัดการเดินของผู้เล่นในความมืดได้!");
                        OnPlayerSpotted(); 
                        return; 
                    }

                    // 3. 💡 [ระบบไมค์ออนไลน์ของเพื่อน] 💡
                    if (canDetectMic)
                    {
                        // สั่งดึงคอมโพเนนต์ PlayerNoise ที่แปะอยู่บนตัวละครผู้เล่นคนนั้นมาตรวจสอบค่าบน Server
                        PlayerNoise playerNoise = playerTransform.GetComponent<PlayerNoise>();
                        
                        if (playerNoise != null)
                        {
                            // ดึงค่าน้ำหนักเสียงไมค์สดๆ ที่เพื่อนซิงค์ข้ามเน็ตเวิร์กมาเก็บไว้ที่ ServerMic
                            float currentMicVolume = playerNoise.ServerMic;

                            // เช็คระดับเสียง: ถ้าตะโกนดังเกินค่ากำหนด (หรือใช้เงื่อนไข playerNoise.ServerTooLoud ตามที่เพื่อนจดโน้ตไว้ก็ได้ครับ)
                            if (currentMicVolume > micVolumeThreshold || playerNoise.ServerTooLoud)
                            {
                                Debug.Log($"[Ghost AI] ผีได้ยินผู้เล่นทำเสียงดังออนไลน์! ไมค์: {playerNoise.ServerMic} เกม: {playerNoise.ServerGame}");
                                OnPlayerSpotted(); 
                                return;
                            }
                        }
                    }
                }
            }
        }

        lastPlayerPosition = playerTransform.position;
    }

    protected override void PatrolBehavior()
    {
        if (isJumpscareTriggered || playerTransform == null) return;
        if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        if (isSceneLightOn)
        {
            agent.SetDestination(playerTransform.position);
        }
        else
        {
            // ไฟดับ: สั่งตั้งค่าจุดหมายปลายทางให้ผีก้าวขาเดินสุ่มทันทีฉลุยรอบด่าน
            if (!agent.pathPending && agent.remainingDistance < 1f)
            {
                Vector3 randomDestination = GetRandomNavMeshLocation();
                agent.SetDestination(randomDestination);
                Debug.Log($"[Ghost AI] ผีกำลังเดินสุ่มลาดตระเวนไปที่: {randomDestination}");
            }
        }
    }

    private Vector3 GetRandomNavMeshLocation()
    {
        Vector3 randomDirection = Random.insideUnitSphere * randomPatrolRadius;
        randomDirection += transform.position;
        
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, randomPatrolRadius, NavMesh.AllAreas))
        {
            return hit.position;
        }
        
        return transform.position;
    }

    protected override void ChaseBehavior()
    {
        if (isJumpscareTriggered || playerTransform == null) return;

        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(playerTransform.position);
        }

        if (Vector3.Distance(transform.position, playerTransform.position) <= 1.5f)
        {
            TriggerJumpscare();
        }
    }

    protected override void SearchBehavior()
    {
        currentState = EnemyState.Patrol;
    }

    public override void TriggerJumpscare()
    {
        if (isJumpscareTriggered) return;
        isJumpscareTriggered = true;
        
        if (agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.isStopped = true; 
        }

        RpcShowJumpscareUI(); 
    }

    [ClientRpc] 
    private void RpcShowJumpscareUI()
    {
        if (jumpscareUI != null)
        {
            jumpscareUI.SetActive(true);
        }

        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.CancelDespawnTimer();
        }

        Invoke("EndJumpscareEffect", jumpscareDuration);
    }

    private void EndJumpscareEffect()
    {
        if (jumpscareUI != null)
        {
            jumpscareUI.SetActive(false); 
        }

        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.ResetManagerAfterJumpscare();
        }

        if (isServer)
        {
            NetworkServer.Destroy(gameObject); // ใช้ฟังก์ชันลบวัตถุเน็ตเวิร์กออนไลน์ของ Mirror ทันที
        }
    }
}
