using UnityEngine;
using UnityEngine.AI; // เรียกใช้งานระบบ NavMesh ของ Unity เพื่อควบคุมการเดินหลบสิ่งกีดขวาง

// กำหนดให้เป็น abstract class (คลาสแม่) เพื่อให้ผีตัวอื่นๆ นำโครงสร้างนี้ไปสืบทอดใช้งานต่อ
public abstract class Enemy_Abstract_Class : MonoBehaviour
{
    // กำหนดกลุ่มสถานะ (State) ของผี: ลาดตระเวน, ไล่ล่า, ค้นหา, โดนดึงความสนใจจากเสียง
    public enum EnemyState { Patrol, Chase, Search, Distracted }
    
    [Header("AI State")]
    // [SerializeField] ทำให้เราเห็นและเปลี่ยนสถานะของผีลองทดสอบในหน้าต่าง Inspector ได้
    [SerializeField] protected EnemyState currentState = EnemyState.Patrol;

    [Header("Movement Settings")]
    [SerializeField] protected float patrolSpeed = 2f;  // ความเร็วตอนผีเดินตรวจตราปกติ
    [SerializeField] protected float chaseSpeed = 5f;   // ความเร็วตอนผีวิ่งไล่ล่าผู้เล่น
    protected NavMeshAgent agent;                       // ตัวควบคุมการเคลื่อนที่บน NavMesh

    [Header("Detection Settings")]
    [SerializeField] protected float viewDistance = 10f;    // ระยะสายตาของผี (มองเห็นไกลแค่ไหน)
    [SerializeField] protected float viewAngle = 45f;       // องศากรอบสายตาของผี (มุมมองกว้างแคบแค่ไหน)
    [SerializeField] protected float hearingRadius = 8f;    // ระยะรัศมีการได้ยินเสียงของผู้เล่น
    [SerializeField] protected LayerMask playerLayer;       // เลเยอร์ที่ระบุว่าเป็นตัวผู้เล่น (Player)
    [SerializeField] protected LayerMask obstacleLayer;     // เลเยอร์ของกำแพงหรือสิ่งกีดขวาง (เอาไว้เช็คการบังสายตา)

    protected Transform playerTransform;     // ตัวเก็บพิกัดตำแหน่งของผู้เล่น
    protected bool isPlayerDetected = false; // ตัวแปรเช็คว่าขณะนี้ผีเจอผู้เล่นแล้วหรือยัง

    // Awake ทำงานเป็นอันดับแรกสุดเมื่อ Object ถูกสร้างขึ้นมา
    protected virtual void Awake()
    {
        // ดึงคอมโพเนนต์ NavMeshAgent จากตัวผีมาเก็บไว้ในตัวแปร agent
        agent = GetComponent<NavMeshAgent>();
    }

    // Start ทำงานครั้งแรกก่อนเริ่มเฟรมแรกของเกม
    protected virtual void Start()
    {
        // ค้นหา GameObject ในฉากที่มี Tag ชื่อ "Player"
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) 
        {
            // ถ้าเจอผู้เล่น ให้เก็บพิกัด Transform ของผู้เล่นไว้ใช้งาน
            playerTransform = player.transform;
        }
    }

    // Update ทำงานซ้ำๆ ทุกๆ เฟรมของเกม
    protected virtual void Update()
    {
        // สั่งให้ระบบเช็คสายตาและการรับรู้ผู้เล่นรันทำงานตลอดเวลาในทุกเฟรม
        CheckForPlayer();
        
        // สั่งให้ระบบสลับพฤติกรรมการเคลื่อนที่ทำงานตามสถานะปัจจุบัน
        SwitchStateBehavior();
    }

    // --- LOGIC การตรวจจับ (ผีทุกตัวจะใช้การมองเห็นและการได้ยินแบบเดียวกันนี้เป็นฐาน) ---
    protected virtual void CheckForPlayer()
    {
        // ถ้าหาตัวผู้เล่นในฉากไม่เจอ ไม่ต้องทำคำสั่งด้านล่างต่อ
        if (playerTransform == null) return;

        // คำนวณระยะห่างระหว่างตัวผีกับตัวผู้เล่น ณ ปัจจุบัน
        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // [ส่วนที่ 1: ตรวจสอบการมองเห็น (Vision)]
        // ถ้าผู้เล่นอยู่ในระยะสายตาของผี
        if (distanceToPlayer <= viewDistance)
        {
            // หาเวกเตอร์ทิศทางชี้จากผีไปหาผู้เล่น
            Vector3 directionToPlayer = (playerTransform.position - transform.position).normalized;
            
            // เช็คว่าทิศทางของผู้เล่นอยู่ในกรอบองศาหน้าสายตาของผีหรือไม่
            if (Vector3.Angle(transform.forward, directionToPlayer) < viewAngle)
            {
                // ยิงลำแสง Raycast ออกไปจากตัวผีตามทิศทางผู้เล่น เพื่อเช็คว่าติดสิ่งกีดขวาง (เช่น กำแพง) ไหม
                if (!Physics.Raycast(transform.position, directionToPlayer, distanceToPlayer, obstacleLayer))
                {
                    // ถ้าไม่ติดอะไรเลย แสดงว่าผีเห็นตัวผู้เล่นจะๆ ให้เข้าฟังก์ชัน OnPlayerSpotted
                    OnPlayerSpotted();
                    return; // จบฟังก์ชันทันที
                }
            }
        }

        // [ส่วนที่ 2: ตรวจสอบกรณีคลาดสายตา]
        // ถ้าสถานะปัจจุบันคือไล่ล่าอยู่ แต่ผู้เล่นหนีห่างออกไปเกินระยะสายตาแล้ว
        if (currentState == EnemyState.Chase && distanceToPlayer > viewDistance)
        {
            // ให้เข้าฟังก์ชันคลาดสายตา
            OnPlayerLost();
        }
    }

    // ฟังก์ชันรับสัญญาณเสียงภายนอก (เมื่อผู้เล่นทำของตก หรือวิ่งใกล้ๆ)
    public virtual void HearSound(Vector3 soundPosition, float soundIntensity)
    {
        // คำนวณระยะห่างระหว่างตัวผีกับจุดกำเนิดเสียง
        float distanceToSound = Vector3.Distance(transform.position, soundPosition);
        
        // ถ้าระยะห่างน้อยกว่ารัศมีการได้ยิน (คูณกับความดังของเสียง)
        if (distanceToSound <= hearingRadius * soundIntensity)
        {
            // สั่งให้ผีเดินไปยังตำแหน่งที่เกิดเสียงนั้นทันที
            agent.SetDestination(soundPosition);
            // สลับสถานะเป็นโดนดึงความสนใจ เพื่อให้ผีเดินไปตรวจตรงจุดนั้น
            currentState = EnemyState.Distracted;
        }
    }

    // --- ABSTRACT METHODS (ฟังก์ชันบังคับให้สคริปต์ลูก เช่น ผีนางรำ ผีปอบ ไปเขียนโค้ดเองภายหลัง) ---
    protected abstract void PatrolBehavior(); // วิธีการเดินตรวจตรา (เช่น เดินตามจุด, เดินสุ่ม)
    protected abstract void ChaseBehavior();  // วิธีการวิ่งไล่ผู้เล่น (เช่น วิ่งตรงๆ, อ้อมดักหน้า)
    protected abstract void SearchBehavior(); // วิธีการเดินตามหาเมื่อคลาดสายตา
    public abstract void TriggerJumpscare();  // วิธีการฆ่าหรือหลอนผู้เล่นเมื่อประชิดตัวได้

    // --- STATE MANAGER (ระบบควบคุมและแบ่งงานตามสถานะ) ---
    private void SwitchStateBehavior()
    {
        // เช็คว่าสถานะปัจจุบันคืออะไร แล้วสั่งงานให้สอดคล้อง
        switch (currentState)
        {
            case EnemyState.Patrol:
                agent.speed = patrolSpeed; // ปรับความเร็ว NavMesh เป็นเดินปกติ
                PatrolBehavior();          // เรียกใช้พฤติกรรมเดินลาดตระเวนที่เขียนไว้ในคลาสลูก
                break;
                
            case EnemyState.Chase:
                agent.speed = chaseSpeed;  // ปรับความเร็ว NavMesh เป็นวิ่งเร็ว
                ChaseBehavior();           // เรียกใช้พฤติกรรมไล่ล่าที่เขียนไว้ในคลาสลูก
                break;
                
            case EnemyState.Search:
                SearchBehavior();          // เรียกใช้พฤติกรรมตามหาที่เขียนไว้ในคลาสลูก
                break;
        }
    }

    // ฟังก์ชันที่จะทำงานอัตโนมัติเมื่อผีส่องเห็นผู้เล่น
    protected virtual void OnPlayerSpotted()
    {
        // ถ้าก่อนหน้านี้ไม่ได้อยู่ในโหมดไล่ล่า
        if (currentState != EnemyState.Chase)
        {
            currentState = EnemyState.Chase; // สลับโหมดเป็นไล่ล่าทันที
            Debug.Log("ผีเห็นผู้เล่นแล้ว! เริ่มไล่ล่า!"); // แสดงข้อความแจ้งเตือนในหน้าต่าง Console
        }
    }

    // ฟังก์ชันที่จะทำงานอัตโนมัติเมื่อผู้เล่นวิ่งหลุดสายตาไปได้
    protected virtual void OnPlayerLost()
    {
        currentState = EnemyState.Search; // สลับโหมดเป็นเดินตามหาบริเวณรอบๆ
        Debug.Log("ผู้เล่นคลาดสายตา ผีกำลังเดินหาแถวนี้..."); // แสดงข้อความแจ้งเตือนในหน้าต่าง Console
    }
}
