using UnityEngine;
using Mirror;
using EpicTransport;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using System.Collections.Generic;

public class LobbyController : EOSLobby
{
    // Assign this in the Inspector if LobbyController lives on a different
    // GameObject than NetworkManager (e.g. a dedicated "EOS_Manager" object).
    // Falls back to GetComponent(same object) below for the case where they
    // really are on the same GameObject, but don't rely on that silently --
    // an unassigned, unfound netManager used to fail as a null-reference the
    // moment StartHost()/StartClient() ran, with no clue why.
    [SerializeField] private NetworkManager netManager;

    // Kept server-side only; never sent as an EOS lobby attribute (those are
    // always Public-visible in this wrapper, and even LobbyAttributeVisibility.Private
    // only means "visible to the client that set it" -- not "visible to lobby
    // members". A real client-facing password check happens after Mirror
    // connects, e.g. via a NetworkAuthenticator, not through EOS attributes.
    private string pendingRoomPassword = string.Empty;

    // Guards against a second CreateRoom/FindRooms firing while one is still
    // waiting on EOS -- without this, mashing the button (or a slow network)
    // sends multiple CreateLobby requests before the first one resolves, each
    // of which succeeds independently and leaves an orphaned lobby behind.
    private bool isCreateRoomInFlight = false;
    private bool isFindRoomsInFlight = false;

    // Set by Button_QuickJoinFirstRoom so the next search result auto-joins.
    private bool _joinFirstRoomWhenFound = false;

    /// <summary>
    /// Raised when CreateRoom rejects a RoomConfig before ever contacting EOS
    /// (e.g. a private room with no password). This is deliberately separate
    /// from CreateLobbyFailed: that event lives on the base EOSLobby class and
    /// only fires for an actual EOS-side failure once a request is in flight --
    /// C# does not let a subclass raise an event it merely inherited, only the
    /// declaring class can. A future UI controller should listen to both this
    /// and CreateLobbyFailed to cover every way room creation can fail.
    /// </summary>
    public event System.Action<string> RoomValidationFailed;

    /// <summary>
    /// Raised every time a FindLobbies() search completes, with each result
    /// already parsed into a RoomListEntry (map, difficulty, current/max
    /// players, locked). A room browser UI should subscribe to this instead of
    /// FindLobbiesSucceeded -- it never needs to touch LobbyDetails directly.
    /// </summary>
    public event System.Action<List<RoomListEntry>> RoomsFound;

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
    }

    private void OnDisable()
    {
        CreateLobbySucceeded -= OnCreateLobbySuccess;
        CreateLobbyFailed -= OnCreateLobbyFailed;
        JoinLobbySucceeded -= OnJoinLobbySuccess;
        JoinLobbyFailed -= OnJoinLobbyFailed;
        FindLobbiesSucceeded -= OnFindLobbiesSuccess;
        FindLobbiesFailed -= OnFindLobbiesFailed;
    }

    /// <summary>
    /// Zero-argument entry point for wiring directly to a UI Button's OnClick --
    /// use this until the team locks the actual Room Creation layout. It just
    /// calls CreateRoom with RoomConfig's defaults (the one demo map, Normal
    /// difficulty, max players, public/no password). Once there's a real panel
    /// with a difficulty dropdown / player-count slider / privacy toggle, wire
    /// the button to a UI controller that builds a real RoomConfig from those
    /// controls and calls CreateRoom(config) instead -- swap the wiring, not
    /// this method, so nothing here needs to change.
    /// </summary>
    public void Button_CreateRoom()
    {
        CreateRoom(new RoomConfig());
    }

    /// <summary>
    /// Host-side entry point for the Room Creation screen once one exists. A
    /// RoomCreationUIController would read the panel's UI fields, build a
    /// RoomConfig from them, and call this -- it should never call
    /// CreateLobby(...) directly, so every room always goes through the same
    /// validation and the same set of lobby attributes.
    /// </summary>
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
            ? "ยังเชื่อมต่อ EOS ไม่เสร็จ กรุณารอสักครู่แล้วลองใหม่ (EOS login still in progress)"
            : "ยังไม่ได้ล็อกอิน EOS (EOS is not logged in)";

        Debug.LogWarning($"[LobbyController] {action}ไม่ได้: {reason}", this);
        RoomValidationFailed?.Invoke(reason);
        return false;
    }

    public void CreateRoom(RoomConfig config)
    {
        if (!IsEosReady("สร้างห้อง")) return;

        if (isCreateRoomInFlight)
        {
            Debug.LogWarning("กำลังสร้างห้องอยู่ กรุณารอสักครู่ก่อนกดซ้ำ (a create request is already in flight)");
            return;
        }

        if (!config.Validate(out string error))
        {
            Debug.LogWarning("สร้างห้องไม่ได้: " + error);
            RoomValidationFailed?.Invoke(error);
            return;
        }

        var attributes = new AttributeData[]
        {
            new AttributeData { Key = LobbyKeys.MapID,       Value = config.mapID },
            new AttributeData { Key = LobbyKeys.Difficulty,  Value = config.difficulty.ToString() },
            new AttributeData { Key = LobbyKeys.PlayerLimit, Value = config.playerLimit },
            new AttributeData { Key = LobbyKeys.HasPassword, Value = config.isPrivate },
        };

        // NOTE: both public and private rooms use Publicadvertised here, so
        // FindLobbies() can list both -- privacy is enforced by the password
        // gate after Mirror connects, not by EOS visibility. See the message
        // in chat about the Inviteonly alternative if you'd rather private
        // rooms not appear in the browser at all.
        pendingRoomPassword = config.isPrivate ? config.password : string.Empty;

        isCreateRoomInFlight = true;
        Debug.Log($"กำลังส่งคำสั่งสร้างห้อง... (map={config.mapID}, difficulty={config.difficulty}, players={config.playerLimit}, private={config.isPrivate})");
        CreateLobby((uint)config.playerLimit, LobbyPermissionLevel.Publicadvertised, false, attributes);
    }

    public void Button_FindRooms()
    {
        _joinFirstRoomWhenFound = false;
        StartFindRooms();
    }

    /// <summary>
    /// Temporary test helper: search, then immediately join whatever comes back
    /// first. Wire a button to this to prove two clients can actually connect
    /// before the real room browser UI exists. Delete it once the browser lists
    /// rooms with their own Join buttons.
    /// </summary>
    public void Button_QuickJoinFirstRoom()
    {
        _joinFirstRoomWhenFound = true;
        StartFindRooms();
    }

    private void StartFindRooms()
    {
        if (!IsEosReady("ค้นหาห้อง"))
        {
            _joinFirstRoomWhenFound = false;
            return;
        }

        if (isFindRoomsInFlight)
        {
            Debug.LogWarning("กำลังค้นหาห้องอยู่ กรุณารอสักครู่ก่อนกดซ้ำ (a search is already in flight)");
            _joinFirstRoomWhenFound = false;
            return;
        }

        isFindRoomsInFlight = true;
        Debug.Log("กำลังค้นหาห้อง...");
        FindLobbies();
    }

    /// <summary>
    /// Call this when the player picks a row in the room browser. Wraps the
    /// inherited JoinLobby so UI code only ever deals with RoomListEntry, never
    /// LobbyDetails or the EOS attribute API.
    /// </summary>
    public void JoinRoom(RoomListEntry entry)
    {
        JoinLobby(entry.details);
    }

    private void OnCreateLobbySuccess(List<Attribute> attributes)
    {
        isCreateRoomInFlight = false;
        Debug.Log("เปิดห้องสำเร็จ! สั่ง Mirror เริ่มโฮสต์เกม");
        // TODO: once Mirror's authenticator is wired up (next step), hand it
        // pendingRoomPassword here so it can challenge joining clients.
        netManager.StartHost();
    }

    private void OnCreateLobbyFailed(string errorMessage)
    {
        isCreateRoomInFlight = false;
        Debug.LogError("สร้างห้องล้มเหลว: " + errorMessage);
        // TODO: surface this on the Room Creation UI (e.g. an inline error
        // label) instead of only logging it -- right now a failed create
        // leaves the player staring at a screen that did nothing.
    }

    private void OnJoinLobbySuccess(List<Attribute> attributes)
    {
        Debug.Log("เข้าห้องสำเร็จ! กำลังดึง PUID เพื่อเชื่อมต่อ...");
        netManager.networkAddress = attributes.Find((x) => x.Data.Key == hostAddressKey).Data.Value.AsUtf8;
        netManager.StartClient();
    }

    private void OnJoinLobbyFailed(string errorMessage)
    {
        Debug.LogError("เข้าห้องล้มเหลว: " + errorMessage);
        // TODO: surface this on the Room Browser UI instead of only logging.
    }

    private void OnFindLobbiesSuccess(List<LobbyDetails> lobbiesFound)
    {
        isFindRoomsInFlight = false;
        Debug.Log($"เจอห้องทั้งหมด {lobbiesFound.Count} ห้อง");

        var entries = new List<RoomListEntry>(lobbiesFound.Count);
        foreach (LobbyDetails details in lobbiesFound)
        {
            entries.Add(ParseRoomListEntry(details));
        }

        RoomsFound?.Invoke(entries);

        if (!_joinFirstRoomWhenFound) return;
        _joinFirstRoomWhenFound = false;

        if (entries.Count == 0)
        {
            Debug.LogWarning("[LobbyController] ไม่เจอห้องให้เข้า (no rooms found to quick-join).", this);
            return;
        }

        Debug.Log($"[LobbyController] Quick-join: เข้าห้องแรกที่เจอ (map={entries[0].mapID}, players={entries[0].currentPlayers}/{entries[0].maxPlayers}).", this);
        JoinRoom(entries[0]);
    }

    private void OnFindLobbiesFailed(string errorMessage)
    {
        isFindRoomsInFlight = false;
        Debug.LogError("ค้นหาห้องล้มเหลว: " + errorMessage);
    }

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

        // NOTE: this call has no existing precedent elsewhere in EOSLobby.cs to
        // copy the exact calling convention from (unlike the attribute reads
        // above, which mirror JoinLobby/FindLobbies exactly). If Unity throws
        // CS1620 on this line, add `ref` before the options argument.
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
