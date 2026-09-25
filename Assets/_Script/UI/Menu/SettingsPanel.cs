using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

/// <summary>
/// Settings screen, used in two places: the main menu and the Esc pause menu.
///
///   General: player name, mouse sensitivity, brightness, fullscreen, resolution
///   Sound:   master, music, SFX, ambient
///   Controls: Walkie-Talkie talk key + on/off key (rebindable, any key or mouse button)
///
/// Nothing changes until APPLY is pressed. Moving a slider only edits a
/// pending copy (the labels show the pending value). Apply writes every value
/// to GameSettings, which raises Changed so the game reacts, then saves to
/// disk. Back closes without applying: pending edits are thrown away and the
/// panel shows the saved values next time it opens.
///
/// Values live in GameSettings (PlayerPrefs), not in the encrypted save.
/// </summary>
[DisallowMultipleComponent]
public class SettingsPanel : MonoBehaviour
{
    [Tooltip("Set only in the main menu. Leave empty in the pause menu.")]
    [SerializeField] private MainMenuController menu;

    [Header("General")]
    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityText;
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private TMP_Text brightnessText;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;

    [Header("Sound")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeText;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private TMP_Text musicText;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text sfxText;
    [SerializeField] private Slider ambientSlider;
    [SerializeField] private TMP_Text ambientText;

    [Header("Controls (optional: rebuilt by Tools > Pre-Thesis > Rebuild Settings + Pause Menu)")]
    [SerializeField] private Button walkieTalkButton;
    [SerializeField] private Button walkiePowerButton;

    [Header("Buttons")]
    [SerializeField] private Button applyButton;
    [SerializeField] private Button backButton;

    /// <summary>Raised when Back is pressed. The pause menu listens to this.</summary>
    public event System.Action BackRequested;

    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    // Snapshot of the saved values when the panel opened (or after Apply).
    // Used to grey out Apply when nothing has changed.
    private string savedName;
    private float savedSensitivity, savedBrightness, savedMaster, savedMusic, savedSfx, savedAmbient;
    private bool savedFullscreen;
    private int savedResolutionIndex;
    private string savedWalkieTalk, savedWalkiePower;

    // Pending key bindings (control paths), written on Apply like everything else.
    private string pendingWalkieTalk, pendingWalkiePower;

    // Rebinding: which binding is waiting for a key (null = none).
    private enum RebindTarget { None, WalkieTalk, WalkiePower }
    private RebindTarget listening = RebindTarget.None;
    private System.IDisposable rebindListener;
    private float ignoreClicksUntil;

    private void Awake()
    {
        nameField.characterLimit = RoHRoomPlayer.MaxNameLength;
        nameField.onValueChanged.AddListener(_ => RefreshApplyButton());

        SetupSlider(sensitivitySlider, GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        SetupSlider(brightnessSlider, 0f, 1f);
        SetupSlider(volumeSlider, 0f, 1f);
        SetupSlider(musicSlider, 0f, 1f);
        SetupSlider(sfxSlider, 0f, 1f);
        SetupSlider(ambientSlider, 0f, 1f);

        fullscreenToggle.onValueChanged.AddListener(_ => RefreshApplyButton());
        resolutionDropdown.onValueChanged.AddListener(_ => RefreshApplyButton());

        if (applyButton != null) applyButton.onClick.AddListener(Apply);
        else Debug.LogWarning("[SettingsPanel] No Apply button assigned. Run Tools > Pre-Thesis > Rebuild Settings + Pause Menu.", this);

        backButton.onClick.AddListener(Back);

        if (walkieTalkButton != null) walkieTalkButton.onClick.AddListener(() => StartRebind(RebindTarget.WalkieTalk));
        if (walkiePowerButton != null) walkiePowerButton.onClick.AddListener(() => StartRebind(RebindTarget.WalkiePower));

        BuildResolutionList();
    }

    private void SetupSlider(Slider slider, float min, float max)
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(_ => { RefreshLabels(); RefreshApplyButton(); });
    }

    // Every time the panel opens it shows the SAVED values, so edits that were
    // never applied are gone.
    private void OnEnable() => LoadSavedValues();

    private void OnDisable() => StopRebind();

    private void LoadSavedValues()
    {
        savedName = GameSettings.PlayerName;
        savedSensitivity = GameSettings.MouseSensitivityScale;
        savedBrightness = GameSettings.Brightness;
        savedMaster = GameSettings.MasterVolume;
        savedMusic = GameSettings.MusicVolume;
        savedSfx = GameSettings.SfxVolume;
        savedAmbient = GameSettings.AmbientVolume;
        savedFullscreen = GameSettings.Fullscreen;
        savedResolutionIndex = Mathf.Max(0, resolutions.IndexOf(GameSettings.SavedResolution));
        savedWalkieTalk = pendingWalkieTalk = GameSettings.WalkieTalkBinding;
        savedWalkiePower = pendingWalkiePower = GameSettings.WalkiePowerBinding;
        StopRebind();

        nameField.SetTextWithoutNotify(savedName);
        SetSilently(sensitivitySlider, savedSensitivity);
        SetSilently(brightnessSlider, savedBrightness);
        SetSilently(volumeSlider, savedMaster);
        SetSilently(musicSlider, savedMusic);
        SetSilently(sfxSlider, savedSfx);
        SetSilently(ambientSlider, savedAmbient);
        fullscreenToggle.SetIsOnWithoutNotify(savedFullscreen);
        resolutionDropdown.SetValueWithoutNotify(savedResolutionIndex);

        RefreshLabels();
        RefreshApplyButton();
    }

    private static void SetSilently(Slider slider, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
    }

    // ---- Apply / Back --------------------------------------------------------

    private void Apply()
    {
        // Empty names are refused: keep the old one.
        string clean = RoHRoomPlayer.SanitizeName(nameField.text);
        if (!string.IsNullOrEmpty(clean)) GameSettings.PlayerName = clean;

        if (sensitivitySlider != null) GameSettings.MouseSensitivityScale = sensitivitySlider.value;
        if (brightnessSlider != null) GameSettings.Brightness = brightnessSlider.value;
        if (volumeSlider != null) GameSettings.MasterVolume = volumeSlider.value;
        if (musicSlider != null) GameSettings.MusicVolume = musicSlider.value;
        if (sfxSlider != null) GameSettings.SfxVolume = sfxSlider.value;
        if (ambientSlider != null) GameSettings.AmbientVolume = ambientSlider.value;

        // Screen changes are the slow ones: only touch them if they changed.
        if (fullscreenToggle.isOn != savedFullscreen) GameSettings.Fullscreen = fullscreenToggle.isOn;

        int index = resolutionDropdown.value;
        if (index != savedResolutionIndex && index >= 0 && index < resolutions.Count)
            GameSettings.SetResolution(resolutions[index].x, resolutions[index].y);

        if (pendingWalkieTalk != savedWalkieTalk) GameSettings.WalkieTalkBinding = pendingWalkieTalk;
        if (pendingWalkiePower != savedWalkiePower) GameSettings.WalkiePowerBinding = pendingWalkiePower;

        GameSettings.Save();

        // The applied values are the new "saved" ones (also puts a refused
        // empty name back in the field).
        LoadSavedValues();
    }

    private void Back()
    {
        // Not applied = not kept. OnEnable reloads the saved values next time.
        BackRequested?.Invoke();
        if (menu != null) menu.ShowMain();
    }

    private bool HasPendingChanges()
    {
        if (nameField.text != savedName) return true;
        if (Changed(sensitivitySlider, savedSensitivity)) return true;
        if (Changed(brightnessSlider, savedBrightness)) return true;
        if (Changed(volumeSlider, savedMaster)) return true;
        if (Changed(musicSlider, savedMusic)) return true;
        if (Changed(sfxSlider, savedSfx)) return true;
        if (Changed(ambientSlider, savedAmbient)) return true;
        if (fullscreenToggle.isOn != savedFullscreen) return true;
        if (pendingWalkieTalk != savedWalkieTalk || pendingWalkiePower != savedWalkiePower) return true;
        return resolutionDropdown.value != savedResolutionIndex;
    }

    private static bool Changed(Slider slider, float saved) =>
        slider != null && !Mathf.Approximately(slider.value, saved);

    private void RefreshApplyButton()
    {
        if (applyButton != null) applyButton.interactable = HasPendingChanges();
    }

    // ---- Labels ----------------------------------------------------------------

    private void RefreshLabels()
    {
        SetPercent(volumeText, "Master volume", volumeSlider);
        SetPercent(musicText, "Music", musicSlider);
        SetPercent(sfxText, "Sound effects", sfxSlider);
        SetPercent(ambientText, "Ambient", ambientSlider);

        if (sensitivityText != null && sensitivitySlider != null)
            sensitivityText.text = $"Mouse sensitivity: {sensitivitySlider.value:0.0}x";

        SetBindingLabel(walkieTalkButton, "Walkie talk (hold)", pendingWalkieTalk, listening == RebindTarget.WalkieTalk);
        SetBindingLabel(walkiePowerButton, "Walkie on / off", pendingWalkiePower, listening == RebindTarget.WalkiePower);

        if (brightnessText != null && brightnessSlider != null)
        {
            // Middle = 0 (as lit). Shown as -100 .. +100 so "0" means default.
            int offset = Mathf.RoundToInt((brightnessSlider.value - 0.5f) * 200f);
            brightnessText.text = offset == 0 ? "Brightness: default" : $"Brightness: {offset:+0;-0}";
        }
    }

    private static void SetPercent(TMP_Text label, string name, Slider slider)
    {
        if (label == null || slider == null) return;
        label.text = $"{name}: {Mathf.RoundToInt(slider.value * 100f)}%";
    }

    // ---- Key rebinding ---------------------------------------------------------
    // Click a binding button, then press any key or mouse button (Mouse0..4).
    // Esc cancels. The new key is only pending until Apply, like every setting.

    private void StartRebind(RebindTarget target)
    {
        // The mouse click that picked a new binding also "clicks" this button when
        // it is released. Ignore that one so it does not start listening again.
        if (Time.unscaledTime < ignoreClicksUntil) return;

        StopRebind();
        listening = target;
        RefreshLabels();

        rebindListener = InputSystem.onAnyButtonPress.CallOnce(control =>
        {
            rebindListener = null;
            RebindTarget finished = listening;
            listening = RebindTarget.None;
            ignoreClicksUntil = Time.unscaledTime + 0.35f;

            bool cancelled = control == null || control.path.EndsWith("/escape");
            if (!cancelled)
            {
                if (finished == RebindTarget.WalkieTalk) pendingWalkieTalk = control.path;
                else if (finished == RebindTarget.WalkiePower) pendingWalkiePower = control.path;
            }

            RefreshLabels();
            RefreshApplyButton();
        });
    }

    private void StopRebind()
    {
        rebindListener?.Dispose();
        rebindListener = null;
        if (listening == RebindTarget.None) return;
        listening = RebindTarget.None;
        RefreshLabels();
    }

    private static void SetBindingLabel(Button button, string action, string path, bool isListening)
    {
        if (button == null) return;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        label.text = isListening
            ? action + ": press a key or mouse button... (Esc = cancel)"
            : action + ": " + BoundButton.DisplayName(path);
    }

    // ---- Resolution list -------------------------------------------------------

    private void BuildResolutionList()
    {
        resolutions.Clear();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!resolutions.Contains(size)) resolutions.Add(size);
        }

        // Biggest first; the list can be empty on some platforms/Editor setups.
        resolutions.Sort((a, b) => (b.x * b.y).CompareTo(a.x * a.y));
        if (resolutions.Count == 0) resolutions.Add(new Vector2Int(Screen.width, Screen.height));

        var labels = new List<string>(resolutions.Count);
        foreach (Vector2Int size in resolutions) labels.Add($"{size.x} x {size.y}");

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
    }
}
