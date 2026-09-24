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

    public static bool IsOpen => instance != null && instance.content != null && instance.content.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateFromResources()
    {
        if (instance != null) return;

        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[PauseMenu] Resources/" + ResourcePath + " not found. Run Tools > Pre-Thesis > Rebuild Settings + Pause Menu.");
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

        resumeButton.onClick.AddListener(Close);
        settingsButton.onClick.AddListener(ShowSettings);
        leaveButton.onClick.AddListener(AskLeave);
        confirmLeaveButton.onClick.AddListener(Leave);
        confirmCancelButton.onClick.AddListener(() => confirmPopup.SetActive(false));
        settingsPanel.BackRequested += ShowPause;

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
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        if (!content.activeSelf)
        {
            // Only in a match: there must be a local player.
            if (PlayerHealth.LocalInstance != null) Open();
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
        content.SetActive(true);
        ShowPause();

        GameplayInput.Blocked = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        CloseWithoutCursor();

        // Back to FPS look. Always lock rather than "restore": in the Unity
        // Editor, Esc itself already frees the cursor before this script runs,
        // so a restored state would leave the camera unable to turn.
        // (If you were using the popcorn maker's free cursor, press Tab again.)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void CloseWithoutCursor()
    {
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
    }

    // ---- Leave ---------------------------------------------------------------

    private static bool IsHosting() => NetworkServer.active && NetworkClient.active;

    private void AskLeave()
    {
        confirmText.text = IsHosting()
            ? "You are the host.\nLeaving ends the match and closes the room for everyone."
            : "Leave the match and go back to the main menu?";
        confirmPopup.SetActive(true);
        confirmPopup.transform.SetAsLastSibling();
    }

    private void Leave()
    {
        CloseWithoutCursor();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RoHRoomManager room = RoHRoomManager.Instance;
        if (room != null && (NetworkClient.active || NetworkServer.active))
        {
            // Mirror loads the offline scene (MainMenu) by itself.
            room.LeaveSession();
            return;
        }

        // Offline test (Play pressed inside a map scene): just load the menu.
        SceneManager.LoadScene(0);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
