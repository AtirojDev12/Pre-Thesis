using UnityEngine;

/// <summary>Sound categories the player can turn up or down separately.</summary>
public enum SoundCategory
{
    Music = 0,    // background music / score
    Sfx = 1,      // footsteps, doors, task sounds, jumpscares
    Ambient = 2,  // room tone, wind, hum, distant noises
}

/// <summary>
/// Per-machine player settings: name, volumes, microphone, noise meter, mouse
/// sensitivity, brightness, screen, key bindings.
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
    private const string KeyWalkieTalk = "settings.bind.walkieTalk";
    private const string KeyWalkiePower = "settings.bind.walkiePower";
    private const string KeyMicDevice = "settings.micDevice";
    private const string KeyMicSensitivity = "settings.micSensitivity";
    private const string KeyNoiseReduction = "settings.mic.noiseReduction";
    private const string KeyNoiseGate = "settings.mic.noiseGate";
    private const string KeyNoiseGateDb = "settings.mic.noiseGateDb";
    private const string KeyMeterVertical = "settings.noiseMeter.vertical";
    private const string KeyMeterX = "settings.noiseMeter.x";
    private const string KeyMeterY = "settings.noiseMeter.y";

    /// <summary>Default key bindings (Input System control paths).</summary>
    public const string DefaultWalkieTalkBinding = "<Mouse>/leftButton";
    public const string DefaultWalkiePowerBinding = "<Mouse>/rightButton";

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

    // ---- Microphone & noise meter ------------------------------------------------

    /// <summary>Microphone to record from. Empty = Windows default.</summary>
    public static string MicrophoneDevice
    {
        get => PlayerPrefs.GetString(KeyMicDevice, string.Empty);
        set { PlayerPrefs.SetString(KeyMicDevice, value ?? string.Empty); NotifyChanged(); }
    }

    /// <summary>
    /// How sensitive the MIC bar of the noise meter is, 0..1 (0.5 = unchanged).
    /// Mics differ a lot (a quiet USB mic reads far lower than a headset), so
    /// the player can match the bar to their mic. Only the meter / ghost
    /// loudness uses this; the voice other players hear is not changed.
    /// </summary>
    public static float MicSensitivity
    {
        get => PlayerPrefs.GetFloat(KeyMicSensitivity, 0.5f);
        set { PlayerPrefs.SetFloat(KeyMicSensitivity, Mathf.Clamp01(value)); NotifyChanged(); }
    }

    /// <summary>Max boost / cut of the mic sensitivity slider, in dB.</summary>
    public const float MicSensitivityRangeDb = 12f;

    /// <summary>The sensitivity slider as a dB offset (-12 .. +12).</summary>
    public static float MicSensitivityDb => SensitivityToDb(MicSensitivity);
    public static float SensitivityToDb(float slider) => (Mathf.Clamp01(slider) - 0.5f) * 2f * MicSensitivityRangeDb;

    /// <summary>Noise meter bars standing (true) or lying (false).</summary>
    // ---- Mic clean-up (for cheap mics / mics without their own noise cancelling) ----

    /// <summary>Noise reduction: removes steady background noise (hiss, fan, hum). Default ON.</summary>
    public static bool NoiseReduction
    {
        get => PlayerPrefs.GetInt(KeyNoiseReduction, 1) == 1;
        set { PlayerPrefs.SetInt(KeyNoiseReduction, value ? 1 : 0); NotifyChanged(); }
    }

    /// <summary>Noise gate: the mic stays silent until you really speak (keyboard clicks stay out). Default ON.</summary>
    public static bool NoiseGate
    {
        get => PlayerPrefs.GetInt(KeyNoiseGate, 1) == 1;
        set { PlayerPrefs.SetInt(KeyNoiseGate, value ? 1 : 0); NotifyChanged(); }
    }

    public const float NoiseGateMinDb = -70f;
    public const float NoiseGateMaxDb = -20f;
    public const float DefaultNoiseGateDb = -45f;

    /// <summary>How loud (dBFS, RMS) the mic must be before the gate opens. Higher = blocks more.</summary>
    public static float NoiseGateThresholdDb
    {
        get => Mathf.Clamp(PlayerPrefs.GetFloat(KeyNoiseGateDb, DefaultNoiseGateDb), NoiseGateMinDb, NoiseGateMaxDb);
        set { PlayerPrefs.SetFloat(KeyNoiseGateDb, Mathf.Clamp(value, NoiseGateMinDb, NoiseGateMaxDb)); NotifyChanged(); }
    }

    public static bool NoiseMeterVertical
    {
        get => PlayerPrefs.GetInt(KeyMeterVertical, 1) == 1;
        set { PlayerPrefs.SetInt(KeyMeterVertical, value ? 1 : 0); NotifyChanged(); }
    }

    /// <summary>Where the player dragged the meter (centre, as a fraction of the screen). False = default top-right.</summary>
    public static bool TryGetNoiseMeterPosition(out float x, out float y)
    {
        x = PlayerPrefs.GetFloat(KeyMeterX, -1f);
        y = PlayerPrefs.GetFloat(KeyMeterY, -1f);
        return x >= 0f && y >= 0f;
    }

    public static void SetNoiseMeterPosition(float x, float y)
    {
        PlayerPrefs.SetFloat(KeyMeterX, Mathf.Clamp01(x));
        PlayerPrefs.SetFloat(KeyMeterY, Mathf.Clamp01(y));
        NotifyChanged();
    }

    public static void ResetNoiseMeterPosition()
    {
        PlayerPrefs.DeleteKey(KeyMeterX);
        PlayerPrefs.DeleteKey(KeyMeterY);
        PlayerPrefs.Save();
        NotifyChanged();
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

    // ---- Key bindings ------------------------------------------------------------
    // Stored as Input System control paths ("<Mouse>/leftButton", "/Keyboard/v").
    // Read them through BoundButton, not InputSystem directly.

    /// <summary>Hold to talk on the Walkie-Talkie (walkie in hand and switched on).</summary>
    public static string WalkieTalkBinding
    {
        get => PlayerPrefs.GetString(KeyWalkieTalk, DefaultWalkieTalkBinding);
        set { PlayerPrefs.SetString(KeyWalkieTalk, string.IsNullOrEmpty(value) ? DefaultWalkieTalkBinding : value); NotifyChanged(); }
    }

    /// <summary>Press to switch the Walkie-Talkie in your hand on / off.</summary>
    public static string WalkiePowerBinding
    {
        get => PlayerPrefs.GetString(KeyWalkiePower, DefaultWalkiePowerBinding);
        set { PlayerPrefs.SetString(KeyWalkiePower, string.IsNullOrEmpty(value) ? DefaultWalkiePowerBinding : value); NotifyChanged(); }
    }

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
