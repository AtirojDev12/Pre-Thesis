using System.Collections;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Esc menu during a match: Resume, Settings, Leave match.
///
/// Loaded automatically from Resources/UI/PauseMenu (built by Tools > Pre-Thesis >
/// Rebuild Settings + Pause Menu), so no gameplay scene has to contain it. That
/// keeps it out of the map scenes the team edits, so there are no merge conflicts.
///
/// Esc only works when a local player exists (in the match, not in the main
/// menu or waiting lobby). Pressing Esc again goes back one step:
/// confirm -> pause, settings -> pause, pause -> game.
///
/// Online co-op cannot freeze time for everyone, so this menu does NOT pause
/// the game: ghosts keep moving. It only frees the cursor and blocks your
/// movement and interaction keys (GameplayInput.Blocked).
/// </summary>
[DisallowMultipleComponent]
public class PauseMenuController : MonoBehaviour
{
    private const string ResourcePath = "UI/PauseMenu";

    [SerializeField] private GameObject content;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private SettingsPanel settingsPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TMP_Text leaveButtonText;

    [Header("Confirm leave")]
    [SerializeField] private GameObject confirmPopup;
    [SerializeField] private TMP_Text confirmText;
    [SerializeField] private Button confirmLeaveButton;
    [SerializeField] private Button confirmCancelButton;

    private static PauseMenuController instance;

    // Black screen shown while leaving, so the half-closed match is never
    // visible and nothing can be clicked twice. Built in code: no prefab change.
    private GameObject leavingCover;
    private Camera leavingCamera;
    private bool leaving;

    // 25 Nov: while the monitor is being lowered (closing) the menu already counts as closed.
    public static bool IsOpen => instance != null && instance.content != null && instance.content.activeSelf && !instance.closing;

    // ---- 25 Nov: "lift the monitor up" open / close animation + monitor frame ------------------
    // Settings and the frame drawing: Assets/Resources/UI/Styles/Monitor Frame (MonitorFrameStyle).
    private UiMonitorLift lift;
    private bool closing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateFromResources()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[PauseMenu] Resources/" + ResourcePath + " not found. Run Tools > Pre-Thesis > Scenes > Menu: Rebuild Settings + Pause Menu.");
            return;
        }

        GameObject go = Instantiate(prefab);
        go.name = "PauseMenu";
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        // Include inactive settings pages and confirmation buttons in existing prefabs.
        // Keep MainMenu's click sound, but silence hover in the in-game menu.
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (!button.TryGetComponent<UISoundEmitter>(out var emitter))
                emitter = button.gameObject.AddComponent<UISoundEmitter>();
            emitter.HoverEnabled = false;
        }

        resumeButton.onClick.AddListener(Close);
        settingsButton.onClick.AddListener(ShowSettings);
        leaveButton.onClick.AddListener(AskLeave);
        confirmLeaveButton.onClick.AddListener(Leave);
        confirmCancelButton.onClick.AddListener(() => confirmPopup.SetActive(false));
        settingsPanel.BackRequested += ShowPause;

        ApplyVcrLook(); // 25 Nov: before the monitor (its frame goes on top)

        Transform dim = content.transform.Find("Dim");
        lift = UiMonitorLift.On(pausePanel.transform as RectTransform, "Pause", dim != null ? dim.GetComponent<Graphic>() : null);
        UiMonitorLift.On(settingsPanel.transform as RectTransform, "Pause Settings"); // frame only

        content.SetActive(false);
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        GameplayInput.Blocked = false;
    }

    // A scene change (match start, leaving) always closes the menu.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (content.activeSelf) CloseWithoutCursor();

        // The main menu (or any scene with no session) finishes a leave.
        if (leaving && !NetworkServer.active && !NetworkClient.active)
        {
            leaving = false;
            if (leavingCover != null) leavingCover.SetActive(false);
            if (leavingCamera != null) leavingCamera.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame || leaving) return;

        // Esc while a Settings key waits for a new key only cancels that (20 Oct).
        if (SettingsPanel.IsRebinding) return;

        // The end-of-round results screen has its own buttons.
        if (MatchResultsUI.IsShowing) return;

        // Lobby panel (Tab) / shop: Esc closes those instead.
        if (OverlayPanels.BlocksEscape) return;

        if (!content.activeSelf || closing) // 25 Nov: Esc while it is going down = up again
        {
            // Only in a match: there must be a local player.
            if (PlayerHealth.LocalInstance != null || SpectatorSession.Active) Open();
            return;
        }

        // Step back one level.
        if (confirmPopup.activeSelf) confirmPopup.SetActive(false);
        else if (settingsPanel.gameObject.activeSelf) ShowPause();
        else Close();
    }

    // ---- Open / close --------------------------------------------------------

    private void Open()
    {
        EnsureEventSystem();
        closing = false;
        content.SetActive(true);
        ShowPause();
        if (lift != null) lift.PlayOpen(); // 25 Nov
        AudioManager.Instance?.PlayUI("UI_MenuOpen");

        GameplayInput.Blocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        if (closing) return;
        if (lift != null && content.activeSelf && pausePanel.activeSelf)
        {
            // 25 Nov: you can move again at once; the picture switches off and is lowered first.
            GameSettings.Save();
            confirmPopup.SetActive(false);
            GameplayInput.Blocked = false;
            closing = true;
            lift.PlayClose(() => { closing = false; content.SetActive(false); });
        }
        else CloseWithoutCursor();

        // Back to FPS look. Always lock rather than "restore": in the Unity
        // Editor, Esc itself already frees the cursor before this script runs,
        // so a restored state would leave the camera unable to turn.
        // (If you were using the popcorn maker's free cursor, press Tab again.)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void CloseWithoutCursor()
    {
        closing = false;
        if (lift != null) lift.Stop(); // 25 Nov: shut at once (scene change / leaving)
        GameSettings.Save();
        confirmPopup.SetActive(false);
        content.SetActive(false);
        GameplayInput.Blocked = false;
    }

    private void ShowPause()
    {
        pausePanel.SetActive(true);
        settingsPanel.gameObject.SetActive(false);
        confirmPopup.SetActive(false);
        leaveButtonText.text = IsHosting() ? "End match" : "Leave match";
    }

    private void ShowSettings()
    {
        pausePanel.SetActive(false);
        settingsPanel.gameObject.SetActive(true);
        // 25 Nov: the key list (with its scrollbar) is built the first time Settings opens.
        if (vcr != null && !settingsStyled)
        {
            settingsStyled = true;
            UiVcrRestyle.StyleScrollbars(settingsPanel.transform, vcr);
        }
    }

    // ---- 25 Nov: VCR look -----------------------------------------------------
    // The Esc menu (Pause, Settings, Leave confirmation) gets the same VCR look as the lobby screens.
    // Colours / font: Assets/Resources/UI/Styles/Pause Style (Inspector). Applied when the game
    // starts; the prefab itself is not changed. No asset = the old brown look.
    private const string VcrStyleName = "Pause Style";
    private CodeScreenStyle vcr;
    private bool settingsStyled;

    private void ApplyVcrLook()
    {
        if (Resources.Load<CodeScreenStyle>(CodeScreenStyle.Folder + VcrStyleName) == null) return;
        vcr = CodeScreenStyle.Load(VcrStyleName, null);

        var cards = new List<Image>();
        AddCard(cards, pausePanel.transform);
        AddCard(cards, settingsPanel.transform);
        AddCard(cards, confirmPopup.transform.Find("Confirm Card"));

        Transform apply = settingsPanel.transform.Find("Buttons/Apply Button");
        var primary = new List<Button> { resumeButton };
        if (apply != null && apply.TryGetComponent(out Button applyButton)) primary.Add(applyButton);
        var danger = new List<Button> { leaveButton, confirmLeaveButton };

        UiVcrRestyle.Apply(content.transform, vcr, cards, primary, danger);
    }

    private static void AddCard(List<Image> cards, Transform t)
    {
        if (t != null && t.TryGetComponent(out Image image)) cards.Add(image);
    }

    // ---- Leave ---------------------------------------------------------------

    private static bool IsHosting() => NetworkServer.active && NetworkClient.active;

    private void AskLeave()
    {
        confirmText.text = IsHosting()
            ? "You are the host.\nLeaving ends the match and closes the room for everyone."
            : "Leave the match and go back to the main menu?";
        if (MatchResultsUI.LeavingLosesItems())
            confirmText.text += "\n<color=#FF6B5A>You will lose the items you brought.</color>";
        confirmPopup.SetActive(true);
        confirmPopup.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Leave the match to the main menu, exactly like the menu's Leave button
    /// (used by the results screen). False if there is no pause menu.
    /// </summary>
    public static bool LeaveMatch()
    {
        if (instance == null) return false;
        instance.Leave();
        return true;
    }

    private void Leave()
    {
        if (leaving) return;
        leaving = true;

        // Leaving a running match on purpose = like dying: lose the items you brought (3 Oct, bug #1).
        MatchResultsUI.SettleLeftMatch();

        CloseWithoutCursor();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowLeavingCover();

        StartCoroutine(LeaveNextFrame());
    }

    // Leaving happens in three steps, each in its own frame:
    //   1. the click finishes (the black cover appears),
    //   2. the session stops (players and network objects are torn down),
    //   3. two frames later the main menu loads.
    // Mirror normally does 2 and 3 in the SAME frame. In the Unity Editor that
    // froze the whole Editor on host leave (builds were fine). Splitting them
    // fixed it.
    private IEnumerator LeaveNextFrame()
    {
        yield return null;

        RoHRoomManager room = RoHRoomManager.Instance;
        if (room != null && (NetworkClient.active || NetworkServer.active))
        {
            // 1) Tear down the session this frame. 2) Load the menu two frames
            // later ourselves (Mirror normally does both in the same frame).
            string menuScene = room.StopWithoutMenu();

            // Two frames with the black cover (and its camera) up, then load.
            yield return null;
            yield return null;
            if (string.IsNullOrWhiteSpace(menuScene)) SceneManager.LoadScene(0);
            else SceneManager.LoadScene(menuScene);
            yield break;
        }

        // Offline test (Play pressed inside a map scene): just load the menu.
        SceneManager.LoadScene(0);
    }

    private void ShowLeavingCover()
    {
        if (leavingCover == null)
        {
            leavingCover = new GameObject("Leaving Cover", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            leavingCover.transform.SetParent(transform, false);
            Stretch((RectTransform)leavingCover.transform); // fill the screen, not a 100x100 box

            Canvas canvas = leavingCover.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; // needed when nested under the menu's own canvas
            canvas.sortingOrder = 32000;   // above every other canvas

            var bg = new GameObject("Black", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(leavingCover.transform, false);
            Stretch((RectTransform)bg.transform);
            Image image = bg.GetComponent<Image>();
            image.sprite = null;          // plain rectangle: no rounded corners, no gaps
            image.color = Color.black;
            image.raycastTarget = true;   // swallows clicks while leaving

            var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(leavingCover.transform, false);
            Stretch((RectTransform)label.transform);
            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            text.text = "Leaving...";
            text.fontSize = 36;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.8f, 0.8f, 0.8f);
            text.raycastTarget = false;
        }

        leavingCover.SetActive(true);
        if (leavingCamera == null) leavingCamera = CreateLeavingCamera();
        leavingCamera.gameObject.SetActive(true);
    }

    // The player's camera is destroyed with the session, a few frames before
    // the menu loads. Without any camera the Editor shows "No cameras
    // rendering". This plain black camera fills that gap. It renders nothing
    // (culling mask 0), so it costs almost nothing.
    private Camera CreateLeavingCamera()
    {
        var go = new GameObject("Leaving Camera");
        go.transform.SetParent(transform, false);
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;
        cam.depth = -100f;
        return cam;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
