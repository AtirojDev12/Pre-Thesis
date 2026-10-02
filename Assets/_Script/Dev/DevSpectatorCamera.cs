#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SPECTATOR CAMERA (2 Oct, Development Build only). Active while this PC is
/// connected as a spectator (Room Browser > Spectate). You have no body: nobody
/// sees you, ghosts ignore you, you cannot press E on anything.
///
///   Mouse          look
///   WASD           fly          Space / Ctrl  up / down     Shift  fast
///   E              follow next player      Q            previous player
///   F              back to free fly        Mouse wheel   follow distance
///   M              lobby menu (Leave)      Esc           pause menu (Leave)
///   F1             cheat panel (server cheats only if the host is a dev build)
///   Left mouse (hold)  push-to-talk: PLAYERS near the camera hear you in 3D,
///                      other spectators hear you flat (2D)
///
/// The camera carries the AudioListener, so you hear the game (and player voice,
/// relayed by the server to spectators) from where the camera is.
/// </summary>
public sealed class DevSpectatorCamera : MonoBehaviour
{
    private static DevSpectatorCamera instance;

    private Camera cam;
    private AudioListener listener;
    private float yaw, pitch;
    private float followDistance = 3f;
    private PlayerHealth followTarget;
    private float nextSceneSweep;
    private readonly List<PlayerHealth> players = new List<PlayerHealth>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Watch()
    {
        var go = new GameObject("Dev Spectator Watcher");
        DontDestroyOnLoad(go);
        go.AddComponent<Watcher>();
    }

    /// <summary>Creates / removes the camera as spectating starts / stops.</summary>
    private sealed class Watcher : MonoBehaviour
    {
        private void Update()
        {
            bool want = SpectatorSession.Active && NetworkClient.isConnected && NetworkClient.localPlayer == null;
            if (want && instance == null)
            {
                var go = new GameObject("Spectator Camera (dev)");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<DevSpectatorCamera>();
            }
            else if (!want && instance != null)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }
    }

    private void Awake()
    {
        cam = gameObject.AddComponent<Camera>();
        cam.depth = 100f;            // above any scene camera
        cam.nearClipPlane = 0.05f;
        gameObject.tag = "MainCamera";
        listener = gameObject.AddComponent<AudioListener>();
        transform.position = new Vector3(0f, 3f, -10f);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        SpectatorSession.TalkHeld = false;
    }

    private bool InputFree =>
        !GameplayInput.Blocked && !OverlayPanels.AnyOpen && !PauseMenuController.IsOpen &&
        Cursor.lockState == CursorLockMode.Locked;

    private void Update()
    {
        // Scene cameras / listeners from the loaded scene must not compete with ours.
        if (Time.unscaledTime >= nextSceneSweep)
        {
            nextSceneSweep = Time.unscaledTime + 0.5f;
            foreach (Camera other in FindObjectsByType<Camera>())
            {
                if (other == cam || !other.enabled) continue;
                AudioListener otherListener = other.GetComponent<AudioListener>();
                if (otherListener != null && otherListener.enabled) otherListener.enabled = false;
            }
            // Re-lock the mouse after a scene change / closed menu.
            if (!GameplayInput.Blocked && !OverlayPanels.AnyOpen && !PauseMenuController.IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        Mouse mouse = Mouse.current;
        Keyboard k = Keyboard.current;

        // Push-to-talk = hold LEFT mouse. Menus / panels open (mouse free) = not talking.
        SpectatorSession.TalkHeld = mouse != null && mouse.leftButton.isPressed && InputFree;
        if (InputFree && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * 0.1f;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);

            if (k != null && k.eKey.wasPressedThisFrame) Cycle(+1);
            if (k != null && k.qKey.wasPressedThisFrame) Cycle(-1);
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) followDistance = Mathf.Clamp(followDistance - Mathf.Sign(scroll) * 0.5f, 1f, 10f);
        }
        if (InputFree && k != null && k.fKey.wasPressedThisFrame) followTarget = null;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        if (followTarget != null)
        {
            // Orbit behind the followed player, looking at their head.
            Vector3 head = followTarget.transform.position + Vector3.up * 1.6f;
            transform.SetPositionAndRotation(head - rotation * Vector3.forward * followDistance, rotation);
            SpectatorSession.VoicePosition = transform.position;
            return;
        }

        transform.rotation = rotation;
        SpectatorSession.VoicePosition = transform.position;
        if (!InputFree || k == null) return;
        Vector3 move = Vector3.zero;
        if (k.wKey.isPressed) move += transform.forward;
        if (k.sKey.isPressed) move -= transform.forward;
        if (k.dKey.isPressed) move += transform.right;
        if (k.aKey.isPressed) move -= transform.right;
        if (k.spaceKey.isPressed) move += Vector3.up;
        if (k.leftCtrlKey.isPressed) move -= Vector3.up;
        float speed = k.leftShiftKey.isPressed ? 18f : 6f;
        transform.position += move.normalized * speed * Time.unscaledDeltaTime;
        SpectatorSession.VoicePosition = transform.position;
    }

    private void Cycle(int step)
    {
        players.Clear();
        foreach (PlayerHealth p in FindObjectsByType<PlayerHealth>())
            if (p != null) players.Add(p);
        if (players.Count == 0) { followTarget = null; return; }
        players.Sort((a, b) => a.netId.CompareTo(b.netId));

        int index = followTarget != null ? players.IndexOf(followTarget) : -1;
        index = index < 0 ? (step > 0 ? 0 : players.Count - 1) : (index + step + players.Count) % players.Count;
        followTarget = players[index];
        yaw = followTarget.transform.eulerAngles.y;
        pitch = 15f;
    }

    private string FollowName()
    {
        if (followTarget == null) return "free fly";
        MatchDirector match = MatchDirector.Instance;
        if (match != null)
            for (int i = 0; i < match.RoundPlayerCount; i++)
                if (match.RoundPlayerAt(i).netId == followTarget.netId) return match.RoundPlayerAt(i).name;
        return "player " + followTarget.netId;
    }

    private void OnGUI()
    {
        float scale = Screen.height / 1080f;
        var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22f * scale), richText = true };
        string talk = SpectatorSession.TalkHeld ? "   <b><color=#73FF8C>● TALKING</color></b>" : "";
        string text = $"<b><color=#7FD7FF>SPECTATING</color></b>  ·  {FollowName()}{talk}\n" +
                      "<color=#DDDDDD>Hold LMB talk · E next player · Q previous player · F free fly · WASD Space Ctrl Shift · M lobby · Esc menu · F1 cheats</color>";
        GUI.Label(new Rect(20f * scale, Screen.height - 90f * scale, 1400f * scale, 80f * scale), text, style);
    }
}
#endif
