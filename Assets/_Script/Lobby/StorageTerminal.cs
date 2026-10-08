using UnityEngine;

/// <summary>
/// 8 Oct (Mr.k). The locker in the lobby STORAGE ROOM. Look at it and press E:
/// opens YOUR OWN storage (StorageUI). One room for everybody, but each player
/// only sees their own items. Same interaction setup as ShopTerminal (local only,
/// no network: the storage itself is server-checked in PlayerInventory).
///
/// Placed by Tools > Pre-Thesis > Scenes > Lobby: Add Spawn Points + Storage Room.
/// </summary>
[DisallowMultipleComponent]
public sealed class StorageTerminal : MonoBehaviour, ILocalInteractable, IInteractionHighlight
{
    [SerializeField] private string prompt = "[E] Storage (your items)";

    private Renderer[] renderers;
    private MaterialPropertyBlock highlightBlock;
    private bool highlighted;

    public string GetInteractionPrompt() => prompt;
    public bool CanInteract() => isActiveAndEnabled && !StorageUI.IsOpen;
    public Transform GetTransform() => transform;

    public void Interact(GameObject interactor)
    {
        // Only the player on this PC opens their own shop.
        PlayerHealth local = PlayerHealth.LocalInstance;
        if (local == null || interactor != local.gameObject) return;
        StorageUI.Open();
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
