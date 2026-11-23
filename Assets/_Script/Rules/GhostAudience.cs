using Mirror;
using UnityEngine;

/// <summary>
/// 16 Oct 2026 (Mr.k): the ghost customers sitting in the Ghost Screen Room.
/// Used by the Screen Room rules ("don't look at the audience", "the audience
/// stands up", "staff time" = the room is almost empty).
///
/// Each member is a mockup prefab instance ("RM_GhostAudience") with two children:
/// "Seated" and "Standing". Replace the prefab to change every member.
///
/// 23 Nov 2026 (Mr.k):
///   - Each member is shown as an NPC GHOST MODEL (CustomerModels.asset, Ghost Models) sitting on
///     its cinema seat (SitPose: legs bent, feet on the floor). The mockup looks are hidden.
///     Standing (the "audience stands" rule) = the model stands up. Off = the old mockups.
///   - AWAY: when the ghost room film is due (the movie stopped), every member leaves its seat
///     and roams the map (FilmGhostEvent, same model per member). The room is empty until the
///     film is changed.
///
/// SERVER AUTHORITY: visible / standing / away / where they look are SyncVars.
/// </summary>
[DisallowMultipleComponent]
public sealed class GhostAudience : NetworkBehaviour
{
    [Tooltip("The audience members (mockup prefab instances on the seats).")]
    [SerializeField] private Transform[] members;
    [Tooltip("Height of a seated member's head above its pivot (for the look check).")]
    [SerializeField] private float seatedHeadHeight = 0.85f;
    [Tooltip("Height of a standing member's head above its pivot.")]
    [SerializeField] private float standingHeadHeight = 1.7f;
    [Tooltip("2 Nov (Mr.k): scale the mockup looks (seated + standing) so every ghost is as tall as a player.")]
    [SerializeField] private bool matchPlayerHeight = true;
    [Tooltip("Player height in metres (the Y Bot player model is about 1.8 m). Used when \"Match Player Height\" is on.")]
    [SerializeField, Min(0.5f)] private float playerHeight = 1.8f;
    [Tooltip("23 Nov: show every member as an NPC ghost model sitting on its seat (off = the old mockups).")]
    [SerializeField] private bool useGhostModels = true;

    [SyncVar(hook = nameof(OnStateChanged))] private bool visible = true;
    [SyncVar(hook = nameof(OnStateChanged))] private bool standing;
    [SyncVar(hook = nameof(OnStateChanged))] private bool away;
    [SyncVar] private Vector3 lookAt;

    private Quaternion[] seatedRotations;
    private GameObject[] seatedLooks;
    private GameObject[] standingLooks;
    private SitPose[] poses;
    private float seatedHead;
    private float standingHead;

    /// <summary>In the room and in their seats (not hidden by Staff Time, not away roaming).</summary>
    public bool Visible => visible && !away;
    /// <summary>23 Nov: they left their seats and roam the map (the film is due).</summary>
    public bool Away => away;
    public bool Standing => standing;
    public int Count => members != null ? members.Length : 0;
    public Transform Member(int i) => members[i];

    /// <summary>23 Nov: the model seed of member i (the roaming copy uses the same one = the same ghost).</summary>
    public static uint MemberSeed(int i) => (uint)(i * 7 + 3);

    /// <summary>Head point of member i (for "is the player looking at it").</summary>
    public Vector3 HeadOf(int i) => members[i].position + Vector3.up * (standing ? standingHead : seatedHead);

    private void Awake()
    {
        int n = Count;
        seatedRotations = new Quaternion[n];
        seatedLooks = new GameObject[n];
        standingLooks = new GameObject[n];
        poses = new SitPose[n];
        seatedHead = matchPlayerHeight ? playerHeight - 0.15f : seatedHeadHeight;
        standingHead = matchPlayerHeight ? playerHeight - 0.15f : standingHeadHeight;
        float modelHeight = 0f;
        for (int i = 0; i < n; i++)
        {
            if (members[i] == null) continue;
            seatedRotations[i] = members[i].rotation;
            Transform s = members[i].Find("Seated");
            Transform u = members[i].Find("Standing");
            seatedLooks[i] = s != null ? s.gameObject : null;
            standingLooks[i] = u != null ? u.gameObject : null;
            CustomerModelVisual model = useGhostModels ? MakeModel(i) : null;
            if (model != null)
            {
                modelHeight = Mathf.Max(modelHeight, model.Height);
                if (s != null) s.gameObject.SetActive(false);
                if (u != null) u.gameObject.SetActive(false);
                seatedLooks[i] = null;
                standingLooks[i] = null;
            }
            else if (matchPlayerHeight)
            {
                RuleFx.ScaleToHeight(s, members[i].position.y, playerHeight);
                RuleFx.ScaleToHeight(u, members[i].position.y, playerHeight);
            }
            // Members never block walking or the interaction ray.
            foreach (Collider c in members[i].GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
        if (modelHeight > 0.5f)
        {
            seatedHead = modelHeight * 0.68f;  // sitting: the head is lower
            standingHead = modelHeight * 0.93f;
        }
        Apply();
    }

    /// <summary>23 Nov: the NPC ghost model on member i's seat (same seed on every PC).</summary>
    private CustomerModelVisual MakeModel(int i)
    {
        Transform m = members[i];
        CustomerModelVisual v = CustomerModelVisual.TryCreate("Ghost Model", m.position + Vector3.up, m.rotation,
            MemberSeed(i), false, 0.25f, false, true);
        if (v == null) return null;
        v.transform.SetParent(m, true);
        v.transform.localPosition = Vector3.up; // capsule-centre convention of the customer bodies
        v.transform.localRotation = Quaternion.identity;
        poses[i] = SitPose.For(v.gameObject);
        return v;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Apply();
    }

    /// <summary>SERVER. Show / hide the whole audience (hidden = "the room is almost empty").</summary>
    public void ServerSetVisible(bool value)
    {
        if (!NetworkMode.HasServerAuthority(this) || visible == value) return;
        visible = value;
        if (!value) standing = false;
        Apply();
    }

    /// <summary>SERVER. 23 Nov: everyone leaves the seats to roam (true) or is back in the seats (false).</summary>
    public void ServerSetAway(bool value)
    {
        if (!NetworkMode.HasServerAuthority(this) || away == value) return;
        away = value;
        if (value) standing = false;
        Apply();
    }

    /// <summary>SERVER. Everyone stands up and turns to <paramref name="target"/>, or sits back down.</summary>
    public void ServerSetStanding(bool value, Vector3 target)
    {
        if (!NetworkMode.HasServerAuthority(this)) return;
        lookAt = target;
        if (standing == value) { Apply(); return; }
        standing = value;
        Apply();
    }

    private void OnStateChanged(bool oldValue, bool newValue) => Apply();

    private void Update()
    {
        // Standing members keep turning toward the player they stare at.
        if (!standing || !Visible || members == null) return;
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] == null) continue;
            Vector3 dir = lookAt - members[i].position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) continue;
            members[i].rotation = Quaternion.Slerp(members[i].rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-6f * Time.deltaTime));
        }
    }

    private void Apply()
    {
        if (members == null || seatedLooks == null) return;
        bool shown = Visible;
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] == null) continue;
            if (members[i].gameObject.activeSelf != shown) members[i].gameObject.SetActive(shown);
            if (seatedLooks[i] != null) seatedLooks[i].SetActive(!standing);
            if (standingLooks[i] != null) standingLooks[i].SetActive(standing);
            if (poses != null && poses[i] != null) poses[i].Sitting = !standing;
            if (!standing) members[i].rotation = seatedRotations[i];
        }
    }

    /// <summary>Editor tool.</summary>
    public void SetMembers(Transform[] list) => members = list;
}
