using Mirror;
using UnityEngine;

/// <summary>
/// Optional convenience base class. You don't have to inherit from this --
/// implementing IInteractable directly is enough -- but this handles the common
/// cases (prompt text, highlight, one-time-use) so future interactables can just
/// override OnInteracted().
///
/// It is a NetworkBehaviour because almost every interactable in a co-op horror
/// game has state the whole party must agree on: a door is open for everyone, a
/// one-time pickup is gone for everyone. Inheriting this gives you a
/// NetworkIdentity automatically (NetworkBehaviour requires one).
/// </summary>
public abstract class InteractableBase : NetworkBehaviour, IInteractable
{
    [Tooltip("Text shown to the player, e.g. 'Press E to open'")]
    [SerializeField] protected string interactionPrompt = "Interact";

    [Tooltip("If true, this object can only be interacted with once -- for everyone, not once per client")]
    [SerializeField] protected bool oneTimeUse = false;

    [Tooltip("Optional: renderer to tint when the player is looking at this")]
    [SerializeField] protected Renderer highlightRenderer;
    [SerializeField] protected Color highlightColor = Color.yellow;

    // Server-owned and replicated. Previously this was a plain local bool,
    // which meant a "one time use" object could be used once by EVERY player
    // because each client tracked its own copy.
    [SyncVar] private bool hasBeenUsed;

    // Highlighting uses a MaterialPropertyBlock rather than reading/writing
    // renderer.material. Touching .material instantiates a private copy of the
    // material for that object at runtime -- an allocation plus a leaked
    // material instance for every interactable in the level. A property block
    // changes the tint with no allocation and no new material.
    private MaterialPropertyBlock _propertyBlock;
    private int _colorPropertyId;
    private bool _canHighlight;
    private Color _originalColor;
    private bool _isHighlighted;

    // URP shaders expose "_BaseColor"; the old built-in pipeline used "_Color".
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    protected virtual void Awake()
    {
        CacheHighlightTarget();
    }

    private void CacheHighlightTarget()
    {
        if (highlightRenderer == null) return;

        // sharedMaterial, not material -- see the note above.
        Material shared = highlightRenderer.sharedMaterial;
        if (shared == null) return;

        if (shared.HasProperty(BaseColorId)) _colorPropertyId = BaseColorId;
        else if (shared.HasProperty(LegacyColorId)) _colorPropertyId = LegacyColorId;
        else return; // shader has no color property we know how to tint

        _originalColor = shared.GetColor(_colorPropertyId);
        _propertyBlock = new MaterialPropertyBlock();
        _canHighlight = true;
    }

    public virtual string GetInteractionPrompt() => interactionPrompt;

    public virtual bool CanInteract() => !(oneTimeUse && hasBeenUsed);

    public Transform GetTransform() => transform;

    /// <summary>
    /// SERVER-SIDE ONLY. PlayerInteractor routes client key presses through a
    /// [Command], so by the time execution reaches here we are on the machine
    /// allowed to change state. Calling this directly from client code is a bug
    /// and is refused below.
    /// </summary>
    public void Interact(GameObject interactor)
    {
        if (!NetworkMode.HasServerAuthority(this))
        {
            Debug.LogWarning(
                $"[{name}] Interact() was called on a machine with no server authority, so it was ignored. " +
                "Interactions must go through PlayerInteractor, which routes them to the server.", this);
            return;
        }

        if (!CanInteract()) return;

        // Only latch the flag when it actually means something. Previously this
        // was set on every interaction, so the field didn't match its name.
        if (oneTimeUse) hasBeenUsed = true;

        OnInteracted(interactor);
    }

    /// <summary>
    /// Override this to define what actually happens. Runs on the SERVER.
    /// Anything you change here must be replicated (a [SyncVar] or a
    /// [ClientRpc]) or only the server will see it. See ExampleDoor.
    /// </summary>
    protected abstract void OnInteracted(GameObject interactor);

    /// <summary>
    /// Purely cosmetic, purely local -- called by the local player's
    /// PlayerInteractor when this becomes/stops being the focused target.
    /// Never replicate this: each player highlights what THEY are looking at.
    /// </summary>
    public virtual void SetHighlighted(bool highlighted)
    {
        if (!_canHighlight || _isHighlighted == highlighted) return;
        _isHighlighted = highlighted;

        highlightRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(_colorPropertyId, highlighted ? highlightColor : _originalColor);
        highlightRenderer.SetPropertyBlock(_propertyBlock);
    }
}
