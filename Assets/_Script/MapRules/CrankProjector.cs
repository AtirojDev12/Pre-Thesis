using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): the hand-crank projector of Map Rule 3 (VIP guest).
/// Once the VIP is seated, press E fast here to play its film (MapRuleTuning.crankPresses).
/// Placed in the ghost screen room by the setup tool (for now). Model: the "Model" child.
/// </summary>
public sealed class CrankProjector : InteractableBase
{
    [Tooltip("Turns a little on every crank (optional).")]
    [SerializeField] private Transform handle;

    private int shownCranks = -1;

    public override string GetInteractionPrompt()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        MapRuleTuning t = MapRuleTuning.Current;
        if (vip == null) return "";
        if (vip.Current == MapRuleVip.Stage.Arrived) return t.crankWaitPrompt;
        return $"{t.crankPrompt}  ({vip.Cranks} / {t.crankPresses})";
    }

    public override bool CanInteract()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        return vip != null && vip.Current == MapRuleVip.Stage.Seated && base.CanInteract();
    }

    protected override void OnInteracted(GameObject interactor)
    {
        if (MapRuleVip.Instance != null) MapRuleVip.Instance.ServerCrank(interactor);
    }

    private void Update()
    {
        MapRuleVip vip = MapRuleVip.Instance;
        int now = vip != null ? vip.Cranks : 0;
        if (now == shownCranks) return;
        shownCranks = now;
        if (handle != null) handle.localRotation = Quaternion.Euler(now * 36f, 0f, 0f);
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetHandle(Transform t) => handle = t;
}
