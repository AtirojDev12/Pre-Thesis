using UnityEngine;
using UnityEngine.AI;

public class TimedGhost :  Enemy_Abstract_Class
{
    [Header("Ghost Visual Settings")]
    [SerializeField] private GameObject jumpscareUI; 
    [SerializeField] private float jumpscareDuration = 3.0f; 
    
    [Header("Movement Detection Settings")]
    [SerializeField] private float movementThreshold = 0.02f; 

    [Header("Patrol Settings (ความมืด)")]
    [SerializeField] private float randomPatrolRadius = 15f; 
    
    [HideInInspector] public bool isSceneLightOn = true; 

    private Vector3 lastPlayerPosition; 
    private bool isJumpscareTriggered = false; 

    protected override void Start()
    {
        base.Start(); 
        
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        
        if (agent != null)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 2.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
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

    protected override void CheckForPlayer()
    {
        if (isJumpscareTriggered) return;
        if (playerTransform == null) return;

        if (isSceneLightOn)
        {
            base.CheckForPlayer();
        }
        else
        {
            if (currentState != EnemyState.Chase) 
            {
                float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

                if (distanceToPlayer <= viewDistance)
                {
                    float playerMovedDistance = Vector3.Distance(playerTransform.position, lastPlayerPosition);
                    
                    if (playerMovedDistance > movementThreshold)
                    {
                        OnPlayerSpotted(); 
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
            if (!agent.pathPending && agent.remainingDistance < 1f)
            {
                Vector3 randomDestination = GetRandomNavMeshLocation();
                agent.SetDestination(randomDestination);
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

        if (jumpscareUI != null)
        {
            jumpscareUI.SetActive(true);
        }

        // 💡 [เพิ่มโค้ดใหม่] สั่งไปบอก GhostManager ให้หยุดเวลาถอยหลัง 15 วินาทีชั่วคราว ห้ามมาชิงทำลายผีตัวนี้ 💡
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
            jumpscareUI.SetActive(false); // ปิดภาพ Jumpscare หายไป
        }

        // 💡 [เพิ่มโค้ดใหม่] สั่งให้ GhostManager เปิดไฟในฉาก และเริ่มนับลูป 1 นาทีใหม่อย่างถูกต้อง 💡
        GhostManager manager = FindObjectOfType<GhostManager>();
        if (manager != null)
        {
            manager.ResetManagerAfterJumpscare();
        }

        Destroy(gameObject); // ทำลายตัวผีทิ้งอย่างปลอดภัย
    }
}
