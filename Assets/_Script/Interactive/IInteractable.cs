using UnityEngine;

/// <summary>
/// Implement this on anything the player should be able to interact with.
/// This is the only contract the rest of the system depends on, so adding new
/// interactable behaviour later just means creating a new class that implements
/// this interface -- no changes needed to the detector or UI.
///
/// NETWORKING CONTRACT (read before implementing):
/// - GetInteractionPrompt() / CanInteract() / GetTransform() are called on the
///   LOCAL client every frame to drive the prompt UI. Keep them cheap, and do
///   not change game state in them.
/// - Interact() runs on the SERVER only (PlayerInteractor routes the local
///   key press through a [Command] and the server re-validates it). Any state
///   you change in Interact() must be replicated -- a [SyncVar] or a
///   [ClientRpc] -- or only the server will ever see the change.
/// - Anything implementing this that must replicate state needs a
///   NetworkIdentity on the same GameObject. Inheriting InteractableBase gives
///   you that automatically.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Text shown in the prompt UI, e.g. "Press E to open chest".
    /// Return null/empty to hide the prompt for this object.
    /// Called every frame on the local client -- avoid allocating here
    /// (build strings once and cache them, don't concatenate per call).
    /// </summary>
    string GetInteractionPrompt();

    /// <summary>
    /// Whether this object can currently be interacted with.
    /// Useful for locked doors, cooldowns, quest gating, etc.
    /// Checked on the client for UI purposes AND re-checked on the server
    /// before the interaction is allowed to run.
    /// </summary>
    bool CanInteract();

    /// <summary>
    /// Called when the interaction actually happens. SERVER-SIDE ONLY.
    /// </summary>
    void Interact(GameObject interactor);

    /// <summary>
    /// The transform used to position the world-space prompt, and to look up
    /// the NetworkIdentity when routing the interaction to the server.
    /// Usually just `transform` on the implementing MonoBehaviour.
    /// </summary>
    Transform GetTransform();
}
