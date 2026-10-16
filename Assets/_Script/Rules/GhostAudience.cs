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
/// SERVER AUTHORITY: visible / standing / where they look are SyncVars.
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

    [SyncVar(hook = nameof(OnStateChanged))] private bool visible = true;
    [SyncVar(hook = nameof(OnStateChanged))] private bool standing;
    [SyncVar] private Vector3 lookAt;

    private Quaternion[] seatedRotations;
    private GameObject[] seatedLooks;
    private GameObject[] standingLooks;

    public bool Visible => visible;
    public bool Standing => standing;
    public int Count => members != null ? members.Length : 0;
    public Transform Member(int i) => members[i];

    /// <summary>Head point of member i (for "is the player looking at it").</summary>
    public Vector3 HeadOf(int i) => members[i].position + Vector3.up * (standing ? standingHeadHeight : seatedHeadHeight);

    private void Awake()
    {
        int n = Count;
        seatedRotations = new Quaternion[n];
        seatedLooks = new GameObject[n];
        standingLooks = new GameObject[n];
        for (int i = 0; i < n; i++)
        {
            if (members[i] == null) continue;
            seatedRotations[i] = members[i].rotation;
            Transform s = members[i].Find("Seated");
            Transform u = members[i].Find("Standing");
            seatedLooks[i] = s != null ? s.gameObject : null;
            standingLooks[i] = u != null ? u.gameObject : null;
            // Members never block walking or the interaction ray.
            foreach (Collider c in members[i].GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
        Apply();
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
        if (!standing || !visible || members == null) return;
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
        for (int i = 0; i < members.Length; i++)
        {
            if (members[i] == null) continue;
            if (members[i].gameObject.activeSelf != visible) members[i].gameObject.SetActive(visible);
            if (seatedLooks[i] != null) seatedLooks[i].SetActive(!standing);
            if (standingLooks[i] != null) standingLooks[i].SetActive(standing);
            if (!standing) members[i].rotation = seatedRotations[i];
        }
    }

    /// <summary>Editor tool.</summary>
    public void SetMembers(Transform[] list) => members = list;
}
