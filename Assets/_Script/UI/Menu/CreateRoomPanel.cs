using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Create Room panel: difficulty, player limit (1-6), public/private and a
/// password. Builds a RoomConfig and hands it to the main menu, which passes it
/// to LobbyController.CreateRoom. RoomConfig.Validate is the final judge; the
/// checks here only give a quicker, friendlier message.
/// </summary>
[DisallowMultipleComponent]
public class CreateRoomPanel : MonoBehaviour
{
    public const int PasswordMaxLength = 24;

    [SerializeField] private MainMenuController menu;

    [SerializeField] private TMP_Text mapText;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private Slider playerLimitSlider;
    [SerializeField] private TMP_Text playerLimitText;
    [SerializeField] private Toggle privateToggle;
    [SerializeField] private TMP_InputField passwordField;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private Button createButton;
    [SerializeField] private Button backButton;

    private void Awake()
    {
        difficultyDropdown.ClearOptions();
        difficultyDropdown.AddOptions(new System.Collections.Generic.List<string>(RoomDisplay.DifficultyOptions));
        difficultyDropdown.SetValueWithoutNotify((int)DifficultyLevel.Normal);

        playerLimitSlider.minValue = RoomConfig.MinPlayers;
        playerLimitSlider.maxValue = RoomConfig.MaxPlayers;
        playerLimitSlider.wholeNumbers = true;
        playerLimitSlider.SetValueWithoutNotify(RoomConfig.MaxPlayers);
        playerLimitSlider.onValueChanged.AddListener(_ => RefreshLabels());

        passwordField.contentType = TMP_InputField.ContentType.Password;
        passwordField.characterLimit = PasswordMaxLength;
        passwordField.onValueChanged.AddListener(_ => errorText.text = string.Empty);

        privateToggle.SetIsOnWithoutNotify(false);
        privateToggle.onValueChanged.AddListener(_ => RefreshLabels());

        createButton.onClick.AddListener(Create);
        backButton.onClick.AddListener(() => menu.ShowMain());

        mapText.text = "Map: " + RoomDisplay.MapName(RoomConfig.DemoMapID);
    }

    private void OnEnable()
    {
        errorText.text = string.Empty;
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        int limit = Mathf.RoundToInt(playerLimitSlider.value);
        playerLimitText.text = limit == 1 ? "Players: 1 (solo)" : $"Players: {limit}";

        bool isPrivate = privateToggle.isOn;
        passwordField.interactable = isPrivate;
        if (!isPrivate) passwordField.SetTextWithoutNotify(string.Empty);
    }

    private void Create()
    {
        var config = new RoomConfig
        {
            mapID = RoomConfig.DemoMapID,
            difficulty = (DifficultyLevel)Mathf.Clamp(difficultyDropdown.value, 0, RoomDisplay.DifficultyOptions.Length - 1),
            playerLimit = Mathf.RoundToInt(playerLimitSlider.value),
            isPrivate = privateToggle.isOn,
            password = privateToggle.isOn ? passwordField.text.Trim() : string.Empty,
        };

        if (!config.Validate(out string error))
        {
            errorText.text = config.isPrivate ? "Private rooms need a password." : error;
            return;
        }

        errorText.text = string.Empty;
        menu.CreateRoom(config);
    }
}
