using System.Collections.Generic;
using UnityEngine;

public sealed partial class PopcornPreparation
{
    private readonly Dictionary<int, PlacedDrinkState> offlineBottles = new Dictionary<int, PlacedDrinkState>();
    private readonly Dictionary<int, GameObject> bottleVisuals = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, PopcornFlavor> renderedFlavors = new Dictionary<int, PopcornFlavor>();
    private uint nextOfflineBottle;

    public int SpotId(PopcornStation station)
    {
        if (station == null || station.PlacementAnchor == null) return -1;
        foreach (var candidate in stations)
            if (candidate.Kind == PopcornStationKind.Water && candidate.PlacementAnchor == station.PlacementAnchor) return candidate.Id;
        return -1;
    }

    public bool TryGetBottle(PopcornStation station, out PlacedDrinkState bottle)
    {
        int spot = SpotId(station);
        bottle = default;
        if (spot < 0) return false;
        return NetworkMode.IsOffline ? offlineBottles.TryGetValue(spot, out bottle) :
            PopcornNetSync.Instance != null && PopcornNetSync.Instance.PlacedDrinks.TryGetValue(spot, out bottle);
    }

    public bool PlaceOfflineBottle(PopcornStation station)
    {
        int spot = SpotId(station);
        if (!NetworkMode.IsOffline || spot < 0 || offlineBottles.ContainsKey(spot) || !Holder.IsInHand || !Holder.IsCup || Holder.IsReady) return false;
        offlineBottles.Add(spot, new PlacedDrinkState { revision = ++nextOfflineBottle });
        Holder.Consume();
        RenderPlacedBottles();
        return true;
    }

    private bool FinishOfflineBottle(PopcornStation station, PopcornFlavor flavor)
    {
        int spot = SpotId(station);
        if (!offlineBottles.TryGetValue(spot, out var bottle) || bottle.IsReady) return false;
        bottle.flavor = flavor;
        bottle.revision = ++nextOfflineBottle;
        offlineBottles[spot] = bottle;
        RenderPlacedBottles();
        return true;
    }

    public GameObject BottleVisualAt(PopcornStation station) => bottleVisuals.TryGetValue(SpotId(station), out var visual) ? visual : null;

    private void RenderPlacedBottles()
    {
        foreach (var station in stations)
        {
            if (station.Id != SpotId(station)) continue;
            int spot = station.Id;
            if (!TryGetBottle(station, out var state))
            {
                if (bottleVisuals.TryGetValue(spot, out var old)) { old.SetActive(false); Destroy(old); bottleVisuals.Remove(spot); renderedFlavors.Remove(spot); }
                continue;
            }
            if (!bottleVisuals.TryGetValue(spot, out var visual) || visual == null || renderedFlavors[spot] != state.flavor)
            {
                if (visual != null) { visual.SetActive(false); Destroy(visual); }
                var prefab = Holder.PrefabFor(new TaskHeldState { hasItem = true, isCup = true, flavor = state.flavor });
                if (prefab == null) continue;
                visual = Instantiate(prefab);
                visual.name = "Placed PapperBottle " + spot;
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
                foreach (var body in visual.GetComponentsInChildren<Rigidbody>()) { body.isKinematic = true; body.detectCollisions = false; }
                bottleVisuals[spot] = visual;
                renderedFlavors[spot] = state.flavor;
            }
            PopcornItemHoldingSystem.PlaceBottleVisual(visual, station.PlacementAnchor);
            PlacedBottleInteractable.ConfigureVisual(visual, station, 0, true);
        }
    }

    private void ClearBottleVisuals()
    {
        foreach (var visual in bottleVisuals.Values) if (visual != null) Destroy(visual);
        bottleVisuals.Clear();
    }
}
