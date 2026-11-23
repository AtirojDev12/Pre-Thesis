using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 23 Nov 2026 (Mr.k): who sits on which cinema seat (players and ghosts).
///   Player: E on a free seat (CinemaSeat) -> asks the host -> sits (lowered, legs bent,
///   facing the screen, cannot walk). SPACE = stand up in front of the seat.
///   Getting downed / dying / the round ending also stands you up.
///   Ghosts (the VIP) take a seat through ServerSit with their own netId.
/// Everyone sees the seat fold open and the sitting pose (synced list of seat uses).
/// Rules ask ServerSeatOf(player) (Staff Time: sit on the shadow's seat).
/// Lives on the "Map Rules" object (made by Tools > Pre-Thesis > One Step > Map Rules).
/// SERVER AUTHORITY: only the host changes the list. A NetworkList, not a Dictionary.
/// </summary>
public sealed class SeatSystem : NetworkBehaviour
{
    [System.Serializable]
    public struct SeatUse
    {
        public int seat;
        public uint who; // player or walker netId (offline: 1 = the local player)
    }

    [Tooltip("Every seat (index = position in this list). Filled by the setup tool.")]
    [SerializeField] private CinemaSeat[] seats = new CinemaSeat[0];

    private readonly SyncList<SeatUse> uses = new SyncList<SeatUse>();

    public static SeatSystem Instance { get; private set; }
    public int Count => seats != null ? seats.Length : 0;
    public CinemaSeat Seat(int i) => seats != null && i >= 0 && i < seats.Length ? seats[i] : null;

    /// <summary>LOCAL. The seat the local player sits on (-1 = standing).</summary>
    public int LocalSeat { get; private set; } = -1;

    private const uint OfflinePlayerId = 1u;
    private Rigidbody localBody;
    private bool localWasKinematic;
    private bool requestPending;
    private float seatedAt, requestAt;
    private float nextServerCheck;
    private readonly HashSet<uint> poseShown = new HashSet<uint>();
    private readonly HashSet<uint> poseNow = new HashSet<uint>();

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (LocalSeat >= 0) LocalUnseat();
    }

    // ---- Queries (any machine) ----------------------------------------------------------------

    public bool IsTaken(int seat)
    {
        for (int i = 0; i < uses.Count; i++) if (uses[i].seat == seat) return true;
        return false;
    }

    private int SeatOfId(uint who)
    {
        for (int i = 0; i < uses.Count; i++) if (uses[i].who == who) return uses[i].seat;
        return -1;
    }

    private static uint IdOf(PlayerHealth p) => p == null ? 0u : NetworkMode.IsOffline ? OfflinePlayerId : p.netId;

    /// <summary>The seat nearest to a point (within <paramref name="maxDistance"/>), or -1.</summary>
    public int NearestSeat(Vector3 point, float maxDistance = 1.2f, bool ghostRoomOnly = false)
    {
        int best = -1;
        float bestSqr = maxDistance * maxDistance;
        for (int i = 0; i < Count; i++)
        {
            CinemaSeat s = seats[i];
            if (s == null || (ghostRoomOnly && !s.GhostRoom)) continue;
            Vector3 d = s.FloorPoint - point;
            d.y *= 0.3f; // rows are steps: height matters less
            float sqr = d.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = i; }
        }
        return best;
    }

    // ---- Server ---------------------------------------------------------------------------------

    /// <summary>SERVER. The seat this player sits on (-1 = none).</summary>
    public int ServerSeatOf(PlayerHealth p) => SeatOfId(IdOf(p));

    /// <summary>SERVER. Someone (a player or a ghost walker, by netId) sits down. False if taken.</summary>
    public bool ServerSit(int seat, uint who)
    {
        if (!NetworkMode.HasServerAuthority(this) || Seat(seat) == null || who == 0 || IsTaken(seat)) return false;
        ServerFree(who);
        uses.Add(new SeatUse { seat = seat, who = who });
        return true;
    }

    /// <summary>SERVER. Whoever this is stands up (no-op if not seated).</summary>
    public void ServerFree(uint who)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        for (int i = uses.Count - 1; i >= 0; i--) if (uses[i].who == who) uses.RemoveAt(i);
    }

    [Command(requiresAuthority = false)]
    private void CmdSit(int seat, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null) return;
        bool ok = ServerCanSit(seat, sender.identity.GetComponent<PlayerHealth>()) && ServerSit(seat, sender.identity.netId);
        TargetSitResult(sender, seat, ok);
    }

    [Command(requiresAuthority = false)]
    private void CmdStand(NetworkConnectionToClient sender = null)
    {
        if (sender != null && sender.identity != null) ServerFree(sender.identity.netId);
    }

    private bool ServerCanSit(int seat, PlayerHealth p)
    {
        CinemaSeat s = Seat(seat);
        if (s == null || s.GhostOnly || p == null || p.IsDead || p.IsDowned || IsTaken(seat)) return false;
        return (p.transform.position - s.FloorPoint).sqrMagnitude <= 4.5f * 4.5f;
    }

    private void ServerTick()
    {
        if (Time.time < nextServerCheck) return;
        nextServerCheck = Time.time + 0.5f;
        // Downed, dead, gone players stand up. Ghost walkers free their own seat.
        for (int i = uses.Count - 1; i >= 0; i--)
        {
            uint who = uses[i].who;
            if (NetworkMode.IsOffline)
            {
                PlayerHealth me = PlayerHealth.LocalInstance;
                if (who == OfflinePlayerId && (me == null || me.IsDead || me.IsDowned)) uses.RemoveAt(i);
                continue;
            }
            if (!NetworkServer.spawned.TryGetValue(who, out NetworkIdentity id) || id == null) { uses.RemoveAt(i); continue; }
            if (id.TryGetComponent(out PlayerHealth p) && (p.IsDead || p.IsDowned)) uses.RemoveAt(i);
        }
    }

    // ---- Local player -------------------------------------------------------------------------

    /// <summary>LOCAL. E on a free seat.</summary>
    public void LocalRequestSit(int seat)
    {
        if (requestPending && Time.time - requestAt > 2f) requestPending = false; // the answer got lost
        if (LocalSeat >= 0 || requestPending) return;
        if (NetworkMode.IsOffline)
        {
            PlayerHealth me = PlayerHealth.LocalInstance;
            if (ServerCanSit(seat, me) && ServerSit(seat, OfflinePlayerId)) LocalSeatDown(seat);
            return;
        }
        if (!NetworkClient.isConnected) return;
        requestPending = true;
        requestAt = Time.time;
        CmdSit(seat);
    }

    [TargetRpc]
    private void TargetSitResult(NetworkConnectionToClient target, int seat, bool ok)
    {
        requestPending = false;
        if (ok) LocalSeatDown(seat);
    }

    private void LocalSeatDown(int seat)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        CinemaSeat s = Seat(seat);
        if (me == null || s == null) return;
        LocalSeat = seat;
        seatedAt = Time.time;
        localBody = me.GetComponent<Rigidbody>();
        if (localBody != null)
        {
            localWasKinematic = localBody.isKinematic;
            if (!localBody.isKinematic) localBody.linearVelocity = Vector3.zero;
            localBody.isKinematic = true; // PlayerMovement stops moving a kinematic body (same as the dev noclip)
        }
        PlaceLocal(me, s);
        RuleFx.Toast(MapRuleTuning.Current.standHint, 2.5f);
    }

    private void PlaceLocal(PlayerHealth me, CinemaSeat s)
    {
        Vector3 at = s.FloorPoint + Vector3.down * MapRuleTuning.Current.playerSitDrop;
        me.transform.SetPositionAndRotation(at, s.Facing);
        if (localBody != null) { localBody.position = at; localBody.rotation = s.Facing; }
    }

    /// <summary>LOCAL. Stand up (SPACE, or forced).</summary>
    public void LocalStand()
    {
        if (LocalSeat < 0) return;
        if (NetworkMode.IsOffline) ServerFree(OfflinePlayerId);
        else if (NetworkClient.isConnected) CmdStand();
        LocalUnseat();
    }

    private void LocalUnseat()
    {
        CinemaSeat s = Seat(LocalSeat);
        LocalSeat = -1;
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me != null && s != null)
        {
            me.transform.position = s.StandPoint;
            if (localBody != null) localBody.position = s.StandPoint;
        }
        if (localBody != null)
        {
            localBody.isKinematic = localWasKinematic;
            if (!localBody.isKinematic) localBody.linearVelocity = Vector3.zero;
        }
        localBody = null;
    }

    private void LocalTick()
    {
        if (LocalSeat < 0) return;
        PlayerHealth me = PlayerHealth.LocalInstance;
        MatchDirector match = MatchDirector.Instance;
        bool forced = me == null || me.IsDead || me.IsDowned || (match != null && !match.InMatch);
        // The host freed the seat (e.g. it thought we were downed): stand up here too.
        bool lost = !NetworkMode.IsOffline && Time.time - seatedAt > 1.5f && me != null && SeatOfId(me.netId) != LocalSeat;
        Keyboard keyboard = Keyboard.current;
        bool space = keyboard != null && keyboard.spaceKey.wasPressedThisFrame && !GameplayInput.Blocked;
        if (forced || space || lost) { LocalStand(); return; }
        // Keep the body on the seat (the facing follows the mouse; only the position is held).
        Vector3 at = Seat(LocalSeat).FloorPoint + Vector3.down * MapRuleTuning.Current.playerSitDrop;
        if ((me.transform.position - at).sqrMagnitude > 0.0004f)
        {
            me.transform.position = at;
            if (localBody != null) localBody.position = at;
        }
    }

    // ---- Every machine: sitting pose for players and ghosts ------------------------------------

    private void ShowPoses()
    {
        poseNow.Clear();
        for (int i = 0; i < uses.Count; i++) poseNow.Add(uses[i].who);
        foreach (uint who in poseNow) if (!poseShown.Contains(who)) SetPose(who, true);
        foreach (uint who in poseShown) if (!poseNow.Contains(who)) SetPose(who, false);
        poseShown.Clear();
        foreach (uint who in poseNow) poseShown.Add(who);
    }

    private static void SetPose(uint who, bool sitting)
    {
        GameObject body = null;
        if (NetworkMode.IsOffline) body = who == OfflinePlayerId && PlayerHealth.LocalInstance != null ? PlayerHealth.LocalInstance.gameObject : null;
        else if (NetworkClient.spawned.TryGetValue(who, out NetworkIdentity id) && id != null) body = id.gameObject;
        if (body == null) return;
        SitPose pose = SitPose.For(body);
        if (pose != null) pose.Sitting = sitting;
    }

    private void Update()
    {
        if (NetworkMode.HasServerAuthority(this)) ServerTick();
        LocalTick();
        ShowPoses();
    }

    /// <summary>Editor tool.</summary>
    public void EditorSetSeats(CinemaSeat[] list) => seats = list;
}
