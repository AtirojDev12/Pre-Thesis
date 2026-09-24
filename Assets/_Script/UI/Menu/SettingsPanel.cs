using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings screen, used in two places: the main menu and the Esc pause menu.
///
///   General: player name, mouse sensitivity, brightness, fullscreen, resolution
///   Sound:   master, music, SFX, ambient
///
/// Every change applies immediately (GameSettings raises Changed and the game
/// reacts). Back saves to disk and raises BackRequested; in the main menu it
/// also returns to the main panel.
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

    [SerializeField] private Button backButton;

    /// <summary>Raised when Back is pressed (after saving). The pause menu listens to this.</summary>
    public event System.Action BackRequested;

    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    private void Awake()
    {
        nameField.characterLimit = RoHRoomPlayer.MaxNameLength;
        nameField.onEndEdit.AddListener(OnNameEdited);

        SetupSlider(sensitivitySlider, GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
            v => { GameSettings.MouseSensitivityScale = v; RefreshLabels(); });
        SetupSlider(brightnessSlider, 0f, 1f, v => { GameSettings.Brightness = v; RefreshLabels(); });

        SetupSlider(volumeSlider, 0f, 1f, v => { GameSettings.MasterVolume = v; RefreshLabels(); });
        SetupSlider(musicSlider, 0f, 1f, v => { GameSettings.MusicVolume = v; RefreshLabels(); });
        SetupSlider(sfxSlider, 0f, 1f, v => { GameSettings.SfxVolume = v; RefreshLabels(); });
        SetupSlider(ambientSlider, 0f, 1f, v => { GameSettings.AmbientVolume = v; RefreshLabels(); });

        fullscreenToggle.onValueChanged.AddListener(on => GameSettings.Fullscreen = on);
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        backButton.onClick.AddListener(Back);

        BuildResolutionList();
    }

    private static void SetupSlider(Slider slider, float min, float max, UnityEngine.Events.UnityAction<float> onChange)
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(onChange);
    }

    private void OnEnable()
    {
        nameField.SetTextWithoutNotify(GameSettings.PlayerName);
        SetSilently(sensitivitySlider, GameSettings.MouseSensitivityScale);
        SetSilently(brightnessSlider, GameSettings.Brightness);
        SetSilently(volumeSlider, GameSettings.MasterVolume);
        SetSilently(musicSlider, GameSettings.MusicVolume);
        SetSilently(sfxSlider, GameSettings.SfxVolume);
        SetSilently(ambientSlider, GameSettings.AmbientVolume);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        SelectSavedResolution();
        RefreshLabels();
    }

    private static void SetSilently(Slider slider, float value)
    {
        if (slider != null) slider.SetValueWithoutNotify(value);
    }

    private void OnNameEdited(string value)
    {
        string clean = RoHRoomPlayer.SanitizeName(value);
        if (string.IsNullOrEmpty(clean))
        {
            // Empty names are refused: put the old one back.
            nameField.SetTextWithoutNotify(GameSettings.PlayerName);
            return;
        }

        GameSettings.PlayerName = clean;
        nameField.SetTextWithoutNotify(clean);
    }

    private void RefreshLabels()
    {
        SetPercent(volumeText, "Master volume", volumeSlider);
        SetPercent(musicText, "Music", musicSlider);
        SetPercent(sfxText, "Sound effects", sfxSlider);
        SetPercent(ambientText, "Ambient", ambientSlider);

        if (sensitivityText != null && sensitivitySlider != null)
            sensitivityText.text = $"Mouse sensitivity: {sensitivitySlider.value:0.0}x";

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

    private void SelectSavedResolution()
    {
        Vector2Int saved = GameSettings.SavedResolution;
        int index = resolutions.IndexOf(saved);
        resolutionDropdown.SetValueWithoutNotify(index >= 0 ? index : 0);
    }

    private void OnResolutionChanged(int index)
    {
        if (index < 0 || index >= resolutions.Count) return;
        GameSettings.SetResolution(resolutions[index].x, resolutions[index].y);
    }

    private void Back()
    {
        // Commit a name that was typed but never "submitted".
        OnNameEdited(nameField.text);
        GameSettings.Save();

        BackRequested?.Invoke();
        if (menu != null) menu.ShowMain();
    }
}
