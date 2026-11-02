using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// SCREEN ROOM rule 4 "Staff time" (เวลาของพนักงาน).
///
/// 2 Nov 2026 (Mr.k) — NEW DESIGN (replaces the 16 Oct "hold E, then change the film" version):
///   1. It appears: the cinema screen shows TV STATIC with a static sound.
///   2. Every ghost inside the Screen Room disappears (seated audience, rule ghosts,
///      the timed ghost). Ghosts that walk in while the static runs disappear too.
///   3. A black SHADOW appears on one of the shadow spots (the seat players must use).
///      For now: just stand on it. (Later: press E to sit on the real cinema chair.)
///   4. Stay on the shadow until the static stops: random 3–6 s. Step off = start again.
///   5. Nobody on the shadow within 13 s: the ghost comes out of the screen and roams
///      the whole map until 06:00 (mockup rule ghost). The 13 s pauses while someone
///      stands on the shadow (2 Nov fix) and goes on if they step off.
///   One player on the shadow is enough for everyone.
///
/// WHEN IT APPEARS (both ways, Mr.k 2 Nov):
///   - A "Change film" chore becomes due while someone is in the hall: <see cref="chance"/>.
///   - A random roll every <see cref="randomRollEvery"/> s while someone is in the hall.
///   "In the hall" = at least as close to the screen as the projector (2 Nov fix: before,
///   standing on the entrance stairs was enough).
///
/// LIKE A REAL CINEMA (2 Nov): the screen is always lit (<see cref="CinemaScreen"/>): blank
/// when no film plays, static when this ghost comes (later: a real film the ghost swaps).
/// Its picture ignores the room's fog, so it is seen from the projector. A light from the
/// screen shines over the seats. The static sound fills the whole room.
///   Then a cooldown. Changing the film during the static is no longer punished.
///
/// Not touched: the Missing Parts Ghost (a quest customer, not a room ghost).
/// Nonnapat's ghost code is not edited: the timed ghost is removed through its own
/// public calls (GhostManager.CancelDespawnTimer / ResetManagerAfterJumpscare,
/// GhostManifestation.Despawn).
///
/// SERVER AUTHORITY: start, who stands on the shadow, success, punishment and ghost
/// removal run on the server. Clients only draw the static, the shadow and the sound
/// from SyncVars (late joiners get the current state).
/// </summary>
public sealed class RuleStaffTime : ZoneRuleBase
{
    [Header("Staff time — scene objects")]
    [SerializeField] private GhostAudience audience;
    [Tooltip("The picture on the cinema screen (mockup prefab \"RM_GhostMovie\"). Shows TV static while the rule runs.")]
    [SerializeField] private GameObject ghostMovie;
    [Tooltip("Old \"Hold E: sit\" object. Not used for sitting any more (kept hidden). Its \"Shadow\" look is copied as the black shadow.")]
    [SerializeField] private RuleInteractable shadowSeat;
    [Tooltip("Places where the shadow may appear (one is picked each time). Move them onto / in front of the cinema chairs.")]
    [SerializeField] private Transform[] shadowSpots;
    [Tooltip("The mockup ghost that comes out of the screen and roams the map.")]
    [SerializeField] private RuleGhost roamer;
    [Tooltip("The cinema screen to cover with static. Empty = the object named \"Screen\" inside this room.")]
    [SerializeField] private Transform screen;
    [Tooltip("Optional static sound. Empty = generated TV noise.")]
    [SerializeField] private AudioClip staticClip;

    [Header("When it appears")]
    [Tooltip("Chance when a \"Change film\" chore becomes due and someone is in the room (once per film change).")]
    [SerializeField, Range(0f, 1f)] private float chance = 0.5f;
    [Tooltip("Random roll every this many seconds while someone is in the room.")]
    [SerializeField, Min(1f)] private float randomRollEvery = 6f;
    [Tooltip("Chance of each random roll.")]
    [SerializeField, Range(0f, 1f)] private float randomChance = 0.2f;
    [Tooltip("After the static ends (either way), wait this long before it can come again.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 45f;
    [Tooltip("A player must be at least as close to the screen as this point (the projector). Empty = the \"Change film\" projector.")]
    [SerializeField] private Transform projector;
    [Tooltip("Metres behind the projector (away from the screen) that still count as \"in the hall\".")]
    [SerializeField, Min(0f)] private float extraReach = 2f;

    [Header("The shadow")]
    [Tooltip("Players must reach the shadow within this many seconds, or the ghost comes out.")]
    [SerializeField, Min(1f)] private float sitDeadlineSeconds = 13f;
    [Tooltip("Shortest time to stay on the shadow (random between min and max each time).")]
    [SerializeField, Min(0.5f)] private float minSitSeconds = 3f;
    [Tooltip("Longest time to stay on the shadow.")]
    [SerializeField, Min(0.5f)] private float maxSitSeconds = 6f;
    [Tooltip("How close to the shadow's centre counts as \"on it\" (metres, flat). Also the size of the black floor circle.")]
    [SerializeField, Min(0.2f)] private float standRadius = 0.8f;
    [Tooltip("Height of the shadow figure in metres (player height: the Y Bot model is about 1.8 m).")]
    [SerializeField, Min(0.5f)] private float shadowFigureHeight = 1.8f;
    [Tooltip("The roaming ghost leaves at this in-game hour (6 = 06:00).")]
    [SerializeField, Range(1, 7)] private int roamUntilHour = 6;

    [Header("Static look + sound")]
    [Tooltip("Static sound loudness. Full loudness everywhere in the room.")]
    [SerializeField, Range(0f, 1f)] private float staticLoudness = 0.8f;
    [Tooltip("Outside the room the sound fades to silent over this many metres.")]
    [SerializeField, Min(1f)] private float soundFadeOutside = 12f;
    [Tooltip("Brightness of the static picture.")]
    [SerializeField, Min(0f)] private float staticBrightness = 1.2f;
    [Tooltip("Brightness of the empty screen when no film plays (it never goes black).")]
    [SerializeField, Min(0f)] private float blankBrightness = 0.45f;
    [Tooltip("Brightness of a film picture (later).")]
    [SerializeField, Min(0f)] private float filmBrightness = 1f;

    [Header("Screen light (like a real cinema)")]
    [Tooltip("A light from the screen over the seats (soft on a blank screen, flickering with the static).")]
    [SerializeField] private bool screenLight = true;
    [SerializeField, Min(0f)] private float screenLightIntensity = 60f;
    [SerializeField] private Color screenLightColor = new Color(0.78f, 0.83f, 0.95f);
    [Tooltip("How many times per second the static picture changes.")]
    [SerializeField, Range(5, 60)] private int staticFps = 24;

    [SyncVar(hook = nameof(OnMovieChanged))] private bool movieOn;
    [SyncVar(hook = nameof(OnSpotChanged))] private int spot = -1;
    [SyncVar] private uint sitterId;
    [SyncVar] private double sitStart;
    [SyncVar] private float sitNeed = 4f;

    // Server only.
    private ZoneChoreSpot film;
    private bool rolledThisSlot;
    private bool lastNeeded;
    private double timeLeft;  // the 13 s; only runs while nobody stands on the shadow
    private double lastTick;
    private double nextRoll;
    private double nextAllowed;
    private float nextSweep;
    private GameObject offlineSitter;
    private readonly HashSet<TimedGhost> removedGhosts = new HashSet<TimedGhost>();

    // Every machine (looks).
    private CinemaScreen cinema;
    private Transform marker;
    private AudioSource staticSource;
    private Vector3 screenNormal = Vector3.zero; // into the room
    private static AudioClip generatedNoise;

    private void Start()
    {
        FitMovieToScreen();
        SetupCinemaScreen();
        SetupShadowMarker();
        SetupStaticSound();
        ApplyMovie(movieOn);
        ApplySpot(spot);

        if (!IsServerSide) return;
        film = FindFilmSpot();
        if (film == null) Log("No 'Change film' chore found in this zone (only the random trigger will work).");
        if (projector == null && film != null) projector = film.transform;
        if (shadowSeat != null) shadowSeat.ServerSetUsable(false); // no more "Hold E: sit"
    }

    private void OnDestroy()
    {
    }

    private ZoneChoreSpot FindFilmSpot()
    {
        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.Kind == ZoneTaskKind.FilmChange && zone != null && zone.Contains(c.transform.position)) return c;
        foreach (ZoneChoreSpot c in FindObjectsByType<ZoneChoreSpot>(FindObjectsInactive.Include))
            if (c.Kind == ZoneTaskKind.FilmChange) return c;
        return null;
    }

    // ---- Server ------------------------------------------------------------------------

    private void Update()
    {
        LocalSitBar();
        if (!IsServerSide) return;
        if (!ServerRuleLive) { if (movieOn) StopMovie(); return; }

        if (!movieOn) { ServerTryStart(); return; }

        if (Time.time >= nextSweep)
        {
            nextSweep = Time.time + 0.5f;
            ServerClearGhosts();
        }
        ServerSitCheck();
    }

    private void ServerTryStart()
    {
        bool needed = film != null && film.Needed;
        if (needed && !lastNeeded) rolledThisSlot = false; // a new film change is due
        lastNeeded = needed;

        if (Now < nextAllowed) return;
        if (!ServerAnyoneInHall()) return;
        if (audience != null && audience.Standing) return; // "the audience stands" is running

        if (needed && !rolledThisSlot)
        {
            rolledThisSlot = true;
            if (Random.value < chance) { StartMovie("film change"); return; }
        }
        if (Now >= nextRoll)
        {
            nextRoll = Now + randomRollEvery;
            if (Random.value < randomChance) StartMovie("random roll");
        }
    }

    private void StartMovie(string why)
    {
        sitterId = 0;
        offlineSitter = null;
        sitNeed = Random.Range(minSitSeconds, Mathf.Max(minSitSeconds, maxSitSeconds));
        timeLeft = sitDeadlineSeconds;
        lastTick = Now;
        spot = shadowSpots != null && shadowSpots.Length > 0 ? Random.Range(0, shadowSpots.Length) : -1;
        ApplySpot(spot);
        movieOn = true;
        ApplyMovie(true);
        removedGhosts.Clear();
        ServerClearGhosts();
        nextSweep = Time.time + 0.5f;
        if (spot < 0) Log("No shadow spots: nobody can stand on the shadow (add points under \"Shadow Spots\").");
        Log($"Static on the screen ({why}). Shadow at spot {spot + 1}. Stand on it within {sitDeadlineSeconds:0} s, stay {sitNeed:0.0} s.");
    }

    private void StopMovie()
    {
        movieOn = false;
        ApplyMovie(false);
        sitterId = 0;
        offlineSitter = null;
        if (audience != null) audience.ServerSetVisible(true);
        nextAllowed = Now + cooldownSeconds;
        nextRoll = Now + randomRollEvery;
    }

    private void ServerSitCheck()
    {
        double dt = Now - lastTick;
        lastTick = Now;
        PlayerHealth sitter = SitterBody();
        if (sitterId != 0 && !OnShadow(sitter))
        {
            if (sitter != null) ServerShow(sitter, false, "You left the shadow. Stand on it again.");
            sitterId = 0;
            offlineSitter = null;
            sitter = null;
        }

        if (sitter == null)
        {
            sitter = FirstOnShadow();
            if (sitter != null)
            {
                sitterId = NetworkMode.IsOffline ? 1u : sitter.netId;
                offlineSitter = NetworkMode.IsOffline ? sitter.gameObject : null;
                sitStart = Now;
                Log($"{sitter.name} stands on the shadow.");
            }
        }

        if (sitter != null)
        {
            if (Now - sitStart < sitNeed) return;
            Log($"{sitter.name} stayed on the shadow. The static stops.");
            ServerShow(sitter, false, "The static stops.");
            StopMovie();
            return;
        }

        timeLeft -= dt; // paused while someone stands on the shadow (returned above)
        if (timeLeft <= 0) ServerPunish();
    }

    private void ServerPunish()
    {
        Vector3 from = ghostMovie != null ? ghostMovie.transform.position : zone.Box.center;
        List<PlayerHealth> players = ServerPlayersInside();
        StopMovie();
        if (roamer != null) roamer.ServerRoam(from, roamUntilHour, "It came out of the screen...");
        else Log("No roaming rule ghost assigned: nothing comes out.");
        for (int i = 0; i < players.Count; i++) ServerShow(players[i], true, "Something came out of the screen...");
        Log($"Nobody stood on the shadow in {sitDeadlineSeconds:0} s: a ghost roams the map until {roamUntilHour}:00.");
    }

    /// <summary>SERVER. Is a living player inside the room AND at least as close to the screen as the projector?</summary>
    private bool ServerAnyoneInHall()
    {
        List<PlayerHealth> players = ServerPlayersInside();
        for (int i = 0; i < players.Count; i++)
        {
            PlayerHealth p = players[i];
            if (p != null && !p.IsDead && !p.IsDowned && InHall(p.transform.position)) return true;
        }
        return false;
    }

    private bool InHall(Vector3 position)
    {
        if (projector == null || ghostMovie == null || screenNormal == Vector3.zero) return true; // no projector known: the whole room
        Vector3 screenAt = ghostMovie.transform.position;
        float limit = Vector3.Dot(projector.position - screenAt, screenNormal) + extraReach;
        return Vector3.Dot(position - screenAt, screenNormal) <= limit;
    }

    private bool OnShadow(PlayerHealth p)
    {
        if (p == null || p.IsDead || p.IsDowned || !HasSpot) return false;
        Vector3 d = p.transform.position - shadowSpots[spot].position;
        if (Mathf.Abs(d.y) > 1.5f) return false;
        d.y = 0f;
        return d.sqrMagnitude <= standRadius * standRadius;
    }

    private PlayerHealth FirstOnShadow()
    {
        IReadOnlyList<PlayerHealth> all = PlayerRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (OnShadow(all[i])) return all[i];
        return null;
    }

    private bool HasSpot => shadowSpots != null && spot >= 0 && spot < shadowSpots.Length && shadowSpots[spot] != null;

    private PlayerHealth SitterBody()
    {
        if (sitterId == 0) return null;
        if (NetworkMode.IsOffline) return offlineSitter != null ? offlineSitter.GetComponent<PlayerHealth>() : null;
        return NetworkServer.spawned.TryGetValue(sitterId, out NetworkIdentity id) && id != null ? id.GetComponent<PlayerHealth>() : null;
    }

    /// <summary>SERVER. Every ghost inside the Screen Room disappears.</summary>
    private void ServerClearGhosts()
    {
        if (audience != null) audience.ServerSetVisible(false);
        if (zone == null) return;

        foreach (RuleGhost g in FindObjectsByType<RuleGhost>())
            if (g != null && g.IsOut && zone.Contains(g.transform.position)) g.ServerHide();

        foreach (TimedGhost t in FindObjectsByType<TimedGhost>())
        {
            if (t == null || removedGhosts.Contains(t) || !zone.Contains(t.transform.position)) continue;
            removedGhosts.Add(t);
            RemoveTimedGhost(t);
        }
    }

    private void RemoveTimedGhost(TimedGhost ghost)
    {
        // Nonnapat's GhostManager: stop its timer and let the next cycle start normally.
        GhostManager manager = GhostManager.Instance;
        if (manager != null)
        {
            manager.CancelDespawnTimer();
            manager.ResetManagerAfterJumpscare();
        }
        GhostManifestation manifestation = ghost.GetComponentInParent<GhostManifestation>();
        if (manifestation != null) manifestation.Despawn(); // plays its sink-away animation
        else
        {
            NetworkIdentity identity = ghost.GetComponentInParent<NetworkIdentity>();
            GameObject root = identity != null ? identity.gameObject : ghost.gameObject;
            if (NetworkServer.active) NetworkServer.Destroy(root);
            else Destroy(root);
        }
        Log($"'{ghost.name}' was in the room: it disappears.");
    }

    public override void ServerDevTrigger()
    {
        if (IsServerSide && !movieOn) StartMovie("dev");
    }

    // ---- Every machine -----------------------------------------------------------------------

    private void OnMovieChanged(bool oldValue, bool newValue) => ApplyMovie(newValue);
    private void OnSpotChanged(int oldValue, int newValue) => ApplySpot(newValue);

    private void ApplyMovie(bool on)
    {
        if (cinema != null) { if (on) cinema.BeginGhostFilm(); else cinema.EndGhostFilm(); }
        if (marker != null && marker.gameObject.activeSelf != on) marker.gameObject.SetActive(on);
        if (staticSource != null)
        {
            if (on && !staticSource.isPlaying) staticSource.Play();
            else if (!on && staticSource.isPlaying) staticSource.Stop();
        }
    }

    private void ApplySpot(int index)
    {
        if (marker == null || shadowSpots == null || index < 0 || index >= shadowSpots.Length || shadowSpots[index] == null) return;
        marker.SetPositionAndRotation(shadowSpots[index].position, shadowSpots[index].rotation);
    }

    /// <summary>
    /// 2 Nov fix: the picture was turned 2 degrees off the screen, so half of it was behind
    /// the screen. Now it lies flat on the screen's face (the screen is axis-aligned) and
    /// covers all of it. Every machine does the same, so no network data is needed.
    /// </summary>
    private void FitMovieToScreen()
    {
        if (ghostMovie == null) return;
        screenNormal = -ghostMovie.transform.forward; // until the screen is found
        Transform target = screen != null ? screen : FindScreen();
        if (target == null || !BoundsOf(target, out Bounds b))
        {
            Log("No \"Screen\" object found in this room: the static keeps its old size. Set \"Screen\" in the Inspector.");
            return;
        }
        Vector3 toRoom = zone != null ? zone.Box.center - b.center : -ghostMovie.transform.forward;
        Vector3 normal;
        float width;
        if (b.size.x <= b.size.z) { normal = new Vector3(toRoom.x >= 0f ? 1f : -1f, 0f, 0f); width = b.size.z; }
        else { normal = new Vector3(0f, 0f, toRoom.z >= 0f ? 1f : -1f); width = b.size.x; }
        float thickness = Mathf.Min(b.size.x, b.size.z);
        screenNormal = normal;

        Transform t = ghostMovie.transform;
        t.SetPositionAndRotation(b.center + normal * (thickness * 0.5f + 0.03f), Quaternion.LookRotation(-normal, Vector3.up));
        Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(width / Mathf.Max(0.001f, parentScale.x), b.size.y / Mathf.Max(0.001f, parentScale.y), 1f);
    }

    private Transform FindScreen()
    {
        if (zone == null) return null;
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (t.name == "Screen" && t != ghostMovie.transform && BoundsOf(t, out Bounds b) && zone.Contains(b.center)) return t;
        return null;
    }

    private static bool BoundsOf(Transform t, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>The screen picture: always lit (blank / film / ghost static), ignores fog. See CinemaScreen.</summary>
    private void SetupCinemaScreen()
    {
        if (ghostMovie == null) return;
        Renderer picture = null;
        Transform pic = ghostMovie.transform.Find("Picture");
        if (pic != null) picture = pic.GetComponent<Renderer>();
        foreach (Renderer r in ghostMovie.GetComponentsInChildren<Renderer>(true))
        {
            if (picture == null) picture = r;
            else if (r != picture) r.gameObject.SetActive(false); // old mockup ghost face (eyes / mouth)
        }
        ghostMovie.SetActive(true); // the screen is never a black hole now
        cinema = ghostMovie.GetComponent<CinemaScreen>();
        if (cinema == null) cinema = ghostMovie.AddComponent<CinemaScreen>();
        Vector3 into = screenNormal != Vector3.zero ? screenNormal : -ghostMovie.transform.forward;
        float roomLength = zone != null ? zone.Box.size.magnitude : 70f;
        cinema.Setup(picture, into, roomLength, screenLightColor, screenLightIntensity,
            blankBrightness, filmBrightness, staticBrightness, staticFps, screenLight);
    }

    /// <summary>The black shadow players stand on: a copy of the old shadow look + a black floor circle. No colliders.</summary>
    private void SetupShadowMarker()
    {
        marker = new GameObject("Shadow (stand here)").transform;
        marker.SetParent(transform, false);

        Material shadowMaterial = null;
        Transform look = shadowSeat != null ? shadowSeat.transform.Find("Shadow") : null;
        if (look != null)
        {
            GameObject copy = Instantiate(look.gameObject, marker);
            copy.name = "Shadow Figure";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.SetActive(true);
            RuleFx.ScaleToHeight(copy.transform, marker.position.y, shadowFigureHeight); // as tall as a player
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
            Renderer r = copy.GetComponentInChildren<Renderer>(true);
            if (r != null) shadowMaterial = r.sharedMaterial;
        }

        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Floor Shadow";
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(marker, false);
        disc.transform.localPosition = Vector3.up * 0.02f;
        disc.transform.localScale = new Vector3(standRadius * 2f, 0.01f, standRadius * 2f);
        Renderer discRenderer = disc.GetComponent<Renderer>();
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (shadowMaterial != null) discRenderer.sharedMaterial = shadowMaterial;
        else discRenderer.material.color = Color.black;

        marker.gameObject.SetActive(false);
    }

    private void SetupStaticSound()
    {
        var go = new GameObject("Static Sound");
        go.transform.SetParent(transform, false);
        go.transform.position = ghostMovie != null ? ghostMovie.transform.position : (zone != null ? zone.Box.center : transform.position);
        staticSource = go.AddComponent<AudioSource>();
        staticSource.playOnAwake = false;
        staticSource.loop = true;
        staticSource.clip = staticClip != null ? staticClip : NoiseClip();
        staticSource.spatialBlend = 1f;
        staticSource.dopplerLevel = 0f;
        staticSource.rolloffMode = AudioRolloffMode.Linear;
        // Like cinema speakers: full loudness anywhere in the room, fades out outside it.
        float roomReach = 10f;
        if (zone != null)
        {
            Bounds box = zone.Box;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.center + Vector3.Scale(box.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                roomReach = Mathf.Max(roomReach, Vector3.Distance(go.transform.position, corner));
            }
        }
        staticSource.minDistance = roomReach;
        staticSource.maxDistance = roomReach + soundFadeOutside;
        SoundCategoryVolume volume = go.AddComponent<SoundCategoryVolume>();
        volume.Category = SoundCategory.Sfx;
        volume.SetBaseVolume(staticLoudness);
    }

    /// <summary>Placeholder TV static (2 s loop). Put a real clip in "Static Clip" to replace it.</summary>
    private static AudioClip NoiseClip()
    {
        if (generatedNoise != null) return generatedNoise;
        const int rate = 22050;
        int length = rate * 2;
        var data = new float[length];
        var random = new System.Random(13);
        float soft = 0f;
        for (int i = 0; i < length; i++)
        {
            float white = (float)random.NextDouble() * 2f - 1f;
            soft += (white - soft) * 0.55f; // take the sharpest edge off
            data[i] = soft * 0.6f;
        }
        generatedNoise = AudioClip.Create("Placeholder Static", length, 1, rate, false);
        generatedNoise.SetData(data, 0);
        return generatedNoise;
    }

    private void LocalSitBar()
    {
        PlayerHealth me = LocalBody;
        bool mine = movieOn && sitterId != 0 && me != null && (NetworkMode.IsOffline ? sitterId == 1u : sitterId == me.netId);
        if (!mine) { ChoreProgressHUD.Hide(this); return; }
        float need = sitNeed > 0.1f ? sitNeed : 1f;
        ChoreProgressHUD.ShowText(this, "Stay on the shadow... wait for the static to stop", (float)((Now - sitStart) / need));
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ChoreProgressHUD.Hide(this);
    }
}
