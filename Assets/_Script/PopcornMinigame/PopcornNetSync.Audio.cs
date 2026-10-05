using System.Collections.Generic;
using Mirror;
using UnityEngine;

public struct PopcornSoundAction
{
    public int station, request;
    public double started;
}

public partial class PopcornNetSync
{
    // Stored on the existing scene identity, not dynamically added network components.
    // Keying by player isolates simultaneous users of the same station.
    public readonly SyncDictionary<uint, PopcornSoundAction> SoundActions = new SyncDictionary<uint, PopcornSoundAction>();
    private readonly Dictionary<NetworkConnectionToClient, int> lastSoundRequest = new Dictionary<NetworkConnectionToClient, int>();
    private readonly Dictionary<NetworkConnectionToClient, uint> lastOneShotRevision = new Dictionary<NetworkConnectionToClient, uint>();
    private readonly Dictionary<NetworkConnectionToClient, double> nextSoundRequest = new Dictionary<NetworkConnectionToClient, double>();
    private readonly Dictionary<NetworkConnectionToClient, double> nextOneShot = new Dictionary<NetworkConnectionToClient, double>();
    private readonly List<NetworkConnectionToClient> expiredSoundWork = new List<NetworkConnectionToClient>();

    private void ResetStationSounds()
    {
        SoundActions.Clear();
        lastSoundRequest.Clear();
        lastOneShotRevision.Clear();
        nextSoundRequest.Clear();
        nextOneShot.Clear();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        SoundActions.OnAdd += OnSoundAdded;
        SoundActions.OnRemove += OnSoundRemoved;
        SoundActions.OnClear += OnSoundsCleared;
        RestoreStationSounds();
    }

    public override void OnStopClient()
    {
        SoundActions.OnAdd -= OnSoundAdded;
        SoundActions.OnRemove -= OnSoundRemoved;
        SoundActions.OnClear -= OnSoundsCleared;
        OnSoundsCleared();
        base.OnStopClient();
    }

    public void RestoreStationSounds()
    {
        foreach (var pair in SoundActions)
            PopcornPreparation.Instance?.Audio?.Begin(pair.Key, pair.Value, true);
    }

    private void OnSoundAdded(uint actor) => PopcornPreparation.Instance?.Audio?.Begin(actor, SoundActions[actor], false);
    private void OnSoundRemoved(uint actor, PopcornSoundAction previous) => PopcornPreparation.Instance?.Audio?.End(actor, previous.request);
    private void OnSoundsCleared() => PopcornPreparation.Instance?.Audio?.StopAll();

    private bool AcceptNewSoundRequest(NetworkConnectionToClient sender, int request)
    {
        if (request <= 0 || (lastSoundRequest.TryGetValue(sender, out int last) && request <= last)) return false;
        if (nextSoundRequest.TryGetValue(sender, out double next) && NetworkTime.time < next) return false;
        lastSoundRequest[sender] = request;
        nextSoundRequest[sender] = NetworkTime.time + 0.2;
        return true;
    }

    private bool CanHoldAtStation(NetworkConnectionToClient sender, PopcornStation station)
    {
        var held = sender?.identity != null ? sender.identity.GetComponent<PlayerHeldItems>() : null;
        if (held == null || station == null) return false;
        TaskHeldState item = held.TaskItem;
        if (station.Kind == PopcornStationKind.Maker) return !item.hasItem;
        if (station.Kind == PopcornStationKind.Water) return item.hasItem && item.isCup && !item.IsReady && !item.isRefill;
        return item.hasItem && !item.isCup && !item.hasPopcorn && !item.isRefill && !item.IsReady;
    }

    private void BeginStationSound(uint actor, int station, int request, double started)
    {
        // Add/remove rather than replace: removal closes the previous audio sequence.
        SoundActions.Remove(actor);
        SoundActions.Add(actor, new PopcornSoundAction { station = station, request = request, started = started });
    }
    private void EndStationSound(uint actor) => SoundActions.Remove(actor);

    private void Update()
    {
        if (!isServer) return;
        expiredSoundWork.Clear();
        foreach (var pair in supplyWork)
        {
            var sender = pair.Key;
            var work = pair.Value;
            var station = PopcornPreparation.Instance?.StationAt(work.station);
            if (!NetworkServer.connections.TryGetValue(sender.connectionId, out var live) || live != sender ||
                !CanUseSupply(sender, station) || !CanHoldAtStation(sender, station) ||
                NetworkTime.time - work.started > PopcornPreparation.PreparationSeconds + 3)
                expiredSoundWork.Add(sender);
        }
        foreach (var sender in expiredSoundWork)
        {
            EndStationSound(supplyWork[sender].actor);
            supplyWork.Remove(sender);
        }
        // Do not retain disconnected connection objects across a long match.
        expiredSoundWork.Clear();
        foreach (var sender in lastSoundRequest.Keys)
            if (!NetworkServer.connections.ContainsKey(sender.connectionId)) expiredSoundWork.Add(sender);
        foreach (var sender in expiredSoundWork)
        {
            lastSoundRequest.Remove(sender);
            lastOneShotRevision.Remove(sender);
            nextSoundRequest.Remove(sender);
            nextOneShot.Remove(sender);
            heldRefills.Remove(sender);
        }
        expiredSoundWork.Clear();
        foreach (var sender in lastOneShotRevision.Keys)
            if (!NetworkServer.connections.ContainsKey(sender.connectionId)) expiredSoundWork.Add(sender);
        foreach (var sender in expiredSoundWork)
        {
            lastOneShotRevision.Remove(sender);
            nextOneShot.Remove(sender);
        }
    }

    public void RequestStationOneShot(int station, uint revision) => CmdStationOneShot(station, revision);

    [Command(requiresAuthority = false)]
    private void CmdStationOneShot(int stationId, uint revision, NetworkConnectionToClient sender = null)
    {
        var station = PopcornPreparation.Instance?.StationAt(stationId);
        if (!CanUseSupply(sender, station)) return;
        var held = sender.identity.GetComponent<PlayerHeldItems>();
        if (held == null || held.TaskItem.revision != revision ||
            (lastOneShotRevision.TryGetValue(sender, out uint last) && revision <= last)) return;
        if (nextOneShot.TryGetValue(sender, out double next) && NetworkTime.time < next) return;
        var item = held.TaskItem;
        bool pickup = (station.Kind == PopcornStationKind.Cup || station.Kind == PopcornStationKind.Bucket) &&
            item.hasItem && item.isCup == (station.Kind == PopcornStationKind.Cup) && !item.IsReady && !item.isRefill && !item.hasPopcorn;
        bool flavor = station.Kind == PopcornStationKind.Scoop && item.IsReady && !item.isCup && item.flavor == station.Flavor;
        bool ghost = station.Kind == PopcornStationKind.Ghost && item.IsReady && item.ghostMixed &&
            GhostFavorRecovery.Instance != null && GhostFavorRecovery.Instance.IsHome;
        if (!pickup && !flavor && !ghost) return;
        lastOneShotRevision[sender] = revision;
        nextOneShot[sender] = NetworkTime.time + 0.15;
        RpcStationOneShot(stationId, pickup ? "Popcorn_Pickup" : "Popcorn_Flavor");
    }

    [ClientRpc]
    private void RpcStationOneShot(int stationId, string soundId)
    {
        var station = PopcornPreparation.Instance?.StationAt(stationId);
        if (station != null) AudioManager.Instance?.Play(soundId, station.transform.position, gameObject.scene);
    }
}
