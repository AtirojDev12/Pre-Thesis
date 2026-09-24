using Mirror;
using UnityEngine;

/// <summary>
/// One way out of the map. Put THREE of these in the cinema — front door, back
/// door, fire escape — each with a NetworkIdentity and a trigger collider.
///
/// WHY CAPACITY IS TWO
/// -------------------
/// Three exits at two people each is six, which is exactly the maximum team
/// size. A full lobby therefore has ZERO slack: the team physically cannot
/// leave together, and must split 2/2/2. If three players run for the front
/// door, one is refused and has to cross the cinema to another exit, in the
/// dark, with the ghosts still active, inside a window one in-game hour long.
///
/// That is the point. The exit is not an epilogue, it is the last and hardest
/// coordination test of the round — Punish Chaos as a mechanic rather than a
/// sentence. A team that never agreed on exit assignments loses somebody at the
/// door. Do not "fix" a full door by letting a third player squeeze through;
/// the refusal IS the design.
///
/// NO HUD. THIS IS DELIBERATE.
/// ---------------------------
/// Nothing on screen says which doors are full. A player who could glance at a
/// UI and see "front door 2/2" would simply route around the problem, and the
/// whole reason for splitting the team would evaporate — the capacity would
/// cost nothing but a pathfinding decision.
///
/// Instead a filling door announces itself the way a finished zone task does:
/// the SAME positional, distance-attenuated cue, played at the door. So the
/// information is real but imperfect. Close enough and you hear the door go and
/// you know to run elsewhere. Too far and you hear nothing, or worse, you hear
/// it and cannot tell whether that was a door closing or a teammate finishing a
/// task somewhere. The ambiguity is the feature, not an oversight — use the
/// exact same clip the zones use.
///
/// SERVER AUTHORITY: capacity is decided on the server and only on the server.
/// The count is deliberately NOT a SyncVar; a client that held it could read
/// door occupancy straight out of memory and rebuild the HUD this design is
/// avoiding. A refused player is told privately, over a TargetRpc.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
public class ExitPoint : NetworkBehaviour
{
    [Header("Identity")]
    [Tooltip("Which exit this is, for logs only. Front door / Back door / Fire escape.")]
    [SerializeField] private string exitName = "Exit";

    [Header("Capacity")]
    [Tooltip("How many players this exit can take. TWO, by design — three exits x two people = six = max team size, so a full team has no slack and must split. Raising this quietly removes the hardest decision in the round.")]
    [Min(1)] [SerializeField] private int capacity = 2;

    [Header("Audio")]
    [Tooltip("Played AT THIS DOOR when it fills. Use the SAME clip a zone plays when its task completes — a distant chime the team cannot reliably attribute. Do not give the door a distinctive sound; the ambiguity is what keeps the capacity meaningful.")]
    [SerializeField] private AudioClip exitFullCue;

    [Tooltip("Source on this exit. Must be 3D (Spatial Blend = 1) with the same rolloff the zone task cue uses, or the sound stops being positional information and becomes an announcement.")]
    [SerializeField] private AudioSource cueSource;

    /// <summary>
    /// SERVER-SIDE ONLY. Never replicated — see the class comment. Clients learn
    /// occupancy by hearing the door fill or by walking into it and being told.
    /// </summary>
    private int used;

    public string ExitName => exitName;
    public int Capacity => capacity;

    /// <summary>Server-side only; on a client this is always 0 and must not be trusted.</summary>
    public int ServerUsed => used;
    public bool ServerIsFull => used >= capacity;

    /// <summary>
    /// Fires on the LOCAL player's machine when this exit refuses them, with the
    /// reason. Hook a diegetic response to it — the handle not turning, a shove,
    /// a line of breath — not a UI label.
    /// </summary>
    public event System.Action<string> LocalRefused;

    /// <summary>Fires on every machine when this door fills, for the positional cue.</summary>
    public event System.Action Filled;

    /// <summary>
    /// Try to put a player through this exit. Returns false — loudly, on purpose —
    /// when the doors are still locked, when this exit is full, or when the
    /// player is already out.
    ///
    /// SERVER ONLY. Route a player's interaction here through a [Command] on the
    /// player, never by calling this from client code.
    /// </summary>
    public bool ServerTryUse(NetworkIdentity player, out string refusal)
    {
        refusal = null;

        if (!NetworkMode.HasServerAuthority(this))
        {
            refusal = "not authoritative";
            Debug.LogWarning("[ExitPoint] ServerTryUse was called on a client. Exit capacity is decided on the server only — ignored.", this);
            return false;
        }

        MatchDirector director = MatchDirector.Instance;

        if (director == null)
        {
            refusal = "no MatchDirector";
            Debug.LogWarning("[ExitPoint] There is no MatchDirector in this scene, so no exit can know whether it is 06:00 yet.", this);
            return false;
        }

        // Two separate conditions, refused separately so the player understands
        // which wall they hit.
        //
        // 1. The clock. Only 06:00 opens a door — a team that finished every
        //    task on the map at 02:00 is still locked in until dawn.
        if (!director.ExitsOpen)
        {
            Refuse(player, "locked until 06:00", out refusal);
            return false;
        }

        // 2. The work. A team whose COMBINED completed tasks fell short of the
        //    minimum does not get to walk away at dawn. They are sealed in, and
        //    Method 2 — dealing with the main ghost — is the only way anybody
        //    survives. This is what makes the minimum a threat rather than a
        //    score, so do not soften it into a penalty.
        if (!director.MinimumMet)
        {
            Refuse(player, "the work is not finished", out refusal);
            return false;
        }

        if (director.HasEscaped(player))
        {
            Refuse(player, "already out", out refusal);
            return false;
        }

        if (ServerIsFull)
        {
            Refuse(player, $"{exitName} is full", out refusal);
            return false;
        }

        used++;

        // Announce the door only at the moment it fills, and only as sound in
        // the world. Two people went through; anyone near enough hears it.
        if (ServerIsFull)
        {
            if (NetworkMode.IsOffline) PlayFilledCue();
            else RpcFilled();
        }

        director.ServerReportPlayerEscaped(player);

        Debug.Log($"[ExitPoint] {exitName} — {used}/{capacity} used.", this);
        return true;
    }

    /// <summary>Convenience overload for callers that do not need the reason.</summary>
    public bool ServerTryUse(NetworkIdentity player) => ServerTryUse(player, out _);

    private void Refuse(NetworkIdentity player, string reason, out string refusal)
    {
        refusal = reason;

        // Told to that player alone. Broadcasting a refusal would leak door state
        // to five people who are not standing there.
        if (!NetworkMode.IsOffline && player != null && player.connectionToClient != null)
            TargetRefused(player.connectionToClient, reason);
        else
            LocalRefused?.Invoke(reason);
    }

    [TargetRpc]
    private void TargetRefused(NetworkConnectionToClient target, string reason)
    {
        LocalRefused?.Invoke(reason);
    }

    [ClientRpc]
    private void RpcFilled() => PlayFilledCue();

    private void PlayFilledCue()
    {
        Filled?.Invoke();

        if (exitFullCue == null) return;

        if (cueSource != null) cueSource.PlayOneShot(exitFullCue);
        else AudioSource.PlayClipAtPoint(exitFullCue, transform.position);
    }

    // Mirror's NetworkBehaviour declares OnValidate as protected virtual and uses
    // it to wire up the NetworkIdentity link. Declaring a private one here HID it,
    // so Mirror's own validation silently stopped running. Override it and call
    // base first.
    protected override void OnValidate()
    {
        base.OnValidate();

        if (capacity != 2)
        {
            Debug.LogWarning(
                $"[ExitPoint] '{exitName}' has capacity {capacity}. The design is three exits at TWO each, " +
                "so that six players have exactly no slack and have to split up. Change this only deliberately.", this);
        }

        if (cueSource != null && cueSource.spatialBlend < 0.99f)
        {
            Debug.LogWarning(
                $"[ExitPoint] '{exitName}' has a cue source with Spatial Blend {cueSource.spatialBlend:F2}. " +
                "It must be 1 (fully 3D), or the door announces itself to the whole map instead of only to players near it.", this);
        }
    }
}
