#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Dev cheat request, client -> server. Top-level and public so Mirror's weaver can write it.</summary>
public struct DevCheatMessage : NetworkMessage
{
    public byte cheat;
    public uint target; // 14 Oct: netId of the ghost source / task board (0 = none / offline)
    public int index;   // 14 Oct: offline fallback: position in the local list
    public int arg;     // 14 Oct: task line / ghost component index
}

/// <summary>
/// DEV CHEAT PANEL (2 Oct). Press F1. Only exists in the Unity Editor and in
/// Development Builds — a normal build does not even contain this file.
///
/// Cheats that change the shared game (health, clock, zones, lights, ghosts,
/// giving items) are asked of the SERVER with a Mirror message, so they work
/// for the host and for every client. Money, save reset, noclip, teleport and
/// ghost markers only touch your own PC.
/// </summary>
public sealed class DevCheatPanel : MonoBehaviour
{
    // ---- Server side -----------------------------------------------------------

    private enum Cheat : byte
    {
        GiveWalkie = 1, GodOn, GodOff, HealFull, GoDown, Die,
        ClockPlusHour, CompleteBoards, OpenExit, LightsToggle, GhostsFreeze, GhostsUnfreeze,
        GiveFlashlights, // 5 Oct: added at the END so older numbers keep their meaning
        GiveBatteries,
        SanityFull, SanityMinus25, SanityZero, GiveSanityItems, // 8 Oct
        ClockMinusHour, GhostSummonNow, GhostFireTrigger, CompleteTaskLine, CompleteZone, // 14 Oct
        InfHealthOn, InfHealthOff, InfSanityOn, InfSanityOff, UntargetableOn, UntargetableOff, // 14 Oct
    }

    private static bool serverHandlerReady;

    private static void OnServerCheat(NetworkConnectionToClient conn, DevCheatMessage msg)
    {
        GameObject player = conn != null && conn.identity != null ? conn.identity.gameObject : null;
        ApplyOnServer((Cheat)msg.cheat, player, msg.target, msg.index, msg.arg);
    }

    /// <summary>Runs on the server (or offline). player = the body of whoever asked.</summary>
    private static void ApplyOnServer(Cheat cheat, GameObject player, uint target = 0, int index = -1, int arg = 0)
    {
        PlayerHealth health = player != null ? player.GetComponent<PlayerHealth>() : null;
        MatchDirector match = MatchDirector.Instance;
        Debug.Log($"[DevCheat] {cheat} by {(player != null ? player.name : "?")}");

        switch (cheat)
        {
            case Cheat.GiveWalkie:
                PlayerInventory inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
                if (inventory != null) inventory.ServerAddItem(InventorySlot.Of(ItemCatalog.WalkieTalkie));
                break;
            case Cheat.GiveFlashlights:
                PlayerInventory pockets = player != null ? player.GetComponent<PlayerInventory>() : null;
                if (pockets != null)
                {
                    pockets.ServerAddItem(InventorySlot.Of(ItemCatalog.FlashlightBasic));
                    pockets.ServerAddItem(InventorySlot.Of(ItemCatalog.Flashlight));
                }
                break;
            case Cheat.GiveBatteries:
                PlayerInventory bag = player != null ? player.GetComponent<PlayerInventory>() : null;
                if (bag != null) { InventorySlot three = InventorySlot.Of(ItemCatalog.Battery); three.count = 3; bag.ServerAddStack(three); }
                break;
            case Cheat.SanityFull:
            case Cheat.SanityMinus25:
            case Cheat.SanityZero:
                PlayerSanity mind = player != null ? player.GetComponent<PlayerSanity>() : null;
                if (mind != null)
                    mind.ServerDevSet(cheat == Cheat.SanityFull ? PlayerSanity.Max : cheat == Cheat.SanityZero ? 0f : mind.Value - 25f);
                break;
            case Cheat.GiveSanityItems:
                PlayerInventory sack = player != null ? player.GetComponent<PlayerInventory>() : null;
                if (sack != null)
                {
                    InventorySlot snacks = InventorySlot.Of(ItemCatalog.Snack);
                    snacks.count = ItemCatalog.MaxStack(ItemCatalog.Snack);
                    sack.ServerAddStack(snacks);
                    sack.ServerAddItem(InventorySlot.Of(ItemCatalog.HolyBook));
                }
                break;
            // 14 Oct: god mode = infinite health + infinite sanity + untargetable
            // (infinite stamina is local, the panel sets it on your own PC).
            case Cheat.GodOn:
            case Cheat.GodOff:
                bool godOn = cheat == Cheat.GodOn;
                if (health != null)
                {
                    health.DevGodMode = godOn;
                    health.ServerDevSetInfiniteHealth(godOn);
                    health.DevUntargetable = godOn;
                }
                PlayerSanity godMind = player != null ? player.GetComponent<PlayerSanity>() : null;
                if (godMind != null) godMind.ServerDevSetInfinite(godOn);
                break;
            case Cheat.InfHealthOn: if (health != null) health.ServerDevSetInfiniteHealth(true); break;
            case Cheat.InfHealthOff:
                if (health != null) { health.ServerDevSetInfiniteHealth(false); health.DevGodMode = false; }
                break;
            case Cheat.InfSanityOn:
            case Cheat.InfSanityOff:
                PlayerSanity infMind = player != null ? player.GetComponent<PlayerSanity>() : null;
                if (infMind != null) infMind.ServerDevSetInfinite(cheat == Cheat.InfSanityOn);
                break;
            case Cheat.UntargetableOn: if (health != null) health.DevUntargetable = true; break;
            case Cheat.UntargetableOff: if (health != null) health.DevUntargetable = false; break;
            case Cheat.HealFull:
                if (health == null || health.IsDead) break;
                if (health.IsDowned) health.Revive(health.MaxHealth);
                else health.Heal(health.MaxHealth);
                break;
            case Cheat.GoDown: if (health != null) health.ServerDevGoDown(); break;
            case Cheat.Die: if (health != null) health.ServerKill("dev cheat", true); break;
            case Cheat.ClockPlusHour: if (match != null) match.ServerDevSkipSeconds(match.SecondsPerInGameHour); break;
            case Cheat.ClockMinusHour: if (match != null) match.ServerDevSkipSeconds(-match.SecondsPerInGameHour); break;
            case Cheat.GhostSummonNow:
            case Cheat.GhostFireTrigger:
                IDevGhostSource ghost = ResolveGhost(target, arg, index);
                if (ghost == null) break;
                if (cheat == Cheat.GhostSummonNow) ghost.ServerDevSummonNow(player);
                else ghost.ServerDevFireTrigger(player);
                break;
            case Cheat.CompleteTaskLine:
            case Cheat.CompleteZone:
                ZoneTaskList board = Resolve(target, index, Boards());
                if (board == null) break;
                if (cheat == Cheat.CompleteZone) board.ServerDevCompleteBoard();
                else board.ServerDevCompleteLine(arg);
                break;
            case Cheat.CompleteBoards: if (match != null) match.ServerDevCompleteAllZones(); break;
            case Cheat.OpenExit: if (match != null) match.ServerDevOpenExitNow(); break;
            case Cheat.LightsToggle:
                if (BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetLights(!BuildingLightManager.LightsOn);
                break;
            case Cheat.GhostsFreeze: Enemy_Abstract_Class.DevFrozen = true; break;
            case Cheat.GhostsUnfreeze: Enemy_Abstract_Class.DevFrozen = false; break;
        }
    }

    private static void Send(Cheat cheat, uint target = 0, int index = -1, int arg = 0)
    {
        if (NetworkMode.IsOffline)
        {
            PlayerHealth me = PlayerHealth.LocalInstance;
            ApplyOnServer(cheat, me != null ? me.gameObject : null, target, index, arg);
        }
        else if (NetworkClient.isConnected)
            NetworkClient.Send(new DevCheatMessage { cheat = (byte)cheat, target = target, index = index, arg = arg });
    }

    // ---- Targets (14 Oct): ghosts and task boards are found by netId (online) or list position (offline) ----

    private static System.Collections.Generic.List<IDevGhostSource> GhostSources()
    {
        var list = new System.Collections.Generic.List<IDevGhostSource>();
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>())
            if (behaviour is IDevGhostSource source) list.Add(source);
        list.Sort((a, b) => string.CompareOrdinal(a.DevGhostName, b.DevGhostName));
        return list;
    }

    private static System.Collections.Generic.List<ZoneTaskList> Boards()
    {
        var list = new System.Collections.Generic.List<ZoneTaskList>();
        foreach (ZoneTaskList board in ZoneTaskList.All) if (board != null) list.Add(board);
        list.Sort((a, b) => string.CompareOrdinal(a.ZoneID, b.ZoneID));
        return list;
    }

    private static T Resolve<T>(uint netId, int index, System.Collections.Generic.List<T> local) where T : class
    {
        if (netId != 0 && NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
            return identity.GetComponent<T>();
        return index >= 0 && index < local.Count ? local[index] : null;
    }

    /// <summary>
    /// 14 Oct fix: one object can hold TWO ghost sources (GhostLightOutMng has the timed
    /// GhostManager AND the TicketGhostSpawner), so GetComponent picked the wrong one.
    /// Online we send the behaviour's ComponentIndex as well.
    /// </summary>
    private static IDevGhostSource ResolveGhost(uint netId, int componentIndex, int index)
    {
        if (netId != 0 && NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null)
        {
            NetworkBehaviour[] behaviours = identity.NetworkBehaviours;
            if (behaviours != null && componentIndex >= 0 && componentIndex < behaviours.Length
                && behaviours[componentIndex] is IDevGhostSource exact)
                return exact;
            return identity.GetComponent<IDevGhostSource>();
        }
        System.Collections.Generic.List<IDevGhostSource> local = GhostSources();
        return index >= 0 && index < local.Count ? local[index] : null;
    }

    private static int ComponentIndexOf(object target) => target is NetworkBehaviour b ? b.ComponentIndex : -1;

    private static uint NetIdOf(object target) =>
        target is Component c && c.TryGetComponent(out NetworkIdentity identity) ? identity.netId : 0u;

    // ---- Panel -------------------------------------------------------------------

    private static DevCheatPanel instance;

    private bool open;
    private bool frozen, noclip, ghostMarkers;
    // 14 Oct: god mode = all four below on.
    private bool infHealth, infSanity, infStamina, untargetable;
    private bool God => infHealth && infSanity && infStamina && untargetable;
    private string lastAction = "";
    private Vector2 scroll;

    // 14 Oct: collapsible sections (click the arrow line). All closed at start = small panel.
    private bool showMoney, showPlayer, showMatch, showTasks, showDarkness, showGhost;
    private readonly System.Collections.Generic.HashSet<string> openBoards = new System.Collections.Generic.HashSet<string>();
    private System.Collections.Generic.List<IDevGhostSource> ghostSources = new System.Collections.Generic.List<IDevGhostSource>();
    private System.Collections.Generic.List<ZoneTaskList> boards = new System.Collections.Generic.List<ZoneTaskList>();
    private float nextListRefresh;

    // Noclip state
    private Rigidbody noclipBody;
    private Collider noclipCollider;
    private bool noclipWasKinematic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("Dev Cheat Panel (F1)");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DevCheatPanel>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        serverHandlerReady = false;
        Enemy_Abstract_Class.DevFrozen = false;
    }

    private void Update()
    {
        // (Re)register the server handler whenever a server is running.
        if (NetworkServer.active && !serverHandlerReady)
        {
            NetworkServer.ReplaceHandler<DevCheatMessage>(OnServerCheat);
            serverHandlerReady = true;
        }
        else if (!NetworkServer.active && serverHandlerReady)
        {
            serverHandlerReady = false;
            Enemy_Abstract_Class.DevFrozen = false;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) SetOpen(!open);
        else if (open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false);

        // A new body (new scene / respawn): noclip and god do not carry over
        // (the server's DevGodMode lives on the old body).
        if (PlayerHealth.LocalInstance != lastBody)
        {
            lastBody = PlayerHealth.LocalInstance;
            infHealth = infSanity = infStamina = untargetable = false;
        }
        // Infinite stamina is this PC only (stamina is local).
        PlayerStamina stamina = PlayerStamina.LocalInstance;
        if (stamina != null && stamina.DevInfinite != infStamina) stamina.DevInfinite = infStamina;
        if (noclip && (noclipBody == null || PlayerHealth.LocalInstance == null || noclipBody.gameObject != PlayerHealth.LocalInstance.gameObject))
            StopNoclip();

        // Checked once per frame (it searches the seats), not on every GUI event.
        serverCheatsAllowed = NetworkMode.IsOffline || NetworkServer.active || RoHRoomPlayer.HostIsDevBuild;

        if (noclip) MoveNoclip();

        // Ghost / board lists for the panel: refreshed once a second, only while open.
        if (open && Time.unscaledTime >= nextListRefresh)
        {
            nextListRefresh = Time.unscaledTime + 1f;
            ghostSources = GhostSources();
            boards = Boards();
        }
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
        nextListRefresh = 0f;
        if (open) OverlayPanels.Opened(); else OverlayPanels.Closed();
        if (PlayerHealth.LocalInstance != null || SpectatorSession.Active) OverlayPanels.SetMouseForUi(open);
        else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    private void OnDestroy()
    {
        if (open) OverlayPanels.Closed();
    }

    private void OnGUI()
    {
        if (ghostMarkers) DrawGhostMarkers();
        if (!open) return;

        float scale = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUI.skin.button.fontSize = 22;
        GUI.skin.label.fontSize = 22;
        GUI.skin.box.fontSize = 24;

        GUILayout.BeginArea(new Rect(30f, 120f, 520f, 900f), GUI.skin.box);
        GUILayout.Label("<b>DEV CHEATS</b>   (F1 / Esc to close)");
        if (SpectatorSession.Active) GUILayout.Label("<color=#7FD7FF>You are SPECTATING (no body).</color>");
        if (!ServerCheatsAllowed) GUILayout.Label("<color=#FF8060>Host = normal build: only your own-PC cheats work.</color>");
        scroll = GUILayout.BeginScrollView(scroll);

        if (Section("Money + items  (your save)", ref showMoney))
        {
            Row(("+100 currency", () => AddMoney(100)), ("+1000 currency", () => AddMoney(1000)));
            Row(("Give Walkie-Talkie", () => Do(Cheat.GiveWalkie, "Walkie given (spare if you have one)")),
                ("Reset save", ResetSave));
            Row(("Give both flashlights", () => Do(Cheat.GiveFlashlights, "Basic + paid flashlight given")),
                ("Give 3 batteries", () => Do(Cheat.GiveBatteries, "Batteries filled to 3")));
        }

        if (Section("Player", ref showPlayer))
        {
            bool god = God;
            Row((god ? "God mode: ON" : "God mode: OFF", () => SetGod(!god)),
                ("Heal to full", () => Do(Cheat.HealFull, "Healed")));
            GUILayout.Label("  <size=17>God mode = all 4 below (ghosts ignore you).</size>");
            Row((infHealth ? "Inf. health: ON" : "Inf. health: OFF",
                    () => { if (Do(infHealth ? Cheat.InfHealthOff : Cheat.InfHealthOn, infHealth ? "Infinite health off" : "Infinite health on")) infHealth = !infHealth; }),
                (infSanity ? "Inf. sanity: ON" : "Inf. sanity: OFF",
                    () => { if (Do(infSanity ? Cheat.InfSanityOff : Cheat.InfSanityOn, infSanity ? "Infinite sanity off" : "Infinite sanity on")) infSanity = !infSanity; }));
            Row((infStamina ? "Inf. stamina: ON" : "Inf. stamina: OFF",
                    () => { infStamina = !infStamina; Done(infStamina ? "Infinite stamina on" : "Infinite stamina off"); }),
                (untargetable ? "Untargetable: ON" : "Untargetable: OFF",
                    () => { if (Do(untargetable ? Cheat.UntargetableOff : Cheat.UntargetableOn, untargetable ? "Ghosts can target you" : "Ghosts ignore you")) untargetable = !untargetable; }));
            Row(("Go down", () => Do(Cheat.GoDown, "Downed")),
                ("Die", () => Do(Cheat.Die, "Dead")));
            Row((noclip ? "Noclip / fly: ON" : "Noclip / fly: OFF", ToggleNoclip), ("", null));
            Row(("Sanity full", () => Do(Cheat.SanityFull, "Sanity 100")),
                ("Sanity -25", () => Do(Cheat.SanityMinus25, "Sanity -25")));
            Row(("Sanity 0", () => Do(Cheat.SanityZero, "Sanity 0 (jumpscare soon)")),
                ("Give snacks + Holy Book", () => Do(Cheat.GiveSanityItems, "Snacks + Holy Book given")));
            if (noclip) GUILayout.Label("  WASD move, Space up, Ctrl down, Shift fast");
        }

        if (Section("Match", ref showMatch))
        {
            Row(("Clock -1 hour", () => Do(Cheat.ClockMinusHour, "Clock -1 hour")),
                ("Clock +1 hour", () => Do(Cheat.ClockPlusHour, "Clock +1 hour")));
            Row(("Open the exit now", () => Do(Cheat.OpenExit, "Zones done + 06:00")),
                ("Lights on / off", () => Do(Cheat.LightsToggle, "Lights toggled")));
        }

        if (Section("Tasks  (finish lines / zones)", ref showTasks)) TaskBoards();

        if (Section("Darkness  (this PC only, try values)", ref showDarkness)) DarknessSliders();

        if (Section("Ghost", ref showGhost))
        {
            Row((frozen ? "Ghosts frozen: ON" : "Ghosts frozen: OFF", () => { if (Do(frozen ? Cheat.GhostsUnfreeze : Cheat.GhostsFreeze, frozen ? "Ghosts move" : "Ghosts frozen")) frozen = !frozen; }),
                ("Teleport to ghost", TeleportToGhost));
            Row((ghostMarkers ? "Ghost markers: ON" : "Ghost markers: OFF", () => ghostMarkers = !ghostMarkers), ("", null));
            GhostSummons();
        }

        GUILayout.EndScrollView();
        GUILayout.Label(lastAction);
        GUILayout.EndArea();
        GUI.matrix = Matrix4x4.identity;
    }

    private static void Header(string text) => GUILayout.Label("\n<b>" + text + "</b>");

    /// <summary>14 Oct: a clickable section title with an arrow. Returns true while open.</summary>
    private static bool Section(string title, ref bool isOpen)
    {
        GUILayout.Space(6f);
        if (GUILayout.Button((isOpen ? "\u25BC  " : "\u25BA  ") + title, Fold, GUILayout.Height(40f))) isOpen = !isOpen;
        return isOpen;
    }

    private static GUIStyle fold;
    private static GUIStyle Fold
    {
        get
        {
            if (fold == null)
                fold = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, richText = true };
            fold.fontSize = 22;
            return fold;
        }
    }

    // ---- Tasks (14 Oct) ------------------------------------------------------------

    private void TaskBoards()
    {
        Row(("Finish ALL zones", () => Do(Cheat.CompleteBoards, "All zones done")), ("", null));
        if (boards.Count == 0) { GUILayout.Label("  No task boards in this scene."); return; }

        for (int b = 0; b < boards.Count; b++)
        {
            ZoneTaskList board = boards[b];
            if (board == null) continue;
            int done = 0;
            for (int i = 0; i < board.TaskCount; i++) if (board.IsTaskDone(i)) done++;

            bool isOpen = openBoards.Contains(board.ZoneID);
            string title = $"{board.Title}  ({done}/{board.TaskCount}){(board.AllDone ? "  <color=#7CFC7C>DONE</color>" : "")}";
            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            if (GUILayout.Button((isOpen ? "\u25BC  " : "\u25BA  ") + title, Fold, GUILayout.Height(36f)))
            {
                if (isOpen) openBoards.Remove(board.ZoneID); else openBoards.Add(board.ZoneID);
            }
            GUILayout.EndHorizontal();
            if (!isOpen) continue;

            int boardIndex = b;
            uint boardId = NetIdOf(board);
            for (int i = 0; i < board.TaskCount; i++)
            {
                int line = i;
                bool lineDone = board.IsTaskDone(i);
                GUILayout.BeginHorizontal();
                GUILayout.Space(32f);
                GUILayout.Label($"{(lineDone ? "<color=#7CFC7C>" : "")}{board.TaskAt(i).label}  {board.ProgressOf(i)}/{board.TargetOf(i)}{(lineDone ? "</color>" : "")}",
                    GUILayout.Width(340f));
                GUI.enabled = !lineDone;
                if (GUILayout.Button("Finish", GUILayout.Width(100f), GUILayout.Height(34f)))
                    Do(Cheat.CompleteTaskLine, $"Finished: {board.TaskAt(line).label}", boardId, boardIndex, line);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Space(32f);
            GUI.enabled = !board.AllDone;
            if (GUILayout.Button($"Finish zone: {board.Title}", GUILayout.Width(440f), GUILayout.Height(38f)))
                Do(Cheat.CompleteZone, $"Zone done: {board.Title}", boardId, boardIndex);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }

    // ---- Ghost summons (14 Oct) ------------------------------------------------------

    private void GhostSummons()
    {
        GUILayout.Label("  <b>Summon</b> = appears now.  <b>Trigger x1</b> = its trigger happens once.");
        if (ghostSources.Count == 0) { GUILayout.Label("  No ghost sources in this scene."); return; }
        for (int g = 0; g < ghostSources.Count; g++)
        {
            IDevGhostSource source = ghostSources[g];
            if (source == null || (source is Object unityObject && unityObject == null)) continue;
            int index = g;
            uint id = NetIdOf(source);
            int component = ComponentIndexOf(source);
            GUILayout.Label($"  <b>{source.DevGhostName}</b>\n  <size=17>Trigger: {source.DevTriggerText}</size>");
            Row(("Summon now", () => Do(Cheat.GhostSummonNow, $"{source.DevGhostName}: summoned", id, index, component)),
                ("Trigger x1", () => Do(Cheat.GhostFireTrigger, $"{source.DevGhostName}: trigger x1", id, index, component)));
        }
    }

    // 5 Oct: tune the darkness live (this PC only, visuals only). Copy the numbers
    // you like into the map's DarknessController (Tools > Pre-Thesis > Darkness).
    private DarknessController darkness;

    private void DarknessSliders()
    {
        if (darkness == null) darkness = FindAnyObjectByType<DarknessController>();
        if (darkness == null) { GUILayout.Label("  No darkness in this scene."); return; }
        darkness.previewDark = GUILayout.Toggle(darkness.previewDark, " Preview dark (without turning lights off)");
        darkness.visibleDistance = Slider("See without light (m)", darkness.visibleDistance, 1f, 30f);
        darkness.nearBrightness = Slider("Near brightness", darkness.nearBrightness, 0f, 3f);
        darkness.fogDistance = Slider("Black fog distance (m)", darkness.fogDistance, 5f, 100f);
        darkness.reflectionsLeft = Slider("Reflections left", darkness.reflectionsLeft, 0f, 1f);
    }

    private static float Slider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {value:0.##}", GUILayout.Width(250f));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(220f));
        GUILayout.EndHorizontal();
        return value;
    }

    private static void Row((string label, System.Action action) a, (string label, System.Action action) b)
    {
        GUILayout.BeginHorizontal();
        Button(a.label, a.action);
        Button(b.label, b.action);
        GUILayout.EndHorizontal();
    }

    private static void Button(string label, System.Action action)
    {
        if (action == null) { GUILayout.Label("", GUILayout.Width(235f)); return; }
        if (GUILayout.Button(label, GUILayout.Width(235f), GUILayout.Height(46f))) action();
    }

    private void Done(string text) => lastAction = text;

    /// <summary>
    /// Server cheats need a host running the Editor / a Development Build. A
    /// normal-build host has no handler and Mirror would disconnect us, so we
    /// do not send at all then.
    /// </summary>
    private bool serverCheatsAllowed;
    private PlayerHealth lastBody;
    private bool ServerCheatsAllowed => serverCheatsAllowed;

    private bool Do(Cheat cheat, string message, uint target = 0, int index = -1, int arg = 0)
    {
        if (!ServerCheatsAllowed) { Done("Host is a NORMAL build: server cheats are off."); return false; }
        Send(cheat, target, index, arg);
        Done(message);
        return true;
    }

    /// <summary>14 Oct: god mode = infinite health + sanity + stamina + untargetable, all on or all off.</summary>
    private void SetGod(bool on)
    {
        if (!Do(on ? Cheat.GodOn : Cheat.GodOff, on ? "God mode on (ghosts ignore you)" : "God mode off")) return;
        infHealth = infSanity = infStamina = untargetable = on;
    }

    // ---- Local cheats ------------------------------------------------------------

    private void AddMoney(int amount)
    {
        if (SaveManager.Current == null) { Done("No save loaded"); return; }
        SaveManager.AddCurrency(amount);
        SaveManager.SaveToDisk();
        Done($"Currency: {SaveManager.Current.currency}");
    }

    private void ResetSave()
    {
        SaveData save = SaveManager.Current;
        if (save == null) { Done("No save loaded"); return; }
        save.currency = 0;
        if (save.permanentItems != null)
            foreach (PermanentItemData item in save.permanentItems) if (item != null) item.isOwned = false;
        if (save.consumables != null) save.consumables.Clear();
        PlayerInventory.LastLoadout.Clear();
        SaveManager.SaveToDisk();
        Done("Save reset (currency 0, no items). Hotbar changes after the next spawn.");
    }

    private void ToggleNoclip()
    {
        if (noclip) { StopNoclip(); Done("Noclip off"); return; }
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null) { Done("No body"); return; }
        noclipBody = me.GetComponent<Rigidbody>();
        noclipCollider = me.GetComponent<CapsuleCollider>();
        if (noclipBody == null) { Done("No Rigidbody"); return; }
        noclipWasKinematic = noclipBody.isKinematic;
        noclipBody.linearVelocity = Vector3.zero;
        noclipBody.isKinematic = true;        // PlayerMovement stops moving a kinematic body
        if (noclipCollider != null) noclipCollider.enabled = false;
        noclip = true;
        Done("Noclip on");
    }

    private void StopNoclip()
    {
        if (noclipBody != null) noclipBody.isKinematic = noclipWasKinematic;
        if (noclipCollider != null) noclipCollider.enabled = true;
        noclipBody = null;
        noclipCollider = null;
        noclip = false;
    }

    private void MoveNoclip()
    {
        if (open || GameplayInput.Blocked || noclipBody == null) return;
        Keyboard k = Keyboard.current;
        if (k == null) return;
        Camera cam = noclipBody.GetComponentInChildren<Camera>();
        Transform view = cam != null ? cam.transform : noclipBody.transform;

        Vector3 move = Vector3.zero;
        if (k.wKey.isPressed) move += view.forward;
        if (k.sKey.isPressed) move -= view.forward;
        if (k.dKey.isPressed) move += view.right;
        if (k.aKey.isPressed) move -= view.right;
        if (k.spaceKey.isPressed) move += Vector3.up;
        if (k.leftCtrlKey.isPressed) move -= Vector3.up;
        float speed = k.leftShiftKey.isPressed ? 18f : 6f;
        noclipBody.transform.position += move.normalized * speed * Time.unscaledDeltaTime;
    }

    private void TeleportToGhost()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        Enemy_Abstract_Class ghost = NearestGhost(me != null ? me.transform.position : Vector3.zero);
        if (me == null || ghost == null) { Done("No ghost right now"); return; }
        Vector3 target = ghost.transform.position - ghost.transform.forward * 2.5f + Vector3.up * 0.2f;
        Rigidbody body = me.GetComponent<Rigidbody>();
        if (body != null) { body.linearVelocity = Vector3.zero; body.position = target; }
        me.transform.position = target;  // owner-authoritative movement: the NetworkTransform sends it
        Done("Teleported behind the ghost");
    }

    private static Enemy_Abstract_Class NearestGhost(Vector3 from)
    {
        Enemy_Abstract_Class best = null;
        float bestDistance = float.MaxValue;
        foreach (Enemy_Abstract_Class ghost in FindObjectsByType<Enemy_Abstract_Class>())
        {
            float d = (ghost.transform.position - from).sqrMagnitude;
            if (d < bestDistance) { bestDistance = d; best = ghost; }
        }
        return best;
    }

    private void DrawGhostMarkers()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        Camera cam = me != null ? me.GetComponentInChildren<Camera>() : Camera.main;
        if (cam == null) return;

        GUI.matrix = Matrix4x4.identity;
        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * Screen.height / 1080f), richText = true };
        foreach (Enemy_Abstract_Class ghost in FindObjectsByType<Enemy_Abstract_Class>())
        {
            Vector3 p = cam.WorldToScreenPoint(ghost.transform.position + Vector3.up * 1.8f);
            if (p.z <= 0f) continue; // behind the camera
            float distance = Vector3.Distance(cam.transform.position, ghost.transform.position);
            GUI.Label(new Rect(p.x - 80f, Screen.height - p.y - 20f, 220f, 40f),
                $"<color=#FF5050><b>GHOST</b> {distance:F0} m</color>", style);
        }
    }
}
#endif
