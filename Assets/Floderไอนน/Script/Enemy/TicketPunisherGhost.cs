using UnityEngine;
using UnityEngine.AI;
using Mirror; // เรียกใช้งานระเบียบเน็ตเวิร์กออนไลน์ของ Mirror

public class TicketPunisherGhost : Enemy_Abstract_Class
{
    [Header("Punishment Settings")]
    [SerializeField] private int maxWrongAttempts = 3;
    [SerializeField] private float riseAmountPerMistake = 0.8f;
    [SerializeField] private float attackDamage = 40f;

    [Header("Jumpscare Sync Settings (ดึงมาจากผีตัวแรก)")]
    [Tooltip("ปล่อยว่างไว้ได้เลยครับ ระบบ Start จะเปิดเรดาร์สแกนหาวัตถุ Tag: JumpscareUI ในฉากให้เองอัตโนมัติ")]
    [SerializeField] private GameObject jumpscareUI;
    [SerializeField] private float jumpscareDuration = 3.0f;
    [Tooltip("ถ้าติ๊กถูกเปิดไว้ โดนผีตั๋วตบแล้วจะตายทันทีโดยไม่เข้าสู่สถานะล้มชุบ")]
    [SerializeField] private bool killsOutright = false;

    [Header("Animation Settings")]
    [Tooltip("ชื่อของ Trigger ใน Animator ที่ใช้สั่งให้ผีเล่นแอนิเมชันจู่โจม (พิมพ์ให้ตรงกับใน Unity)")]
    [SerializeField] private string attackTriggerName = "Attack";

    private TicketMinigame targetMinigame; // ตัวแปรเก็บอ้างอิงระบบมินิเกมตั๋วในฉาก
    private Animator ghostAnimator;        // คอมโพเนนต์คุมแอนิเมชันบนตัวผี
    private int lastObservedRound = -1;    // ตัวจำรอบการขายล่าสุดของลูกค้า
    private int lastObservedScore = 0;     // ตัวจำคะแนนล่าสุดในตู้ตั๋ว
    private bool isFirstFrameCaptured = false; // ตัวล็อกเฟสป้องกันผีเอ๋อตบคนตอนเริ่มเกม
    private TicketCustomerStage lastObservedStage; // ตัวแปรคอยจำสถานะคิวลูกค้าในเฟรมที่แล้ว

    [HideInInspector] public bool isScenelightOn = true; 

    // syncVar ตัวเก่ง คอยกระจายตัวเลขผิดพลาดบอกผู้เล่นออนไลน์ทุกคน
    [SyncVar] 
    private int currentWrongCount = 0;

    private Vector3 initialLocalPosition;  // เก็บคอนพิกัดเริ่มต้นตอนผีสปอว์นออกมา (ใต้ดิน/ซ่อนอยู่)
    private bool isAttackTriggered = false; // ตัวแปรป้องกันคำสั่งทำงานซ้ำซ้อน

    protected override void Start()
    {
        FindTicketMinigameInScene();

        base.Start(); // รันระบบดั้งเดิมคลาสแม่

        // ปิดคอมโพเนนต์การเดินทิ้งถาวร เพราะผีตู้ตั๋วจะปักหลักนิ่งๆ อยู่กับที่
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false; 

        ghostAnimator = GetComponent<Animator>();
        
        // บังคับหักพิกัดแกน Y ดึงตัวโมเดลผีให้มุดจมลงดินไปเลย 1.2 เมตร ทันทีตั้งแต่เริ่มเกม!
        transform.position = transform.position + (Vector3.down * 1.2f);
        initialLocalPosition = transform.position;

        // 💡 [ดึงระบบจากผีตัวแรก]: ค้นหาวัตถุ Inactive ในฉากด่านที่มีชื่อป้าย Tag ว่า JumpscareUI อัตโนมัติ
        if (jumpscareUI == null)
        {
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (obj.CompareTag("JumpscareUI") && obj.scene.IsValid())
                {
                    jumpscareUI = obj;
                    break;
                }
            }
        }
        if (jumpscareUI != null) jumpscareUI.SetActive(false);
    }

    private void FindTicketMinigameInScene()
    {
        TicketMinigame foundMinigame = FindObjectOfType<TicketMinigame>();
        if (foundMinigame != null)
        {
            targetMinigame = foundMinigame;
        }
    }

    protected override void CheckForPlayer()
    {
        if (isAttackTriggered) return;
        if (targetMinigame == null) return;

        if (!isFirstFrameCaptured)
        {
            lastObservedRound = targetMinigame.State.round;
            lastObservedScore = targetMinigame.State.score;
            lastObservedStage = targetMinigame.State.stage;
            isFirstFrameCaptured = true;
            return;
        }

        if (lastObservedStage == TicketCustomerStage.Waiting && targetMinigame.State.stage == TicketCustomerStage.WalkingOut)
        {
            if (targetMinigame.State.score == lastObservedScore)
            {
                currentWrongCount++; 
                Debug.Log($"[{name}] ตรวจพบผู้เล่นกดปุ่มตั๋วผิดคิว! นับแต้มสะสมปัจจุบัน: {currentWrongCount} ครั้ง", this);

                UpdateGhostHeightOnServer();

                if (currentWrongCount >= maxWrongAttempts)
                {
                    PlayerHealth actualVictim = FindActiveMinigamePlayer();
                    ExecuteGhostAttack(actualVictim);
                    return;
                }
            }

            lastObservedRound = targetMinigame.State.round;
            lastObservedScore = targetMinigame.State.score;
        }

        if (targetMinigame.State.round != lastObservedRound && targetMinigame.State.stage == TicketCustomerStage.Waiting)
        {
            lastObservedRound = targetMinigame.State.round;
            lastObservedScore = targetMinigame.State.score;
        }

        lastObservedStage = targetMinigame.State.stage;
    }

    private void UpdateGhostHeightOnServer()
    {
        Vector3 newPosition = initialLocalPosition + (Vector3.up * (currentWrongCount * riseAmountPerMistake));
        transform.position = newPosition;
    }

    private PlayerHealth FindActiveMinigamePlayer()
    {
        PlayerHealth[] players = FindObjectsOfType<PlayerHealth>();
        foreach (PlayerHealth p in players)
        {
            if (p != null && !p.IsDead)
            {
                float dist = Vector3.Distance(transform.position, p.transform.position);
                if (dist <= 3.0f) return p;
            }
        }
        return null;
    }

    // 💡 [จุดที่คุณถามถึง]: ปรับจูนฟังก์ชันเปิดฉากโจมตีให้เหลือหน้าที่แค่ส่งไม้ต่อเพียวๆ ไร้รอยต่อ 💡
    private void ExecuteGhostAttack(PlayerHealth victim)
    {
        // สั่งกระโดดพุ่งรันเข้าสู่ฟังก์ชันทำฉากฆ่ารวมเน็ตเวิร์กด้านล่างทันทีโดยไม่ต้องตั้งเวลากิ๊กอื่นซ้ำซ้อน
        TriggerJumpscare(victim); 
    }

    // 💡 [ดึงระบบจากผีตัวแรก]: ฟังก์ชันส่งสัญญาณเน็ตเวิร์กเจาะลึกเฉพาะเครื่องคนกดผิดเพื่อเปิดรูปภาพ
    [TargetRpc]
    private void TargetShowJumpscare(NetworkConnectionToClient target) => ShowJumpscare();

    private void ShowJumpscare()
    {
        if (jumpscareUI != null) jumpscareUI.SetActive(true); // ดีดป้ายภาพหน้าผี PNG โผล่ขึ้นมาหลอนเต็มตา
        Invoke(nameof(HideJumpscare), jumpscareDuration); // สั่งตั้งเวลาถอยหลังเพื่อปิดภาพหน้าผีลงไป
    }

    private void HideJumpscare()
    {
        if (jumpscareUI != null) jumpscareUI.SetActive(false); // ซ่อนภาพลงคืนสู่จอปกติ
    }

    [ClientRpc]
    private void RpcPlayAttackAnimation()
    {
        if (ghostAnimator != null && !string.IsNullOrEmpty(attackTriggerName))
        {
            ghostAnimator.SetTrigger(attackTriggerName);
        }
    }

    private void DestroySelfOnServer()
    {
        if (!HasAiAuthority) return; // เช็คสิทธิ์ความปลอดภัยระบบผู้เป็นเจ้าของสมอง AI (Server Only)

        TicketGhostSpawner spawner = FindObjectOfType<TicketGhostSpawner>();
        if (spawner != null) spawner.NotifyGhostDestroyed();

        if (NetworkServer.active) NetworkServer.Destroy(gameObject); // ทำลายตัวเองทิ้งออกจากเน็ตเวิร์ก
        else Destroy(gameObject);
    }

    protected override void PatrolBehavior() { }
    protected override void ChaseBehavior()  { }
    protected override void SearchBehavior() { }

    // 💡 [ดึงระบบจากผีตัวแรก]: ฟังก์ชันทำดาเมจและสั่งดีดหน้าจอ Jumpscare UI ผ่านสายเน็ตเวิร์ก Mirror อย่างสมบูรณ์ 💡
    public override void TriggerJumpscare(PlayerHealth victim)
    {
        if (!HasAiAuthority || isAttackTriggered) return; // ล็อกความปลอดภัยป้องกันรันซ้ำซ้อน
        isAttackTriggered = true; 

        // คำนวณหักเลือดทำดาเมจหรือฆ่าทันทีคาตู้ตั๋วตามกติกาของเพื่อน
        if (victim != null)
        {
            if (killsOutright) victim.ServerKill("jump scare"); // สั่งฆ่าตายคาที่ทันที
            else victim.TakeDamage(attackDamage); // แผนปกติ: หักเลือดลดวูบ 40 HP ตามเกณฑ์ของคุณ
            Debug.Log($"[{name}] สั่งทำดาเมจหักเลือดสำเร็จ: {attackDamage} HP ไปที่คนชื่อ {victim.name}", this);
        }

        // ดีดคำสั่งแยกมิติอินเทอร์เน็ตในการโชว์ภาพหน้าผีป๊อปอัปสยองขวัญ
        if (NetworkMode.IsOffline)
        {
            ShowJumpscare(); // เล่นคนเดียวออฟไลน์ -> เปิดภาพทันที
        }
        else if (victim != null && victim.connectionToClient != null)
        {
            // เล่นออนไลน์ -> สั่งยิงคำสั่ง TargetRpc ข้ามสายเน็ตเวิร์กไปโผล่หลอนที่เครื่องเหยื่อคนเดียวตรงๆ!
            TargetShowJumpscare(victim.connectionToClient);
        }

        // วิ่งไปแจ้งสคริปต์หลัก GhostManager ห้ามชิงสั่งลบตัวฉันทิ้งกลางคัน
        if (GhostManager.Instance != null) GhostManager.Instance.CancelDespawnTimer();

        // เล่นแอนิเมชันท่าตบซิงค์ออนไลน์พร้อมกันทุกคน
        RpcPlayAttackAnimation();

        // สั่งนับเวลาถอยหลัง 3 วินาทีเพื่อทำลายตัวเองทิ้งและส่งไม้ต่อให้ Spawner ชุบชีวิตผีตัวใหม่ขึ้นมาแทน
        Invoke(nameof(DestroySelfOnServer), jumpscareDuration);
    }
}
