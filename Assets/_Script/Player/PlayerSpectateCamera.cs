using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 31 Oct 2026 (Mr.k): SPECTATE for players whose round is over (escaped or dead).
/// Results screen > "Watch Teammates". Every build (not a dev tool).
///
/// RULES
///   - Always locked on a LIVING player still in the round. No free fly.
///   - Orbit behind them (mouse = look around). Wheel up past the closest = their eyes
///     (first person). Wheel down = back out.
///   - Interact key (E) = next player, Throw key (Q) = previous player. Esc = back to results.
///   - Nobody left alive / the round ended = back to the results screen by itself.
///
/// SOUND + VOICE
///   - The camera carries the AudioListener: you hear the game where the camera is.
///   - The server sends you the voices the watched player hears (PlayerVoice.RequestSpectate
///     -> VoiceNetwork), played 3D at the talkers. Their walkie too, if they carry one that is on.
///   - Your own mic still works, but only dead / escaped players hear you (as before):
///     living players NEVER hear spectators.
///
/// Local only, except telling the server who you watch (for the voices).
/// </summary>
public sealed class PlayerSpectateCamera : MonoBehaviour
{
    private static PlayerSpectateCamera instance;

    /// <summary>True while this machine's player is spectating.</summary>
    public static bool Active => instance != null;

    private const float MinFollowDistance = 1f;
    private const float MaxFollowDistance = 8f;
    private const float CameraRadius = 0.2f;

    private Camera cam;
    private AudioListener listener;
    private float yaw, pitch = 15f;
    private float followDistance = 3f, shownDistance = 3f;
    private bool firstPerson;
    private PlayerHealth target;
    private FirstPersonCamera targetView;
    private PlayerVoice targetVoice;
    private PlayerMovement targetMovement;
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();
    private readonly RaycastHit[] wallHits = new RaycastHit[16];
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private PlayerHealth hiddenBodyOf;
    private readonly List<Behaviour> turnedOff = new List<Behaviour>(); // other cameras / listeners we switched off
    private float nextSweep;
    private uint sentTarget = uint.MaxValue;

    // ---- Start / stop -------------------------------------------------------------

    /// <summary>Start watching (results screen button). False if there is nobody alive to watch.</summary>
    public static bool Begin()
    {
        if (instance != null) return true;
        if (!AnyoneToWatch()) return false;
        var go = new GameObject("Spectate Camera");
        instance = go.AddComponent<PlayerSpectateCamera>();
        return true;
    }

    /// <summary>Stop watching (Esc, or nobody left). The results screen comes back.</summary>
    public static void End()
    {
        if (instance != null) Destroy(instance.gameObject);
    }

    /// <summary>Is there a living player still in the round (not me)?</summary>
    public static bool AnyoneToWatch()
    {
        foreach (PlayerHealth p in PlayerRegistryOrScene())
            if (IsWatchable(p)) return true;
        return false;
    }

    private static IEnumerable<PlayerHealth> PlayerRegistryOrScene() => FindObjectsByType<PlayerHealth>();

    private static bool IsWatchable(PlayerHealth p)
    {
        if (p == null || p.IsDead || p == PlayerHealth.LocalInstance) return false;
        MatchDirector match = MatchDirector.Instance;
        if (match == null || match.Phase == MatchPhase.Ended) return false;
        return !match.IsFinished(p.netId);
    }

    private void Awake()
    {
        cam = gameObject.AddComponent<Camera>();
        cam.depth = 100f;
        cam.nearClipPlane = 0.05f;
        listener = gameObject.AddComponent<AudioListener>();
        TurnOffOtherViews();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PickTarget(0, +1);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        ShowHiddenBody();
        // Give the screen and the ears back to whatever we switched off.
        foreach (Behaviour b in turnedOff) if (b != null) b.enabled = true;
        turnedOff.Clear();
        SendTarget(0);
    }

    // ---- Every frame -------------------------------------------------------------------

    private bool InputFree => !PauseMenuController.IsOpen && !OverlayPanels.AnyOpen;

    private void Update()
    {
        // Round over / results screen gone: stop.
        if (!MatchResultsUI.IsShowing) { End(); return; }
        if (Time.unscaledTime >= nextSweep)
        {
            nextSweep = Time.unscaledTime + 0.5f;
            TurnOffOtherViews();
        }
        // The watched player died / escaped / left: next living player, or back to results.
        if (!IsWatchable(target) && !PickTarget(0, +1)) { End(); return; }

        Keyboard k = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (InputFree)
        {
            if (Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (k != null && k.escapeKey.wasPressedThisFrame) { End(); return; }
            if (Keybinds.WasPressed(GameAction.Interact)) PickTarget(+1, +1);
            if (Keybinds.WasPressed(GameAction.ThrowOrDrop)) PickTarget(-1, -1);
            if (mouse != null)
            {
                Vector2 look = mouse.delta.ReadValue() * 0.1f;
                yaw += look.x;
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) Zoom(scroll > 0f);
            }
        }

        SendTarget(target != null ? target.netId : 0);
        UpdateHiddenBody();
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        if (target == null) return;
        Vector3 head = TargetEyes();
        if (firstPerson)
        {
            Quaternion look = target.transform.rotation *
                Quaternion.Euler(targetVoice != null ? targetVoice.LookPitch : 0f, targetVoice != null ? targetVoice.LookYawOffset : 0f, 0f);
            Quaternion smooth = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            transform.SetPositionAndRotation(head, smooth);
            return;
        }
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = -(rotation * Vector3.forward);
        float allowed = WallFreeDistance(head, back, followDistance);
        shownDistance = allowed < shownDistance ? allowed
            : Mathf.Lerp(shownDistance, allowed, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
        transform.SetPositionAndRotation(head + back * shownDistance, rotation);
    }

    // ---- Targets ------------------------------------------------------------------------

    /// <summary>Step through living players (sorted by netId). step 0 = first valid. False if nobody.</summary>
    private bool PickTarget(int step, int direction)
    {
        players.Clear();
        foreach (PlayerHealth p in FindObjectsByType<PlayerHealth>())
            if (IsWatchable(p)) players.Add(p);
        if (players.Count == 0) { target = null; return false; }
        players.Sort((a, b) => a.netId.CompareTo(b.netId));

        int index = target != null ? players.IndexOf(target) : -1;
        if (index < 0) index = direction > 0 ? 0 : players.Count - 1;
        else index = (index + step + players.Count) % players.Count;

        PlayerHealth next = players[index];
        if (next != target)
        {
            target = next;
            targetView = target.GetComponentInChildren<FirstPersonCamera>(true);
            targetVoice = target.GetComponent<PlayerVoice>();
            targetMovement = target.GetComponent<PlayerMovement>();
            yaw = target.transform.eulerAngles.y;
            pitch = 15f;
        }
        return true;
    }

    private void SendTarget(uint netId)
    {
        if (netId == sentTarget) return;
        sentTarget = netId;
        if (PlayerVoice.Local != null) PlayerVoice.Local.RequestSpectate(netId);
    }

    private string TargetName()
    {
        if (target == null) return "-";
        MatchDirector match = MatchDirector.Instance;
        if (match != null)
            for (int i = 0; i < match.RoundPlayerCount; i++)
                if (match.RoundPlayerAt(i).netId == target.netId) return match.RoundPlayerAt(i).name;
        return "Player";
    }

    // ---- Zoom, walls, first person (same as the dev spectator) --------------------------

    private void Zoom(bool closer)
    {
        if (closer)
        {
            if (firstPerson) return;
            if (followDistance <= MinFollowDistance + 0.01f) { firstPerson = true; return; }
            followDistance = Mathf.Max(MinFollowDistance, followDistance - 0.5f);
            return;
        }
        if (firstPerson)
        {
            firstPerson = false;
            yaw = target != null ? target.transform.eulerAngles.y + (targetVoice != null ? targetVoice.LookYawOffset : 0f) : yaw;
            pitch = Mathf.Clamp(targetVoice != null ? targetVoice.LookPitch : 15f, -85f, 85f);
            followDistance = shownDistance = MinFollowDistance;
            return;
        }
        followDistance = Mathf.Min(MaxFollowDistance, followDistance + 0.5f);
    }

    private Vector3 TargetEyes()
    {
        bool crouching = targetMovement != null && targetMovement.IsCrouching;
        return targetView != null ? targetView.EyePosition(crouching) : target.transform.position + Vector3.up * 1.6f;
    }

    private float WallFreeDistance(Vector3 from, Vector3 dir, float wanted)
    {
        int count = Physics.SphereCastNonAlloc(from, CameraRadius, dir, wallHits, wanted, ~0, QueryTriggerInteraction.Ignore);
        float best = wanted;
        for (int i = 0; i < count; i++)
        {
            Collider c = wallHits[i].collider;
            if (c == null || c.GetComponentInParent<PlayerHealth>() != null) continue;
            if (wallHits[i].distance < best) best = wallHits[i].distance;
        }
        return Mathf.Max(0f, best);
    }

    private void UpdateHiddenBody()
    {
        PlayerHealth want = firstPerson ? target : null;
        if (want == hiddenBodyOf) return;
        ShowHiddenBody();
        if (want == null) return;
        hiddenBodyOf = want;
        foreach (Renderer r in want.GetComponentsInChildren<Renderer>(true))
        {
            if (r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            hiddenRenderers.Add(r);
        }
    }

    private void ShowHiddenBody()
    {
        foreach (Renderer r in hiddenRenderers) if (r != null) r.forceRenderingOff = false;
        hiddenRenderers.Clear();
        hiddenBodyOf = null;
    }

    /// <summary>Other cameras and listeners (your own body's view) must not compete with ours.</summary>
    private void TurnOffOtherViews()
    {
        foreach (Camera other in FindObjectsByType<Camera>())
        {
            if (other == cam || !other.enabled || other.targetTexture != null) continue; // keep TV / render-texture cameras
            other.enabled = false;
            turnedOff.Add(other);
        }
        foreach (AudioListener other in FindObjectsByType<AudioListener>())
        {
            if (other == listener || !other.enabled) continue;
            other.enabled = false;
            turnedOff.Add(other);
        }
    }

    private void OnGUI()
    {
        float scale = Screen.height / 1080f;
        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(24f * scale), richText = true };
        string text = $"<b><color=#7FD7FF>WATCHING</color></b>  {TargetName()}{(firstPerson ? "  (first person)" : "")}\n" +
                      $"<color=#DDDDDD>[{Keybinds.KeyName(GameAction.Interact)}] next · [{Keybinds.KeyName(GameAction.ThrowOrDrop)}] previous · " +
                      "Wheel zoom / first person · [Esc] back to results · Only dead / escaped players hear you</color>";
        GUI.Label(new Rect(20f * scale, Screen.height - 90f * scale, 1600f * scale, 80f * scale), text, style);
    }
}
