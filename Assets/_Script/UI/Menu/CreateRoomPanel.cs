using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Create Room panel: map, difficulty, player limit (1-6), public/private and a
/// password. Builds a RoomConfig and hands it to the main menu, which passes it
/// to LobbyController.CreateRoom. RoomConfig.Validate is the final judge; the
/// checks here only give a quicker, friendlier message.
///
/// 15 Nov 2026 (Mr.k):
///   - Hard and 13 Rules are LOCKED (RoomLocks): shown greyed with "(Locked)", cannot be picked.
///   - Map selection (MapCatalog): Cinema + "The Forest". Every map except Cinema is locked.
///   - The map list is the "Map Dropdown" object. A menu made before 15 Nov has none: a copy of
///     the difficulty dropdown is made at runtime, so nothing has to be rebuilt. To get a real
///     object you can restyle: Tools > Pre-Thesis > Scenes > Menu: Add Map Dropdown (keeps edits).
/// </summary>
[DisallowMultipleComponent]
public class CreateRoomPanel : MonoBehaviour
{
    public const int PasswordMaxLength = 24;

    [SerializeField] private MainMenuController menu;
    [SerializeField] private TMP_Text mapText;
    [Tooltip("15 Nov. List of maps (locked ones are greyed). Empty = made at runtime as a copy of the difficulty dropdown.")]
    [SerializeField] private TMP_Dropdown mapDropdown;
    [Tooltip("Optional (added 1 Oct). Room name shown and searchable in the Room Browser.")]
    [SerializeField] private TMP_InputField roomNameField;
    [SerializeField] private TMP_Dropdown difficultyDropdown;
    [SerializeField] private Slider playerLimitSlider;
    [SerializeField] private TMP_Text playerLimitText;
    [SerializeField] private Toggle privateToggle;
    [SerializeField] private TMP_InputField passwordField;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private Button createButton;
    [SerializeField] private Button backButton;

    private int difficultyChoice = (int)DifficultyLevel.Normal; // last allowed pick
    private int mapChoice;                                      // last allowed pick
    private Transform lockedDifficultyList, lockedMapList;      // open lists already greyed

    private void Awake()
    {
        EnsureMapDropdown(); // before any listener is added: the copy must start clean

        BuildDifficultyOptions();
        difficultyDropdown.onValueChanged.AddListener(OnDifficultyPicked);

        if (mapDropdown != null)
        {
            var maps = new List<string>(MapCatalog.All.Count);
            mapChoice = 0;
            for (int i = 0; i < MapCatalog.All.Count; i++)
            {
                MapCatalog.MapInfo info = MapCatalog.All[i];
                maps.Add(info.displayName + (info.locked ? RoomLocks.LockedSuffix : ""));
                if (info.id == RoomConfig.DemoMapID) mapChoice = i;
            }
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(maps);
            mapDropdown.SetValueWithoutNotify(mapChoice);
            mapDropdown.onValueChanged.AddListener(OnMapPicked);
            mapText.text = "Map";
        }
        else mapText.text = "Map: " + RoomDisplay.MapName(RoomConfig.DemoMapID);

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

        if (roomNameField != null) roomNameField.characterLimit = RoomConfig.MaxRoomNameLength;
    }

    /// <summary>
    /// A menu built before 15 Nov has no map list: copy the difficulty dropdown (same look)
    /// right under the "Map" label and make the card taller by one row.
    /// </summary>
    private void EnsureMapDropdown()
    {
        if (mapDropdown != null || difficultyDropdown == null || mapText == null) return;
        Transform parent = difficultyDropdown.transform.parent;
        if (parent == null || mapText.transform.parent != parent) return; // unknown layout: keep the plain "Map: Cinema" text

        mapDropdown = Instantiate(difficultyDropdown, parent);
        mapDropdown.name = "Map Dropdown";
        mapDropdown.transform.SetSiblingIndex(mapText.transform.GetSiblingIndex() + 1);

        var row = (RectTransform)difficultyDropdown.transform;
        LayoutElement element = difficultyDropdown.GetComponent<LayoutElement>();
        float rowHeight = element != null && element.preferredHeight > 0f ? element.preferredHeight
                        : row.rect.height > 1f ? row.rect.height : row.sizeDelta.y > 1f ? row.sizeDelta.y : 56f;
        VerticalLayoutGroup group = parent.GetComponent<VerticalLayoutGroup>();
        if (parent is RectTransform card && group != null)
            card.sizeDelta += new Vector2(0f, rowHeight + group.spacing);
    }

    /// <summary>Fills the difficulty list. Run again when the dev unlock is switched (16 Nov).</summary>
    private void BuildDifficultyOptions()
    {
        devUnlocked = RoomLocks.DevUnlocked;
        var difficulties = new List<string>(RoomDisplay.DifficultyOptions.Length);
        for (int i = 0; i < RoomDisplay.DifficultyOptions.Length; i++)
            difficulties.Add(RoomDisplay.DifficultyOptions[i] + (DifficultyLocked(i) ? RoomLocks.LockedSuffix : ""));
        if (DifficultyLocked(difficultyChoice)) difficultyChoice = (int)DifficultyLevel.Normal; // was picked while unlocked
        difficultyDropdown.Hide();
        difficultyDropdown.ClearOptions();
        difficultyDropdown.AddOptions(difficulties);
        difficultyDropdown.SetValueWithoutNotify(difficultyChoice);
        lockedDifficultyList = null;
    }

    private bool devUnlocked; // the dev unlock state the list was built with

    private static bool DifficultyLocked(int index) => RoomLocks.IsLocked((DifficultyLevel)index);
    private static bool MapLocked(int index) => index < 0 || index >= MapCatalog.All.Count || MapCatalog.All[index].locked;

    private void OnDifficultyPicked(int index)
    {
        if (!DifficultyLocked(index)) { difficultyChoice = index; errorText.text = string.Empty; return; }
        difficultyDropdown.SetValueWithoutNotify(difficultyChoice);
        errorText.text = RoomDisplay.DifficultyOptions[Mathf.Clamp(index, 0, RoomDisplay.DifficultyOptions.Length - 1)] + " is locked for now.";
    }

    private void OnMapPicked(int index)
    {
        if (!MapLocked(index)) { mapChoice = index; errorText.text = string.Empty; return; }
        mapDropdown.SetValueWithoutNotify(mapChoice);
        errorText.text = (index >= 0 && index < MapCatalog.All.Count ? MapCatalog.All[index].displayName : "This map") + " is locked for now.";
    }

    // While a list is open, its locked rows are greyed and cannot be clicked.
    private void LateUpdate()
    {
        if (devUnlocked != RoomLocks.DevUnlocked) BuildDifficultyOptions(); // F1 panel switched it
        GreyLockedRows(difficultyDropdown, ref lockedDifficultyList, true);
        GreyLockedRows(mapDropdown, ref lockedMapList, false);
    }

    private static void GreyLockedRows(TMP_Dropdown dropdown, ref Transform done, bool difficulty)
    {
        if (dropdown == null) return;
        Transform list = dropdown.transform.Find("Dropdown List"); // made by TMP_Dropdown while the list is open
        if (list == null) { done = null; return; }
        if (list == done) return;
        done = list;

        Toggle[] rows = list.GetComponentsInChildren<Toggle>(false); // the hidden template row is skipped
        for (int i = 0; i < rows.Length; i++)
        {
            bool locked = difficulty ? DifficultyLocked(i) : MapLocked(i);
            if (!locked) continue;
            rows[i].interactable = false;
            foreach (TMP_Text label in rows[i].GetComponentsInChildren<TMP_Text>(true))
            {
                Color c = label.color;
                c.a *= 0.4f;
                label.color = c;
            }
        }
    }

    private void OnEnable()
    {
        errorText.text = string.Empty;
        RefreshLabels();
        // Suggest "<name>'s room" until the host types their own.
        if (roomNameField != null && string.IsNullOrWhiteSpace(roomNameField.text))
            roomNameField.SetTextWithoutNotify(DefaultRoomName());
    }

    private static string DefaultRoomName() => RoomConfig.SanitizeRoomName(GameSettings.PlayerName + "'s room");

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
        string mapID = mapDropdown != null && mapDropdown.value >= 0 && mapDropdown.value < MapCatalog.All.Count
            ? MapCatalog.All[mapDropdown.value].id : RoomConfig.DemoMapID;

        var config = new RoomConfig
        {
            roomName = roomNameField != null && !string.IsNullOrWhiteSpace(roomNameField.text)
                ? roomNameField.text : DefaultRoomName(),
            mapID = mapID,
            difficulty = (DifficultyLevel)Mathf.Clamp(difficultyDropdown.value, 0, RoomDisplay.DifficultyOptions.Length - 1),
            playerLimit = Mathf.RoundToInt(playerLimitSlider.value),
            isPrivate = privateToggle.isOn,
            password = privateToggle.isOn ? passwordField.text.Trim() : string.Empty,
        };

        if (!config.Validate(out string error))
        {
            errorText.text = error; // 15 Nov: also "X is locked for now."
            return;
        }

        errorText.text = string.Empty;
        menu.CreateRoom(config);
    }
}
