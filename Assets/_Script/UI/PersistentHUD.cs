using UnityEngine;

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
    private static void ResetStatics() => _instance = null;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A second copy arrived, almost always because the prefab is also
            // placed in the scene we just loaded. Keep the original -- it holds
            // the live bindings -- and drop the newcomer.
            Destroy(gameObject);
            return;
        }

        _instance = this;

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
}
