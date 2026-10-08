#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Dev cheat request, client -> server. Top-level and public so Mirror's weaver can write it.</summary>
public struct DevCheatMessage : NetworkMessage
{
    public byte cheat;
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
    }

    private static bool serverHandlerReady;

    private static void OnServerCheat(NetworkConnectionToClient conn, DevCheatMessage msg)
    {
        GameObject player = conn != null && conn.identity != null ? conn.identity.gameObject : null;
        ApplyOnServer((Cheat)msg.cheat, player);
    }

    /// <summary>Runs on the server (or offline). player = the body of whoever asked.</summary>
    private static void ApplyOnServer(Cheat cheat, GameObject player)
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
            case Cheat.GodOn: if (health != null) health.DevGodMode = true; break;
            case Cheat.GodOff: if (health != null) health.DevGodMode = false; break;
            case Cheat.HealFull:
                if (health == null || health.IsDead) break;
                if (health.IsDowned) health.Revive(health.MaxHealth);
                else health.Heal(health.MaxHealth);
                break;
            case Cheat.GoDown: if (health != null) health.ServerDevGoDown(); break;
            case Cheat.Die: if (health != null) health.ServerKill("dev cheat"); break;
            case Cheat.ClockPlusHour: if (match != null) match.ServerDevSkipSeconds(match.SecondsPerInGameHour); break;
            case Cheat.CompleteBoards: if (match != null) match.ServerDevCompleteAllZones(); break;
            case Cheat.OpenExit: if (match != null) match.ServerDevOpenExitNow(); break;
            case Cheat.LightsToggle:
                if (GhostManager.Instance != null) GhostManager.Instance.PlayerToggleLights(!GhostManager.Instance.areLightsOnCurrently);
                break;
            case Cheat.GhostsFreeze: Enemy_Abstract_Class.DevFrozen = true; break;
            case Cheat.GhostsUnfreeze: Enemy_Abstract_Class.DevFrozen = false; break;
        }
    }

    private static void Send(Cheat cheat)
    {
        if (NetworkMode.IsOffline)
        {
            PlayerHealth me = PlayerHealth.LocalInstance;
            ApplyOnServer(cheat, me != null ? me.gameObject : null);
        }
        else if (NetworkClient.isConnected) NetworkClient.Send(new DevCheatMessage { cheat = (byte)cheat });
    }

    // ---- Panel -------------------------------------------------------------------

    private static DevCheatPanel instance;

    private bool open;
    private bool god, frozen, noclip, ghostMarkers;
    private string lastAction = "";
    private Vector2 scroll;

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
            god = false;
        }
        if (noclip && (noclipBody == null || PlayerHealth.LocalInstance == null || noclipBody.gameObject != PlayerHealth.LocalInstance.gameObject))
            StopNoclip();

        // Checked once per frame (it searches the seats), not on every GUI event.
        serverCheatsAllowed = NetworkMode.IsOffline || NetworkServer.active || RoHRoomPlayer.HostIsDevBuild;

        if (noclip) MoveNoclip();
    }

    private void SetOpen(bool value)
    {
        if (open == value) return;
        open = value;
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

        Header("Money + items  (your save)");
        Row(("+100 currency", () => AddMoney(100)), ("+1000 currency", () => AddMoney(1000)));
        Row(("Give Walkie-Talkie", () => Do(Cheat.GiveWalkie, "Walkie given (spare if you have one)")),
            ("Reset save", ResetSave));
        Row(("Give both flashlights", () => Do(Cheat.GiveFlashlights, "Basic + paid flashlight given")),
            ("Give 3 batteries", () => Do(Cheat.GiveBatteries, "Batteries filled to 3")));

        Header("Player");
        Row((god ? "God mode: ON" : "God mode: OFF", () => { if (Do(god ? Cheat.GodOff : Cheat.GodOn, god ? "God mode off" : "God mode on")) god = !god; }),
            ("Heal to full", () => Do(Cheat.HealFull, "Healed")));
        Row(("Go down", () => Do(Cheat.GoDown, "Downed")),
            ("Die", () => Do(Cheat.Die, "Dead")));
        Row((noclip ? "Noclip / fly: ON" : "Noclip / fly: OFF", ToggleNoclip), ("", null));
        Row(("Sanity full", () => Do(Cheat.SanityFull, "Sanity 100")),
            ("Sanity -25", () => Do(Cheat.SanityMinus25, "Sanity -25")));
        Row(("Sanity 0", () => Do(Cheat.SanityZero, "Sanity 0 (jumpscare soon)")),
            ("Give snacks + Holy Book", () => Do(Cheat.GiveSanityItems, "Snacks + Holy Book given")));
        if (noclip) GUILayout.Label("  WASD move, Space up, Ctrl down, Shift fast");

        Header("Match");
        Row(("Clock +1 hour", () => Do(Cheat.ClockPlusHour, "Clock +1 hour")),
            ("Complete all boards", () => Do(Cheat.CompleteBoards, "All zones done")));
        Row(("Open the exit now", () => Do(Cheat.OpenExit, "Zones done + 06:00")),
            ("Lights on / off", () => Do(Cheat.LightsToggle, "Lights toggled")));
        DarknessSliders();

        Header("Ghost");
        Row((frozen ? "Ghosts frozen: ON" : "Ghosts frozen: OFF", () => { if (Do(frozen ? Cheat.GhostsUnfreeze : Cheat.GhostsFreeze, frozen ? "Ghosts move" : "Ghosts frozen")) frozen = !frozen; }),
            ("Teleport to ghost", TeleportToGhost));
        Row((ghostMarkers ? "Ghost markers: ON" : "Ghost markers: OFF", () => ghostMarkers = !ghostMarkers), ("", null));

        GUILayout.EndScrollView();
        GUILayout.Label(lastAction);
        GUILayout.EndArea();
        GUI.matrix = Matrix4x4.identity;
    }

    private static void Header(string text) => GUILayout.Label("\n<b>" + text + "</b>");

    // 5 Oct: tune the darkness live (this PC only, visuals only). Copy the numbers
    // you like into the map's DarknessController (Tools > Pre-Thesis > Darkness).
    private DarknessController darkness;

    private void DarknessSliders()
    {
        if (darkness == null) darkness = FindAnyObjectByType<DarknessController>();
        if (darkness == null) return;
        Header("Darkness  (this PC only, try values)");
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

    private bool Do(Cheat cheat, string message)
    {
        if (!ServerCheatsAllowed) { Done("Host is a NORMAL build: server cheats are off."); return false; }
        Send(cheat);
        Done(message);
        return true;
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
