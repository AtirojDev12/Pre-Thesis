using Mirror;
using System.Collections.Generic;
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
public partial class PlayerInteractor : NetworkBehaviour
{
    [Header("Raycast")]
    [Tooltip("Origin/direction of the interaction ray. Leave empty to use this player's own camera.")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float interactionRange = 3f;

    [Tooltip("Layers that can block interaction. This player's own colliders are ignored automatically.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    // 20 Oct (Mr.k): the interact key is in Settings > Controls (Keybinds.Interact, default E).

    [Header("References")]
    [Tooltip("Optional. If empty, this finds a prompt UI on the player prefab, then falls back to one in the scene.")]
    [SerializeField] private InteractionPromptUI promptUI;

    [Tooltip("Show only the focused object's name, including unavailable targets with an enabled collider.")]
    [SerializeField] private bool showNamesOnly = false;

    [Header("Focus logging")]
    [Tooltip("Log the focused interactable, its prompt and availability when they change. Local player only.")]
    [SerializeField] private bool logFocusedInteractable = false;

    [Header("Server validation")]
    [Tooltip("Extra slack the SERVER allows when re-checking range, to absorb latency. Too large weakens the check; too small rejects honest interactions.")]
    [SerializeField] private float serverRangeTolerance = 1.5f;

    private IInteractable _currentTarget;
    private InteractionNameLabel _focusedNameLabel;
    private readonly List<MonoBehaviour> _interactionComponents = new List<MonoBehaviour>();
    private readonly RaycastHit[] _raycastHits = new RaycastHit[16];
    private PlayerHealth _health;
    private Camera _viewCamera;

    // Tracked so the prompt UI is only touched when something actually changed.
    // Assigning TMP text every frame forces a mesh rebuild for no reason.
    private IInteractable _promptTarget;
    private string _lastPromptText;
    private IInteractable _loggedTarget;
    private string _loggedPrompt;
    private bool _loggedCanInteract;

    private void Awake()
    {
        _health = GetComponent<PlayerHealth>();
        _viewCamera = GetComponentInChildren<Camera>(true);
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
        if (promptUI == null && PersistentHUD.Instance != null)
            promptUI = PersistentHUD.Instance.GetComponentInChildren<InteractionPromptUI>(true);
        // FindAnyObjectByType, not FindFirstObjectByType: the "First" variant is
        // deprecated in Unity 6.5 because it depends on instance-ID ordering,
        // and we only need one, whichever it is.
        if (promptUI == null) promptUI = FindAnyObjectByType<InteractionPromptUI>(FindObjectsInactive.Include);

        if (promptUI == null)
            Debug.LogWarning("[PlayerInteractor] No InteractionPromptUI found -- interactions will still work, but no prompt will be shown.", this);
    }

    private void Update()
    {
        UpdateRevival();
        // Every client runs one copy of this prefab per connected player.
        // Without this guard, pressing E on one machine would fire an
        // interaction for all six player objects at once.
        if (!NetworkMode.IsLocalController(this)) return;

        _focusedNameLabel = null;
        IInteractable focused = GameplayInput.Blocked ? null : RaycastForInteractable();
        bool canInteract = focused != null && focused.CanInteract();
        LogFocus(focused, canInteract);
        IInteractable hit = canInteract ? focused : null;

        if (!ReferenceEquals(hit, _currentTarget))
        {
            SetHighlight(_currentTarget, false);
            _currentTarget = hit;
            SetHighlight(_currentTarget, true);
        }

        UpdatePrompt(showNamesOnly || IsPopcornTarget(focused) || _focusedNameLabel != null ? focused : _currentTarget);

        if (_currentTarget != null
            && !CanRevive(_reviveLookTarget)
            && !GameplayInput.Blocked
            && Keybinds.WasPressed(GameAction.Interact)
            && _currentTarget.CanInteract())
        {
            RequestInteract(_currentTarget);
        }
    }

    private void LogFocus(IInteractable target, bool canInteract)
    {
        if (!logFocusedInteractable)
        {
            _loggedTarget = null;
            _loggedPrompt = null;
            return;
        }

        string prompt = target != null ? target.GetInteractionPrompt() : null;
        if (ReferenceEquals(target, _loggedTarget) && prompt == _loggedPrompt &&
            canInteract == _loggedCanInteract) return;
        bool hadTarget = _loggedTarget != null;
        _loggedTarget = target;
        _loggedPrompt = prompt;
        _loggedCanInteract = canInteract;
        if (target == null)
        {
            if (hadTarget) Debug.Log("[Interaction Focus] No interactable in focus.", this);
            return;
        }

        Transform root = target.GetTransform();
        string path = root != null ? root.name : "(no transform)";
        for (Transform parent = root != null ? root.parent : null; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        string details = string.IsNullOrWhiteSpace(prompt) ? "(no shared prompt; may use its own UI)" : prompt;
        Debug.Log($"[Interaction Focus] {path} ({target.GetType().Name}) | Can interact: {canInteract}\n{details}",
            root != null ? (Object)root : this);
    }

    private void UpdatePrompt(IInteractable target)
    {
        if (promptUI == null) return;

        if (target == null)
        {
            if (_promptTarget != null)
            {
                _promptTarget = null;
                _lastPromptText = null;
                promptUI.Hide();
            }
            return;
        }

        bool popcornName = IsPopcornTarget(target);
        PopcornMinigameBootstrap popcorn = popcornName && PopcornPreparation.Instance != null
            ? PopcornPreparation.Instance.GetComponent<PopcornMinigameBootstrap>() : null;
        PopcornFocusSettings settings = popcorn != null ? popcorn.FocusNames : null;
        string text = _focusedNameLabel != null ? _focusedNameLabel.DisplayName :
            popcornName && settings != null ? settings.NameOf(target) :
            showNamesOnly || popcornName ? FocusName(target) : target.GetInteractionPrompt();

        // Refresh the fixed layout as well so Inspector edits preview in Play mode.
        if (popcornName || _focusedNameLabel != null)
        {
            _promptTarget = target;
            _lastPromptText = text;
            promptUI.ShowFixedName(text, settings);
            return;
        }

        // Compare the target too, not just the text: two different objects can
        // share the same prompt string, and the prompt has to move to the new
        // one even though the words didn't change.
        if (!ReferenceEquals(target, _promptTarget) || text != _lastPromptText)
        {
            _promptTarget = target;
            _lastPromptText = text;
            bool bottlePrompt = target is PlacedBottleInteractable ||
                (target is PopcornStation station && station.Kind == PopcornStationKind.Water && station.PlacementAnchor != null);
            Transform promptAnchor = target is PopcornStation promptStation ? promptStation.PromptAnchor : target.GetTransform();
            promptUI.Show(text, promptAnchor, bottlePrompt ? (Vector3?)(Vector3.up * .12f) : null);
        }
    }

    private static bool IsPopcornTarget(IInteractable target) =>
        target is PopcornStation || target is PlacedBottleInteractable || target is PopcornCustomer;

    private static string FocusName(IInteractable target)
    {
        if (target is PopcornStation station)
        {
            switch (station.Kind)
            {
                case PopcornStationKind.Bucket: return "Popcorn Bucket";
                case PopcornStationKind.Cup: return "Drink Cup";
                case PopcornStationKind.Tank: return "Popcorn Tank";
                case PopcornStationKind.Maker: return "Popcorn Maker";
                case PopcornStationKind.Ghost: return "Ghost Flavor";
                case PopcornStationKind.Scoop: return UiFactory.FlavorName(station.Flavor) + " Flavor";
                case PopcornStationKind.Water: return UiFactory.FlavorName(station.Flavor) + " Dispenser";
            }
        }
        if (target is PlacedBottleInteractable) return "Drink Cup";
        if (target is WorldButtonInteractable button) return button.DisplayName;
        if (target is PopcornCustomer) return "Customer";
        if (target is WorldInventoryItem item) return item.DisplayName;
        if (target is ZoneChoreSpot chore)
        {
            switch (chore.Kind)
            {
                case ZoneTaskKind.FilmChange: return "Film Projector";
                case ZoneTaskKind.CleanScreenRoom:
                case ZoneTaskKind.CleanToilet: return "Dirt";
            }
        }
        if (target is RuleInteractable rule) return rule.DisplayName;
        if (target is TicketSellButton ticket) return ticket.DisplayName;
        if (target is LightSwitchInteractable) return "Light Switch";
        if (target is ShopTerminal) return "Shop";
        if (target is StorageTerminal) return "Storage";
        if (target is ExampleDoor) return "Door";
        if (target is Lever) return "Lever";
        if (target is WarningTV) return "Warning TV";
        Transform root = target.GetTransform();
        return root != null ? root.name.Replace("(Clone)", "").Replace('_', ' ').Trim() : "Object";
    }

    /// <summary>
    /// Casts a ray from rayOrigin forward. Returns null if nothing is hit, the
    /// hit isn't an IInteractable or it's out of range. Availability is checked
    /// separately so unavailable targets can still be identified in the log.
    /// </summary>
    private IInteractable RaycastForInteractable()
    {
        return FindInteractableAlongRay(new Ray(rayOrigin.position, rayOrigin.forward), out _focusedNameLabel);
    }

    /// <summary>Shared range/occlusion check for crosshair input and world-space UI clicks.</summary>
    public IInteractable GetInteractableAlongRay(Ray ray)
    {
        IInteractable target = FindInteractableAlongRay(ray, out _);
        return target != null && target.CanInteract() ? target : null;
    }

    private IInteractable FindInteractableAlongRay(Ray ray, out InteractionNameLabel nameLabel)
    {
        nameLabel = null;
        if (_health != null && (_health.IsDead || _health.IsDowned)) return null;
        RaycastHit[] hits = _raycastHits;
        int count = Physics.RaycastNonAlloc(ray, hits, interactionRange, interactableLayers, QueryTriggerInteraction.Ignore);
        if (count == hits.Length)
        {
            // NonAlloc does not guarantee which hits it keeps when full.
            hits = Physics.RaycastAll(ray, interactionRange, interactableLayers, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        Collider closest = null;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || candidate.transform.IsChildOf(transform)) continue;
            if (hits[i].distance >= closestDistance) continue;
            closest = candidate;
            closestDistance = hits[i].distance;
        }

        if (closest == null) return null;
        nameLabel = closest.GetComponentInParent<InteractionNameLabel>();
        if (nameLabel != null && !nameLabel.isActiveAndEnabled) nameLabel = null;
        // A label must never replace an existing station, door or pickup action.
        closest.GetComponentsInParent(false, _interactionComponents);
        foreach (MonoBehaviour component in _interactionComponents)
            if (component is IInteractable interactable && !(component is InteractionNameLabel)) return interactable;
        return nameLabel;
    }

    private void RequestInteract(IInteractable target)
    {
        // World UI selects local inventory; minigame serving routes its own server command.
        if (target is WorldButtonInteractable || target is PopcornCustomer || target is PopcornStation)
        {
            target.Interact(gameObject);
            return;
        }
        // Local-only (lobby shop): opens a screen on this PC, nothing to replicate.
        if (target is ILocalInteractable)
        {
            target.Interact(gameObject);
            return;
        }
        // Ticket buttons route through their shared server-authoritative counter.
        if (target is TicketSellButton ticketButton)
        {
            ticketButton.Interact(gameObject);
            return;
        }
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
        // Interface references do not use Unity's destroyed-object null check.
        if (target is Object unityObject && unityObject == null) return;
        if (target == null) return;
        // Preserve UI/customer prompt callbacks while replacing only whole-surface tints.
        if (target is IInteractionHighlight highlightable && !(target is IInteractionSurfaceTint))
            highlightable.SetHighlighted(state && !(target is PopcornCustomer));
        if (target is WorldButtonInteractable) return;
        Transform root = target.GetTransform();
        if (root == null) return;
        InteractionOutline outline = root.GetComponent<InteractionOutline>();
        if (state && outline == null) outline = root.gameObject.AddComponent<InteractionOutline>();
        if (outline != null) outline.SetVisible(state, _viewCamera);
    }

    private void OnDisable()
    {
        // Don't leave a prompt hanging on screen if the player is despawned,
        // dies, or the scene changes.
        SetHighlight(_currentTarget, false);
        _currentTarget = null;
        _focusedNameLabel = null;
        _promptTarget = null;
        _lastPromptText = null;
        _loggedTarget = null;
        _loggedPrompt = null;
        _loggedCanInteract = false;
        if (promptUI != null) promptUI.Hide();
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = rayOrigin != null ? rayOrigin : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin.position, origin.position + origin.forward * interactionRange);
    }
}
