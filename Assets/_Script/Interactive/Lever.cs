using Mirror;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Minimal networked interactable -- this is the template to copy when adding
/// new ones. Note the three things that make it multiplayer-correct:
///   1. shared state lives in a [SyncVar],
///   2. it is changed in OnInteracted, which runs on the server,
///   3. the visual result is applied in the hook (and re-applied in
///      OnStartClient, so late joiners see the current state).
/// </summary>
public class Lever : InteractableBase
{
    [SerializeField] private string pullPrompt = "Press E to pull lever";
    [SerializeField] private string resetPrompt = "Press E to reset lever";

    [Tooltip("If true the lever can be pulled back and forth; if false it stays pulled.")]
    [SerializeField] private bool canToggleBack = false;

    [Tooltip("Optional: the part that visually rotates when pulled.")]
    [SerializeField] private Transform leverArm;
    [SerializeField] private Vector3 pulledLocalEuler = new Vector3(-45f, 0f, 0f);

    [Header("Hooks for designers")]
    [Tooltip("Raised on every client when the lever changes state. Wire doors, lights, sounds here.")]
    public UnityEvent<bool> OnPulledChanged;

    [SyncVar(hook = nameof(OnPulledSynced))]
    private bool isPulled;

    private Vector3 _restLocalEuler;

    public bool IsPulled => isPulled;

    protected override void Awake()
    {
        base.Awake();
        if (leverArm != null) _restLocalEuler = leverArm.localEulerAngles;
    }

    public override string GetInteractionPrompt() => isPulled ? resetPrompt : pullPrompt;

    public override bool CanInteract()
    {
        // A one-shot lever stops offering itself once it has been pulled.
        if (isPulled && !canToggleBack) return false;
        return base.CanInteract();
    }

    /// <summary>Runs on the SERVER.</summary>
    protected override void OnInteracted(GameObject interactor)
    {
        isPulled = canToggleBack ? !isPulled : true;

        // Hooks don't fire on the machine that made the change.
        ApplyPulledState(isPulled);
    }

    private void OnPulledSynced(bool oldValue, bool newValue) => ApplyPulledState(newValue);

    private void ApplyPulledState(bool pulled)
    {
        if (leverArm != null)
            leverArm.localEulerAngles = pulled ? pulledLocalEuler : _restLocalEuler;

        OnPulledChanged?.Invoke(pulled);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyPulledState(isPulled);
    }
}
