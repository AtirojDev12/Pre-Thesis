using UnityEngine;

/// <summary>
/// Per-machine player settings: name, volume, mouse sensitivity, screen.
///
/// Stored in PlayerPrefs on purpose, NOT in the encrypted save. These belong to
/// this PC (a laptop and a desktop want different resolutions), they are not
/// progress, and there is nothing in them worth protecting. SaveManager.Current
/// stays the single source of truth for game progress only.
///
/// Applied once at startup before the first scene, so volume and screen mode
/// are right even if the player never opens the Settings screen.
/// </summary>
public static class GameSettings
{
    private const string KeyName = "settings.playerName";
    private const string KeyVolume = "settings.masterVolume";
    private const string KeySensitivity = "settings.mouseSensitivity";
    private const string KeyFullscreen = "settings.fullscreen";
    private const string KeyResWidth = "settings.resWidth";
    private const string KeyResHeight = "settings.resHeight";

    public const float MinSensitivity = 0.1f;
    public const float MaxSensitivity = 3f;

    /// <summary>Shown to other players in the waiting lobby.</summary>
    public static string PlayerName
    {
        get
        {
            string saved = PlayerPrefs.GetString(KeyName, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved)) return saved;

            // First launch: give a readable default and keep it.
            string generated = "Player" + Random.Range(1000, 10000);
            PlayerPrefs.SetString(KeyName, generated);
            return generated;
        }
        set => PlayerPrefs.SetString(KeyName, RoHRoomPlayer.SanitizeName(value));
    }

    /// <summary>0..1, applied to AudioListener.volume.</summary>
    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KeyVolume, 1f);
        set
        {
            float v = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyVolume, v);
            AudioListener.volume = v;
        }
    }

    /// <summary>
    /// Multiplier on FirstPersonCamera.mouseSensitivity. 1 = the designer's value.
    /// A multiplier (not an absolute value) so tuning the camera prefab still works.
    /// </summary>
    public static float MouseSensitivityScale
    {
        get => PlayerPrefs.GetFloat(KeySensitivity, 1f);
        set => PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            ApplyScreen();
        }
    }

    public static void SetResolution(int width, int height)
    {
        PlayerPrefs.SetInt(KeyResWidth, width);
        PlayerPrefs.SetInt(KeyResHeight, height);
        ApplyScreen();
    }

    public static Vector2Int SavedResolution => new Vector2Int(
        PlayerPrefs.GetInt(KeyResWidth, Screen.currentResolution.width),
        PlayerPrefs.GetInt(KeyResHeight, Screen.currentResolution.height));

    /// <summary>Write to disk. PlayerPrefs otherwise only flushes on a clean quit.</summary>
    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStartup()
    {
        AudioListener.volume = MasterVolume;

#if !UNITY_EDITOR
        // In the Editor the Game view owns the resolution; forcing it is annoying.
        ApplyScreen();
#endif
    }

    private static void ApplyScreen()
    {
#if !UNITY_EDITOR
        Vector2Int res = SavedResolution;
        FullScreenMode mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(res.x, res.y, mode);
#endif
    }
}
