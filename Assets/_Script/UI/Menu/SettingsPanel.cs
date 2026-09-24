using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings: player name, master volume, mouse sensitivity, fullscreen and
/// resolution. Changes apply immediately; Back saves them to disk.
/// Values live in GameSettings (PlayerPrefs), not in the encrypted save.
/// </summary>
[DisallowMultipleComponent]
public class SettingsPanel : MonoBehaviour
{
    [SerializeField] private MainMenuController menu;

    [SerializeField] private TMP_InputField nameField;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TMP_Text volumeText;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text sensitivityText;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Button backButton;

    private readonly List<Vector2Int> resolutions = new List<Vector2Int>();

    private void Awake()
    {
        nameField.characterLimit = RoHRoomPlayer.MaxNameLength;
        nameField.onEndEdit.AddListener(OnNameEdited);

        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

        sensitivitySlider.minValue = GameSettings.MinSensitivity;
        sensitivitySlider.maxValue = GameSettings.MaxSensitivity;
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);

        fullscreenToggle.onValueChanged.AddListener(on => GameSettings.Fullscreen = on);
        resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);

        backButton.onClick.AddListener(Back);

        BuildResolutionList();
    }

    private void OnEnable()
    {
        nameField.SetTextWithoutNotify(GameSettings.PlayerName);
        volumeSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        sensitivitySlider.SetValueWithoutNotify(GameSettings.MouseSensitivityScale);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        SelectSavedResolution();
        RefreshLabels();
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

    private void OnVolumeChanged(float value)
    {
        GameSettings.MasterVolume = value;
        RefreshLabels();
    }

    private void OnSensitivityChanged(float value)
    {
        GameSettings.MouseSensitivityScale = value;
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        volumeText.text = $"Volume: {Mathf.RoundToInt(volumeSlider.value * 100f)}%";
        sensitivityText.text = $"Mouse sensitivity: {sensitivitySlider.value:0.0}x";
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
        menu.ShowMain();
    }
}
