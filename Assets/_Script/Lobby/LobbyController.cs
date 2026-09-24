using UnityEngine;
using Mirror;
using EpicTransport;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using System.Collections.Generic;

/// <summary>
/// The EOS side of rooms: create, search, join and leave EOS lobbies, and hand
/// the connection over to Mirror. The UI never talks to EOS directly — it calls
/// CreateRoom / FindRooms / JoinRoom / QuickJoin / LeaveRoom here and listens
/// to the events below.
///
/// Lives on the NetworkManager prefab, next to RoHRoomManager and
/// RoomPasswordAuthenticator.
/// </summary>
public class LobbyController : EOSLobby
{
    private static LobbyController instance;

    /// <summary>
    /// The live controller (the one on the running NetworkManager).
    ///
    /// Self-healing on purpose: when a session ends Mirror hands the
    /// NetworkManager back to the MainMenu scene, which destroys it and loads a
    /// fresh copy. Depending on the order Unity runs the old OnDestroy and the
    /// new Awake, the cached reference can briefly go null — so if it is null,
    /// look it up again from NetworkManager.singleton.
    /// </summary>
    public static LobbyController Instance
    {
        get
        {
            if (instance != null) return instance;

            if (NetworkManager.singleton != null &&
                NetworkManager.singleton.TryGetComponent(out LobbyController onManager))
            {
                instance = onManager;
            }
            else
            {
                instance = FindAnyObjectByType<LobbyController>();
            }

            return instance;
        }
    }

    /// <summary>True once EOS has logged in and rooms can be created or searched.</summary>
    public static bool EosReady => EOSSDKComponent.Initialized;

    // Assign this in the Inspector if LobbyController lives on a different
    // GameObject than NetworkManager (e.g. a dedicated "EOS_Manager" object).
    // Falls back to GetComponent(same object) below for the case where they
    // really are on the same GameObject, but don't rely on that silently --
    // an unassigned, unfound netManager used to fail as a null-reference the
    // moment StartHost()/StartClient() ran, with no clue why.
    [SerializeField] private NetworkManager netManager;

    // Kept host-side only; never sent as an EOS lobby attribute (those are
    // readable by anyone browsing lobbies). RoomPasswordAuthenticator checks it
    // over Mirror after the joining player connects.
    private string pendingRoomPassword = string.Empty;

    // The config of the room being created, kept until EOS confirms it.
    private RoomConfig pendingConfig;

    // Guards against a second request firing while one is still waiting on
    // EOS -- without this, mashing a button sends several CreateLobby requests
    // and each one succeeds, leaving orphaned lobbies behind.
    private bool isCreateRoomInFlight = false;
    private bool isFindRoomsInFlight = false;
    private bool isJoinInFlight = false;
    private bool isLeaveInFlight = false;

    // Set by QuickJoin so the next search result auto-joins.
    private bool _joinFirstRoomWhenFound = false;

    /// <summary>
    /// The room this player is in right now (map, difficulty, limit, private).
    /// Set on create and on join, cleared on leave. The password is never kept
    /// here. The waiting lobby screen reads this to show room info.
    /// </summary>
    public RoomConfig CurrentRoom { get; private set; }

    /// <summary>True while a create / search / join is waiting on EOS. The UI disables its buttons.</summary>
    public bool IsBusy => isCreateRoomInFlight || isFindRoomsInFlight || isJoinInFlight;

    /// <summary>
    /// Raised when CreateRoom rejects a RoomConfig before ever contacting EOS
    /// (e.g. a private room with no password). Kept for older listeners —
    /// new UI should listen to OperationFailed, which also covers this.
    /// </summary>
    public event System.Action<string> RoomValidationFailed;

    /// <summary>Any create / search / join failure, as one short sentence for the player.</summary>
    public event System.Action<string> OperationFailed;

    /// <summary>Progress text for the UI: "Creating room...", "Searching...", "Joining...".</summary>
    public event System.Action<string> StatusChanged;

    /// <summary>
    /// Raised every time a FindLobbies() search completes, with each result
    /// already parsed into a RoomListEntry (map, difficulty, current/max
    /// players, locked, in progress).
    /// </summary>
    public event System.Action<List<RoomListEntry>> RoomsFound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Awake()
    {
        // Only the first one claims the slot. When the MainMenu scene loads
        // again, its copy of the NetworkManager prefab is a duplicate that
        // Mirror destroys; it must not steal (and then null) the live instance.
        if (instance == null) instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public override void Start()
    {
        base.Start();

        if (netManager == null)
        {
            netManager = GetComponent<NetworkManager>();
        }

        if (netManager == null)
        {
            Debug.LogError("LobbyController has no NetworkManager reference. " +
                "Assign one in the Inspector -- it does not have to be on this GameObject.");
        }
    }

    private void OnEnable()
    {
        CreateLobbySucceeded += OnCreateLobbySuccess;
        CreateLobbyFailed += OnCreateLobbyFailed;
        JoinLobbySucceeded += OnJoinLobbySuccess;
        JoinLobbyFailed += OnJoinLobbyFailed;
        FindLobbiesSucceeded += OnFindLobbiesSuccess;
        FindLobbiesFailed += OnFindLobbiesFailed;
        LeaveLobbySucceeded += OnLeaveLobbySuccess;
        LeaveLobbyFailed += OnLeaveLobbyFailed;
    }

    private void OnDisable()
    {
        CreateLobbySucceeded -= OnCreateLobbySuccess;
        CreateLobbyFailed -= OnCreateLobbyFailed;
        JoinLobbySucceeded -= OnJoinLobbySuccess;
        JoinLobbyFailed -= OnJoinLobbyFailed;
        FindLobbiesSucceeded -= OnFindLobbiesSuccess;
        FindLobbiesFailed -= OnFindLobbiesFailed;
        LeaveLobbySucceeded -= OnLeaveLobbySuccess;
        LeaveLobbyFailed -= OnLeaveLobbyFailed;
    }

    // ---- Old test-button entry points (still wired in older scenes) ---------

    public void Button_CreateRoom() => CreateRoom(new RoomConfig());
    public void Button_FindRooms() => FindRooms();
    public void Button_QuickJoinFirstRoom() => QuickJoin();

    // ---- Create --------------------------------------------------------------

    /// <summary>
    /// EOS logs in asynchronously after startup, and until it finishes there is
    /// no valid ProductUserId to create or search lobbies with. Calling anyway
    /// fails deep inside the SDK with a bare "InvalidUser", which says nothing
    /// about the real cause -- so refuse early with a message that does.
    /// </summary>
    private bool IsEosReady(string action)
    {
        if (EOSSDKComponent.Initialized) return true;

        string reason = EOSSDKComponent.IsConnecting
            ? "Still connecting to Epic Online Services. Try again in a moment."
            : "Not logged in to Epic Online Services.";

        Debug.LogWarning($"[LobbyController] {action} refused: {reason}", this);
        Fail(reason);
        return false;
    }

    /// <summary>
    /// The only way a room is created. The UI builds a RoomConfig from its
    /// controls and calls this; it never calls CreateLobby directly, so every
    /// room goes through the same validation and the same attributes.
    /// </summary>
    public void CreateRoom(RoomConfig config)
    {
        if (!IsEosReady("Create room")) return;

        if (isCreateRoomInFlight || isJoinInFlight)
        {
            Debug.LogWarning("[LobbyController] A request is already in flight.");
            return;
        }

        if (config == null || !config.Validate(out string error))
        {
            error = config == null ? "No room settings." : error;
            Debug.LogWarning("[LobbyController] Create room refused: " + error);
            RoomValidationFailed?.Invoke(error);
            Fail(error);
            return;
        }

        var attributes = new AttributeData[]
        {
            new AttributeData { Key = LobbyKeys.MapID,       Value = config.mapID },
            new AttributeData { Key = LobbyKeys.Difficulty,  Value = config.difficulty.ToString() },
            new AttributeData { Key = LobbyKeys.PlayerLimit, Value = config.playerLimit },
            new AttributeData { Key = LobbyKeys.HasPassword, Value = config.isPrivate },
            new AttributeData { Key = LobbyKeys.InProgress,  Value = false },
        };

        // Both public and private rooms are Publicadvertised, so the browser
        // can list both. Privacy is enforced by RoomPasswordAuthenticator after
        // Mirror connects, not by EOS visibility.
        pendingRoomPassword = config.isPrivate ? config.password : string.Empty;
        pendingConfig = config;

        // The match reads difficulty/limit from here when it starts.
        MatchState.SetPendingConfig(config);

        RoomPasswordAuthenticator.ServerPassword = pendingRoomPassword;
        RoomPasswordAuthenticator.ClientPassword = pendingRoomPassword;

        isCreateRoomInFlight = true;
        StatusChanged?.Invoke("Creating room...");
        Debug.Log($"[LobbyController] Creating room (map={config.mapID}, difficulty={config.difficulty}, players={config.playerLimit}, private={config.isPrivate})");
        CreateLobby((uint)config.playerLimit, LobbyPermissionLevel.Publicadvertised, false, attributes);
    }

    private void OnCreateLobbySuccess(List<Attribute> attributes)
    {
        isCreateRoomInFlight = false;
        Debug.Log("[LobbyController] Room created. Starting Mirror host.");

        RoomConfig config = pendingConfig ?? new RoomConfig();
        CurrentRoom = PublicCopy(config);
        RoHRoomManager.LastDisconnectReason = null;

        if (netManager is RoHRoomManager room) room.SetRoomPlayerLimit(config.playerLimit);

        StatusChanged?.Invoke("Opening lobby...");
        netManager.StartHost();
    }

    private void OnCreateLobbyFailed(string errorMessage)
    {
        isCreateRoomInFlight = false;
        pendingConfig = null;
        MatchState.Clear();
        RoomPasswordAuthenticator.ServerPassword = string.Empty;
        Debug.LogError("[LobbyController] Create room failed: " + errorMessage);
        Fail("Could not create the room. Please try again.");
    }

    // ---- Search --------------------------------------------------------------

    public void FindRooms()
    {
        _joinFirstRoomWhenFound = false;
        StartFindRooms();
    }

    /// <summary>
    /// Search, then join the first room that is public, not full and not
    /// mid-match. Private rooms are skipped: there is no password to give.
    /// </summary>
    public void QuickJoin()
    {
        _joinFirstRoomWhenFound = true;
        StartFindRooms();
    }

    private void StartFindRooms()
    {
        if (!IsEosReady("Find rooms"))
        {
            _joinFirstRoomWhenFound = false;
            return;
        }

        if (isFindRoomsInFlight)
        {
            Debug.LogWarning("[LobbyController] A search is already in flight.");
            return;
        }

        isFindRoomsInFlight = true;
        StatusChanged?.Invoke("Searching for rooms...");
        FindLobbies();
    }

    private void OnFindLobbiesSuccess(List<LobbyDetails> lobbiesFound)
    {
        isFindRoomsInFlight = false;
        Debug.Log($"[LobbyController] Found {lobbiesFound.Count} room(s).");

        var entries = new List<RoomListEntry>(lobbiesFound.Count);
        foreach (LobbyDetails details in lobbiesFound)
        {
            entries.Add(ParseRoomListEntry(details));
        }

        RoomsFound?.Invoke(entries);

        if (!_joinFirstRoomWhenFound) return;
        _joinFirstRoomWhenFound = false;

        RoomListEntry target = entries.Find(e => !e.isLocked && e.IsJoinable);
        if (target == null)
        {
            Fail("No open public rooms right now. Try creating one.");
            return;
        }

        JoinRoom(target, null);
    }

    private void OnFindLobbiesFailed(string errorMessage)
    {
        isFindRoomsInFlight = false;
        _joinFirstRoomWhenFound = false;
        Debug.LogError("[LobbyController] Find rooms failed: " + errorMessage);
        Fail("Could not search for rooms. Please try again.");
    }

    // ---- Join ----------------------------------------------------------------

    /// <summary>
    /// Join a room picked in the browser. For a locked room, pass the password
    /// the player typed; the host checks it after Mirror connects.
    /// </summary>
    public void JoinRoom(RoomListEntry entry, string password = null)
    {
        if (entry == null) return;
        if (!IsEosReady("Join room")) return;
        if (isJoinInFlight || isCreateRoomInFlight) return;

        if (entry.inProgress) { Fail("That match has already started."); return; }
        if (entry.IsFull) { Fail("That room is full."); return; }
        if (entry.isLocked && string.IsNullOrEmpty(password)) { Fail("This room needs a password."); return; }

        RoomPasswordAuthenticator.ClientPassword = password ?? string.Empty;
        CurrentRoom = new RoomConfig
        {
            mapID = entry.mapID,
            difficulty = entry.difficulty,
            playerLimit = entry.maxPlayers,
            isPrivate = entry.isLocked,
        };

        isJoinInFlight = true;
        StatusChanged?.Invoke("Joining room...");
        JoinLobby(entry.details);
    }

    private void OnJoinLobbySuccess(List<Attribute> attributes)
    {
        isJoinInFlight = false;

        // FindIndex, not Find: it works the same whether this SDK version's
        // Attribute is a struct or a class, and "not found" is explicit.
        int hostIndex = attributes.FindIndex((x) => x.Data.Key == hostAddressKey);
        string address = hostIndex >= 0 ? (string)attributes[hostIndex].Data.Value.AsUtf8 : null;
        if (string.IsNullOrEmpty(address))
        {
            Debug.LogError("[LobbyController] Joined an EOS lobby with no host address.");
            Fail("That room is broken. Try another one.");
            LeaveRoom();
            return;
        }

        RoHRoomManager.LastDisconnectReason = null;
        StatusChanged?.Invoke("Connecting to host...");
        netManager.networkAddress = address;
        netManager.StartClient();
    }

    private void OnJoinLobbyFailed(string errorMessage)
    {
        isJoinInFlight = false;
        CurrentRoom = null;
        RoomPasswordAuthenticator.ClientPassword = string.Empty;
        Debug.LogError("[LobbyController] Join room failed: " + errorMessage);
        Fail("Could not join the room. It may have closed.");
    }

    // ---- Leave / in-progress -------------------------------------------------

    /// <summary>
    /// Leave (or, as host, close) the EOS lobby. Safe to call more than once and
    /// when not in a lobby. RoHRoomManager calls this whenever the Mirror
    /// session stops, so rooms never linger in the browser.
    /// </summary>
    public void LeaveRoom()
    {
        CurrentRoom = null;
        pendingConfig = null;
        RoomPasswordAuthenticator.ServerPassword = string.Empty;
        RoomPasswordAuthenticator.ClientPassword = string.Empty;

        if (!ConnectedToLobby || isLeaveInFlight) return;

        isLeaveInFlight = true;
        LeaveLobby();
    }

    private void OnLeaveLobbySuccess() => isLeaveInFlight = false;

    private void OnLeaveLobbyFailed(string errorMessage)
    {
        isLeaveInFlight = false;
        Debug.LogWarning("[LobbyController] Leave lobby failed: " + errorMessage);
    }

    /// <summary>Host only: flag the room as mid-match so the browser greys it out.</summary>
    public void MarkRoomInProgress(bool inProgress)
    {
        if (!ConnectedToLobby) return;
        UpdateLobbyAttribute(LobbyKeys.InProgress, inProgress);
    }

    // ---- Helpers -------------------------------------------------------------

    private void Fail(string message)
    {
        OperationFailed?.Invoke(message);
    }

    private static RoomConfig PublicCopy(RoomConfig source) => new RoomConfig
    {
        mapID = source.mapID,
        difficulty = source.difficulty,
        playerLimit = source.playerLimit,
        isPrivate = source.isPrivate,
        password = string.Empty,
    };

    /// <summary>
    /// Turns one raw search result into the plain data a room browser row
    /// needs. Falls back to sane defaults for any attribute that's missing --
    /// e.g. a lobby that wasn't created through CreateRoom -- rather than
    /// crashing the whole list over one malformed entry.
    /// </summary>
    private RoomListEntry ParseRoomListEntry(LobbyDetails details)
    {
        var entry = new RoomListEntry { details = details };

        entry.mapID = TryReadUtf8Attribute(details, LobbyKeys.MapID, RoomConfig.DemoMapID);

        string difficultyName = TryReadUtf8Attribute(details, LobbyKeys.Difficulty, DifficultyLevel.Normal.ToString());
        if (!System.Enum.TryParse(difficultyName, out DifficultyLevel difficulty))
        {
            difficulty = DifficultyLevel.Normal;
        }
        entry.difficulty = difficulty;

        entry.maxPlayers = TryReadIntAttribute(details, LobbyKeys.PlayerLimit, RoomConfig.MaxPlayers);
        entry.isLocked = TryReadBoolAttribute(details, LobbyKeys.HasPassword, false);
        entry.inProgress = TryReadBoolAttribute(details, LobbyKeys.InProgress, false);
        entry.currentPlayers = (int)details.GetMemberCount(new LobbyDetailsGetMemberCountOptions { });

        return entry;
    }

    private static string TryReadUtf8Attribute(LobbyDetails details, string key, string fallback)
    {
        Attribute attribute = new Attribute();
        Result result = details.CopyAttributeByKey(new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key }, out attribute);
        if (result != Result.Success) return fallback;

        string value = attribute.Data.Value.AsUtf8;
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private static int TryReadIntAttribute(LobbyDetails details, string key, int fallback)
    {
        Attribute attribute = new Attribute();
        Result result = details.CopyAttributeByKey(new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key }, out attribute);
        if (result != Result.Success) return fallback;

        return (int)(attribute.Data.Value.AsInt64 ?? fallback);
    }

    private static bool TryReadBoolAttribute(LobbyDetails details, string key, bool fallback)
    {
        Attribute attribute = new Attribute();
        Result result = details.CopyAttributeByKey(new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key }, out attribute);
        if (result != Result.Success) return fallback;

        return attribute.Data.Value.AsBool ?? fallback;
    }
}
