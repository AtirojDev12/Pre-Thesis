using UnityEngine;

// Append new values so existing serialized station kinds retain their meaning.
public enum PopcornStationKind { Bucket, Cup, Scoop, Water, Ghost, Tank, Maker }
public enum PopcornSupplyAction { Begin, Complete, Cancel, Refill }

public sealed class PopcornStation : MonoBehaviour, IInteractable
{
    public PopcornStationKind Kind { get; private set; }
    public PopcornFlavor Flavor { get; private set; }
    public int Id { get; private set; }
    private PopcornPreparation preparation;

    public void Configure(PopcornPreparation owner, PopcornStationKind kind, PopcornFlavor flavor, int id = 0)
    {
        preparation = owner;
        Kind = kind;
        Flavor = flavor;
        Id = id;
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
            Kind == PopcornStationKind.Tank ? "PopCornTank" : Kind == PopcornStationKind.Maker ? "Popcorn Maker" :
            Kind == PopcornStationKind.Scoop ? UiFactory.FlavorName(Flavor) + " Flavor" :
            Kind == PopcornStationKind.Water ? UiFactory.FlavorName(Flavor) : Kind.ToString();
        if (GhostFavorRecovery.Instance != null && !GhostFavorRecovery.Instance.IsHome && Kind == PopcornStationKind.Ghost)
            return GhostFavorRecovery.Instance.Prompt;
        return label + "\n" + GetActionPrompt();
    }

    private string GetActionPrompt()
    {
        if (preparation == null || preparation.Holder == null) return string.Empty;
        ItemHoldingSystem holder = preparation.Holder;
        if (Kind == PopcornStationKind.Bucket || Kind == PopcornStationKind.Cup)
            return holder.HasItem ? "Hands full · R to discard" :
                Kind == PopcornStationKind.Cup ? "[E] Pick up empty cup" : "[E] Pick up empty bucket";
        if (Kind == PopcornStationKind.Maker)
            return holder.HasItem ? "Hands must be empty · R to discard" : "Hold [E] Make NewPopcorn · 3 seconds";
        if (Kind == PopcornStationKind.Tank)
        {
            string supply = preparation.TankRemaining + " / " + preparation.TankCapacity + " servings\n";
            if (holder.IsRefill) return supply + (preparation.TankRemaining >= preparation.TankCapacity ?
                "Tank full · Keep NewPopcorn for later" : "[E] Refill popcorn");
            if (!holder.CanScoop) return supply + (holder.HasPopcorn ? "Choose a flavor station" : "Pick up an empty bucket first");
            return supply + (preparation.TankRemaining == 0 ? "Empty · Make NewPopcorn to refill" : "Hold [E] Scoop popcorn · 3 seconds");
        }
        if (Kind == PopcornStationKind.Ghost)
            return !holder.IsReady ? "Fill and flavor popcorn or fill a drink before mixing" : holder.GhostMixed ?
                "Ghost flavor added · Ready to serve" : "[E] Mix Ghost Flavor";
        if (holder.IsRefill) return "Take NewPopcorn to PopCornTank";
        if (!holder.HasItem) return Kind == PopcornStationKind.Water ? "Pick up an empty cup first" : "Pick up an empty bucket first";
        if (holder.IsReady) return "Already prepared · Serve or R to discard";
        if (holder.IsCup != (Kind == PopcornStationKind.Water))
            return Kind == PopcornStationKind.Water ? "Drinks need a cup · R to discard bucket" : "Popcorn needs a bucket · R to discard cup";
        if (Kind == PopcornStationKind.Scoop)
            return holder.HasPopcorn ? "[E] Add " + UiFactory.FlavorName(Flavor) + " flavor" : "Scoop from PopCornTank first";
        return "Hold [E] Fill " + UiFactory.FlavorName(Flavor) + " · 3 seconds";
    }
}
