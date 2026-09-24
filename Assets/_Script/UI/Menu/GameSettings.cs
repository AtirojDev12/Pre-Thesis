using UnityEngine;

/// <summary>Sound categories the player can turn up or down separately.</summary>
public enum SoundCategory
{
    Music = 0,    // background music / score
    Sfx = 1,      // footsteps, doors, task sounds, jumpscares
    Ambient = 2,  // room tone, wind, hum, distant noises
}

/// <summary>
/// Per-machine player settings: name, volumes, mouse sensitivity, brightness,
/// screen.
///
/// Stored in PlayerPrefs on purpose, NOT in the encrypted save. These belong to
/// this PC (a laptop and a desktop want different resolutions), they are not
/// progress, and there is nothing in them worth protecting. SaveManager.Current
/// stays the single source of truth for game progress only.
///
/// Anything that depends on a setting (SoundCategoryVolume, BrightnessApplier)
/// listens to <see cref="Changed"/>, so moving a slider updates the game live.
/// </summary>
public static class GameSettings
{
    private const string KeyName = "settings.playerName";
    private const string KeyVolume = "settings.masterVolume";
    private const string KeyMusic = "settings.musicVolume";
    private const string KeySfx = "settings.sfxVolume";
    private const string KeyAmbient = "settings.ambientVolume";
    private const string KeySensitivity = "settings.mouseSensitivity";
    private const string KeyBrightness = "settings.brightness";
    private const string KeyFullscreen = "settings.fullscreen";
    private const string KeyResWidth = "settings.resWidth";
    private const string KeyResHeight = "settings.resHeight";

    public const float MinSensitivity = 0.1f;
    public const float MaxSensitivity = 3f;

    /// <summary>Brightness is stored 0..1 (0.5 = unchanged) and mapped to this exposure range in EV.</summary>
    public const float MinExposure = -1.5f;
    public const float MaxExposure = 1.5f;

    /// <summary>Raised whenever any setting changes. Listeners re-read what they need.</summary>
    public static event System.Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Changed = null;

    private static void NotifyChanged() => Changed?.Invoke();

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

    // ---- Sound ---------------------------------------------------------------

    /// <summary>0..1, applied to AudioListener.volume — scales every sound in the game.</summary>
    public static float MasterVolume
    {
        get => PlayerPrefs.GetFloat(KeyVolume, 1f);
        set
        {
            float v = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyVolume, v);
            AudioListener.volume = v;
            NotifyChanged();
        }
    }

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(KeyMusic, 1f);
        set { PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value)); NotifyChanged(); }
    }

    public static float SfxVolume
    {
        get => PlayerPrefs.GetFloat(KeySfx, 1f);
        set { PlayerPrefs.SetFloat(KeySfx, Mathf.Clamp01(value)); NotifyChanged(); }
    }

    public static float AmbientVolume
    {
        get => PlayerPrefs.GetFloat(KeyAmbient, 1f);
        set { PlayerPrefs.SetFloat(KeyAmbient, Mathf.Clamp01(value)); NotifyChanged(); }
    }

    /// <summary>The category slider value (0..1). Master is applied separately by AudioListener.</summary>
    public static float GetVolume(SoundCategory category)
    {
        switch (category)
        {
            case SoundCategory.Music: return MusicVolume;
            case SoundCategory.Ambient: return AmbientVolume;
            default: return SfxVolume;
        }
    }

    // ---- Controls / picture --------------------------------------------------

    /// <summary>
    /// Multiplier on FirstPersonCamera.mouseSensitivity. 1 = the designer's value.
    /// A multiplier (not an absolute value) so tuning the camera prefab still works.
    /// </summary>
    public static float MouseSensitivityScale
    {
        get => PlayerPrefs.GetFloat(KeySensitivity, 1f);
        set { PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity)); NotifyChanged(); }
    }

    /// <summary>0..1 slider value. 0.5 = as the artists lit it.</summary>
    public static float Brightness
    {
        get => PlayerPrefs.GetFloat(KeyBrightness, 0.5f);
        set { PlayerPrefs.SetFloat(KeyBrightness, Mathf.Clamp01(value)); NotifyChanged(); }
    }

    /// <summary>Brightness converted to post-exposure (EV) for the camera.</summary>
    public static float BrightnessExposure => Mathf.Lerp(MinExposure, MaxExposure, Brightness);

    // ---- Screen ----------------------------------------------------------------

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreen ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(KeyFullscreen, value ? 1 : 0);
            ApplyScreen();
            NotifyChanged();
        }
    }

    public static void SetResolution(int width, int height)
    {
        PlayerPrefs.SetInt(KeyResWidth, width);
        PlayerPrefs.SetInt(KeyResHeight, height);
        ApplyScreen();
        NotifyChanged();
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
