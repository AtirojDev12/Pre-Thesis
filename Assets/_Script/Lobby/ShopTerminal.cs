using UnityEngine;

/// <summary>
/// The lobby shop counter (1 Oct). Look at it and press E: the shop screen
/// opens on YOUR PC only. Buying spends your own saved currency
/// (SaveManager.Current) — nothing here is networked.
///
/// Needs a collider so PlayerInteractor's ray can hit it. Built by
/// Tools > Pre-Thesis > Build 3D Lobby.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShopTerminal : MonoBehaviour, ILocalInteractable, IInteractionHighlight
{
    [SerializeField] private string prompt = "[E] Shop";

    private Renderer[] renderers;
    private MaterialPropertyBlock highlightBlock;
    private bool highlighted;

    public string GetInteractionPrompt() => prompt;
    public bool CanInteract() => isActiveAndEnabled && !ShopUI.IsOpen;
    public Transform GetTransform() => transform;

    public void Interact(GameObject interactor)
    {
        // Only the player on this PC opens their own shop.
        PlayerHealth local = PlayerHealth.LocalInstance;
        if (local == null || interactor != local.gameObject) return;
        ShopUI.Open();
    }

    public void SetHighlighted(bool on)
    {
        if (on == highlighted) return;
        highlighted = on;
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
        if (highlightBlock == null) highlightBlock = new MaterialPropertyBlock();

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (on)
            {
                r.GetPropertyBlock(highlightBlock);
                highlightBlock.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f));
                highlightBlock.SetColor("_Color", new Color(1f, 0.85f, 0.4f));
                r.SetPropertyBlock(highlightBlock);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }

    private void OnDisable() => SetHighlighted(false);
}
