using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): shared helpers for the Map Rules and map events (on the scene
/// object "Map Rules", made by Tools > Pre-Thesis > One Step > Map Rules).
///   - is the night running, what time is it,
///   - a message and / or jumpscare on one player's screen or on everyone's.
/// SERVER decides, clients only show.
/// </summary>
public abstract class MapEventBase : NetworkBehaviour
{
    protected static MapRuleTuning T => MapRuleTuning.Current;
    protected static double Now => NetworkMode.IsOffline ? Time.timeAsDouble : NetworkTime.time;
    protected bool IsServerSide => NetworkMode.HasServerAuthority(this);

    /// <summary>The night shift is running (00:00 - 06:00, not before, not the escape).</summary>
    protected static bool NightRunning
    {
        get
        {
            MatchDirector match = MatchDirector.Instance;
            return match != null && match.RoundRunning && match.Phase == MatchPhase.Night;
        }
    }

    /// <summary>In-game minutes since 00:00 during the night (0 when there is no night).</summary>
    protected static int NightMinutes
    {
        get
        {
            MatchDirector match = MatchDirector.Instance;
            if (match == null || match.Phase != MatchPhase.Night) return 0;
            match.GetInGameTime(out int hour, out int minute);
            return hour * 60 + minute;
        }
    }

    /// <summary>Living players still in the round (not dead, not escaped, not spectating).</summary>
    protected static void Players(List<PlayerHealth> into)
    {
        into.Clear();
        IReadOnlyList<PlayerHealth> all = PlayerRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (PlayerRegistry.IsHuntable(all[i])) into.Add(all[i]);
    }

    /// <summary>SERVER. Message and / or jumpscare on one player's screen.</summary>
    protected void ServerShow(PlayerHealth player, bool jumpscare, string message)
    {
        if (player == null || (!jumpscare && string.IsNullOrEmpty(message))) return;
        if (NetworkMode.IsOffline || (player.isLocalPlayer && player.isClient)) LocalShow(jumpscare, message);
        else if (player.connectionToClient != null) TargetShow(player.connectionToClient, jumpscare, message);
    }

    /// <summary>SERVER. Message and / or jumpscare on every screen.</summary>
    protected void ServerShowAll(bool jumpscare, string message)
    {
        if (!jumpscare && string.IsNullOrEmpty(message)) return;
        if (NetworkMode.IsOffline) { LocalShow(jumpscare, message); return; }
        RpcShow(jumpscare, message);
    }

    [TargetRpc]
    private void TargetShow(NetworkConnectionToClient target, bool jumpscare, string message) => LocalShow(jumpscare, message);

    [ClientRpc]
    private void RpcShow(bool jumpscare, string message) => LocalShow(jumpscare, message);

    private static void LocalShow(bool jumpscare, string message)
    {
        if (jumpscare) RuleFx.ShowJumpscare(2.5f);
        if (!string.IsNullOrEmpty(message)) RuleFx.Toast(message, 4f);
    }
}
