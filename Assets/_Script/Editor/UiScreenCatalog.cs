/// <summary>
/// 8 Nov 2026 (Mr.k): every UI screen of the game, for the UI Hub on GAME HUD.
///   screen  = object name of the screen's root Canvas.
///   prefab  = a real prefab of that screen (edit it directly).
///   scene   = the scene that holds the screen (edit it directly).
///   related = models / prefabs / settings the screen shows or uses (the designer can open them).
///   Screens with no prefab and no scene are built by code: the UI Hub makes an editable
///   prefab for them (Assets/UI Skins) the first time they appear in Play mode.
/// New screens still show up by themselves in Play mode.
/// </summary>
public static class UiScreenCatalog
{
    public sealed class Entry
    {
        public string screen, label, how, script, prefab, scene;
        public string[] related = new string[0];
        public bool CodeBuilt => string.IsNullOrEmpty(prefab) && string.IsNullOrEmpty(scene);
    }

    private static Entry E(string screen, string label, string how, string script, params string[] related) =>
        new Entry { screen = screen, label = label, how = how, script = script, related = related };

    public static readonly Entry[] All =
    {
        new Entry { screen = "GAME HUD", label = "Game HUD (health, stamina, crosshair, [E] prompt, down screen)", how = "Lobby and match.", prefab = "Assets/Prefab/GAME HUD.prefab", script = "Assets/_Script/UI/PersistentHUD.cs" },
        new Entry { screen = "Main Menu UI", label = "Main menu + Settings", how = "Main menu scene.", scene = "Assets/Scenes/MainMenu.unity", script = "Assets/_Script/Editor/MainMenuBuilder.cs" },
        new Entry { screen = "Waiting Lobby UI", label = "Waiting lobby", how = "Lobby scene.", scene = "Assets/Scenes/Lobby.unity", script = "Assets/_Script/Editor/MainMenuBuilder.cs" },
        new Entry { screen = "PauseMenu", label = "Esc menu", how = "Press Esc in the lobby or the match.", prefab = "Assets/Resources/UI/PauseMenu.prefab", script = "Assets/_Script/UI/Menu/PauseMenuController.cs" },
        E("Hotbar HUD", "Hotbar (4 item slots)", "Lobby and match.", "Assets/_Script/Inventory/HotbarHUD.cs", "Assets/Resources/Items"),
        E("Match HUD (clock)", "Match clock", "Match, top of the screen.", "Assets/_Script/UI/MatchHUD.cs"),
        E("Noise Meter HUD", "Noise meter (MIC / GAME bars)", "Match.", "Assets/_Script/UI/NoiseMeterHUD.cs"),
        E("Player List HUD", "Player list / lobby menu", "Press Tab in the lobby or the match.", "Assets/_Script/UI/PlayerListHUD.cs"),
        E("Chore Progress HUD", "Task progress bar", "Match: hold E on a task (sweep, film change ...).", "Assets/_Script/UI/ChoreProgressHUD.cs"),
        E("Worksheet Book", "Work sheet memo book", "Match: take a work sheet from the Staff, then open it.", "Assets/_Script/UI/WorksheetReader.cs",
            "Assets/Resources/Items/WorldWorksheet.prefab", "Assets/Resources/MapGuides/demo_map_01.asset"),
        E("Match Results", "Results screen", "End of a round (F1 > Open the exit now, then escape or wait).", "Assets/_Script/UI/MatchResultsUI.cs"),
        E("Lobby Shop", "Shop", "Lobby: use the shop counter.", "Assets/_Script/UI/ShopUI.cs", "Assets/Resources/Items"),
        E("Lobby Storage", "Storage", "Lobby: use the storage.", "Assets/_Script/UI/StorageUI.cs", "Assets/Resources/Items"),
        E("Flashlight HUD", "Flashlight battery", "Hold a flashlight.", "Assets/_Script/Inventory/FlashlightHUD.cs",
            "Assets/Resources/Items/Flashlight.prefab", "Assets/Resources/Tuning/FlashlightTuning.asset"),
        E("Throw Charge HUD", "Throw charge bar", "Hold an item and charge a throw.", "Assets/_Script/Inventory/ThrowChargeHUD.cs"),
        E("Rule Toast", "Rule messages", "Break a zone rule (short red line).", "Assets/_Script/Rules/RuleFx.cs"),
        E("Sanity Screen", "Sanity screen effects", "Match: low sanity.", "Assets/_Script/Sanity/SanityEffects.cs", "Assets/Resources/Tuning/SanitySettings.asset"),
        E("Sanity Jumpscare", "Sanity jumpscare", "Match: very low sanity.", "Assets/_Script/Sanity/SanityEffects.cs", "Assets/Resources/Tuning/SanitySettings.asset"),
        E("Local Ghost Warning", "Ghost-near dark border", "Match: a ghost comes near.", "Assets/_Script/Player/GhostWarningEffect.cs"),
        E("Paper Reader", "Paper note reader", "Read a paper note (the pages are on the paper object in the scene).", "Assets/_Script/Interactive/ReadablePaper.cs"),
        E("Lobby Hint", "Waiting lobby hint strip", "Waiting lobby.", "Assets/_Script/UI/Menu/WaitingLobbyController.cs"),
        E("Leaving Cover", "Leaving-match black screen", "Leave a match from the Esc menu.", "Assets/_Script/UI/Menu/PauseMenuController.cs"),
        E("Task Board Canvas", "Zone task boards (in the world)", "Match: the boards in each zone. One change = every board.", "Assets/_Script/Match/ZoneTaskList.cs"),
        E("Popcorn Focus Name", "Popcorn item name", "Ticket Zone: look at a popcorn item.", "Assets/_Script/Interactive/InteractionPromptUI.cs"),
        E("Held Popcorn HUD", "Held popcorn info", "Ticket Zone: carry a popcorn order.", "Assets/_Script/PopcornMinigame/PopcornItemHoldingSystem.cs"),
        E("Preparation HUD", "Popcorn preparation", "Ticket Zone: hold E at a popcorn station.", "Assets/_Script/PopcornMinigame/PopcornPreparation.cs"),
        E("Tank Supply Display", "Popcorn tank supply (in the world)", "Ticket Zone: the popcorn tank.", "Assets/_Script/PopcornMinigame/PopcornPreparation.cs"),
        E("Order Screen (World Space)", "Popcorn order screen (in the world)", "Ticket Zone: the cashier screen.", "Assets/_Script/PopcornMinigame/PopcornMinigameBootstrap.cs"),
        E("Customer Submit Prompt", "Popcorn customer prompt", "Ticket Zone: give an order to a customer.", "Assets/_Script/PopcornMinigame/PopcornMinigameBootstrap.cs"),
        E("Movie Selection (World Space)", "Ticket movie selection (in the world)", "Ticket Zone: the ticket counter.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),
        E("Ticket Request", "Ticket customer request", "Ticket Zone: a customer asks for a ticket.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),
        E("Ticket Score", "Ticket score", "Ticket Zone: sell tickets.", "Assets/_Script/TicketMinigame/TicketMinigame.cs"),
        E("Ghost Countdown", "Missing Parts Ghost countdown", "Match: the Missing Parts Ghost asks for its parts.", "Assets/_Script/Ghosts/MissingPartsGhost.cs",
            "Assets/Resources/Items/WorldGhostPart_Head.prefab", "Assets/Resources/Items/WorldGhostPart_LeftArm.prefab",
            "Assets/Resources/Items/WorldGhostPart_RightArm.prefab", "Assets/Resources/Items/WorldGhostPart_UpperBody.prefab"),
        E("Ghost Speech", "Missing Parts Ghost speech bubble", "Match: the Missing Parts Ghost talks.", "Assets/_Script/Ghosts/MissingPartsGhost.cs"),
    };

    public static Entry Find(string screen)
    {
        foreach (Entry e in All) if (e.screen == screen) return e;
        return null;
    }
}
