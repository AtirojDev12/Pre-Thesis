using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 23 Nov 2026 (Mr.k): TOILET CLEANING TIME. While any toilet still needs cleaning, a female
/// and a male ghost (the NPC ghost models) walk around the map and keep saying that the
/// toilet is dirty (speech bubble; players close by also get it on screen).
/// They leave when the toilets are clean (or the night ends). Harmless.
/// Numbers and lines: MapRuleTuning (Toilet ghosts). Walker roles: "ToiletGhostF", "ToiletGhostM".
/// </summary>
public sealed class ToiletGhostEvent : MapEventBase
{
    public const string RoleFemale = "ToiletGhostF";
    public const string RoleMale = "ToiletGhostM";

    private MapWalker female, male;
    private float nextLine;
    private int lineIndex;
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();
    private readonly List<ZoneChoreSpot> dirty = new List<ZoneChoreSpot>();

    /// <summary>SERVER. 23 Nov: F1 dev cheat: the two ghosts walk even if the toilets are clean.</summary>
    public bool DevForced { get; set; }

    private void Update()
    {
        if (!IsServerSide) return;
        bool due = NightRunning && (FindDirty() || DevForced);
        if (due && female == null && male == null) Begin();
        else if (!due && (female != null || male != null)) End();
        if (female != null || male != null) Tick();
    }

    private bool FindDirty()
    {
        dirty.Clear();
        foreach (ZoneChoreSpot spot in ZoneChoreSpot.All)
            if (spot != null && spot.Kind == ZoneTaskKind.CleanToilet && spot.Needed) dirty.Add(spot);
        return dirty.Count > 0;
    }

    private void Begin()
    {
        female = Bring(RoleFemale, MapWalker.Body.Female, 0);
        male = Bring(RoleMale, MapWalker.Body.Male, 1);
        nextLine = 0f;
        lineIndex = 0;
        Debug.Log("[Map Events] Toilet cleaning time: two ghosts complain about the toilets.", this);
    }

    private MapWalker Bring(string role, MapWalker.Body look, int spotIndex)
    {
        MapWalker w = MapWalker.ServerTake(role);
        if (w == null) return null;
        Vector3 from = dirty.Count > 0 ? dirty[Mathf.Min(spotIndex, dirty.Count - 1)].transform.position : transform.position;
        w.ServerAppear(from, look);
        w.ServerSetSpeed(T.toiletGhostSpeed);
        w.ServerRoam();
        return w;
    }

    private void End()
    {
        if (female != null) female.ServerHide();
        if (male != null) male.ServerHide();
        female = male = null;
        Debug.Log("[Map Events] Toilets clean (or night over): the two ghosts leave.", this);
    }

    private void Tick()
    {
        if (Time.time < nextLine || T.toiletLines == null || T.toiletLines.Length == 0) return;
        nextLine = Time.time + T.toiletLineSeconds;
        Say(female, T.toiletLines[lineIndex % T.toiletLines.Length]);
        Say(male, T.toiletLines[(lineIndex + 1) % T.toiletLines.Length]);
        lineIndex++;
    }

    private void Say(MapWalker w, string line)
    {
        if (w == null) return;
        w.ServerSay(line);
        if (T.toiletShoutRange <= 0f) return;
        Players(players);
        float r2 = T.toiletShoutRange * T.toiletShoutRange;
        for (int i = 0; i < players.Count; i++)
            if ((players[i].transform.position - w.transform.position).sqrMagnitude <= r2) ServerShow(players[i], false, line);
    }
}
