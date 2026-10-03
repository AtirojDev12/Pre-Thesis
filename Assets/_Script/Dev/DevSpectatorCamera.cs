#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// SPECTATOR CAMERA (2 Oct, Development Build only). Active while this PC is
/// connected as a spectator (Room Browser > Spectate). You have no body: nobody
/// sees you, ghosts ignore you, you cannot press E on anything.
///
///   Mouse          look
///   WASD           fly          Space / Ctrl  up / down     Shift  fast
///   E              follow next player      Q            previous player
///   F              back to free fly        Mouse wheel   zoom; past the closest = FIRST PERSON (their eyes)
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

    // 3 Oct (Mr.k): orbit camera is pulled in front of walls instead of clipping
    // through them, and scrolling in past the closest distance looks through the
    // followed player's own eyes (first person).
    private const float MinFollowDistance = 1f;
    private const float MaxFollowDistance = 10f;
    private const float CameraRadius = 0.2f;
    private float shownDistance = 3f;          // after wall pull-in, eased back out
    private bool firstPerson;
    private readonly RaycastHit[] wallHits = new RaycastHit[16];
    private FirstPersonCamera targetView;
    private PlayerVoice targetVoice;
    private PlayerMovement targetMovement;
    private PlayerHealth cachedFor;
    private PlayerHealth hiddenBodyOf;          // body hidden while in first person
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private float nextSceneSweep;

    // 3 Oct: after joining and after every scene change (lobby <-> match) the old
    // bodies are gone and the camera was left at a random spot. Now it jumps to a
    // player by itself: the same place in the player list as before, else the first.
    private bool autoFollow = true;
    private float autoFollowUntil;
    private float nextAutoFollowTry;
    private int lastFollowIndex;
    private bool wantFollow;    // false after F (free fly on purpose)
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

        // 3 Oct: no body = the player HUD (HP bar, stamina, prompts, downed text)
        // only showed its "New Text" placeholders in the middle of the screen.
        PersistentHUD.PushHidden();

        SceneManager.activeSceneChanged += OnSceneChanged;
        StartAutoFollow();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        SpectatorSession.TalkHeld = false;
        SceneManager.activeSceneChanged -= OnSceneChanged;
        PersistentHUD.PopHidden();
        ShowHiddenBody();
    }

    private void OnSceneChanged(Scene from, Scene to)
    {
        followTarget = null;
        StartAutoFollow();
    }

    private void StartAutoFollow()
    {
        autoFollow = true;
        wantFollow = true;
        autoFollowUntil = Time.unscaledTime + 30f; // players can take a while to load in
        nextAutoFollowTry = 0f;
    }

    private void TryAutoFollow()
    {
        if (Time.unscaledTime < nextAutoFollowTry) return;
        nextAutoFollowTry = Time.unscaledTime + 0.5f;
        if (Time.unscaledTime > autoFollowUntil) { autoFollow = false; return; }
        if (!CollectPlayers()) return;
        Follow(Mathf.Clamp(lastFollowIndex, 0, players.Count - 1));
        autoFollow = false;
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
            if (Mathf.Abs(scroll) > 0.01f && followTarget != null) Zoom(scroll > 0f);
        }
        if (InputFree && k != null && k.fKey.wasPressedThisFrame)
        {
            followTarget = null;
            wantFollow = false;
            autoFollow = false;
            firstPerson = false;
        }
        // The followed player left / their body was replaced: find someone again.
        if (followTarget == null && wantFollow && !autoFollow) StartAutoFollow();
        if (autoFollow) TryAutoFollow();

        UpdateHiddenBody();

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        if (followTarget != null)
        {
            CacheTargetParts();
            Vector3 head = TargetEyes();
            if (firstPerson)
            {
                // Their eyes, their look direction (body yaw + synced pitch).
                Quaternion look = followTarget.transform.rotation *
                    Quaternion.Euler(targetVoice != null ? targetVoice.LookPitch : 0f, targetVoice != null ? targetVoice.LookYawOffset : 0f, 0f);
                // Pitch arrives ~10x a second: smooth the steps out.
                Quaternion smooth = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
                transform.SetPositionAndRotation(head, smooth);
            }
            else
            {
                // Orbit behind the followed player, looking at their head, but
                // never behind a wall: pull in to the first wall in the way.
                Vector3 back = -(rotation * Vector3.forward);
                float allowed = WallFreeDistance(head, back, followDistance);
                // In at once (no clipping), back out smoothly.
                shownDistance = allowed < shownDistance ? allowed
                    : Mathf.Lerp(shownDistance, allowed, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                transform.SetPositionAndRotation(head + back * shownDistance, rotation);
            }
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
        PlayerHealth current = followTarget;
        if (!CollectPlayers()) { followTarget = null; return; }

        int index = current != null ? players.IndexOf(current) : -1;
        index = index < 0 ? (step > 0 ? 0 : players.Count - 1) : (index + step + players.Count) % players.Count;
        Follow(index);
        wantFollow = true;
        autoFollow = false;
    }

    /// <summary>Fills 'players' (sorted by netId). False if there is nobody.</summary>
    private bool CollectPlayers()
    {
        players.Clear();
        foreach (PlayerHealth p in FindObjectsByType<PlayerHealth>())
            if (p != null) players.Add(p);
        if (players.Count == 0) return false;
        players.Sort((a, b) => a.netId.CompareTo(b.netId));
        return true;
    }

    // ---- Zoom / first person / walls (3 Oct) ----------------------------------

    /// <summary>Wheel up = closer; at the closest, one more = first person. Wheel down = back out.</summary>
    private void Zoom(bool closer)
    {
        if (closer)
        {
            if (firstPerson) return;
            if (followDistance <= MinFollowDistance + 0.01f) { firstPerson = true; return; }
            followDistance = Mathf.Max(MinFollowDistance, followDistance - 0.5f);
        }
        else
        {
            if (firstPerson)
            {
                firstPerson = false;
                // Leave from behind their head, facing where they look.
                yaw = followTarget.transform.eulerAngles.y + (targetVoice != null ? targetVoice.LookYawOffset : 0f);
                pitch = Mathf.Clamp(targetVoice != null ? targetVoice.LookPitch : 15f, -85f, 85f);
                followDistance = shownDistance = MinFollowDistance;
                return;
            }
            followDistance = Mathf.Min(MaxFollowDistance, followDistance + 0.5f);
        }
    }

    private void CacheTargetParts()
    {
        if (cachedFor == followTarget) return;
        cachedFor = followTarget;
        targetView = followTarget != null ? followTarget.GetComponentInChildren<FirstPersonCamera>(true) : null;
        targetVoice = followTarget != null ? followTarget.GetComponent<PlayerVoice>() : null;
        targetMovement = followTarget != null ? followTarget.GetComponent<PlayerMovement>() : null;
    }

    private Vector3 TargetEyes()
    {
        bool crouching = targetMovement != null && targetMovement.IsCrouching;
        return targetView != null ? targetView.EyePosition(crouching) : followTarget.transform.position + Vector3.up * 1.6f;
    }

    /// <summary>How far the camera can sit from 'from' along 'dir' before touching a wall (players ignored).</summary>
    private float WallFreeDistance(Vector3 from, Vector3 dir, float wanted)
    {
        int count = Physics.SphereCastNonAlloc(from, CameraRadius, dir, wallHits, wanted, ~0, QueryTriggerInteraction.Ignore);
        float best = wanted;
        for (int i = 0; i < count; i++)
        {
            Collider c = wallHits[i].collider;
            if (c == null || c.GetComponentInParent<PlayerHealth>() != null) continue; // bodies never push the camera
            if (wallHits[i].distance < best) best = wallHits[i].distance;
        }
        return Mathf.Max(0f, best);
    }

    // First person: the followed body is hidden on THIS screen only (we are inside its head).
    private void UpdateHiddenBody()
    {
        PlayerHealth want = firstPerson ? followTarget : null;
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

    private void Follow(int index)
    {
        followTarget = players[index];
        lastFollowIndex = index;
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
        string text = $"<b><color=#7FD7FF>SPECTATING</color></b>  ·  {FollowName()}{(firstPerson && followTarget != null ? "  (first person)" : "")}{talk}\n" +
                      "<color=#DDDDDD>Hold LMB talk · E / Q next / previous player · Wheel zoom → first person · F free fly · WASD Space Ctrl Shift · M lobby · Esc menu · F1 cheats</color>";
        GUI.Label(new Rect(20f * scale, Screen.height - 90f * scale, 1400f * scale, 80f * scale), text, style);
    }
}
#endif
