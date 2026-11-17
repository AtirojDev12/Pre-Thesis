using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps one gameplay HUD alive across scene loads (MainMenu -> map -> back to
/// lobby), and makes sure there is never more than one of it.
///
/// Put this on the root of the HUD prefab, then drop that prefab into every
/// scene. Whichever scene you press Play in, the first copy survives and every
/// later copy deletes itself -- so testing straight from a map scene works just
/// as well as starting from the menu, with no duplicate bars stacking up.
///
/// The HUD does not need to be hidden manually in the menu: HealthBarUI binds
/// to PlayerHealth.LocalInstance and hides itself whenever no local player
/// exists, which is exactly the case in the lobby and between scenes.
/// </summary>
[DisallowMultipleComponent]
public class PersistentHUD : MonoBehaviour
{
    private static PersistentHUD _instance;

    [Tooltip("Uncheck to make this a normal scene-local HUD instead of one that survives scene loads.")]
    [SerializeField] private bool surviveSceneLoads = true;

    public static PersistentHUD Instance => _instance;

    // Statics survive between Play sessions when Unity 6's domain reload is
    // turned off, which would otherwise leave a reference to a destroyed HUD
    // and cause the real one to delete itself on the next run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        hiddenRequests = 0;
    }

    // ---- Hiding while a menu is open -----------------------------------------
    //
    // The main menu and the waiting lobby are full-screen menus; the crosshair
    // and bars should not draw over them. Each menu calls PushHidden when it
    // opens and PopHidden when it closes. A counter (not a bool) so the order
    // two screens open and close in during a scene change can never leave the
    // HUD stuck hidden or shown.

    private static int hiddenRequests;

    public static void PushHidden()
    {
        hiddenRequests++;
        ApplyVisibility();
    }

    public static void PopHidden()
    {
        hiddenRequests = Mathf.Max(0, hiddenRequests - 1);
        ApplyVisibility();
    }

    private static void ApplyVisibility()
    {
        if (_instance == null) return;

        // CanvasGroup rather than Canvas.enabled: it also covers nested canvases.
        CanvasGroup group = _instance.GetComponent<CanvasGroup>();
        if (group == null) group = _instance.gameObject.AddComponent<CanvasGroup>();

        group.alpha = hiddenRequests > 0 ? 0f : 1f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void Awake()
    {
        // Health, stamina, prompts and the crosshair only display information.
        // Their overlay graphics must not eat clicks intended for world-space controls.
        // 16 Nov: not the screens kept here for the UI Hub (UiHubScreen): their copies have real buttons.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            if (graphic.GetComponentInParent<UiHubScreen>(true) == null) graphic.raycastTarget = false;

        if (_instance != null && _instance != this)
        {
            // A second copy arrived, almost always because the prefab is also
            // placed in the scene we just loaded. Keep the original -- it holds
            // the live bindings -- and drop the newcomer.
            Destroy(gameObject);
            return;
        }

        _instance = this;
        ApplyVisibility();

        // 16 Nov (Mr.k): the status bars and the hotbar are now parts of this prefab. The game
        // switches them on when needed, so start with all of them off (whatever state the
        // designer saved them in while editing the prefab).
        foreach (HudBar bar in GetComponentsInChildren<HudBar>(true)) bar.Show(false);
        foreach (HotbarView hotbar in GetComponentsInChildren<HotbarView>(true)) hotbar.Show(false);
        // Editable versions of code-built screens: only copies of them are shown (UiLayoutRuntime).
        foreach (UiHubScreen screen in GetComponentsInChildren<UiHubScreen>(true))
            if (screen.gameObject.activeSelf) screen.gameObject.SetActive(false);

        if (!surviveSceneLoads) return;

        // DontDestroyOnLoad only works on root objects; if someone nests this
        // under a scene object, Unity ignores the call and logs a warning.
        if (transform.parent != null) transform.SetParent(null, true);

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (!surviveSceneLoads) return;

        // Screen Space - Camera canvases point at a camera that gets destroyed
        // with the old scene, and the HUD then silently stops rendering. Overlay
        // needs no camera at all, which is what a persistent HUD wants.
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning(
                $"[PersistentHUD] Canvas '{name}' is set to {canvas.renderMode}. A HUD that survives scene loads should " +
                "use Screen Space - Overlay, otherwise it loses its camera on the next scene load and stops drawing.", this);
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

#if UNITY_EDITOR
    // 17 Nov 2026 (Mr.k): there is ONE GAME HUD in the project, in the Lobby scene. It follows the
    // players into the map by itself. Pressing Play straight in a map scene (testing) would have no
    // HUD, so in the Unity Editor only the prefab is added for that run. Builds always start from
    // the menu, so they never need this.
    private const string EditorPrefabPath = "Assets/Prefab/GAME HUD.prefab";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] // after the scene's Awake, before any Start
    private static void EditorAddForDirectPlay()
    {
        if (_instance != null || FindAnyObjectByType<PersistentHUD>(FindObjectsInactive.Include) != null) return;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene == "MainMenu" || scene == "Lobby") return; // the normal way in: the Lobby brings the HUD
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefabPath);
        if (prefab == null) return;
        Instantiate(prefab).name = prefab.name;
        Debug.Log($"[GAME HUD] \"{scene}\" has no GAME HUD (the only copy is in the Lobby scene). One was added for this test run. Editor only.");
    }
#endif
}
