using System.Collections;
using System.Collections.Generic;
using EpicTransport;
using Mirror;
using UnityEngine;

/// <summary>
/// Links a player body to their EOS voice, and carries the Walkie-Talkie
/// "transmitting" flag. Lives on Player.prefab.
///
/// VOICE RULES (13RoH)
///   - Proximity voice is ALWAYS ON. The mic cannot be switched off in
///     Settings. It is muted only while the player has the Esc menu open.
///   - The voice plays in 3D from this player's mouth (VoicePlayback).
///   - Walkie-Talkie: hold the talk key with a switched-on walkie IN HAND
///     and every player who CARRIES a switched-on walkie (any slot, no need to
///     hold it) also hears you, at any distance.
///
/// NOISE
///   Adds a <see cref="PlayerNoise"/> to the body at runtime and carries its
///   numbers from the owner to the server (CmdReportNoise), so ghost code on
///   the server can read how loud each player is.
///
/// AUTHORITY
///   The EOS id and the transmit flag are SyncVars written by the server only.
///   A client asks with a Command; the server re-checks the inventory before
///   letting anyone onto the radio.
/// </summary>
[RequireComponent(typeof(NetworkIdentity))]
[DisallowMultipleComponent]
public class PlayerVoice : NetworkBehaviour
{
    /// <summary>Every spawned player body on this machine. Read by VoiceChatManager.</summary>
    public static readonly List<PlayerVoice> All = new List<PlayerVoice>();

    /// <summary>This machine's own player (null outside a match).</summary>
    public static PlayerVoice Local { get; private set; }

    [Tooltip("Height of the mouth above the player's pivot. The 3D voice plays from here.")]
    [SerializeField] private float mouthHeight = 1.6f;

    /// <summary>EOS ProductUserId of the person controlling this body.</summary>
    [SyncVar] private string productUserId = string.Empty;

    /// <summary>True while talking on the Walkie-Talkie.</summary>
    [SyncVar] private bool radioTransmitting;

    // 3 Oct (Mr.k): where this player is looking, for the spectator's first-person
    // view. Body yaw already syncs (NetworkTransform); this adds up/down + the
    // downed head turn. Owner -> server (unreliable, max 10x/s, only on change).
    [SyncVar] private float lookPitch;
    [SyncVar] private float lookYawOffset;
    private FirstPersonCamera ownView;
    private float lastSentPitch = float.NaN, lastSentYaw, nextLookSend, lookResendAt;

    /// <summary>Up/down look angle (degrees) on any machine.</summary>
    public float LookPitch => isLocalPlayer && ownView != null ? ownView.LookPitch : lookPitch;
    /// <summary>Extra left/right look while downed (degrees) on any machine.</summary>
    public float LookYawOffset => isLocalPlayer && ownView != null ? ownView.LookYawOffset : lookYawOffset;

    private Transform mouth;
    private PlayerInventory inventory;
    private PlayerHealth health;
    private PlayerNoise noise;

    public string ProductUserId => productUserId;
    public bool RadioTransmitting => radioTransmitting;
    public Transform Mouth => mouth;
    public PlayerNoise Noise => noise;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        Local = null;
    }

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<PlayerHealth>();

        // Plain MonoBehaviour added in code: the prefab needs no change, and the
        // network part goes through this NetworkBehaviour's Command below.
        noise = GetComponent<PlayerNoise>();
        if (noise == null) noise = gameObject.AddComponent<PlayerNoise>();
        noise.Init(this);

        // A dedicated, always-active child: the camera object of a remote
        // player may be disabled, and a disabled object plays no audio.
        var go = new GameObject("Voice Mouth");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, mouthHeight, 0f);
        mouth = go.transform;
    }

    public override void OnStartClient()
    {
        if (!All.Contains(this)) All.Add(this);

        // Only YOUR camera may listen. Remote players' cameras kept their
        // AudioListener enabled ("3 listeners in scene"), so Unity could hear
        // the 3D voices from another player's head.
        if (!isLocalPlayer)
            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
    }

    public override void OnStopClient() => All.Remove(this);

    public override void OnStartLocalPlayer()
    {
        Local = this;
        StartCoroutine(SendProductUserIdWhenReady());
    }

    private void OnDestroy()
    {
        All.Remove(this);
        if (Local == this) Local = null;
    }

    private IEnumerator SendProductUserIdWhenReady()
    {
        // EOS logs in once at startup; by the time a match runs it is ready,
        // but wait anyway rather than send an empty id.
        while (!EOSSDKComponent.IsReady || string.IsNullOrEmpty(EOSSDKComponent.LocalUserProductIdString))
            yield return null;

        CmdSetProductUserId(EOSSDKComponent.LocalUserProductIdString);
    }

    [Command]
    private void CmdSetProductUserId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return;
        productUserId = id;
    }

    // ---- Noise (for the noise meter and, later, ghosts) -------------------------

    /// <summary>Owner only: send the latest loudness (0..255) to the server. Called by PlayerNoise ~10x a second.</summary>
    public void SendNoise(byte mic, byte game)
    {
        if (NetworkMode.IsOffline)
        {
            noise.ServerReceive(mic, game); // solo test scene: this machine is the "server"
            return;
        }
        if (!isLocalPlayer || !NetworkClient.ready) return;
        CmdReportNoise(mic, game);
    }

    // Unreliable: the owner re-sends every 0.5 s anyway, so a lost packet only
    // delays the value a little, and a slow reliable queue can never build up.
    [Command(channel = Channels.Unreliable)]
    private void CmdReportNoise(byte mic, byte game) => noise.ServerReceive(mic, game);

    // ---- Look direction (spectator first-person view) ----------------------------

    private void LateUpdate()
    {
        if (!isLocalPlayer || NetworkMode.IsOffline || !NetworkClient.ready) return;
        if (ownView == null) ownView = GetComponentInChildren<FirstPersonCamera>(true);
        if (ownView == null || Time.unscaledTime < nextLookSend) return;

        float p = ownView.LookPitch, y = ownView.LookYawOffset;
        bool changed = float.IsNaN(lastSentPitch) || Mathf.Abs(p - lastSentPitch) > 0.5f || Mathf.Abs(y - lastSentYaw) > 0.5f;
        if (!changed && Time.unscaledTime < lookResendAt) return;

        nextLookSend = Time.unscaledTime + 0.1f;  // max 10 per second
        lookResendAt = Time.unscaledTime + 1f;    // and once a second anyway (unreliable)
        lastSentPitch = p;
        lastSentYaw = y;
        CmdReportLook(p, y);
    }

    [Command(channel = Channels.Unreliable)]
    private void CmdReportLook(float pitch, float yawOffset)
    {
        if (float.IsNaN(pitch) || float.IsInfinity(pitch) || float.IsNaN(yawOffset) || float.IsInfinity(yawOffset)) return;
        lookPitch = Mathf.Clamp(pitch, -89f, 89f);
        lookYawOffset = Mathf.Clamp(yawOffset, -180f, 180f);
    }

    // ---- Walkie-Talkie ---------------------------------------------------------

    /// <summary>Local player only: start / stop talking on the radio.</summary>
    public void RequestRadioTransmit(bool on)
    {
        if (!isLocalPlayer || NetworkMode.IsOffline) return;

        // Called every frame: only send when the wish changes, not while the
        // answer is still travelling back from the server.
        if (on == lastRadioRequest) return;
        lastRadioRequest = on;
        CmdSetRadioTransmit(on);
    }

    private bool lastRadioRequest;

    [Command]
    private void CmdSetRadioTransmit(bool on)
    {
        radioTransmitting = on && ServerCanTransmit();
    }

    /// <summary>
    /// SERVER. Call after anything that could take the radio away (slot changed,
    /// walkie switched off, item lost, player downed).
    /// </summary>
    public void ServerRefreshRadio()
    {
        if (!isServer) return;
        if (radioTransmitting && !ServerCanTransmit()) radioTransmitting = false;
    }

    private bool ServerCanTransmit()
    {
        if (inventory == null || !inventory.IsHoldingPoweredRadio) return false;
        if (health != null && (health.IsDead || health.IsDowned)) return false;
        return true;
    }

    [ServerCallback]
    private void Update()
    {
        // Cheap safety net: downed / dead players drop off the radio even if
        // nothing else told us.
        if (radioTransmitting && health != null && (health.IsDead || health.IsDowned))
            radioTransmitting = false;
    }
}
