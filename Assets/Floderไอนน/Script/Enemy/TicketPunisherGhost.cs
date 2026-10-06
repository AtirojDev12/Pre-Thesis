using Mirror;
using UnityEngine;

/// <summary>Stationary ticket punishment, driven by accepted sales on the server.</summary>
public class TicketPunisherGhost : Enemy_Abstract_Class
{
    [Header("Punishment Settings")]
    [SerializeField, Min(1)] private int maxWrongAttempts = 3;
    [SerializeField, Min(0)] private float riseAmountPerMistake = 0.8f;
    [SerializeField, Min(0)] private float attackDamage = 40f;
    [Header("Jumpscare")]
    [SerializeField] private GameObject jumpscareUI;
    [SerializeField, Min(0.1f)] private float jumpscareDuration = 3f;
    [SerializeField] private bool killsOutright;
    [SerializeField] private string attackTriggerName = "Attack";

    [SyncVar] private int currentWrongCount;
    private Vector3 initialPosition;
    private bool isAttackTriggered;
    private Animator ghostAnimator;
    private TicketGhostSpawner owner;
    private bool isShowingJumpscare;

    public void Initialize(TicketGhostSpawner spawner)
    {
        owner = spawner;
        // The authored spawn point already describes the hidden position.
        initialPosition = transform.position;
    }

    protected override void Start()
    {
        base.Start();
        if (agent != null) agent.enabled = false;
        ghostAnimator = GetComponent<Animator>();
        ResolveJumpscareUI();
        // Another ghost may already be displaying the shared overlay.
    }

    private void ResolveJumpscareUI()
    {
        if (jumpscareUI == null)
        {
            GameObject fallback = null;
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                // GAME HUD moves its inactive overlay into DontDestroyOnLoad.
                // Prefer this scene, but also accept loaded persistent UI.
                if (!obj.scene.IsValid() || !obj.scene.isLoaded || !obj.CompareTag("JumpscareUI")) continue;
                if (obj.scene == gameObject.scene)
                {
                    jumpscareUI = obj;
                    return;
                }
                if (fallback == null) fallback = obj;
            }
            jumpscareUI = fallback;
        }
    }

    public void RecordWrongSale(PlayerHealth seller)
    {
        if (!HasAiAuthority || isAttackTriggered || !PlayerRegistry.IsHuntable(seller)) return;
        currentWrongCount++;
        transform.position = initialPosition + Vector3.up * (currentWrongCount * riseAmountPerMistake);
        if (currentWrongCount >= Mathf.Max(1, maxWrongAttempts)) TriggerJumpscare(seller);
        else GetComponent<GhostWarningSource>()?.WarnPlayer(seller);
    }

    protected override void CheckForPlayer() { }
    protected override void PatrolBehavior() { }
    protected override void ChaseBehavior() { }
    protected override void SearchBehavior() { }

    public override void TriggerJumpscare(PlayerHealth victim)
    {
        if (!HasAiAuthority || isAttackTriggered || !PlayerRegistry.IsHuntable(victim)) return;
        isAttackTriggered = true;
        GetComponent<GhostWarningSource>()?.SetWarningActive(false);
        if (killsOutright) victim.ServerKill("ticket ghost jumpscare");
        else victim.TakeDamage(attackDamage);
        if (NetworkMode.IsOffline)
        {
            ShowJumpscare();
            PlayAttackAnimation();
        }
        else
        {
            if (victim.connectionToClient != null) TargetShowJumpscare(victim.connectionToClient);
            RpcPlayAttackAnimation();
        }
        Invoke(nameof(DestroySelfOnServer), jumpscareDuration);
    }

    [TargetRpc]
    private void TargetShowJumpscare(NetworkConnectionToClient target) => ShowJumpscare();

    private void ShowJumpscare()
    {
        PlayerGhostWarningController.SuppressLocal(jumpscareDuration);
        // Resolve again for late-created HUDs and RPCs arriving before Start.
        ResolveJumpscareUI();
        if (jumpscareUI != null)
        {
            jumpscareUI.SetActive(true);
            isShowingJumpscare = true;
        }
        else Debug.LogWarning("[Ticket Ghost] No loaded overlay tagged JumpscareUI was found.", this);
        Invoke(nameof(HideJumpscare), jumpscareDuration);
    }

    private void HideJumpscare()
    {
        if (isShowingJumpscare && jumpscareUI != null) jumpscareUI.SetActive(false);
        isShowingJumpscare = false;
    }

    // A server despawn can arrive before the client's local hide timer.
    private void OnDestroy() => HideJumpscare();

    [ClientRpc]
    private void RpcPlayAttackAnimation() => PlayAttackAnimation();

    private void PlayAttackAnimation()
    {
        if (ghostAnimator != null && !string.IsNullOrEmpty(attackTriggerName))
            ghostAnimator.SetTrigger(attackTriggerName);
    }

    private void DestroySelfOnServer()
    {
        if (!HasAiAuthority) return;
        if (owner != null) owner.NotifyGhostDestroyed(this);
        if (NetworkServer.active) NetworkServer.Destroy(gameObject);
        else Destroy(gameObject);
    }
}
