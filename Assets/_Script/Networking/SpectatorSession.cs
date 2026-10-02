using Mirror;
using UnityEngine;

/// <summary>
/// SPECTATOR (2 Oct). A Development-Build player can join a room as an invisible
/// spectator: no seat, no body, not counted as a player, cannot interact.
///
/// This small part is in EVERY build, so a friend's NORMAL build can host a
/// room that a dev build spectates. Only the "Spectate" button and the
/// spectator camera are Development-Build only (DevSpectatorCamera).
/// </summary>
public static class SpectatorSession
{
    /// <summary>This PC asked to join the current/next session as a spectator.</summary>
    public static bool Requested { get; set; }

    /// <summary>This PC is connected to someone else's room as a spectator.</summary>
    public static bool Active => Requested && NetworkClient.active && !NetworkServer.active;

    // ---- Spectator push-to-talk (2 Oct) -----------------------------------------
    // Set every frame by the spectator camera (Development Build). Read by
    // VoiceNetwork, which sends the spectator's voice with this position so
    // PLAYERS near it hear it in 3D. Other spectators do not hear it.

    /// <summary>The spectator is holding the push-to-talk key.</summary>
    public static bool TalkHeld { get; set; }

    /// <summary>Where the spectator's voice comes from (the spectator camera).</summary>
    public static Vector3 VoicePosition { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Requested = false;
        TalkHeld = false;
        VoicePosition = Vector3.zero;
    }
}
