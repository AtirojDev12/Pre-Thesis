using UnityEngine;
using UnityEngine.AI;
using Mirror; // เรียกใช้งานระบบเน็ตเวิร์กออนไลน์ของ Mirror

// กำหนดให้เป็น abstract class และสืบทอดจาก NetworkBehaviour เพื่อคุมระบบออนไลน์
public abstract class Enemy_Abstract_Class : NetworkBehaviour
{
    // กำหนดกลุ่มสถานะ (State) ของผี
    public enum EnemyState { Patrol, Chase, Search, Distracted }
    
    [Header("AI State")]
    [SerializeField] protected EnemyState currentState = EnemyState.Patrol;

    [Header("Movement Settings")]
    [SerializeField] protected float patrolSpeed = 2f;  // ความเร็วตอนเดินตรวจตราปกติ
    [SerializeField] protected float chaseSpeed = 5f;   // ความเร็วตอนวิ่งไล่ล่าผู้เล่น
    protected NavMeshAgent agent;                       // ตัวควบคุมการเคลื่อนที่บน NavMesh

    [Header("Detection Settings")]
    [SerializeField] protected float viewDistance = 10f;    // ระยะสายตาของผี (มองเห็นไกลแค่ไหน)
    [SerializeField] protected float viewAngle = 45f;       // องศากรอบสายตาของผี (มุมมองกว้างแคบแค่ไหน)
    [SerializeField] protected float hearingRadius = 8f;    // ระยะรัศมีการได้ยินเสียงฝีเท้า/สิ่งของ
    [SerializeField] protected LayerMask playerLayer;       // เลเยอร์ของผู้เล่น (Player)
    [SerializeField] protected LayerMask obstacleLayer;     // เลเยอร์ของกำแพงหรือสิ่งกีดขวาง

    [Header("Microphone Detection")]
    [SerializeField] protected bool canDetectMic = true; 

    protected Transform playerTransform;     // ตัวเก็บพิกัดตำแหน่งของผู้เล่นเป้าหมาย
    protected bool isPlayerDetected = false; // ตัวแปรเช็คว่าผีเจอผู้เล่นหรือยัง

    protected virtual void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    protected virtual void Start()
    {
        FindTargetPlayer();

        // 💡 [จุดแก้ไขปลดล็อกบั๊กผีไม่ขยับขารอบสุดท้าย] 💡
        // ปรับเงื่อนไขใหม่: ระบบจะยอมสั่งปิดระบบเดิน (Agent) เฉพาะตอนที่เปิดห้องรันออนไลน์ออนไลน์อยู่จริงๆ เท่านั้น
        // หากเรากำลังกดปุ่ม Play เทสเกมออฟไลน์คนเดียวในเครื่อง ตัว Agent จะเปิดใช้งานทำงานปกติ ผีจะยอมเดินครับ!
        if (NetworkServer.active && !isServer && agent != null)
        {
            agent.enabled = false;
        }
        else if (agent != null)
        {
            agent.enabled = true; // มั่นใจว่าถ้าเทสปกติ ระบบเดินต้องเปิดใช้งาน
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (agent != null) agent.enabled = true;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isServer && agent != null)
        {
            agent.enabled = false;
        }
    }

    protected void FindTargetPlayer()
    {
        foreach (var networkUser in Mirror.NetworkServer.connections.Values)
        {
            if (networkUser != null && networkUser.identity != null)
            {
                playerTransform = networkUser.identity.transform;
                return;
            }
        }

        if (playerTransform == null)
        {
            GameObject backupPlayer = GameObject.Find("Player (offline test)");
            if (backupPlayer == null) backupPlayer = GameObject.FindWithTag("Player");
            
            if (backupPlayer != null)
            {
                playerTransform = backupPlayer.transform;
            }
        }
    }

    protected virtual void Update()
    {
        // สั่งให้สมอง AI คำนวณความคิดปกติได้เลยถ้ากดเทสคนเดียว หรือเป็นเครื่อง Server ออนไลน์
        if (NetworkServer.active && !isServer) return; 

        checkForPlayer(); 
        SwitchStateBehavior(); 
    }

    protected virtual void checkForPlayer()
    {
        if (playerTransform == null)
        {
            FindTargetPlayer();
            return; 
        }

        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        if (distanceToPlayer <= viewDistance)
        {
            Vector3 directionToPlayer = (playerTransform.position - transform.position).normalized;
            
            if (Vector3.Angle(transform.forward, directionToPlayer) < viewAngle)
            {
                if (!Physics.Raycast(transform.position, directionToPlayer, distanceToPlayer, obstacleLayer))
                {
                    OnPlayerSpotted();
                    return; 
                }
            }
        }

        if (currentState == EnemyState.Chase && distanceToPlayer > viewDistance)
        {
            OnPlayerLost();
        }
    }

    public virtual void HearSound(Vector3 soundPosition, float soundIntensity)
    {
        float distanceToSound = Vector3.Distance(transform.position, soundPosition);
        if (distanceToSound <= hearingRadius * soundIntensity)
        {
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.SetDestination(soundPosition);
            }
            currentState = EnemyState.Distracted;
        }
    }

    protected abstract void PatrolBehavior(); 
    protected abstract void ChaseBehavior();  
    protected abstract void SearchBehavior(); 
    public abstract void TriggerJumpscare();  

    private void SwitchStateBehavior()
    {
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

        switch (currentState)
        {
            case EnemyState.Patrol:
                agent.speed = patrolSpeed; 
                PatrolBehavior();          
                break;
                
            case EnemyState.Chase:
                agent.speed = chaseSpeed;  
                ChaseBehavior();           
                break;
                
            case EnemyState.Search:
                SearchBehavior();          
                break;
        }
    }

    protected virtual void OnPlayerSpotted()
    {
        if (currentState != EnemyState.Chase)
        {
            currentState = EnemyState.Chase;
            Debug.Log("ผีเห็นผู้เล่นแล้ว! สลับเข้าโหมดไล่ล่า!");
        }
    }

    protected virtual void OnPlayerLost()
    {
        currentState = EnemyState.Search;
        Debug.Log("ผู้เล่นคลาดสายตา ผีกำลังเดินหาแถวนี้...");
    }
}
