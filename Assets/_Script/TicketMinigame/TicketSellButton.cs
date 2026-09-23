using UnityEngine;

[DisallowMultipleComponent]
public sealed class TicketSellButton : MonoBehaviour, IInteractable, IInteractionHighlight
{
    [SerializeField] private TicketMinigame minigame;
    [SerializeField] private bool ghostTicket;

    [Header("Aim highlight")]
    [SerializeField] private Renderer highlightRenderer;
    [SerializeField] private Color highlightColor = new Color(1f, 0.94f, 0.35f, 1f);

    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock highlightedProperties;
    private bool isHighlighted;

    public void SetHighlighted(bool highlighted)
    {
        highlighted = highlighted && CanInteract();
        if (highlighted == isHighlighted) return;
        if (highlightRenderer == null) highlightRenderer = GetComponent<Renderer>();
        if (highlightRenderer == null) return;

        if (highlighted)
        {
            if (originalProperties == null) originalProperties = new MaterialPropertyBlock();
            if (highlightedProperties == null) highlightedProperties = new MaterialPropertyBlock();
            // Preserve existing overrides and avoid modifying the shared material.
            highlightRenderer.GetPropertyBlock(originalProperties);
            highlightRenderer.GetPropertyBlock(highlightedProperties);
            highlightedProperties.SetColor("_BaseColor", highlightColor);
            highlightedProperties.SetColor("_Color", highlightColor);
            highlightRenderer.SetPropertyBlock(highlightedProperties);
        }
        else
        {
            highlightRenderer.SetPropertyBlock(originalProperties);
        }
        isHighlighted = highlighted;
    }

    private void OnDisable() => SetHighlighted(false);

    public string GetInteractionPrompt() => ghostTicket ? "[E] Sell ghost ticket" : "[E] Sell human ticket";
    public bool CanInteract() => isActiveAndEnabled && minigame != null && minigame.CanServe;
    public Transform GetTransform() => transform;
    public void Interact(GameObject interactor)
    {
        if (CanInteract()) minigame.RequestSale(ghostTicket, interactor);
    }
}
