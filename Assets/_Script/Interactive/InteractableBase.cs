using UnityEngine;

/// <summary>
/// Optional convenience base class. You don't have to inherit from this —
/// implementing IInteractable directly is enough — but this handles the
/// common cases (prompt text, enable/disable, one-time-use) so future
/// interactables can just override Interact() and OnInteracted().
/// </summary>
public abstract class InteractableBase : MonoBehaviour, IInteractable
{
    [Tooltip("Text shown to the player, e.g. 'Press E to open'")]
    [SerializeField] protected string interactionPrompt = "Interact";

    [Tooltip("If true, this object can only be interacted with once")]
    [SerializeField] protected bool oneTimeUse = false;

    [Tooltip("Optional: renderer to highlight when player is in range")]
    [SerializeField] protected Renderer highlightRenderer;
    [SerializeField] protected Color highlightColor = Color.yellow;

    private Color _originalColor;
    private bool _hasBeenUsed = false;
    private bool _isHighlighted = false;

    protected virtual void Awake()
    {
        if (highlightRenderer != null)
            _originalColor = highlightRenderer.material.color;
    }

    public virtual string GetInteractionPrompt() => interactionPrompt;

    public virtual bool CanInteract() => !(oneTimeUse && _hasBeenUsed);

    public Transform GetTransform() => transform;

    public void Interact(GameObject interactor)
    {
        if (!CanInteract()) return;

        OnInteracted(interactor);
        _hasBeenUsed = true;
    }

    /// <summary>
    /// Override this in derived classes to define what actually happens.
    /// This is the main extension point for new interactable types.
    /// </summary>
    protected abstract void OnInteracted(GameObject interactor);

    /// <summary>
    /// Called by PlayerInteractor when this becomes/stops being the focused target.
    /// Override to customize highlight behaviour (outline shader, glow, etc).
    /// </summary>
    public virtual void SetHighlighted(bool highlighted)
    {
        if (highlightRenderer == null || _isHighlighted == highlighted) return;

        _isHighlighted = highlighted;
        highlightRenderer.material.color = highlighted ? highlightColor : _originalColor;
    }
}
