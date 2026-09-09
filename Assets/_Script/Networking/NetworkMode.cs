using Mirror;

/// <summary>
/// Lets the gameplay systems be correctly server-authoritative in a real match
/// while still being playable in a test scene that has no NetworkManager at all
/// (TestWalk, or any scratch scene someone presses Play in).
///
/// "Offline" here means literally no Mirror server AND no Mirror client are
/// running in this process. In that state there is exactly one machine, one
/// player, and nobody to cheat -- so it is safe to run the authoritative code
/// path directly. The instant a real match is running, every check below falls
/// straight back to Mirror's normal isServer / isLocalPlayer rules, so this
/// never weakens authority in the shipped game.
///
/// Why this exists: without it, converting these systems to NetworkBehaviour
/// would silently break solo prototyping -- isLocalPlayer and isServer are both
/// false when nothing is hosting, so every input check and every damage call
/// would just do nothing and the test scene would look broken.
/// </summary>
public static class NetworkMode
{
    /// <summary>No Mirror server and no Mirror client running in this process.</summary>
    public static bool IsOffline => !NetworkServer.active && !NetworkClient.active;

    /// <summary>
    /// May this machine mutate authoritative state (health, door open/closed,
    /// one-time-use flags) for this object?
    /// </summary>
    public static bool HasServerAuthority(NetworkBehaviour behaviour)
    {
        // Short-circuits before touching netIdentity, so an offline scene never
        // needs a NetworkIdentity present at all.
        return IsOffline || (behaviour != null && behaviour.isServer);
    }

    /// <summary>
    /// Should this machine read local input for this object? Guards against the
    /// classic Mirror bug where every client runs one copy of the player prefab
    /// per connected player, and all of them read the same keyboard.
    /// </summary>
    public static bool IsLocalController(NetworkBehaviour behaviour)
    {
        return IsOffline || (behaviour != null && behaviour.isLocalPlayer);
    }

    /// <summary>
    /// Same check for plain MonoBehaviours that live on a child object (a camera
    /// pivot, say) and only need to know whose player they belong to. Pass the
    /// NetworkIdentity found with GetComponentInParent.
    /// </summary>
    public static bool IsLocalController(NetworkIdentity identity)
    {
        return IsOffline || (identity != null && identity.isLocalPlayer);
    }
}
