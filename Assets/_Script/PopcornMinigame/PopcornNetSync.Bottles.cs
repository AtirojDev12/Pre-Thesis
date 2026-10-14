using Mirror;

public partial class PopcornNetSync
{
    public readonly SyncDictionary<int, PlacedDrinkState> PlacedDrinks = new SyncDictionary<int, PlacedDrinkState>();
    private uint nextBottleRevision;

    public void RequestPlaceBottle(int station, int request, uint revision) => CmdPlaceBottle(station, request, revision);
    [Command(requiresAuthority = false)]
    private void CmdPlaceBottle(int station, int request, uint revision, NetworkConnectionToClient sender = null)
    {
        if (sender?.identity == null) return;
        TargetBottlePlaced(sender, request, station, revision, ServerTryPlaceBottle(sender, station, revision));
    }
    public bool ServerTryPlaceBottle(NetworkConnectionToClient sender, int stationId, uint revision)
    {
        var station = PopcornPreparation.Instance?.StationAt(stationId);
        int spot = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.SpotId(station) : -1;
        var held = sender?.identity != null ? sender.identity.GetComponent<PlayerHeldItems>() : null;
        if (!isServer || spot < 0 || !CanUseSupply(sender, station) || supplyWork.ContainsKey(sender) || heldRefills.Contains(sender) ||
            PlacedDrinks.ContainsKey(spot) || held == null || !held.ServerReleaseEmptyBottle(revision)) return false;
        PlacedDrinks.Add(spot, new PlacedDrinkState { revision = ++nextBottleRevision });
        return true;
    }
    [TargetRpc]
    private void TargetBottlePlaced(NetworkConnectionToClient target, int request, int station, uint revision, bool success)
        => PopcornPreparation.Instance?.ReceivePlacementResult(request, station, revision, success);

    public void RequestPickUpBottle(int station, int request, uint revision, uint bottleRevision) => CmdPickUpBottle(station, request, revision, bottleRevision);
    [Command(requiresAuthority = false)]
    private void CmdPickUpBottle(int station, int request, uint revision, uint bottleRevision, NetworkConnectionToClient sender = null)
    {
        if (sender?.identity == null) return;
        bool success = ServerTryPickUpBottle(sender, station, revision, bottleRevision, out var flavor);
        TargetBottlePickedUp(sender, request, revision, success, flavor);
    }
    public bool ServerTryPickUpBottle(NetworkConnectionToClient sender, int stationId, uint revision, uint bottleRevision, out PopcornFlavor flavor)
    {
        flavor = PopcornFlavor.None;
        var station = PopcornPreparation.Instance?.StationAt(stationId);
        int spot = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.SpotId(station) : -1;
        var held = sender?.identity != null ? sender.identity.GetComponent<PlayerHeldItems>() : null;
        if (!isServer || spot < 0 || !CanUseSupply(sender, station) || supplyWork.ContainsKey(sender) || heldRefills.Contains(sender) ||
            held == null || !PlacedDrinks.TryGetValue(spot, out var bottle) ||
            bottle.revision != bottleRevision || bottle.fillingActor != 0 ||
            (station.PlacementAnchor.position - sender.identity.transform.position).sqrMagnitude > 4.5f * 4.5f ||
            !held.ServerReceiveBottle(bottle.flavor, revision)) return false;
        flavor = bottle.flavor;
        PlacedDrinks.Remove(spot);
        return true;
    }
    [TargetRpc]
    private void TargetBottlePickedUp(NetworkConnectionToClient target, int request, uint revision, bool success, PopcornFlavor flavor)
        => PopcornPreparation.Instance?.ReceiveBottlePickupResult(request, revision, success, flavor);

    private bool ServerFinishPlacedDrink(PopcornStation station, SupplyWork work)
    {
        int spot = PopcornPreparation.Instance.SpotId(station);
        if (!PlacedDrinks.TryGetValue(spot, out var bottle) || bottle.revision != work.heldRevision || bottle.fillingActor != work.actor || bottle.IsReady) return false;
        bottle.flavor = station.Flavor == PopcornFlavor.None ? PopcornFlavor.Drink : station.Flavor;
        bottle.revision = ++nextBottleRevision;
        bottle.fillingActor = 0;
        PlacedDrinks[spot] = bottle;
        return true;
    }
    private void ReleaseDrinkWork(SupplyWork work)
    {
        var station = PopcornPreparation.Instance?.StationAt(work.station);
        int spot = PopcornPreparation.Instance != null ? PopcornPreparation.Instance.SpotId(station) : -1;
        if (PlacedDrinks.TryGetValue(spot, out var bottle) && bottle.fillingActor == work.actor && bottle.revision == work.heldRevision)
        { bottle.fillingActor = 0; PlacedDrinks[spot] = bottle; }
    }
}
