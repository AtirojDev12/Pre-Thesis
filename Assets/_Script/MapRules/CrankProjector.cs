using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): the hand-crank projector of Map Rule 3 (VIP guest).
/// It is SWAPPED IN only when the time comes: while the VIP is here, the ghost room's own
/// projector is hidden and this crank projector stands in its place (facing the screen).
/// When the VIP is served (or the time runs out) the normal projector comes back.
/// 23 Nov: first put the VIP film in (gold reel from the ghost room film shelf, E with it in hand),
/// then, once the VIP is seated, press E fast here to play the film (MapRuleTuning.crankPresses).
/// While it is swapped in, the film chore's box is switched off so the crank can be aimed at.
/// Placed by Tools > Pre-Thesis > One Step > Map Rules. Model: the "Model" child.
/// </summary>
public sealed class CrankProjector : InteractableBase
{
    [Tooltip("Turns a little on every crank (optional).")]
    [SerializeField] private Transform handle;
    [Tooltip("The crank projector's look (hidden until the VIP comes).")]
    [SerializeField] private GameObject model;
    [Tooltip("The room's normal projector (hidden while the crank projector is in).")]
    [SerializeField] private GameObject original;
    [Tooltip("Film chore box at the same place (switched off while the crank projector is in).")]
    [SerializeField] private Collider filmChoreBox;

    private int shownCranks = -1;
    private int shownIn = -1;
    private Collider ownCollider;
    private Renderer[] originalRenderers;
    private Collider[] originalColliders;   // 23 Nov fix: they blocked the E ray (only "PROJECTOR" showed)
    private bool[] originalCollidersOn;

    protected override void Awake()
    {
        base.Awake();
        ownCollider = GetComponent<Collider>();
        originalRenderers = original != null ? original.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        originalColliders = original != null ? original.GetComponentsInChildren<Collider>(true) : new Collider[0];
        originalCollidersOn = new bool[originalColliders.Length];
        for (int i = 0; i < originalColliders.Length; i++)
            originalCollidersOn[i] = originalColliders[i] != null && originalColliders[i].enabled;
        Apply(false);
    }

    public override string GetInteractionPrompt()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        MapRuleTuning t = MapRuleTuning.Current;
        if (vip == null || !vip.Running) return "";
        if (!vip.FilmIn) return CarryProp.LocalHolds(MapRuleVip.FilmId) ? t.crankFilmPrompt : t.crankNeedFilmPrompt;
        if (vip.Current == MapRuleVip.Stage.Arrived) return t.crankWaitPrompt;
        return $"{t.crankPrompt}  ({vip.Cranks} / {t.crankPresses})";
    }

    public override bool CanInteract()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        if (vip == null || !vip.Running || !base.CanInteract()) return false;
        // 23 Nov: first the VIP film (any time the VIP is here), then the crank (VIP seated).
        return !vip.FilmIn || vip.Current == MapRuleVip.Stage.Seated;
    }

    protected override void OnInteracted(GameObject interactor)
    {
        if (MapRuleVip.Instance != null) MapRuleVip.Instance.ServerUseProjector(interactor);
    }

    private void Update()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        bool swapIn = vip != null && vip.Running;
        if ((swapIn ? 1 : 0) != shownIn) Apply(swapIn);

        int now = vip != null ? vip.Cranks : 0;
        if (now == shownCranks) return;
        shownCranks = now;
        if (handle != null) handle.localRotation = Quaternion.Euler(now * 36f, 0f, 0f);
    }

    /// <summary>Crank projector in (true) or the normal projector back (false). Every PC does the same.</summary>
    private void Apply(bool swapIn)
    {
        shownIn = swapIn ? 1 : 0;
        if (model != null) model.SetActive(swapIn);
        if (ownCollider != null) ownCollider.enabled = swapIn;
        if (filmChoreBox != null) filmChoreBox.enabled = !swapIn;
        if (originalRenderers != null)
            foreach (Renderer r in originalRenderers) if (r != null) r.enabled = !swapIn;
        // 23 Nov fix (clip): the hidden normal projector's colliders still caught the E ray first,
        // so you saw its "PROJECTOR" label and could not put the VIP film in. Off while swapped in.
        if (originalColliders != null)
            for (int i = 0; i < originalColliders.Length; i++)
            {
                Collider c = originalColliders[i];
                if (c == null || c == ownCollider || c == filmChoreBox) continue;
                c.enabled = !swapIn && originalCollidersOn[i];
            }
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetup(Transform crankHandle, GameObject look, GameObject normalProjector, Collider choreBox)
    {
        handle = crankHandle;
        model = look;
        original = normalProjector;
        filmChoreBox = choreBox;
    }
}
