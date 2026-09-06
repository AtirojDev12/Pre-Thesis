using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the player (or camera). Casts a ray forward each frame — the
/// player must be looking directly at an interactable's collider for the
/// prompt to appear. The prompt hides the instant the ray leaves the
/// collider or the target moves out of range.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("Raycast")]
    [Tooltip("Origin/direction of the interaction ray. Usually the main camera. If left empty, uses this transform.")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float interactionRange = 3f;
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("Input")]
    [SerializeField] private Key interactKey = Key.E;

    [Header("References")]
    [SerializeField] private InteractionPromptUI promptUI;

    private IInteractable _currentTarget;

    private void Awake()
    {
        if (rayOrigin == null)
            rayOrigin = Camera.main != null ? Camera.main.transform : transform;
    }

    private void Update()
    {
        IInteractable hit = RaycastForInteractable();

        // Target changed (including hit -> null, i.e. ray left the collider / went out of range)
        if (hit != _currentTarget)
        {
            SetHighlight(_currentTarget, false);
            _currentTarget = hit;
            SetHighlight(_currentTarget, true);

            if (promptUI != null)
            {
                if (_currentTarget != null)
                    promptUI.Show(_currentTarget.GetInteractionPrompt(), _currentTarget.GetTransform());
                else
                    promptUI.Hide();
            }
        }
        else if (_currentTarget != null && promptUI != null)
        {
            // Keep prompt text fresh in case CanInteract()/prompt changes dynamically
            promptUI.Show(_currentTarget.GetInteractionPrompt(), _currentTarget.GetTransform());
        }

        if (_currentTarget != null
            && Keyboard.current != null
            && Keyboard.current[interactKey].wasPressedThisFrame
            && _currentTarget.CanInteract())
        {
            _currentTarget.Interact(gameObject);
        }
    }

    /// <summary>
    /// Casts a ray from rayOrigin forward. Returns null if nothing is hit,
    /// the hit isn't an IInteractable, it's out of range, or CanInteract() is false.
    /// </summary>
    private IInteractable RaycastForInteractable()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, interactionRange, interactableLayers))
        {
            var interactable = hitInfo.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract())
                return interactable;
        }

        return null;
    }

    private void SetHighlight(IInteractable target, bool state)
    {
        if (target is InteractableBase baseInteractable)
            baseInteractable.SetHighlighted(state);
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = rayOrigin != null ? rayOrigin : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * interactionRange);
    }
}