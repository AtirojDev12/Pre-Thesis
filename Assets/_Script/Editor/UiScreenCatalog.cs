/// <summary>
/// 8 Nov 2026 (Mr.k): every UI screen of the game, for the UI Hub on GAME HUD.
///   screen   = object name of the screen's root Canvas.
///   category = the heading it is listed under in the hub (16 Nov).
///   prefab   = a real prefab of that screen (edit it directly).
///   part     = (16 Nov) the screen is this child of the prefab (a part of the GAME HUD).
///   scene    = the scene that holds the screen (edit it directly).
///   related  = models / prefabs / settings the screen shows or uses (the designer can open them).
///   Screens with no prefab and no scene are built by code: the UI Hub makes an editable
///   prefab for them (Assets/UI Skins) the first time they appear in Play mode.
/// New screens still show up by themselves in Play mode (under "Other").
///
/// 16 Nov: the hotbar and the status bars (noise, flashlight, throw, task progress) are no
/// longer code-built screens. They are real children of the GAME HUD prefab.
/// </summary>
public static class UiScreenCatalog
{
    public const string Hud = "HUD", Menus = "Menus", Lobby = "Lobby", MatchPanels = "Match panels", Popcorn = "Popcorn",
        Ticket = "Ticket", GhostsAndRules = "Ghosts and Rules", WorldBoards = "World boards", Models = "3D models", Other = "Other";

    /// <summary>The headings of the hub, in order.</summary>
    public static readonly string[] Categories = { Hud, Menus, Lobby, MatchPanels, Popcorn, Ticket, GhostsAndRules, WorldBoards, Models, Other };

    public const string GameHudPrefab = "Assets/Prefab/GAME HUD.prefab";
    public const string HotbarScreen = "GAME HUD > Hotbar";
    public const string BarsScreen = "GAME HUD > Status Bars";

    public sealed class Entry
    {
        public string screen, label, how, script, prefab, scene, category = Other, part;
        public string[] related = new string[0];
        public bool CodeBuilt => string.IsNullOrEmpty(prefab) && string.IsNullOrEmpty(scene);
        public bool IsPart => !string.IsNullOrEmpty(part);
    }

    private static Entry E(string category, string screen, string label, string how, string script, params string[] related) =>
        new Entry { category = category, screen = screen, label = label, how = how, script = script, related = related };

    public static readonly Entry[] All =
    {
        // ---- HUD
        new Entry { category = Hud, screen = "GAME HUD", label = "Game HUD (health, stamina, sanity, crosshair)", how = "Lobby and match.", prefab = GameHudPrefab, script = "Assets/_Script/UI/PersistentHUD.cs" },
        new Entry { category = Hud, screen = BarsScreen, label = "Status bars (frame + fill)", how = "Match: noise, flashlight, throw, task progress, walkie battery.", prefab = GameHudPrefab, part = "HUD Bars", script = "Assets/_Script/UI/Hub/HudBar.cs" },
        new Entry { category = Hud, screen = HotbarScreen, label = "Hotbar (slots + item icons)", how = "Lobby and match.", prefab = GameHudPrefab, part = "Hotbar", script = "Assets/_Script/UI/Hub/HotbarView.cs", related = new[] { "Assets/Resources/Items" } },
        E(Hud, "Match HUD (clock)", "Match clock", "Match, top of the screen.", "Assets/_Script/UI/MatchHUD.cs"),

        // ---- Menus
        new Entry { category = Menus, screen = "Main Menu UI", label = "Main menu + Settings", how = "Main menu scene.", scene = "Assets/Scenes/MainMenu.unity", script = "Assets/_Script/Editor/MainMenuBuilder.cs" },
        new Entry { category = Menus, screen = "PauseMenu", label = "Esc menu", how = "Press Esc in the lobby or the match.", prefab = "Assets/Resources/UI/PauseMenu.prefab", script = "Assets/_Script/UI/Menu/PauseMenuController.cs" },
        E(Menus, "Leaving Cover", "Leaving-match black screen", "Leave a match from the Esc menu.", "Assets/_Script/UI/Menu/PauseMenuController.cs"),

        // ---- Lobby
        new Entry { category = Lobby, screen = "Waiting Lobby UI", label = "Waiting lobby", how = "Lobby scene.", scene = "Assets/Scenes/Lobby.unity", script = "Assets/_Script/Editor/MainMenuBuilder.cs" },
        E(Lobby, "Lobby Hint", "Waiting lobby hint strip", "Waiting lobby.", "Assets/_Script/UI/Menu/WaitingLobbyController.cs"),
        E(Lobby, "Lobby Shop", "Shop", "Lobby: use the shop counter.", "Assets/_Script/UI/ShopUI.cs", "Assets/Resources/Items"),
        E(Lobby, "Lobby Storage", "Storage", "Lobby: use the storage.", "Assets/_Script/UI/StorageUI.cs", "Assets/Resources/Items"),

        // ---- Match panels
        E(MatchPanels, "Player List HUD", "Player list / lobby menu", "Press Tab in the lobby or the match.", "Assets/_Script/UI/PlayerListHUD.cs"),
        E(MatchPanels, "Worksheet Book", "Work sheet memo book", "Match: take a work sheet from the Staff, then open it.", "Assets/_Script/UI/WorksheetReader.cs",
            "Assets/Resources/Items/WorldWorksheet.prefab", "Assets/Resources/MapGuides/demo_map_01.asset"),
        E(MatchPanels, "Match Results", "Results screen", "End of a round (F1 > Open the exit now, then escape or wait).", "Assets/_Script/UI/MatchResultsUI.cs"),
        E(MatchPanels, "Paper Reader", "Paper note reader", "Read a paper note (the pages are on the paper object in the scene).", "Assets/_Script/Interactive/ReadablePaper.cs"),

        // ---- Popcorn
        E(Popcorn, "Popcorn Focus Name", "Popcorn item name", "Ticket Zone: look at a popcorn item.", "Assets/_Script/Interactive/InteractionPromptUI.cs"),
        E(Popcorn, "Held Popcorn HUD", "Held popcorn info", "Ticket Zone: carry a popcorn order.", "Assets/_Script/PopcornMinigame/PopcornItemHoldingSystem.cs"),
        E(Popcorn, "Preparation HUD", "Popcorn preparation", "Ticket Zone: hold E at a popcorn station.", "Assets/_Script/PopcornMinigame/PopcornPreparation.cs"),
        E(Popcorn, "Tank Supply Display", "Popcorn tank supply (in the world)", "Ticket Zone: the popcorn tank.", "Assets/_Script/PopcornMinigame/PopcornPreparation.cs"),
        E(Popcorn, "Order Screen (World Space)", "Popcorn order screen (in the world)", "Ticket Zone: the cashier screen.", "Assets/_Script/PopcornMinigame/PopcornMinigameBootstrap.cs"),
        E(Popcorn, "Customer Submit Prompt", "Popcorn customer prompt", "Ticket Zone: give an order to a customer.", "Assets/_Script/PopcornMinigame/PopcornMinigameBootstrap.cs"),

        // ---- Ticket
        E(Ticket, "Movie Selection (World Space)", "Ticket movie selection (in the world)", "Ticket Zone: the ticket counter.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),
        E(Ticket, "Ticket Request", "Ticket customer request", "Ticket Zone: a customer asks for a ticket.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),
        E(Ticket, "Ticket Score", "Ticket score", "Ticket Zone: sell tickets.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),

        // ---- Ghosts and Rules
        E(GhostsAndRules, "Rule Toast", "Rule messages", "Break a zone rule (short red line).", "Assets/_Script/Rules/RuleFx.cs"),
        E(GhostsAndRules, "Sanity Screen", "Sanity screen effects", "Match: low sanity.", "Assets/_Script/Sanity/SanityEffects.cs", "Assets/Resources/Tuning/SanitySettings.asset"),
        E(GhostsAndRules, "Sanity Jumpscare", "Sanity jumpscare", "Match: very low sanity.", "Assets/_Script/Sanity/SanityEffects.cs", "Assets/Resources/Tuning/SanitySettings.asset"),
        E(GhostsAndRules, "Local Ghost Warning", "Ghost-near dark border", "Match: a ghost comes near.", "Assets/_Script/Player/GhostWarningEffect.cs"),
        E(GhostsAndRules, "Ghost Countdown", "Missing Parts Ghost countdown", "Match: the Missing Parts Ghost asks for its parts.", "Assets/_Script/Ghosts/MissingPartsGhost.cs",
            "Assets/Resources/Items/WorldGhostPart_Head.prefab", "Assets/Resources/Items/WorldGhostPart_LeftArm.prefab",
            "Assets/Resources/Items/WorldGhostPart_RightArm.prefab", "Assets/Resources/Items/WorldGhostPart_UpperBody.prefab"),
        E(GhostsAndRules, "Ghost Speech", "Missing Parts Ghost speech bubble", "Match: the Missing Parts Ghost talks.", "Assets/_Script/Ghosts/MissingPartsGhost.cs"),

        // ---- World boards
        E(WorldBoards, "Task Board Canvas", "Zone task boards (in the world)", "Match: the boards in each zone. One change = every board.", "Assets/_Script/Match/ZoneTaskList.cs"),
    };

    /// <summary>16 Nov: old code-built screens that are now GAME HUD parts. Their old prefabs and saved entries are removed.</summary>
    public static readonly string[] MovedIntoGameHud = { "Hotbar HUD", "Noise Meter HUD", "Chore Progress HUD", "Flashlight HUD", "Throw Charge HUD" };

    public static bool WasMovedIntoGameHud(string screen)
    {
        foreach (string s in MovedIntoGameHud) if (s == screen) return true;
        return false;
    }

    public static Entry Find(string screen)
    {
        foreach (Entry e in All) if (e.screen == screen) return e;
        return null;
    }
}
