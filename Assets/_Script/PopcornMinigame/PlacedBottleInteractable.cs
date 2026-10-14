using UnityEngine;

/// <summary>A local ray target; pickup is committed by the existing minigame server identity.</summary>
public sealed class PlacedBottleInteractable : MonoBehaviour, IInteractable, ILocalInteractable
{
    private PopcornStation station;
    private uint owner;
    private BoxCollider pickupCollider;

    public static void DisableVisual(GameObject visual)
    {
        var interaction = visual.GetComponent<PlacedBottleInteractable>();
        if (interaction != null) interaction.pickupCollider.enabled = false;
        visual.layer = 2;
    }

    public static void ConfigureVisual(GameObject visual, PopcornStation station, uint owner, bool ready)
    {
        var interaction = visual.GetComponent<PlacedBottleInteractable>();
        if (!ready) { if (interaction != null) interaction.pickupCollider.enabled = false; return; }
        if (interaction == null)
        {
            interaction = visual.AddComponent<PlacedBottleInteractable>();
            interaction.pickupCollider = visual.AddComponent<BoxCollider>();
            var renderers = visual.GetComponentsInChildren<Renderer>();
            Bounds bounds = new Bounds(visual.transform.position, Vector3.one * .224f);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            }
            interaction.pickupCollider.center = visual.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = visual.transform.lossyScale;
            interaction.pickupCollider.size = new Vector3(bounds.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), bounds.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }
        interaction.station = station;
        interaction.owner = owner;
        visual.layer = 0;
        interaction.pickupCollider.enabled = true;
    }

    public bool CanInteract() => isActiveAndEnabled && station != null && PopcornPreparation.Instance != null;
    public Transform GetTransform() => transform;
    public string GetInteractionPrompt() => PopcornPreparation.Instance.TryGetBottle(station, out var bottle) && bottle.fillingActor != 0 ?
        "Dispensing · Please wait" : PopcornPreparation.Instance.Holder.HasItem ? "Hands full · R to discard held item" : "[E] Pick up PapperBottle";
    private bool IsOwner => PlayerHealth.LocalInstance != null &&
        (owner == 0 || NetworkMode.IsOffline || PlayerHealth.LocalInstance.netId == owner);
    public void Interact(GameObject interactor)
    {
        if (CanInteract() && IsOwner) PopcornPreparation.Instance.RequestBottlePickup(station);
    }
}
