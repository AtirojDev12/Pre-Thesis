using Mirror;
using TMPro;
using UnityEngine;

/// <summary>
/// 31 Oct 2026 (Mr.k): the player's name floats above their head IN THE LOBBY.
///
/// Added by PlayerVoice.OnStartClient on every OTHER player's body (you never see your own).
/// Local only: reads the seat name (RoHRoomPlayer.DisplayName) that is already synced.
/// The body is linked to its seat by RoHRoomPlayer.TalkerNetId (the body's netId).
/// Hidden in the match, and hidden while no name is known yet.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNameTag : MonoBehaviour
{
    private const float Height = 2.15f;     // metres above the feet
    private const float FontSize = 2.2f;    // world text size
    private const float CheckInterval = 0.5f;

    private NetworkIdentity identity;
    private PlayerHealth health;
    private Transform tagRoot;
    private TextMeshPro label;
    private float nextCheck;
    private bool visible;

    private void Awake()
    {
        identity = GetComponent<NetworkIdentity>();
        health = GetComponent<PlayerHealth>();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckInterval;

        string name = PlayerInventory.InLobby ? SeatName() : null;
        bool show = !string.IsNullOrEmpty(name) && (health == null || !health.IsDead);
        if (show && tagRoot == null) Build();
        if (tagRoot == null) return;

        if (show && label.text != name) label.text = name;
        if (visible != show)
        {
            visible = show;
            tagRoot.gameObject.SetActive(show);
        }
    }

    private void LateUpdate()
    {
        if (!visible || tagRoot == null) return;
        // Always face the camera that is rendering (the local player's view).
        Camera cam = Camera.main;
        if (cam == null && PlayerVoice.Local != null) cam = PlayerVoice.Local.GetComponentInChildren<Camera>();
        if (cam == null) return;
        tagRoot.position = transform.position + Vector3.up * Height;
        tagRoot.rotation = Quaternion.LookRotation(tagRoot.position - cam.transform.position, Vector3.up);
    }

    private string SeatName()
    {
        if (identity == null || identity.netId == 0) return null;
        var seats = RoHRoomPlayer.All;
        for (int i = 0; i < seats.Count; i++)
        {
            RoHRoomPlayer seat = seats[i];
            if (seat != null && seat.TalkerNetId == identity.netId) return seat.DisplayName;
        }
        return null;
    }

    private void Build()
    {
        tagRoot = new GameObject("Name Tag").transform;
        tagRoot.SetParent(transform, false);
        tagRoot.localPosition = Vector3.up * Height;
        label = tagRoot.gameObject.AddComponent<TextMeshPro>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = FontSize;
        label.color = Color.white;
        label.outlineWidth = 0.2f;
        label.outlineColor = new Color32(0, 0, 0, 200);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.rectTransform.sizeDelta = new Vector2(4f, 0.6f);
        tagRoot.gameObject.SetActive(false);
        visible = false;
    }

    private void OnDestroy()
    {
        if (tagRoot != null) Destroy(tagRoot.gameObject);
    }
}
