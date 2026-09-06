using UnityEngine;

/// <summary>
/// Implement this on anything the player should be able to interact with.
/// This is the only contract the rest of the system depends on, so adding
/// new interactable behaviour later just means creating a new class that
/// implements this interface — no changes needed to the detector or UI.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Text shown in the prompt UI, e.g. "Press E to open chest".
    /// Return null/empty to hide the prompt for this object.
    /// </summary>
    string GetInteractionPrompt();

    /// <summary>
    /// Whether this object can currently be interacted with.
    /// Useful for locked doors, cooldowns, quest gating, etc.
    /// </summary>
    bool CanInteract();

    /// <summary>
    /// Called when the player actually performs the interaction.
    /// </summary>
    void Interact(GameObject interactor);

    /// <summary>
    /// The transform used to position the world-space prompt / outline.
    /// Usually just `transform` on the implementing MonoBehaviour.
    /// </summary>
    Transform GetTransform();
}
