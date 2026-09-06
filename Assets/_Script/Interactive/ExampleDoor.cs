using UnityEngine;

/// <summary>
/// EXAMPLE ONLY — shows the pattern for adding a new interactable type.
/// To add future interactables (levers, pickups, NPCs, chests...), copy
/// this pattern: inherit InteractableBase, set the prompt, override
/// OnInteracted().
/// </summary>
public class ExampleDoor : InteractableBase
{
    [SerializeField] private Animator doorAnimator;
    private bool _isOpen = false;

    protected override void Awake()
    {
        base.Awake();
        interactionPrompt = "Press E to open door";
    }

    protected override void OnInteracted(GameObject interactor)
    {
        _isOpen = !_isOpen;
        interactionPrompt = _isOpen ? "Press E to close door" : "Press E to open door";

        if (doorAnimator != null)
            doorAnimator.SetBool("IsOpen", _isOpen);
    }
}
