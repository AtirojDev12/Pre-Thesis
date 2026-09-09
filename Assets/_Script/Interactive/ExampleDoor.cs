using Mirror;
using UnityEngine;

/// <summary>
/// EXAMPLE ONLY -- shows the pattern for adding a new networked interactable.
/// To add future interactables (levers, pickups, NPCs, chests...), copy this
/// shape: inherit InteractableBase, put shared state in a [SyncVar], change it
/// in OnInteracted (which runs on the server), and apply the visual result in
/// the SyncVar hook so every client stays in step.
/// </summary>
public class ExampleDoor : InteractableBase
{
    [SerializeField] private Animator doorAnimator;
    [SerializeField] private string openPrompt = "Press E to open door";
    [SerializeField] private string closePrompt = "Press E to close door";

    // Server-owned and replicated: the door has to look the same on every
    // machine. A plain bool would only ever open the door for one player.
    [SyncVar(hook = nameof(OnOpenStateChanged))]
    private bool isOpen;

    // The prompt is derived from state rather than assigned in Awake. The old
    // version overwrote the serialized field at startup, which silently threw
    // away whatever a designer typed into the Inspector.
    public override string GetInteractionPrompt() => isOpen ? closePrompt : openPrompt;

    /// <summary>Runs on the SERVER (see InteractableBase.Interact).</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        isOpen = !isOpen;

        // Mirror does not call a SyncVar hook on the machine that made the
        // change, so the server/host has to apply its own visual result here.
        ApplyOpenState(isOpen);
    }

    private void OnOpenStateChanged(bool oldValue, bool newValue) => ApplyOpenState(newValue);

    private void ApplyOpenState(bool open)
    {
        if (doorAnimator != null) doorAnimator.SetBool("IsOpen", open);
    }

    /// <summary>
    /// A player joining mid-match must see doors in the state they are actually
    /// in, not the closed default baked into the prefab.
    /// </summary>
    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyOpenState(isOpen);
    }
}
