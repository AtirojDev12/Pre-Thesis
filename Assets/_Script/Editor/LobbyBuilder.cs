using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Pre-Thesis > Build 3D Lobby (greybox + shop + practice)   (1 Oct 2026)
///
/// Builds a walkable GREYBOX lobby inside Assets/Scenes/Lobby.unity, under one
/// root object "3D Lobby (greybox)". Running it again replaces only that root;
/// the lobby UI ("Waiting Lobby UI") and anything the team adds elsewhere stay.
///
///   - Room: floor, walls, lights, 6 spawn points (NetworkStartPosition).
///   - Shop counter (ShopTerminal): look at it, press E.
///   - Practice area for the Cinema map, with sign boards:
///       popcorn & drinks (the real PopcornMinigameBootstrap + PopcornNetSync, 2 Oct loop:
///       PopCornTank + refill machine, water / Fanta / orange juice / Pepsi),
///       ticket booth (the real TicketMinigame + TicketNetSync).
///     Practice sales give NO currency (no MatchDirector here) and wrong
///     ghost orders do NO damage.
///   - Offline test spawner, so Play in Lobby.unity alone also gives a body.
///
/// Also: the Walkie-Talkie is no longer free (Player prefab startingItems
/// emptied); players buy it in the shop.
///
/// The team can replace the greybox look later; keep the named objects the
/// scripts point to (or re-run this tool and move things afterwards).
/// </summary>
public static class LobbyBuilder
{
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string RootName = "3D Lobby (greybox)";
    private const string MaterialFolder = "Assets/Materials/Lobby";

    // Popcorn container / held prefabs used by Cinema_GamePlay (by GUID, so moves/renames do not matter).
    private const string EmptyBucketGuid = "76940601842c7b44a8e0ea1b509c1a51";
    private const string EmptyCupGuid = "822cb28a5fda7ac42b5a7065f7e9b96d";
    private const string FilledCupGuid = "31145c7e4b207f34f95cc5b3cea8b10c";
    private const string GhostCupGuid = "c4940806debdabc44b707b50041597f6";
    private const string HeldPopcornGuid = "66596ff46762db5498bc9d4d61a7ddf6";
    // New popcorn loop (2 Oct update): refill batch, 3 more drinks, the real popcorn machine model.
    private const string NewPopcornGuid = "d4b433559df88234aaf69a3160c6aa95";
    private const string FantaCupGuid = "1ef909a72955f4148a3ee4b8a508b51c";
    private const string OrangeJuiceCupGuid = "1b297f1e67ad1cb448cc2b9869bda4dd";
    private const string PepsiCupGuid = "989a0cee5a3f7234fbd7e8c6a50d0ebd";
    private const string PopcornMakerModelGuid = "ef9d96125919a104996c7c7b76dab322"; // Prefab/PrefabModel/Popcorn_Maker
    private const string PlayerPrefabGuid = "1dd82c6745240c446a2b82004c20a793";

    private static Transform root;

    [MenuItem("Tools/Pre-Thesis/Build 3D Lobby (greybox + shop + practice)")]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build 3D Lobby", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyScenePath) == null)
        {
            EditorUtility.DisplayDialog("Build 3D Lobby",
                "Assets/Scenes/Lobby.unity is missing. Run Tools > Pre-Thesis > Build Main Menu + Lobby first.", "OK");
            return;
        }

        string walkieNote = MakeWalkieNotFree();

        Scene scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
        foreach (GameObject candidate in scene.GetRootGameObjects())
            if (candidate.name == RootName) Object.DestroyImmediate(candidate);

        root = new GameObject(RootName).transform;

        BuildRoom();
        BuildSpawns();
        BuildShop();
        BuildPopcornPractice();
        BuildTicketPractice();
        BuildSigns();
        BuildLobbyBoard();
        PlaceSceneCamera(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        string message =
            "3D lobby built in Lobby.unity (root: \"" + RootName + "\").\n\n" +
            "- Room, lights, 6 spawn points\n" +
            "- Shop counter (E)\n" +
            "- Practice: popcorn & drinks (tank + refill machine) + ticket booth, with signs (no currency, no damage)\n" +
            "- " + walkieNote + "\n\n" +
            "Players walk around in the lobby; M or the Lobby Board (E) = Ready / Start; host holds E on a name to kick.";
        Debug.Log("[LobbyBuilder] " + message);
        EditorUtility.DisplayDialog("3D Lobby built", message, "OK");
    }

    // =====================================================================
    //  Room
    // =====================================================================

    private static void BuildRoom()
    {
        Transform room = Group("Room");
        Material floor = Mat("Lobby_Floor", new Color(0.32f, 0.3f, 0.29f));
        Material wall = Mat("Lobby_Wall", new Color(0.42f, 0.36f, 0.31f));
        Material trim = Mat("Lobby_Trim", new Color(0.25f, 0.12f, 0.1f));

        Box(room, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(30f, 0.2f, 22f), floor);
        Box(room, "Wall North", new Vector3(0f, 2.25f, 11.1f), new Vector3(30f, 4.5f, 0.2f), wall);
        Box(room, "Wall South", new Vector3(0f, 2.25f, -11.1f), new Vector3(30f, 4.5f, 0.2f), wall);
        Box(room, "Wall West", new Vector3(-15.1f, 2.25f, 0f), new Vector3(0.2f, 4.5f, 22f), wall);
        Box(room, "Wall East", new Vector3(15.1f, 2.25f, 0f), new Vector3(0.2f, 4.5f, 22f), wall);
        Box(room, "Ceiling", new Vector3(0f, 4.6f, 0f), new Vector3(30f, 0.2f, 22f), wall);
        Box(room, "Carpet", new Vector3(0f, 0.01f, -7f), new Vector3(8f, 0.02f, 4f), trim, collider: false);

        Transform lights = Group("Lights", room);
        var sun = new GameObject("Soft Fill (directional)");
        sun.transform.SetParent(lights, false);
        sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        Light s = sun.AddComponent<Light>();
        s.type = LightType.Directional;
        s.intensity = 0.35f;
        s.color = new Color(1f, 0.92f, 0.82f);

        Vector3[] spots = { new Vector3(-9f, 4f, 2f), new Vector3(9f, 4f, 2f), new Vector3(0f, 4f, -6f), new Vector3(-9f, 4f, -7f), new Vector3(0f, 4f, 6f) };
        for (int i = 0; i < spots.Length; i++)
        {
            var go = new GameObject("Ceiling Light " + (i + 1));
            go.transform.SetParent(lights, false);
            go.transform.position = spots[i];
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 14f;
            l.intensity = 2.2f;
            l.color = new Color(1f, 0.87f, 0.7f);
        }
    }

    private static void BuildSpawns()
    {
        Transform spawns = Group("Spawn Points");
        for (int i = 0; i < 6; i++)
        {
            var go = new GameObject("Lobby Spawn " + (i + 1));
            go.transform.SetParent(spawns, false);
            go.transform.position = new Vector3(-3.75f + i * 1.5f, 0.05f, -8f);
            go.transform.rotation = Quaternion.identity; // facing north, into the room
            go.AddComponent<NetworkStartPosition>();
        }

        // Offline test: Play inside Lobby.unity with no host still gives a body.
        var spawner = new GameObject("Offline Test Spawner");
        spawner.transform.SetParent(root, false);
        OfflinePlayerSpawner offline = spawner.AddComponent<OfflinePlayerSpawner>();
        var so = new SerializedObject(offline);
        SetObject(so, "playerPrefab", LoadByGuid<GameObject>(PlayerPrefabGuid));
        SetObject(so, "spawnPoint", spawns.GetChild(0));
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // =====================================================================
    //  Shop
    // =====================================================================

    private static void BuildShop()
    {
        Transform shop = Group("Shop");
        Material wood = Mat("Lobby_Counter", new Color(0.36f, 0.22f, 0.13f));
        Material gold = Mat("Lobby_Shop", new Color(0.85f, 0.65f, 0.2f));

        Box(shop, "Shop Counter", new Vector3(-10f, 0.55f, -6.5f), new Vector3(3.5f, 1.1f, 0.8f), wood);
        Box(shop, "Shop Shelf", new Vector3(-10f, 1.5f, -8.9f), new Vector3(4f, 3f, 0.6f), wood);

        GameObject register = Box(shop, "Shop Register (press E)", new Vector3(-10f, 1.3f, -6.5f), new Vector3(0.9f, 0.4f, 0.6f), gold);
        register.AddComponent<ShopTerminal>();

        // Just above the register, at eye level (read from the north, facing the counter).
        Sign(shop, "Shop Sign", new Vector3(-10f, 2.05f, -6.15f), Quaternion.Euler(0f, 180f, 0f), 1500, 520,
            "<size=130%><color=#FFCC4D>SHOP</color></size>\nLook at the register: <color=#FFCC4D>E</color>", 120f, false);
    }

    // =====================================================================
    //  Popcorn & water practice (the real minigame, on greybox props)
    // =====================================================================

    private static void BuildPopcornPractice()
    {
        Transform area = Group("Practice - Popcorn & Drinks");
        Material wood = Mat("Lobby_Counter", new Color(0.36f, 0.22f, 0.13f));

        // Counter along X at z = 1.2 (x -14 .. -5.5). Players stand south of it (z < 0.6),
        // customers come to the north side. Same loop as Cinema_GamePlay (Atiroj, 2 Oct):
        // bucket -> hold E PopCornTank -> E flavor;  cup -> hold E drink;  Ghost Favor;  serve.
        GameObject counter = Box(area, "Popcorn Counter", new Vector3(-9.75f, 0.5f, 1.2f), new Vector3(8.5f, 1f, 0.8f), wood);

        Transform bucket = Station(area, "Bucket Stack (E)", -13.6f, Mat("Lobby_Bucket", new Color(0.85f, 0.15f, 0.15f)));
        Transform cup = Station(area, "Cup Stack (E)", -13.0f, Mat("Lobby_Cup", new Color(0.92f, 0.92f, 0.9f)));
        Transform tank = Box(area, "PopCornTank (hold E)", new Vector3(-12.3f, 1.35f, 1.15f), new Vector3(0.6f, 0.7f, 0.5f),
            Mat("Lobby_Tank", new Color(0.95f, 0.85f, 0.55f))).transform;
        Transform cheese = Station(area, "Cheese (E)", -11.55f, Mat("Lobby_Cheese", new Color(0.98f, 0.82f, 0.2f)));
        Transform bbq = Station(area, "BBQ (E)", -10.95f, Mat("Lobby_BBQ", new Color(0.45f, 0.12f, 0.05f)));
        Transform paprika = Station(area, "Paprika (E)", -10.35f, Mat("Lobby_Paprika", new Color(0.95f, 0.42f, 0.1f)));
        Transform ghost = Station(area, "Ghost Favor (E)", -9.65f, Mat("Lobby_GhostFavor", new Color(0.55f, 0.3f, 0.85f)));

        // One dispenser per drink, same colours as the Cinema machine (blue / green / orange / red).
        Transform water = Station(area, "Water (hold E)", -8.9f, Mat("Lobby_Water", new Color(0.25f, 0.5f, 0.95f)));
        Transform fanta = Station(area, "Fanta (hold E)", -8.35f, Mat("Lobby_Fanta", new Color(0.2f, 0.75f, 0.25f)));
        Transform orange = Station(area, "Orange Juice (hold E)", -7.8f, Mat("Lobby_Orange", new Color(1f, 0.55f, 0.05f)));
        Transform pepsi = Station(area, "Pepsi (hold E)", -7.25f, Mat("Lobby_Pepsi", new Color(0.85f, 0.1f, 0.1f)));

        Transform cashier = Box(area, "Cashier", new Vector3(-6.2f, 1.2f, 1.2f), new Vector3(0.7f, 0.4f, 0.5f), Mat("Lobby_Shop", new Color(0.85f, 0.65f, 0.2f))).transform;

        // Refill: the REAL popcorn machine on a back table (turn around from the counter).
        Box(area, "Refill Table", new Vector3(-12.3f, 0.45f, -2.2f), new Vector3(1.6f, 0.9f, 0.9f), wood);
        Transform refillMaker = PlaceMakerModel(area, new Vector3(-12.3f, 0.9f, -2.2f));
        Sign(area, "Refill Sign", new Vector3(-10.6f, 1.55f, -2.2f), Quaternion.Euler(0f, 180f, 0f), 1500, 820,
            "<size=120%><color=#FFCC4D>TANK EMPTY?</color></size>\n" +
            "Hold <color=#FFCC4D>E</color> machine (3 s)\n" +
            "<color=#FFCC4D>E</color> PopCornTank = +10", 100f, true);

        // UI anchors: forward = away from the player (canvas read from the south). Eye level.
        // Cashier screen 0.6x0.35 m; instruction screen 0.62x0.85 m above the flavours.
        Transform cashierUi = Point(area, "Cashier UI Anchor", new Vector3(-6.2f, 1.75f, 1.6f), Quaternion.identity);
        Transform makerUi = Point(area, "Instructions UI Anchor", new Vector3(-10.95f, 1.95f, 1.5f), Quaternion.identity);
        Transform maker = counter.transform; // the instruction screen hangs off the counter

        // Customer route (north side of the counter), facing the player at the counter.
        Transform spawn = Point(area, "Customer Spawn", new Vector3(-3.5f, 1f, 6f), Quaternion.Euler(0f, 180f, 0f));
        Transform approach = Point(area, "Customer Approach 1", new Vector3(-4.5f, 1f, 3.2f), Quaternion.identity);
        Transform wait = Point(area, "Customer Wait (counter)", new Vector3(-6.2f, 1f, 2.2f), Quaternion.Euler(0f, 180f, 0f));
        Transform depart = Point(area, "Customer Departure 1", new Vector3(-8f, 1f, 4.5f), Quaternion.identity);
        Transform exit = Point(area, "Customer Exit", new Vector3(-12.5f, 1f, 6.5f), Quaternion.identity);

        // The minigame itself.
        var systems = new GameObject("Popcorn Minigame Systems (practice)");
        systems.transform.SetParent(area, false);
        PopcornMinigameBootstrap bootstrap = systems.AddComponent<PopcornMinigameBootstrap>();
        var so = new SerializedObject(bootstrap);
        SetObject(so, "cashierObject", cashier);
        SetObject(so, "popcornMakerObject", maker);
        SetObject(so, "bucketSpawner", bucket);
        SetObject(so, "cupSpawner", cup);
        SetObject(so, "cheeseStation", cheese);
        SetObject(so, "bbqStation", bbq);
        SetObject(so, "paprikaStation", paprika);
        SetObject(so, "ghostStation", ghost);
        SetArray(so, "waterDispensers", water);
        SetArray(so, "fantaDispensers", fanta);
        SetArray(so, "orangeJuiceDispensers", orange);
        SetArray(so, "pepsiDispensers", pepsi);
        SetObject(so, "popcornTank", tank);
        SetObject(so, "refillMaker", refillMaker);
        SetObject(so, "newPopcornPrefab", LoadByGuid<GameObject>(NewPopcornGuid));
        SetObject(so, "fantaCupPrefab", LoadByGuid<GameObject>(FantaCupGuid));
        SetObject(so, "orangeJuiceCupPrefab", LoadByGuid<GameObject>(OrangeJuiceCupGuid));
        SetObject(so, "pepsiCupPrefab", LoadByGuid<GameObject>(PepsiCupGuid));
        SetInt(so, "tankCapacity", 20);
        SetInt(so, "refillServings", 10);
        SetArray(so, "ghostFavorRelocationPoints");
        SetObject(so, "emptyBucketPrefab", LoadByGuid<GameObject>(EmptyBucketGuid));
        SetObject(so, "emptyCupPrefab", LoadByGuid<GameObject>(EmptyCupGuid));
        SetObject(so, "filledCupPrefab", LoadByGuid<GameObject>(FilledCupGuid));
        SetObject(so, "ghostCupPrefab", LoadByGuid<GameObject>(GhostCupGuid));
        SetObject(so, "heldPopcornPrefab", LoadByGuid<GameObject>(HeldPopcornGuid));
        SetObject(so, "cashierUiAnchor", cashierUi);
        SetObject(so, "popcornMakerUiAnchor", makerUi);
        SetObject(so, "customerSpawnPoint", spawn);
        SetObject(so, "customerWaitPoint", wait);
        SetObject(so, "customerExitPoint", exit);
        SetArray(so, "customerApproachPath", approach);
        SetArray(so, "customerDeparturePath", depart);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Network sync on its OWN object (Mirror disables scene identities offline).
        var syncGo = new GameObject("Popcorn Practice Sync");
        syncGo.transform.SetParent(area, false);
        syncGo.AddComponent<NetworkIdentity>();
        PopcornNetSync sync = syncGo.AddComponent<PopcornNetSync>();
        var syncSo = new SerializedObject(sync);
        SetString(syncSo, "zoneID", "practice_popcorn");
        SetInt(syncSo, "ordersToComplete", 9999);
        SetFloat(syncSo, "wrongGhostOrderDamage", 0f); // practice: no damage
        syncSo.ApplyModifiedPropertiesWithoutUndo();
    }

    // =====================================================================
    //  Ticket practice (the real ticket minigame)
    // =====================================================================

    private static void BuildTicketPractice()
    {
        Transform area = Group("Practice - Tickets");
        Material wood = Mat("Lobby_Counter", new Color(0.36f, 0.22f, 0.13f));

        Box(area, "Ticket Counter", new Vector3(9f, 0.5f, 1.2f), new Vector3(5f, 1f, 0.8f), wood);

        var gameGo = new GameObject("Ticket Minigame (practice)");
        gameGo.transform.SetParent(area, false);
        TicketMinigame game = gameGo.AddComponent<TicketMinigame>();

        GameObject human = Box(area, "Human Ticket Button (E)", new Vector3(8.3f, 1.1f, 0.95f), new Vector3(0.35f, 0.18f, 0.35f),
            Mat("Lobby_HumanButton", new Color(0.3f, 0.85f, 0.4f)));
        GameObject ghost = Box(area, "Ghost Ticket Button (E)", new Vector3(9.7f, 1.1f, 0.95f), new Vector3(0.35f, 0.18f, 0.35f),
            Mat("Lobby_GhostButton", new Color(0.6f, 0.35f, 0.9f)));
        TicketSellButton humanButton = human.AddComponent<TicketSellButton>();
        TicketSellButton ghostButton = ghost.AddComponent<TicketSellButton>();
        WireButton(humanButton, game, false);
        WireButton(ghostButton, game, true);

        var syncGo = new GameObject("Ticket Practice Sync");
        syncGo.transform.SetParent(area, false);
        syncGo.AddComponent<NetworkIdentity>();
        TicketNetSync sync = syncGo.AddComponent<TicketNetSync>();
        var syncSo = new SerializedObject(sync);
        SetObject(syncSo, "minigame", game);
        syncSo.ApplyModifiedPropertiesWithoutUndo();

        Transform movieUi = Point(area, "Movie UI Anchor", new Vector3(11.1f, 1.55f, 1.0f), Quaternion.identity);
        Transform score = Point(area, "Ticket Score Anchor", new Vector3(7f, 1.9f, 1.55f), Quaternion.identity);
        Transform spawn = Point(area, "Ticket Customer Spawn", new Vector3(13.5f, 1f, 7f), Quaternion.Euler(0f, 180f, 0f));
        Transform counter = Point(area, "Ticket Customer Counter", new Vector3(9f, 1f, 2.3f), Quaternion.Euler(0f, 180f, 0f));
        Transform exit = Point(area, "Ticket Customer Exit", new Vector3(5f, 1f, 7f), Quaternion.identity);

        var so = new SerializedObject(game);
        SetObject(so, "humanButton", humanButton);
        SetObject(so, "ghostButton", ghostButton);
        SetObject(so, "networkSync", sync);
        SetObject(so, "movieUiAnchor", movieUi);
        SetObject(so, "spawnPoint", spawn);
        SetObject(so, "counterPoint", counter);
        SetObject(so, "exitPoint", exit);
        SetObject(so, "scoreAnchor", score);
        SetArray(so, "approachPath");
        SetArray(so, "departurePath");
        SetFloat(so, "wrongGhostDamage", 0f); // practice: no damage
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireButton(TicketSellButton button, TicketMinigame game, bool ghostTicket)
    {
        var so = new SerializedObject(button);
        SetObject(so, "minigame", game);
        so.FindProperty("ghostTicket").boolValue = ghostTicket;
        SetObject(so, "highlightRenderer", button.GetComponent<Renderer>());
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // =====================================================================
    //  Sign boards
    // =====================================================================

    // Signs (2 Oct): eye level (centre ~1.5-1.6 m), BIG bold text, few words,
    // solid dark board. Small text turns to mush through the retro screen filter.
    private static void BuildSigns()
    {
        Transform signs = Group("Signs");

        // Middle of the room, first thing you see from the spawn (read looking north).
        Sign(signs, "Welcome Sign", new Vector3(0f, 1.55f, -3f), Quaternion.identity, 1900, 1100,
            "<size=125%><color=#FFCC4D>13RoH LOBBY</color></size>\n" +
            "<color=#FFCC4D>M</color>  Ready / Start / Leave\n" +   // = WaitingLobbyController.panelKey
            "<color=#FFCC4D>E</color>  Use / Shop\n" +
            "<color=#FFCC4D>TAB</color>  Free / lock mouse\n" +
            "Board (right): Ready / Start\n" +
            "Shop: left.  Practice: ahead", 110f, true);

        // Standing boards at the walkway end of each counter (read looking north).
        Sign(signs, "Popcorn How-To", new Vector3(-4.6f, 1.55f, 0.4f), Quaternion.identity, 1800, 1250,
            "<size=120%><color=#FFCC4D>POPCORN & DRINKS</color></size>\n" +
            "1  Read the order (cashier)\n" +
            "2  <color=#FFCC4D>E</color> bucket or cup\n" +
            "3  Bucket: hold <color=#FFCC4D>E</color> tank, <color=#FFCC4D>E</color> flavor\n" +
            "    Cup: hold <color=#FFCC4D>E</color> drink\n" +
            "4  Ghost? <color=#FFCC4D>E</color> Ghost Favor\n" +
            "5  <color=#FFCC4D>E</color> on customer = serve\n" +
            "<color=#FFCC4D>R</color> throw away", 100f, true);

        Sign(signs, "Ticket How-To", new Vector3(4.6f, 1.55f, 0.4f), Quaternion.identity, 1800, 1150,
            "<size=120%><color=#FFCC4D>TICKETS</color></size>\n" +
            "1  Customer: HUMAN or GHOST?\n" +
            "2  Pick the movie (screen)\n" +
            "3  <color=#FFCC4D>E</color> <color=#5BE07A>GREEN</color> = human\n" +
            "    <color=#FFCC4D>E</color> <color=#B07BFF>PURPLE</color> = ghost\n" +
            "Wrong ghost ticket = hurt", 100f, true);

        // On the east wall (read looking east).
        Sign(signs, "Match Rules", new Vector3(14.9f, 1.6f, -5.5f), Quaternion.Euler(0f, 90f, 0f), 2400, 1400,
            "<size=120%><color=#FFCC4D>HOW A NIGHT WORKS</color></size>\n" +
            "Clock 00:00 to 06:00\n" +
            "Finish every task board\n" +
            "Exit glows: all done + 06:00\n" +
            "06-07 ghosts hunt. 07:00 = death\n" +
            "Dark: ghosts hear moves + loud voices\n" +
            "Pay: tasks x10.  Die: 10, lose items", 105f, false);
    }

    // =====================================================================
    //  Lobby board (2 Oct): the M menu as a board you walk up to
    // =====================================================================

    private static void BuildLobbyBoard()
    {
        Transform group = Group("Lobby Board");
        // Right of the spawn row, facing west (read while looking east from the spawns).
        Vector3 position = new Vector3(6.5f, 1.55f, -7.5f);
        Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);
        const float width = 2.2f, height = 1.65f; // = LobbyBoard canvas (1000 px = 1 m)

        var board = new GameObject("Lobby Board (E: Ready / Start, host hold E: kick)");
        board.transform.SetParent(group, false);
        board.transform.SetPositionAndRotation(position, rotation);
        board.AddComponent<LobbyBoard>(); // builds its display, rows and button at runtime

        Material dark = Mat("Lobby_SignBack", new Color(0.08f, 0.07f, 0.07f));
        GameObject back = Box(group, "Lobby Board Back", position + rotation * new Vector3(0f, 0f, 0.04f),
            new Vector3(width + 0.08f, height + 0.08f, 0.04f), dark);
        back.transform.rotation = rotation;
        float bottom = position.y - height * 0.5f;
        GameObject post = Box(group, "Lobby Board Post", new Vector3(position.x, bottom * 0.5f, position.z) + rotation * new Vector3(0f, 0f, 0.04f),
            new Vector3(0.12f, bottom, 0.12f), dark);
        post.transform.rotation = rotation;
    }

    // =====================================================================
    //  Walkie no longer free
    // =====================================================================

    private static string MakeWalkieNotFree()
    {
        string path = AssetDatabase.GUIDToAssetPath(PlayerPrefabGuid);
        if (string.IsNullOrEmpty(path)) return "Player prefab not found: starting items NOT changed.";

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            PlayerInventory inventory = contents.GetComponent<PlayerInventory>();
            if (inventory == null) return "Player prefab has no PlayerInventory: starting items NOT changed.";

            var so = new SerializedObject(inventory);
            SerializedProperty items = so.FindProperty("startingItems");
            if (items == null || items.arraySize == 0) return "Walkie-Talkie: already not free (bought in the shop).";
            items.ClearArray();
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            return "Walkie-Talkie is no longer free (Player prefab starting items emptied); buy it in the shop.";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static void PlaceSceneCamera(Scene scene)
    {
        foreach (GameObject candidate in scene.GetRootGameObjects())
        {
            Camera cam = candidate.GetComponentInChildren<Camera>(true);
            if (cam == null || candidate.name == RootName) continue;
            // Seen behind the lobby panel until the player's own camera takes over.
            cam.transform.SetPositionAndRotation(new Vector3(0f, 3f, -10f), Quaternion.Euler(12f, 0f, 0f));
            cam.depth = -100f; // always under the player's own camera
            return;
        }
    }

    private static Transform Group(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        return go.transform;
    }

    private static Transform Point(Transform parent, string name, Vector3 position, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        return go.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool collider = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>
    /// The real Popcorn_Maker model (as in Cinema_GamePlay), standing on <paramref name="tableTop"/>.
    /// It gets a solid BoxCollider so the interaction ray hits it. Greybox box if the model is missing.
    /// </summary>
    private static Transform PlaceMakerModel(Transform parent, Vector3 tableTop)
    {
        var prefab = LoadByGuid<GameObject>(PopcornMakerModelGuid);
        GameObject go = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : null;
        if (go == null)
            return Box(parent, "Popcorn Machine (hold E)", tableTop + new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.9f, 0.6f),
                Mat("Lobby_Machine", new Color(0.55f, 0.1f, 0.08f))).transform;

        go.name = "Popcorn Machine (hold E)";
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(tableTop, Quaternion.identity);
        go.transform.localScale = Vector3.one * 1.0795f; // same size as in the Cinema map

        // Sit it on the table and centre it, whatever the model's pivot is.
        Bounds world = RendererBounds(go, tableTop);
        go.transform.position += new Vector3(tableTop.x - world.center.x, tableTop.y - world.min.y, tableTop.z - world.center.z);
        world = RendererBounds(go, tableTop);

        bool hasSolid = false;
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger) hasSolid = true;
        if (!hasSolid)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            Vector3 scale = go.transform.lossyScale;
            box.center = go.transform.InverseTransformPoint(world.center);
            box.size = new Vector3(world.size.x / Mathf.Max(0.0001f, scale.x), world.size.y / Mathf.Max(0.0001f, scale.y),
                world.size.z / Mathf.Max(0.0001f, scale.z));
        }
        return go.transform;
    }

    private static Bounds RendererBounds(GameObject go, Vector3 fallback)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(fallback, Vector3.one * 0.6f);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>A station box on the popcorn counter top (non-trigger collider, as the minigame needs).</summary>
    private static Transform Station(Transform parent, string name, float x, Material material) =>
        Box(parent, name, new Vector3(x, 1.2f, 1.05f), new Vector3(0.45f, 0.4f, 0.45f), material).transform;

    /// <summary>
    /// World-space sign. Canvas read from the side its forward points away from (like the minigame screens).
    /// Text auto-sizes down to fit, never below 60 px (6 cm letters). <paramref name="stand"/> adds a post to the floor.
    /// </summary>
    private static void Sign(Transform parent, string name, Vector3 position, Quaternion rotation, float widthPx, float heightPx,
        string text, float fontPx, bool stand)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * 0.001f; // 1000 px = 1 m
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = new Vector2(widthPx, heightPx);

        var bg = new GameObject("Board", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        bg.transform.SetParent(go.transform, false);
        Stretch((RectTransform)bg.transform, 0f);
        var image = bg.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.03f, 0.025f, 0.025f, 1f);
        image.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)textGo.transform, 45f);
        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontStyle = FontStyles.Bold;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = fontPx;
        tmp.fontSizeMin = 60f;
        tmp.fontSize = fontPx;
        tmp.characterSpacing = 4f;
        tmp.color = new Color(1f, 0.97f, 0.9f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;

        // Solid back (so it is not see-through from behind) and, if standing, a post to the floor.
        Material dark = Mat("Lobby_SignBack", new Color(0.08f, 0.07f, 0.07f));
        float w = widthPx * 0.001f, h = heightPx * 0.001f;
        GameObject back = Box(parent, name + " Back", position + rotation * new Vector3(0f, 0f, 0.03f), new Vector3(w + 0.06f, h + 0.06f, 0.04f), dark);
        back.transform.rotation = rotation;
        if (stand)
        {
            float bottom = position.y - h * 0.5f;
            GameObject post = Box(parent, name + " Post", new Vector3(position.x, bottom * 0.5f, position.z) + rotation * new Vector3(0f, 0f, 0.03f),
                new Vector3(0.1f, bottom, 0.1f), dark);
            post.transform.rotation = rotation;
        }
    }

    private static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    private static Material Mat(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Lobby");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static T LoadByGuid<T>(string guid) where T : Object
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        T asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) Debug.LogWarning($"[LobbyBuilder] Asset {guid} not found — assign it by hand on the practice objects.");
        return asset;
    }

    private static void SetObject(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[LobbyBuilder] {so.targetObject.GetType().Name} has no field '{field}'."); return; }
        p.objectReferenceValue = value;
    }

    private static void SetArray(SerializedObject so, string field, params Object[] values)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[LobbyBuilder] {so.targetObject.GetType().Name} has no field '{field}'."); return; }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void SetString(SerializedObject so, string field, string value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.stringValue = value; else Debug.LogWarning($"[LobbyBuilder] missing field '{field}'.");
    }

    private static void SetInt(SerializedObject so, string field, int value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.intValue = value; else Debug.LogWarning($"[LobbyBuilder] missing field '{field}'.");
    }

    private static void SetFloat(SerializedObject so, string field, float value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.floatValue = value; else Debug.LogWarning($"[LobbyBuilder] missing field '{field}'.");
    }
}
