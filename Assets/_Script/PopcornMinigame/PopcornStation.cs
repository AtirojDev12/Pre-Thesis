using UnityEngine;

public enum PopcornStationKind { Bucket, Cup, Scoop, Water, Ghost }

/// <summary>Local preparation; serving and scoring still use PopcornNetSync.</summary>
public sealed class PopcornStation : MonoBehaviour, IInteractable
{
    public PopcornStationKind Kind { get; private set; }
    public PopcornFlavor Flavor { get; private set; }
    private PopcornPreparation preparation;

    public void Configure(PopcornPreparation owner, PopcornStationKind kind, PopcornFlavor flavor)
    {
        preparation = owner;
        Kind = kind;
        Flavor = flavor;
    }

    public bool CanInteract() => isActiveAndEnabled && preparation != null && preparation.isActiveAndEnabled;
    public Transform GetTransform() => transform;
    public void Interact(GameObject interactor)
    {
        if (CanInteract()) preparation.Interact(this);
    }

    public string GetInteractionPrompt()
    {
        string label = Kind == PopcornStationKind.Ghost ? "Ghost Flavor" :
            Kind == PopcornStationKind.Scoop ? UiFactory.FlavorName(Flavor) + " Popcorn" :
            Kind == PopcornStationKind.Water ? "Water" : Kind.ToString();
        if (GhostFavorRecovery.Instance != null && !GhostFavorRecovery.Instance.IsHome && Kind == PopcornStationKind.Ghost)
            return GhostFavorRecovery.Instance.Prompt;
        return label + "\n" + GetActionPrompt();
    }

    private string GetActionPrompt()
    {
        if (preparation == null) return string.Empty;
        ItemHoldingSystem holder = preparation.Holder;
        if (Kind == PopcornStationKind.Bucket || Kind == PopcornStationKind.Cup)
            return holder.HasItem ? "Hands full · R to discard" :
                Kind == PopcornStationKind.Cup ? "[E] Pick up empty cup" : "[E] Pick up empty bucket";
        if (Kind == PopcornStationKind.Ghost)
            return !holder.IsReady ? "Fill popcorn or water before mixing" : holder.GhostMixed ?
                "Ghost flavor added · Ready to serve" : "[E] Mix Ghost Flavor";
        if (!holder.HasItem) return Kind == PopcornStationKind.Water ? "Pick up an empty cup first" : "Pick up an empty bucket first";
        if (holder.IsReady) return "Already filled · Serve or R to discard";
        if (holder.IsCup != (Kind == PopcornStationKind.Water))
            return Kind == PopcornStationKind.Water ? "Water needs a cup · R to discard bucket" : "Popcorn needs a bucket · R to discard cup";
        return Kind == PopcornStationKind.Water ? "Hold [E] Fill water · 3 seconds" :
            "Hold [E] Scoop " + UiFactory.FlavorName(Flavor) + " · 3 seconds";
    }
}
