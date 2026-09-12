using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the player. Casts a ray forward each frame -- the player must be
/// looking directly at an interactable's collider for the prompt to appear.
///
/// Split of responsibilities:
/// - Raycasting, highlighting and the prompt UI are LOCAL and client-side, so
///   they stay instant and never wait on the network.
/// - The actual interaction is sent to the SERVER as a [Command], and the
///   server re-validates range and CanInteract() before running it. A client
///   never gets to assert "I interacted with that" on its own.
/// </summary>
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("Origin/direction of the interaction ray. Leave empty to use this player's own camera.")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float interactionRange = 3f;

    [Tooltip("Set this to a dedicated 'Interactable' layer. Leaving it as Everything lets the ray hit the player's own collider and block interaction.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("Input")]
    [SerializeField] private Key interactKey = Key.E;

    [Header("References")]
    [Tooltip("Optional. If empty, this finds a prompt UI on the player prefab, then falls back to one in the scene.")]
    [SerializeField] private InteractionPromptUI promptUI;

    [Header("Server validation")]
    [Tooltip("Extra slack the SERVER allows when re-checking range, to absorb latency. Too large weakens the check; too small rejects honest interactions.")]
    [SerializeField] private float serverRangeTolerance = 1.5f;

    private IInteractable _currentTarget;

    // Tracked so the prompt UI is only touched when something actually changed.
    // Assigning TMP text every frame forces a mesh rebuild for no reason.
    private IInteractable _promptTarget;
    private string _lastPromptText;

    private void Awake()
    {
        if (rayOrigin == null)
        {
            // Deliberately NOT Camera.main: in a 6-player match every client
            // holds six copies of this prefab, and Camera.main would hand all
            // of them the same camera. Use the camera on this player instead.
            Camera ownCamera = GetComponentInChildren<Camera>(true);
            rayOrigin = ownCamera != null ? ownCamera.transform : transform;
        }
    }

    private void Start()
    {
        // Offline test scenes never fire OnStartLocalPlayer.
        if (NetworkMode.IsOffline) ResolvePromptUI();
    }

    public override void OnStartLocalPlayer()
    {
        ResolvePromptUI();
    }

    /// <summary>
    /// A prefab cannot hold a reference to a scene object, so a runtime-spawned
    /// player arrives with promptUI empty. Find it: prefer one carried on the
    /// player itself, otherwise take the one sitting in the scene.
    /// </summary>
    private void ResolvePromptUI()
    {
        if (promptUI != null) return;

        promptUI = GetComponentInChildren<InteractionPromptUI>(true);
        // FindAnyObjectByType, not FindFirstObjectByType: the "First" variant is
        // deprecated in Unity 6.5 because it depends on instance-ID ordering,
        // and we only need one, whichever it is.
        if (promptUI == null) promptUI = FindAnyObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);

        if (promptUI == null)
            Debug.LogWarning("[PlayerInteractor] No InteractionPromptUI found -- interactions will still work, but no prompt will be shown.", this);
    }

    private void Update()
    {
        // Every client runs one copy of this prefab per connected player.
        // Without this guard, pressing E on one machine would fire an
        // interaction for all six player objects at once.
        if (!NetworkMode.IsLocalController(this)) return;

        IInteractable hit = RaycastForInteractable();

        if (!ReferenceEquals(hit, _currentTarget))
        {
            SetHighlight(_currentTarget, false);
            _currentTarget = hit;
            SetHighlight(_currentTarget, true);
        }

        UpdatePrompt();

        if (_currentTarget != null
            && Keyboard.current != null
            && Keyboard.current[interactKey].wasPressedThisFrame
            && _currentTarget.CanInteract())
        {
            RequestInteract(_currentTarget);
        }
    }

    private void UpdatePrompt()
    {
        if (promptUI == null) return;

        if (_currentTarget == null)
        {
            if (_promptTarget != null)
            {
                _promptTarget = null;
                _lastPromptText = null;
                promptUI.Hide();
            }
            return;
        }

        string text = _currentTarget.GetInteractionPrompt();

        // Compare the target too, not just the text: two different objects can
        // share the same prompt string, and the prompt has to move to the new
        // one even though the words didn't change.
        if (!ReferenceEquals(_currentTarget, _promptTarget) || text != _lastPromptText)
        {
            _promptTarget = _currentTarget;
            _lastPromptText = text;
            promptUI.Show(text, _currentTarget.GetTransform());
        }
    }

    /// <summary>
    /// Casts a ray from rayOrigin forward. Returns null if nothing is hit, the
    /// hit isn't an IInteractable, it's out of range, or CanInteract() is false.
    /// </summary>
    private IInteractable RaycastForInteractable()
    {
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, interactionRange, interactableLayers))
        {
            IInteractable interactable = hitInfo.collider.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract())
                return interactable;
        }

        return null;
    }

    private void RequestInteract(IInteractable target)
    {
        if (NetworkMode.IsOffline)
        {
            // Solo test scene: no server to ask, so run it directly.
            target.Interact(gameObject);
            return;
        }

        NetworkIdentity targetIdentity = target.GetTransform().GetComponent<NetworkIdentity>();
        if (targetIdentity == null)
        {
            Debug.LogWarning(
                $"[PlayerInteractor] '{target.GetTransform().name}' has no NetworkIdentity, so its interaction cannot be " +
                "replicated to other players. Add one (inheriting InteractableBase does this for you).",
                target.GetTransform());
            return;
        }

        CmdInteract(targetIdentity);
    }

    /// <summary>
    /// Runs on the SERVER. Everything the client claimed is re-checked here --
    /// the client only gets to say WHICH object it wants, never that the
    /// interaction was legal.
    /// </summary>
    [Command]
    private void CmdInteract(NetworkIdentity targetIdentity)
    {
        if (targetIdentity == null) return;

        IInteractable target = targetIdentity.GetComponent<IInteractable>();
        if (target == null) return;

        if (!target.CanInteract()) return;

        // Re-check range against the server's own view of where this player is,
        // so a modified client can't interact with something across the map.
        float maxDistance = interactionRange + serverRangeTolerance;
        Vector3 offset = target.GetTransform().position - transform.position;
        if (offset.sqrMagnitude > maxDistance * maxDistance)
        {
            Debug.LogWarning(
                $"[PlayerInteractor] Rejected an out-of-range interaction on '{targetIdentity.name}' " +
                $"({offset.magnitude:F1}m > {maxDistance:F1}m allowed).", this);
            return;
        }

        target.Interact(gameObject);
    }

    private void SetHighlight(IInteractable target, bool state)
    {
        if (target is InteractableBase baseInteractable)
            baseInteractable.SetHighlighted(state);
    }

    private void OnDisable()
    {
        // Don't leave a prompt hanging on screen if the player is despawned,
        // dies, or the scene changes.
        SetHighlight(_currentTarget, false);
        _currentTarget = null;
        _promptTarget = null;
        _lastPromptText = null;
        if (promptUI != null) promptUI.Hide();
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = rayOrigin != null ? rayOrigin : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * interactionRange);
    }
}
