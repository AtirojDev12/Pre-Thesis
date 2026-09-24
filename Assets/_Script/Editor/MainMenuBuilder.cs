using System.Collections.Generic;
using System.Text;
using Mirror;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE CLICK: Tools > Pre-Thesis > Build Main Menu + Lobby
///
/// Does everything the new menu flow needs, so nobody has to wire 60 fields by
/// hand:
///   1. Creates Assets/Prefab/RoomPlayer.prefab (the lobby seat).
///   2. Turns NetworkManager.prefab into a RoHRoomManager (all existing
///      settings kept), adds RoomPasswordAuthenticator, sets the scenes:
///      offline = MainMenu, online/room = Lobby, gameplay = Cinema_GamePlay.
///   3. Creates / rebuilds Assets/Scenes/Lobby.unity (waiting lobby UI).
///   4. Rebuilds the Main Menu UI inside MainMenu.unity. The old two test
///      buttons are switched off, not deleted.
///   5. Puts MainMenu, Lobby, Cinema_GamePlay first in Build Settings.
///
/// Safe to run again: it deletes only the UI it built itself
/// ("Main Menu UI", "Waiting Lobby UI") and rebuilds it.
/// </summary>
public static class MainMenuBuilder
{
    private const string MenuPath = "Tools/Pre-Thesis/Build Main Menu + Lobby";

    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
    private const string GameplayScenePath = "Assets/Scenes/Map/Cinema_GamePlay.unity";
    private const string NetworkManagerPrefabPath = "Assets/Prefab/NetworkManager.prefab";
    private const string RoomPlayerPrefabPath = "Assets/Prefab/RoomPlayer.prefab";

    private const string MenuRootName = "Main Menu UI";
    private const string LobbyRootName = "Waiting Lobby UI";

    // Plain dark palette. An artist can restyle everything in the scene later.
    private static readonly Color BgColor = Hex(0x0E0B0A);
    private static readonly Color CardColor = Hex(0x1C1614);
    private static readonly Color ButtonColor = Hex(0x2E2420);
    private static readonly Color AccentColor = Hex(0xD9A441);
    private static readonly Color TextColor = Hex(0xF4EFED);
    private static readonly Color MutedColor = Hex(0x9A8F8B);
    private static readonly Color ErrorColor = Hex(0xE05A4F);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.78f);
    private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.05f);

    private static TMP_DefaultControls.Resources tmpRes;
    private static DefaultControls.Resources uiRes;
    private static int uiLayer;

    [MenuItem(MenuPath)]
    private static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build Main Menu + Lobby", "Stop Play mode first.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameplayScenePath) == null ||
            AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath) == null)
        {
            EditorUtility.DisplayDialog("Build Main Menu + Lobby",
                "Could not find one of:\n" + MainMenuScenePath + "\n" + GameplayScenePath + "\n" + NetworkManagerPrefabPath +
                "\n\nNothing was changed.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        LoadResources();
        var log = new StringBuilder();

        try
        {
            GameObject roomPlayer = BuildRoomPlayerPrefab(log);
            if (!ConfigureNetworkManager(roomPlayer, log)) return;

            BuildLobbyScene(log);
            BuildMainMenuScene(log);
            UpdateBuildSettings(log);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Build Main Menu + Lobby", "Stopped with an error. See the Console.\n\n" + e.Message, "OK");
            return;
        }

        EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        Debug.Log("[MainMenuBuilder] Done.\n" + log);
        EditorUtility.DisplayDialog("Main Menu + Lobby built", log.ToString(), "OK");
    }

    // =========================================================================
    // 1. Room player prefab
    // =========================================================================

    private static GameObject BuildRoomPlayerPrefab(StringBuilder log)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RoomPlayerPrefabPath);
        if (existing != null && existing.GetComponent<RoHRoomPlayer>() != null)
        {
            log.AppendLine("- RoomPlayer.prefab already exists (kept).");
            return existing;
        }

        var go = new GameObject("RoomPlayer");
        go.AddComponent<NetworkIdentity>();
        RoHRoomPlayer player = go.AddComponent<RoHRoomPlayer>();
        player.showRoomGUI = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, RoomPlayerPrefabPath);
        Object.DestroyImmediate(go);

        log.AppendLine("- Created Assets/Prefab/RoomPlayer.prefab");
        return prefab;
    }

    // =========================================================================
    // 2. NetworkManager prefab -> RoHRoomManager
    // =========================================================================

    private static bool ConfigureNetworkManager(GameObject roomPlayerPrefab, StringBuilder log)
    {
        MonoScript roomScript = FindScript(typeof(RoHRoomManager));
        if (roomScript == null)
        {
            EditorUtility.DisplayDialog("Build Main Menu + Lobby",
                "Could not find the RoHRoomManager script. Wait for Unity to finish compiling, then run this again.", "OK");
            return false;
        }

        // Step A: swap the component's script from NetworkManager to
        // RoHRoomManager. Same object, same file ID, so every setting on it and
        // every reference to it (LobbyController.netManager, the transport)
        // survives. This is what the Inspector's Debug mode does by hand.
        GameObject root = PrefabUtility.LoadPrefabContents(NetworkManagerPrefabPath);
        try
        {
            NetworkManager manager = root.GetComponent<NetworkManager>();
            if (manager == null)
            {
                EditorUtility.DisplayDialog("Build Main Menu + Lobby", "NetworkManager.prefab has no NetworkManager on its root.", "OK");
                return false;
            }

            if (!(manager is RoHRoomManager))
            {
                var so = new SerializedObject(manager);
                so.FindProperty("m_Script").objectReferenceValue = roomScript;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
                log.AppendLine("- NetworkManager.prefab: NetworkManager -> RoHRoomManager (old settings kept)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Step B: reload so the component really is a RoHRoomManager, then set it up.
        root = PrefabUtility.LoadPrefabContents(NetworkManagerPrefabPath);
        try
        {
            RoHRoomManager room = root.GetComponent<RoHRoomManager>();
            if (room == null)
            {
                EditorUtility.DisplayDialog("Build Main Menu + Lobby",
                    "The swap to RoHRoomManager did not take.\n\nDo it by hand: open NetworkManager.prefab, switch the Inspector " +
                    "to Debug mode (the ⋮ menu), drag RoHRoomManager.cs into the NetworkManager component's Script field, " +
                    "then run this menu again.", "OK");
                return false;
            }

            room.roomPlayerPrefab = roomPlayerPrefab.GetComponent<NetworkRoomPlayer>();
            room.RoomScene = LobbyScenePath;
            room.GameplayScene = GameplayScenePath;
            room.onlineScene = LobbyScenePath;
            room.offlineScene = MainMenuScenePath;
            room.showRoomGUI = false;
            room.minPlayers = 1;

            RoomPasswordAuthenticator auth = root.GetComponent<RoomPasswordAuthenticator>();
            if (auth == null)
            {
                auth = root.AddComponent<RoomPasswordAuthenticator>();
                log.AppendLine("- Added RoomPasswordAuthenticator (private room passwords, room full, match started)");
            }
            room.authenticator = auth;

            PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
            log.AppendLine("- Scenes: offline = MainMenu, room/online = Lobby, gameplay = Cinema_GamePlay");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return true;
    }

    // =========================================================================
    // 3. Lobby scene
    // =========================================================================

    private static void BuildLobbyScene(StringBuilder log)
    {
        bool isNew = AssetDatabase.LoadAssetAtPath<SceneAsset>(LobbyScenePath) == null;
        Scene scene = isNew
            ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
            : EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);

        DestroyRoot(scene, LobbyRootName);
        EnsureCamera(scene);
        EnsureEventSystem(scene);

        RectTransform canvas = CreateCanvas(LobbyRootName);
        Stretch(AddImage(NewRect("Background", canvas), BgColor).rectTransform);

        RectTransform card = Card(canvas, "Lobby Card", new Vector2(1000, 840));

        TMP_Text title = Label(card, "Title", "Waiting Lobby", 52, AccentColor, TextAlignmentOptions.Center, 70);
        TMP_Text info = Label(card, "Room Info", "Cinema | Normal | Public | Players 1 / 6", 26, MutedColor, TextAlignmentOptions.Center, 40);

        // Seats
        RectTransform list = NewRect("Player List", card);
        Layout(list, 0, 6 * 64 + 5 * 8, flexibleHeight: 1);
        VerticalLayoutGroup listLayout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        ConfigureVertical(listLayout, 8, new RectOffset(0, 0, 0, 0));

        RectTransform rowRt = NewRect("Player Row (template)", list);
        Layout(rowRt, 0, 64);
        Image rowBg = AddImage(rowRt, RowColor);
        HorizontalLayoutGroup rowLayout = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureHorizontal(rowLayout, 16, new RectOffset(24, 24, 8, 8));
        TMP_Text rowName = Label(rowRt, "Name", "Player", 26, TextColor, TextAlignmentOptions.MidlineLeft, 48, width: 500, flexibleWidth: 1);
        TMP_Text rowState = Label(rowRt, "State", "Not ready", 24, MutedColor, TextAlignmentOptions.MidlineRight, 48, width: 220);
        LobbyPlayerRow row = rowRt.gameObject.AddComponent<LobbyPlayerRow>();
        Wire(row, ("nameText", rowName), ("stateText", rowState), ("background", rowBg));

        TMP_Text status = Label(card, "Status", "Waiting for players to get ready...", 24, TextColor, TextAlignmentOptions.Center, 40);

        RectTransform buttons = ButtonRow(card, "Buttons");
        Button leave = MakeButton(buttons, "Leave Button", "Leave", false);
        Button ready = MakeButton(buttons, "Ready Button", "Ready", false);
        Button start = MakeButton(buttons, "Start Button", "Start", true);

        WaitingLobbyController controller = canvas.gameObject.AddComponent<WaitingLobbyController>();
        Wire(controller,
            ("titleText", title), ("infoText", info), ("statusText", status),
            ("playerListContent", list), ("rowTemplate", row),
            ("readyButton", ready), ("readyButtonText", ButtonText(ready)),
            ("startButton", start), ("leaveButton", leave));

        SetLayerRecursively(canvas.gameObject, uiLayer);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, LobbyScenePath);
        log.AppendLine(isNew ? "- Created Assets/Scenes/Lobby.unity" : "- Rebuilt Lobby.unity UI");
    }

    // =========================================================================
    // 4. Main menu scene
    // =========================================================================

    private static void BuildMainMenuScene(StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

        DestroyRoot(scene, MenuRootName);
        DisableOldTestButtons(scene, log);
        EnsureCamera(scene);
        EnsureEventSystem(scene);

        RectTransform canvas = CreateCanvas(MenuRootName);
        Stretch(AddImage(NewRect("Background", canvas), BgColor).rectTransform);

        // ---- Main panel: title + five buttons, left side --------------------
        RectTransform main = NewRect("Main Panel", canvas);
        main.anchorMin = main.anchorMax = new Vector2(0f, 0.5f);
        main.pivot = new Vector2(0f, 0.5f);
        main.anchoredPosition = new Vector2(160f, 0f);
        main.sizeDelta = new Vector2(560f, 760f);
        ConfigureVertical(main.gameObject.AddComponent<VerticalLayoutGroup>(), 16, new RectOffset(0, 0, 0, 0));

        Label(main, "Title", "13RoH", 110, AccentColor, TextAlignmentOptions.MidlineLeft, 130);
        Label(main, "Subtitle", "Pre-Thesis Demo", 28, MutedColor, TextAlignmentOptions.MidlineLeft, 40);
        Layout(NewRect("Spacer", main), 0, 40);

        Button createButton = MakeButton(main, "Create Room Button", "Create Room", true);
        Button browseButton = MakeButton(main, "Room Browser Button", "Room Browser", false);
        Button quickJoinButton = MakeButton(main, "Quick Join Button", "Quick Join", false);
        Button settingsButton = MakeButton(main, "Settings Button", "Settings", false);
        Button quitButton = MakeButton(main, "Quit Button", "Quit", false);

        // ---- Footer: name + connection, bottom-left --------------------------
        RectTransform footer = NewRect("Footer", canvas);
        footer.anchorMin = footer.anchorMax = new Vector2(0f, 0f);
        footer.pivot = new Vector2(0f, 0f);
        footer.anchoredPosition = new Vector2(160f, 60f);
        footer.sizeDelta = new Vector2(900f, 80f);
        ConfigureVertical(footer.gameObject.AddComponent<VerticalLayoutGroup>(), 4, new RectOffset(0, 0, 0, 0));
        TMP_Text playerName = Label(footer, "Player Name", "Playing as Player", 24, TextColor, TextAlignmentOptions.MidlineLeft, 34);
        TMP_Text connection = Label(footer, "Connection", "Connecting to Epic Online Services...", 22, MutedColor, TextAlignmentOptions.MidlineLeft, 30);

        // ---- Panels ----------------------------------------------------------
        MainMenuController menu = canvas.gameObject.AddComponent<MainMenuController>();

        CreateRoomPanel createPanel = BuildCreatePanel(canvas, menu);
        RoomBrowserPanel browserPanel = BuildBrowserPanel(canvas, menu);
        SettingsPanel settingsPanel = BuildSettingsPanel(canvas, menu);

        // ---- Busy overlay (above panels) -------------------------------------
        RectTransform busy = NewRect("Busy Overlay", canvas);
        Stretch(busy);
        AddImage(busy, DimColor);
        TMP_Text busyText = Label(busy, "Busy Text", "Please wait...", 34, TextColor, TextAlignmentOptions.Center, 60);
        Stretch((RectTransform)busyText.transform);

        // ---- Message popup (top) ---------------------------------------------
        RectTransform popup = NewRect("Message Popup", canvas);
        Stretch(popup);
        AddImage(popup, DimColor);
        RectTransform popupCard = Card(popup, "Message Card", new Vector2(680, 320));
        TMP_Text messageText = Label(popupCard, "Message", "Message", 28, TextColor, TextAlignmentOptions.Center, 150, flexibleHeight: 1);
        RectTransform popupButtons = ButtonRow(popupCard, "Buttons");
        Button okButton = MakeButton(popupButtons, "OK Button", "OK", true);

        Wire(menu,
            ("mainPanel", main.gameObject),
            ("createPanel", createPanel), ("browserPanel", browserPanel), ("settingsPanel", settingsPanel),
            ("createButton", createButton), ("browseButton", browseButton), ("quickJoinButton", quickJoinButton),
            ("settingsButton", settingsButton), ("quitButton", quitButton),
            ("connectionText", connection), ("playerNameText", playerName),
            ("busyOverlay", busy.gameObject), ("busyText", busyText),
            ("messagePopup", popup.gameObject), ("messageText", messageText), ("messageOkButton", okButton));

        // Start with only the main panel showing, so the scene view is readable.
        createPanel.gameObject.SetActive(false);
        browserPanel.gameObject.SetActive(false);
        settingsPanel.gameObject.SetActive(false);
        busy.gameObject.SetActive(false);
        popup.gameObject.SetActive(false);

        SetLayerRecursively(canvas.gameObject, uiLayer);
        VerifyNetworkManagerInstance(log);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("- Rebuilt Main Menu UI in MainMenu.unity");
    }

    private static CreateRoomPanel BuildCreatePanel(RectTransform canvas, MainMenuController menu)
    {
        RectTransform card = Card(canvas, "Create Room Panel", new Vector2(760, 760));
        Label(card, "Title", "Create Room", 44, AccentColor, TextAlignmentOptions.Center, 60);

        TMP_Text map = Label(card, "Map", "Map: Cinema", 26, MutedColor, TextAlignmentOptions.MidlineLeft, 36);

        Label(card, "Difficulty Label", "Difficulty", 24, TextColor, TextAlignmentOptions.MidlineLeft, 30);
        TMP_Dropdown difficulty = MakeDropdown(card, "Difficulty Dropdown");

        TMP_Text limitText = Label(card, "Player Limit Label", "Players: 6", 24, TextColor, TextAlignmentOptions.MidlineLeft, 30);
        Slider limit = MakeSlider(card, "Player Limit Slider");

        Toggle privateToggle = MakeToggle(card, "Private Toggle", "Private room (needs a password)");
        TMP_InputField password = MakeInput(card, "Password Field", "Password");

        TMP_Text error = Label(card, "Error", "", 22, ErrorColor, TextAlignmentOptions.Center, 32);

        RectTransform buttons = ButtonRow(card, "Buttons");
        Button back = MakeButton(buttons, "Back Button", "Back", false);
        Button create = MakeButton(buttons, "Create Button", "Create", true);

        CreateRoomPanel panel = card.gameObject.AddComponent<CreateRoomPanel>();
        Wire(panel,
            ("menu", menu), ("mapText", map), ("difficultyDropdown", difficulty),
            ("playerLimitSlider", limit), ("playerLimitText", limitText),
            ("privateToggle", privateToggle), ("passwordField", password),
            ("errorText", error), ("createButton", create), ("backButton", back));
        return panel;
    }

    private static RoomBrowserPanel BuildBrowserPanel(RectTransform canvas, MainMenuController menu)
    {
        RectTransform card = Card(canvas, "Room Browser Panel", new Vector2(1180, 800));
        Label(card, "Title", "Room Browser", 44, AccentColor, TextAlignmentOptions.Center, 60);

        // Column headers
        RectTransform header = NewRect("Header", card);
        Layout(header, 0, 36);
        ConfigureHorizontal(header.gameObject.AddComponent<HorizontalLayoutGroup>(), 16, new RectOffset(24, 24, 0, 0));
        Label(header, "Map", "Map", 22, MutedColor, TextAlignmentOptions.MidlineLeft, 36, width: 300, flexibleWidth: 1);
        Label(header, "Difficulty", "Difficulty", 22, MutedColor, TextAlignmentOptions.MidlineLeft, 36, width: 180);
        Label(header, "Players", "Players", 22, MutedColor, TextAlignmentOptions.MidlineLeft, 36, width: 130);
        Label(header, "Status", "Status", 22, MutedColor, TextAlignmentOptions.MidlineLeft, 36, width: 150);
        Layout(NewRect("Join Column", header), 0, 36, width: 150);

        // Scrolling list
        GameObject scrollGo = DefaultControls.CreateScrollView(uiRes);
        scrollGo.name = "Room List";
        scrollGo.transform.SetParent(card, false);
        Layout((RectTransform)scrollGo.transform, 0, 440, flexibleHeight: 1);
        scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        if (scroll.horizontalScrollbar != null)
        {
            Object.DestroyImmediate(scroll.horizontalScrollbar.gameObject);
            scroll.horizontalScrollbar = null;
        }

        RectTransform content = scroll.content;
        ConfigureVertical(content.gameObject.AddComponent<VerticalLayoutGroup>(), 8, new RectOffset(8, 8, 8, 8));
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text empty = Label(content, "Empty Text", "Searching...", 26, MutedColor, TextAlignmentOptions.Center, 80);

        // Row template
        RectTransform rowRt = NewRect("Room Row (template)", content);
        Layout(rowRt, 0, 68);
        AddImage(rowRt, RowColor);
        ConfigureHorizontal(rowRt.gameObject.AddComponent<HorizontalLayoutGroup>(), 16, new RectOffset(16, 16, 8, 8));
        TMP_Text rowMap = Label(rowRt, "Map", "Cinema", 26, TextColor, TextAlignmentOptions.MidlineLeft, 52, width: 300, flexibleWidth: 1);
        TMP_Text rowDiff = Label(rowRt, "Difficulty", "Normal", 24, TextColor, TextAlignmentOptions.MidlineLeft, 52, width: 180);
        TMP_Text rowPlayers = Label(rowRt, "Players", "1 / 6", 24, TextColor, TextAlignmentOptions.MidlineLeft, 52, width: 130);
        TMP_Text rowStatus = Label(rowRt, "Status", "Open", 24, MutedColor, TextAlignmentOptions.MidlineLeft, 52, width: 150);
        Button rowJoin = MakeButton(rowRt, "Join Button", "Join", true, 52, 150);
        RoomBrowserRow row = rowRt.gameObject.AddComponent<RoomBrowserRow>();
        Wire(row, ("mapText", rowMap), ("difficultyText", rowDiff), ("playersText", rowPlayers),
            ("statusText", rowStatus), ("joinButton", rowJoin));

        RectTransform buttons = ButtonRow(card, "Buttons");
        Button back = MakeButton(buttons, "Back Button", "Back", false);
        Button refresh = MakeButton(buttons, "Refresh Button", "Refresh", false);

        // Password prompt: covers the card, ignored by the card's layout.
        RectTransform prompt = NewRect("Password Prompt", card);
        prompt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        Stretch(prompt);
        AddImage(prompt, DimColor);
        RectTransform promptCard = Card(prompt, "Prompt Card", new Vector2(640, 340));
        TMP_Text promptTitle = Label(promptCard, "Title", "This room is private", 28, TextColor, TextAlignmentOptions.Center, 50);
        TMP_InputField promptField = MakeInput(promptCard, "Password Field", "Password");
        RectTransform promptButtons = ButtonRow(promptCard, "Buttons");
        Button promptCancel = MakeButton(promptButtons, "Cancel Button", "Cancel", false);
        Button promptJoin = MakeButton(promptButtons, "Join Button", "Join", true);

        RoomBrowserPanel panel = card.gameObject.AddComponent<RoomBrowserPanel>();
        Wire(panel,
            ("menu", menu), ("listContent", content), ("rowTemplate", row), ("emptyText", empty),
            ("refreshButton", refresh), ("backButton", back),
            ("passwordPrompt", prompt.gameObject), ("promptTitle", promptTitle),
            ("promptPasswordField", promptField), ("promptJoinButton", promptJoin), ("promptCancelButton", promptCancel));

        prompt.gameObject.SetActive(false);
        rowRt.gameObject.SetActive(false);
        return panel;
    }

    /// <summary>
    /// Two-column Settings card. Used by the main menu (menu set) and the Esc
    /// pause menu (menu null — it listens to SettingsPanel.BackRequested).
    /// </summary>
    private static SettingsPanel BuildSettingsPanel(RectTransform parent, MainMenuController menu)
    {
        RectTransform card = Card(parent, "Settings Panel", new Vector2(1180, 800));
        Label(card, "Title", "Settings", 44, AccentColor, TextAlignmentOptions.Center, 60);

        RectTransform columns = NewRect("Columns", card);
        Layout(columns, 0, 540, flexibleHeight: 1);
        HorizontalLayoutGroup row = columns.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureHorizontal(row, 48, new RectOffset(0, 0, 0, 0));
        row.childAlignment = TextAnchor.UpperLeft;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;

        // ---- General -------------------------------------------------------
        RectTransform left = Column(columns, "General");
        Label(left, "Header", "General", 28, AccentColor, TextAlignmentOptions.MidlineLeft, 40);
        Label(left, "Name Label", "Player name", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        TMP_InputField name = MakeInput(left, "Name Field", "Your name");
        TMP_Text sensitivityText = Label(left, "Sensitivity Label", "Mouse sensitivity: 1.0x", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider sensitivity = MakeSlider(left, "Sensitivity Slider");
        TMP_Text brightnessText = Label(left, "Brightness Label", "Brightness: default", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider brightness = MakeSlider(left, "Brightness Slider");
        Toggle fullscreen = MakeToggle(left, "Fullscreen Toggle", "Fullscreen");
        Label(left, "Resolution Label", "Resolution", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        TMP_Dropdown resolution = MakeDropdown(left, "Resolution Dropdown");

        // ---- Sound ---------------------------------------------------------
        RectTransform right = Column(columns, "Sound");
        Label(right, "Header", "Sound", 28, AccentColor, TextAlignmentOptions.MidlineLeft, 40);
        TMP_Text volumeText = Label(right, "Master Label", "Master volume: 100%", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider volume = MakeSlider(right, "Master Slider");
        TMP_Text musicText = Label(right, "Music Label", "Music: 100%", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider music = MakeSlider(right, "Music Slider");
        TMP_Text sfxText = Label(right, "SFX Label", "Sound effects: 100%", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider sfx = MakeSlider(right, "SFX Slider");
        TMP_Text ambientText = Label(right, "Ambient Label", "Ambient: 100%", 22, TextColor, TextAlignmentOptions.MidlineLeft, 28);
        Slider ambient = MakeSlider(right, "Ambient Slider");

        RectTransform buttons = ButtonRow(card, "Buttons");
        Button back = MakeButton(buttons, "Back Button", "Back", true);

        SettingsPanel panel = card.gameObject.AddComponent<SettingsPanel>();
        Wire(panel,
            ("menu", menu), ("nameField", name),
            ("sensitivitySlider", sensitivity), ("sensitivityText", sensitivityText),
            ("brightnessSlider", brightness), ("brightnessText", brightnessText),
            ("fullscreenToggle", fullscreen), ("resolutionDropdown", resolution),
            ("volumeSlider", volume), ("volumeText", volumeText),
            ("musicSlider", music), ("musicText", musicText),
            ("sfxSlider", sfx), ("sfxText", sfxText),
            ("ambientSlider", ambient), ("ambientText", ambientText),
            ("backButton", back));
        return panel;
    }

    private static RectTransform Column(Transform parent, string name)
    {
        RectTransform rt = NewRect(name, parent);
        rt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        ConfigureVertical(rt.gameObject.AddComponent<VerticalLayoutGroup>(), 10, new RectOffset(0, 0, 0, 0));
        return rt;
    }

    // =========================================================================
    // Settings + Esc pause menu (does NOT touch the rest of the main menu)
    // =========================================================================

    private const string RebuildSettingsMenuPath = "Tools/Pre-Thesis/Rebuild Settings + Pause Menu";
    private const string PauseMenuFolder = "Assets/Resources/UI";
    private const string PauseMenuPrefabPath = PauseMenuFolder + "/PauseMenu.prefab";

    [MenuItem(RebuildSettingsMenuPath)]
    private static void RebuildSettingsAndPauseMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Rebuild Settings + Pause Menu", "Stop Play mode first.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        LoadResources();
        var log = new StringBuilder();

        try
        {
            BuildPauseMenuPrefab(log);
            RebuildMainMenuSettings(log);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Rebuild Settings + Pause Menu", "Stopped with an error. See the Console.\n\n" + e.Message, "OK");
            return;
        }

        Debug.Log("[MainMenuBuilder] Settings + Pause Menu done.\n" + log);
        EditorUtility.DisplayDialog("Settings + Pause Menu built", log.ToString(), "OK");
    }

    /// <summary>Replaces only "Main Menu UI/Settings Panel". Background and all other UI stay as they are.</summary>
    private static void RebuildMainMenuSettings(StringBuilder log)
    {
        Scene scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

        GameObject root = null;
        foreach (GameObject candidate in scene.GetRootGameObjects())
        {
            if (candidate.name == MenuRootName) root = candidate;
        }

        if (root == null)
        {
            log.AppendLine("! MainMenu has no 'Main Menu UI'. Run Build Main Menu + Lobby first.");
            return;
        }

        MainMenuController menu = root.GetComponent<MainMenuController>();
        Transform old = root.transform.Find("Settings Panel");
        int sibling = old != null ? old.GetSiblingIndex() : -1;
        if (old != null) Object.DestroyImmediate(old.gameObject);

        SettingsPanel panel = BuildSettingsPanel((RectTransform)root.transform, menu);
        if (sibling >= 0) panel.transform.SetSiblingIndex(sibling);
        panel.gameObject.SetActive(false);
        SetLayerRecursively(panel.gameObject, uiLayer);

        if (menu != null) Wire(menu, ("settingsPanel", panel));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("- MainMenu: Settings panel rebuilt (general + sound). Nothing else in the menu was touched.");
    }

    private static void BuildPauseMenuPrefab(StringBuilder log)
    {
        EnsureFolder(PauseMenuFolder);

        // Build in a throw-away preview scene so the open scene is never dirtied.
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            RectTransform canvas = CreateCanvas("PauseMenu");
            SceneManager.MoveGameObjectToScene(canvas.gameObject, preview);
            canvas.GetComponent<Canvas>().sortingOrder = 50;

            RectTransform content = NewRect("Content", canvas);
            Stretch(content);

            RectTransform dim = NewRect("Dim", content);
            Stretch(dim);
            AddImage(dim, new Color(0f, 0f, 0f, 0.6f));

            RectTransform pause = Card(content, "Pause Panel", new Vector2(620, 580));
            Label(pause, "Title", "Paused", 52, AccentColor, TextAlignmentOptions.Center, 70);
            Label(pause, "Hint", "Online match: the game keeps running while this menu is open.", 22, MutedColor, TextAlignmentOptions.Center, 60);
            Button resume = MakeButton(pause, "Resume Button", "Resume", true);
            Button settings = MakeButton(pause, "Settings Button", "Settings", false);
            Button leave = MakeButton(pause, "Leave Button", "Leave match", false);

            SettingsPanel settingsPanel = BuildSettingsPanel(content, null);

            RectTransform confirm = NewRect("Confirm Popup", content);
            Stretch(confirm);
            AddImage(confirm, DimColor);
            RectTransform confirmCard = Card(confirm, "Confirm Card", new Vector2(720, 340));
            TMP_Text confirmText = Label(confirmCard, "Message", "Leave the match?", 28, TextColor, TextAlignmentOptions.Center, 150, flexibleHeight: 1);
            RectTransform confirmButtons = ButtonRow(confirmCard, "Buttons");
            Button cancel = MakeButton(confirmButtons, "Cancel Button", "Cancel", false);
            Button confirmLeave = MakeButton(confirmButtons, "Leave Button", "Leave", true);

            PauseMenuController controller = canvas.gameObject.AddComponent<PauseMenuController>();
            Wire(controller,
                ("content", content.gameObject), ("pausePanel", pause.gameObject), ("settingsPanel", settingsPanel),
                ("resumeButton", resume), ("settingsButton", settings), ("leaveButton", leave),
                ("leaveButtonText", ButtonText(leave)),
                ("confirmPopup", confirm.gameObject), ("confirmText", confirmText),
                ("confirmLeaveButton", confirmLeave), ("confirmCancelButton", cancel));

            settingsPanel.gameObject.SetActive(false);
            confirm.gameObject.SetActive(false);
            content.gameObject.SetActive(false);
            SetLayerRecursively(canvas.gameObject, uiLayer);

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PauseMenuPrefabPath);
            log.AppendLine("- Built " + PauseMenuPrefabPath + " (loads itself in every match, no scene changes)");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // =========================================================================
    // Audio: tag AudioSources with a Settings category
    // =========================================================================

    [MenuItem("Tools/Pre-Thesis/Audio/Tag Selected As Music")]
    private static void TagMusic() => TagSelected(SoundCategory.Music);

    [MenuItem("Tools/Pre-Thesis/Audio/Tag Selected As SFX")]
    private static void TagSfx() => TagSelected(SoundCategory.Sfx);

    [MenuItem("Tools/Pre-Thesis/Audio/Tag Selected As Ambient")]
    private static void TagAmbient() => TagSelected(SoundCategory.Ambient);

    private static void TagSelected(SoundCategory category)
    {
        int count = 0;
        foreach (GameObject go in Selection.gameObjects)
        {
            foreach (AudioSource source in go.GetComponentsInChildren<AudioSource>(true))
            {
                SoundCategoryVolume tag = source.GetComponent<SoundCategoryVolume>();
                if (tag == null) tag = Undo.AddComponent<SoundCategoryVolume>(source.gameObject);

                var so = new SerializedObject(tag);
                so.FindProperty("category").enumValueIndex = (int)category;
                so.ApplyModifiedProperties();
                count++;
            }
        }

        Debug.Log(count == 0
            ? "[Audio] No AudioSource in the selection."
            : $"[Audio] Tagged {count} AudioSource(s) as {category}. Save the scene/prefab.");
    }

    private static void DisableOldTestButtons(Scene scene, StringBuilder log)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == MenuRootName) continue;

            Transform oldButton = FindDeep(root.transform, "Btn_CreateRoom") ?? FindDeep(root.transform, "Btn_FindRoom");
            if (oldButton == null) continue;

            Canvas oldCanvas = oldButton.GetComponentInParent<Canvas>(true);
            GameObject target = oldCanvas != null ? oldCanvas.rootCanvas.gameObject : root;
            if (target.activeSelf)
            {
                target.SetActive(false);
                if (!target.name.Contains("(old, disabled)")) target.name += " (old, disabled)";
                log.AppendLine($"- Switched off the old test buttons ('{target.name}'). Delete it when you are happy.");
            }
        }
    }

    private static void VerifyNetworkManagerInstance(StringBuilder log)
    {
        RoHRoomManager room = Object.FindAnyObjectByType<RoHRoomManager>();
        if (room == null)
        {
            log.AppendLine("! MainMenu has no RoHRoomManager instance. Drag Assets/Prefab/NetworkManager.prefab into MainMenu.");
            return;
        }

        // A scene instance can override prefab values. Make sure it does not
        // override the ones this flow depends on.
        bool changed = room.onlineScene != LobbyScenePath || room.RoomScene != LobbyScenePath ||
                       room.GameplayScene != GameplayScenePath || room.offlineScene != MainMenuScenePath;
        if (changed)
        {
            room.onlineScene = LobbyScenePath;
            room.RoomScene = LobbyScenePath;
            room.GameplayScene = GameplayScenePath;
            room.offlineScene = MainMenuScenePath;
            EditorUtility.SetDirty(room);
            log.AppendLine("- Fixed scene overrides on the NetworkManager instance in MainMenu");
        }
    }

    // =========================================================================
    // 5. Build settings
    // =========================================================================

    private static void UpdateBuildSettings(StringBuilder log)
    {
        var ordered = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MainMenuScenePath, true),
            new EditorBuildSettingsScene(LobbyScenePath, true),
            new EditorBuildSettingsScene(GameplayScenePath, true),
        };

        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (!ordered.Exists(s => s.path == existing.path)) ordered.Add(existing);
        }

        EditorBuildSettings.scenes = ordered.ToArray();
        log.AppendLine("- Build Settings: 0 MainMenu, 1 Lobby, 2 Cinema_GamePlay");
    }

    // =========================================================================
    // Scene helpers
    // =========================================================================

    private static void DestroyRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) Object.DestroyImmediate(root);
        }
    }

    private static void EnsureCamera(Scene scene)
    {
        // Menu scenes must never show the default sky behind the UI. Existing
        // cameras are switched to plain black too, not only new ones.
        bool found = false;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera existing in root.GetComponentsInChildren<Camera>(true))
            {
                existing.clearFlags = CameraClearFlags.SolidColor;
                existing.backgroundColor = Color.black;
                EditorUtility.SetDirty(existing);
                found = true;
            }
        }
        if (found) return;

        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        go.AddComponent<AudioListener>();
    }

    private static void EnsureEventSystem(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<EventSystem>(true) != null) return;
        }

        // The project uses the new Input System only, so the UI module must be
        // InputSystemUIInputModule — the old StandaloneInputModule throws.
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private static RectTransform CreateCanvas(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return (RectTransform)go.transform;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }

    // =========================================================================
    // UI helpers
    // =========================================================================

    private static void LoadResources()
    {
        Sprite standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        Sprite inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        Sprite checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        Sprite dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd");
        Sprite mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd");

        uiRes = new DefaultControls.Resources
        {
            standard = standard, background = background, inputField = inputField,
            knob = knob, checkmark = checkmark, dropdown = dropdown, mask = mask,
        };

        tmpRes = new TMP_DefaultControls.Resources
        {
            standard = standard, background = background, inputField = inputField,
            knob = knob, checkmark = checkmark, dropdown = dropdown, mask = mask,
        };

        uiLayer = LayerMask.NameToLayer("UI");
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static Image AddImage(RectTransform rt, Color color)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.sprite = uiRes.background;
        image.type = UnityEngine.UI.Image.Type.Sliced;
        image.color = color;
        return image;
    }

    private static LayoutElement Layout(RectTransform rt, float minHeight, float preferredHeight,
        float width = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
    {
        LayoutElement le = rt.GetComponent<LayoutElement>();
        if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = minHeight > 0 ? minHeight : preferredHeight;
        le.preferredHeight = preferredHeight;
        if (width > 0) { le.preferredWidth = width; le.minWidth = Mathf.Min(width, 80f); }
        if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        if (flexibleHeight >= 0) le.flexibleHeight = flexibleHeight;
        return le;
    }

    private static void ConfigureVertical(VerticalLayoutGroup group, float spacing, RectOffset padding)
    {
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.UpperCenter;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
    }

    private static void ConfigureHorizontal(HorizontalLayoutGroup group, float spacing, RectOffset padding)
    {
        group.spacing = spacing;
        group.padding = padding;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
    }

    private static RectTransform Card(Transform parent, string name, Vector2 size)
    {
        RectTransform rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
        AddImage(rt, CardColor);
        ConfigureVertical(rt.gameObject.AddComponent<VerticalLayoutGroup>(), 16, new RectOffset(48, 48, 40, 40));
        return rt;
    }

    private static RectTransform ButtonRow(Transform parent, string name)
    {
        RectTransform rt = NewRect(name, parent);
        Layout(rt, 0, 72);
        HorizontalLayoutGroup group = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureHorizontal(group, 16, new RectOffset(0, 0, 0, 0));
        group.childAlignment = TextAnchor.MiddleCenter;
        group.childForceExpandWidth = true;
        return rt;
    }

    private static TMP_Text Label(Transform parent, string name, string text, float size, Color color,
        TextAlignmentOptions align, float height, float width = -1f, float flexibleWidth = -1f, float flexibleHeight = -1f)
    {
        RectTransform rt = NewRect(name, parent);
        TextMeshProUGUI label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.raycastTarget = false;
        Layout(rt, 0, height, width, flexibleWidth, flexibleHeight);
        return label;
    }

    private static Button MakeButton(Transform parent, string name, string text, bool primary,
        float height = 68f, float width = -1f)
    {
        GameObject go = TMP_DefaultControls.CreateButton(tmpRes);
        go.name = name;
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = primary ? AccentColor : ButtonColor;

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        button.colors = colors;

        TMP_Text label = ButtonText(button);
        label.text = text;
        label.fontSize = 28;
        label.color = primary ? BgColor : TextColor;

        Layout((RectTransform)go.transform, 0, height, width);
        return button;
    }

    private static TMP_Text ButtonText(Button button) => button.GetComponentInChildren<TMP_Text>(true);

    private static TMP_InputField MakeInput(Transform parent, string name, string placeholder)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(tmpRes);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonColor;

        TMP_InputField field = go.GetComponent<TMP_InputField>();
        field.textComponent.color = TextColor;
        field.textComponent.fontSize = 26;
        if (field.placeholder is TMP_Text ph)
        {
            ph.text = placeholder;
            ph.color = MutedColor;
            ph.fontSize = 26;
        }

        Layout((RectTransform)go.transform, 0, 60);
        return field;
    }

    private static TMP_Dropdown MakeDropdown(Transform parent, string name)
    {
        GameObject go = TMP_DefaultControls.CreateDropdown(tmpRes);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonColor;

        TMP_Dropdown dropdown = go.GetComponent<TMP_Dropdown>();
        if (dropdown.captionText != null)
        {
            dropdown.captionText.color = TextColor;
            dropdown.captionText.fontSize = 26;
        }
        if (dropdown.itemText != null) dropdown.itemText.fontSize = 24;

        Layout((RectTransform)go.transform, 0, 60);
        return dropdown;
    }

    private static Slider MakeSlider(Transform parent, string name)
    {
        GameObject go = DefaultControls.CreateSlider(uiRes);
        go.name = name;
        go.transform.SetParent(parent, false);

        Slider slider = go.GetComponent<Slider>();
        if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill)) fill.color = AccentColor;

        Transform background = go.transform.Find("Background");
        if (background != null && background.TryGetComponent(out Image bg)) bg.color = ButtonColor;

        Layout((RectTransform)go.transform, 0, 32);
        return slider;
    }

    private static Toggle MakeToggle(Transform parent, string name, string text)
    {
        GameObject go = DefaultControls.CreateToggle(uiRes);
        go.name = name;
        go.transform.SetParent(parent, false);
        Layout((RectTransform)go.transform, 0, 44);

        Toggle toggle = go.GetComponent<Toggle>();

        // Bigger box, vertically centred.
        Transform box = go.transform.Find("Background");
        if (box != null)
        {
            var boxRt = (RectTransform)box;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.anchoredPosition = Vector2.zero;
            boxRt.sizeDelta = new Vector2(36f, 36f);
            if (box.TryGetComponent(out Image boxImage)) boxImage.color = ButtonColor;

            Transform check = box.Find("Checkmark");
            if (check != null)
            {
                Stretch((RectTransform)check);
                ((RectTransform)check).offsetMin = new Vector2(4f, 4f);
                ((RectTransform)check).offsetMax = new Vector2(-4f, -4f);
                if (check.TryGetComponent(out Image checkImage)) checkImage.color = AccentColor;
            }
        }

        // Replace the legacy Text label with TextMeshPro, like the rest of the UI.
        Transform labelT = go.transform.Find("Label");
        if (labelT != null)
        {
            Text legacy = labelT.GetComponent<Text>();
            if (legacy != null) Object.DestroyImmediate(legacy);

            TextMeshProUGUI label = labelT.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 24;
            label.color = TextColor;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;

            var labelRt = (RectTransform)labelT;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(52f, 0f);
            labelRt.offsetMax = Vector2.zero;
        }

        return toggle;
    }

    private static void Wire(Object target, params (string field, Object value)[] pairs)
    {
        var so = new SerializedObject(target);
        foreach ((string field, Object value) in pairs)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[MainMenuBuilder] {target.GetType().Name} has no serialized field '{field}'.", target);
                continue;
            }
            property.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static MonoScript FindScript(System.Type type)
    {
        foreach (string guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type) return script;
        }
        return null;
    }

    private static Color Hex(int rgb) => new Color(
        ((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
