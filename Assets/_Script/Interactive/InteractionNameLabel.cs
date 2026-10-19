using UnityEngine;

/// <summary>Scene-authored focus name; adds no E action or network state.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Pre-Thesis/Interaction/Interaction Name Label")]
public sealed class InteractionNameLabel : MonoBehaviour, IInteractable
{
    [Tooltip("Name shown on the player's HUD while aiming at this object or its child colliders. Empty hides the name.")]
    [SerializeField] private string displayName = "Object Name";

    public string DisplayName => displayName;
    public string GetInteractionPrompt() => displayName;
    public Transform GetTransform() => transform;
    public bool CanInteract() => false;
    public void Interact(GameObject interactor) { }
}
