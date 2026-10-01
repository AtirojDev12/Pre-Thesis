using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[DefaultExecutionOrder(10000)]
public sealed class PlayerAnimationProbe : MonoBehaviour
{
    public GameObject prefab;
    readonly List<string> results = new List<string>();
    bool failed;
    PlayerHealth[] groundingPlayers;
    readonly Dictionary<PlayerHealth, float> worstPenetration = new Dictionary<PlayerHealth, float>();
    readonly Dictionary<PlayerHealth, int> groundingSamples = new Dictionary<PlayerHealth, int>();
    readonly Dictionary<PlayerHealth, float> worstFloating = new Dictionary<PlayerHealth, float>();
    Mesh groundingMesh;
    readonly Dictionary<PlayerHealth, List<string>> footsteps = new Dictionary<PlayerHealth, List<string>>();
    readonly Dictionary<PlayerHealth, int> previousClip = new Dictionary<PlayerHealth, int>();
    readonly Dictionary<PlayerHealth, float[]> lastFootTime = new Dictionary<PlayerHealth, float[]>();
    void CapturePose(PlayerMovement player, string name)
    {
        var cameraObject = new GameObject("Pose review camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = player.transform.position + new Vector3(3f, 0.8f, -3f);
        camera.transform.LookAt(player.transform.position);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.3f, 0.35f, 0.4f);
        var target = new RenderTexture(640, 640, 24);
        camera.targetTexture = target;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(640, 640, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        target.Release();
        Destroy(texture); Destroy(target); Destroy(cameraObject);
    }
    void LateUpdate()
    {
        if (groundingPlayers == null) return;
        if (groundingMesh == null) groundingMesh = new Mesh();
        foreach (var p in groundingPlayers)
        {
            if (p == null) continue;
            var animator = p.GetComponent<Animator>();
            float bottom = float.PositiveInfinity;
            foreach (var renderer in p.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.BakeMesh(groundingMesh);
                foreach (var vertex in groundingMesh.vertices)
                    bottom = Mathf.Min(bottom, renderer.transform.TransformPoint(vertex).y);
            }
            // Compare the visible soles to the actual test floor. During spawn
            // settling the capsule may still be above it; that is not clipping.
            float penetration = -1f - bottom;
            if (penetration > worstPenetration[p] + 0.02f)
            {
                Debug.Log("Ground penetration=" + penetration + " state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash +
                    " transition=" + animator.IsInTransition(0) + " direction=" + p.GetComponent<PlayerMovement>().AnimationDirection +
                    " capsule=" + p.GetComponent<Collider>().bounds.min.y + " mesh=" + bottom +
                    " left=" + animator.GetBoneTransform(HumanBodyBones.LeftFoot).position + " right=" + animator.GetBoneTransform(HumanBodyBones.RightFoot).position);
                if (p.isLocalPlayer) CapturePose(p.GetComponent<PlayerMovement>(), "grounding-worst");
            }
            worstPenetration[p] = Mathf.Max(worstPenetration[p], penetration);
            if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Running") && !animator.IsInTransition(0))
                if (p.GetComponent<Collider>().bounds.min.y < -0.95f)
                    worstFloating[p] = Mathf.Max(worstFloating[p], bottom + 1f);
            groundingSamples[p]++;
        }
    }
    void Check(bool pass, string text) { results.Add((pass ? "PASS " : "FAIL ") + text); failed |= !pass; }
    IEnumerator WaitForHealth(PlayerHealth[] players, int phase)
    {
        float timeout = Time.realtimeSinceStartup + 10;
        while (Time.realtimeSinceStartup < timeout)
        {
            bool ready = true;
            foreach (var p in players)
                ready &= p != null && (phase == 2 ? p.IsDead : phase == 1 ? p.IsDowned : !p.IsDowned);
            if (ready) break;
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.4f);
    }
    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        bool client = Array.IndexOf(Environment.GetCommandLineArgs(), "--client") >= 0;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Grounding test floor";
        floor.transform.position = new Vector3(0f, -1.5f, 0f);
        floor.transform.localScale = new Vector3(1000f, 1f, 1000f);
        var light = new GameObject("Pose review light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        for (int i = 0; i < 2; i++)
        {
            var spawn = new GameObject("Spawn " + i);
            spawn.transform.position = new Vector3(i * 4f, 0f, 0f);
            spawn.AddComponent<NetworkStartPosition>();
        }
        var root = new GameObject("Animation Test Network");
        var transport = root.AddComponent<kcp2k.KcpTransport>();
        transport.Port = 17996;
        var network = root.AddComponent<NetworkManager>();
        network.transport = transport;
        network.playerPrefab = prefab;
        network.networkAddress = "localhost";
        if (client) network.StartClient(); else network.StartHost();
        float timeout = Time.realtimeSinceStartup + 25;
        while ((NetworkClient.localPlayer == null || FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None).Length < 2) && Time.realtimeSinceStartup < timeout) yield return null;
        Check(NetworkClient.localPlayer != null, "Local network player spawned");
        var players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        Check(players.Length == 2, "Host and joining player spawned");
        // Let spawn gravity, initial network snapshots and the first humanoid
        // evaluation settle before measuring locomotion rather than spawn poses.
        yield return new WaitForSecondsRealtime(1);
        groundingPlayers = players;
        foreach (var p in players) { worstPenetration[p] = 0; worstFloating[p] = 0; groundingSamples[p] = 0; }
        foreach (var p in players)
        {
            footsteps[p] = new List<string>();
            previousClip[p] = -1;
            lastFootTime[p] = new[] { -999f, -999f };
            var stepper = p.GetComponent<PlayerFootsteps>();
            Check(stepper != null, "Player prefab has footsteps");
            stepper.FootstepPlayed += (foot, clip) => {
                if (clip == previousClip[p]) Check(false, "Footstep repeated the previous clip");
                if (Time.time - lastFootTime[p][foot] < 0.22f) Check(false, "Rapid repeated foot contact");
                lastFootTime[p][foot] = Time.time;
                previousClip[p] = clip;
                footsteps[p].Add(foot + ":" + clip);
            };
            var source = p.transform.Find("Left Footstep").GetComponent<AudioSource>();
            Check(source.spatialBlend == 1f && source.rolloffMode == AudioRolloffMode.Linear && source.maxDistance == 15f,
                "Footsteps use 3D attenuation with 15m cutoff");
            float savedVolume = GameSettings.SfxVolume;
            GameSettings.SfxVolume = 0.25f;
            Check(Mathf.Abs(source.volume - 0.05f) < 0.001f, "Footsteps follow SFX volume");
            GameSettings.SfxVolume = savedVolume;
        }
        if (NetworkClient.localPlayer != null)
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            var bones = new Dictionary<PlayerHealth, Transform>();
            var last = new Dictionary<PlayerHealth, Quaternion>();
            var angles = new Dictionary<PlayerHealth, float>();
            foreach (var p in players)
            {
                var animator = p.GetComponent<Animator>();
                var bone = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                bones[p] = bone; last[p] = bone.localRotation; angles[p] = 0;
            }
            foreach (bool sprint in new[] { false, true })
            {
                var stepCounts = new Dictionary<PlayerHealth, int>();
                foreach (var p in players) stepCounts[p] = footsteps[p].Count;
                foreach (var p in players) angles[p] = 0;
                var start = NetworkClient.localPlayer.transform.position; float phaseStart = Time.time; int samples = 0; int remoteSprintFrames = 0;
                for (float until = Time.time + 3; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, sprint ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
                    yield return null;
                    foreach (var p in players)
                    {
                        // Measure steady sprint, excluding the peer's transition
                        // into the next phase (process clocks are not synchronized).
                        if (sprint && !p.isLocalPlayer && Time.time - phaseStart > 1f && Time.time - phaseStart < 2.5f) { samples++; if (p.GetComponent<Animator>().GetBool("IsSprinting")) remoteSprintFrames++; }
                        angles[p] += Quaternion.Angle(last[p], bones[p].localRotation);
                        last[p] = bones[p].localRotation;
                    }
                }
                if (sprint) Check(samples > 0 && remoteSprintFrames >= samples * 0.98f, "Remote sprint stays active between network snapshots: " + remoteSprintFrames + "/" + samples);
                Check(Vector3.Distance(start, NetworkClient.localPlayer.transform.position) > 1, "Local movement " + sprint);
                foreach (var p in players)
                {
                    Check(footsteps[p].Count - stepCounts[p] >= 3,
                        (p.isLocalPlayer ? "Local" : "Remote") + " foot contacts play while " + (sprint ? "running" : "walking") + ": " + (footsteps[p].Count - stepCounts[p]));
                    for (int foot = 0; foot < 2; foot++)
                    {
                        int contacts = footsteps[p].GetRange(stepCounts[p], footsteps[p].Count - stepCounts[p]).FindAll(step => step.StartsWith(foot + ":")).Count;
                        Check(contacts >= 2 && contacts <= (sprint ? 6 : 4), (p.isLocalPlayer ? "Local" : "Remote") + " " + (sprint ? "run" : "walk") + " foot " + foot + " contacts=" + contacts);
                    }
                    var animator = p.GetComponent<Animator>();
                    string role = p.isLocalPlayer ? "local" : "remote";
                    Check(angles[p] > 20, role + " animated " + (sprint ? "sprint" : "walk") + " boneDelta=" + angles[p] + " Speed=" + animator.GetFloat("Speed") + " sprint=" + animator.GetBool("IsSprinting") + " state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
                }
            }
            var mover = NetworkClient.localPlayer.GetComponent<PlayerMovement>();
            var directions = new[] { new Vector2(0,-1), new Vector2(-1,0), new Vector2(1,0),
                new Vector2(1,-1).normalized, new Vector2(-1,1).normalized,
                new Vector2(1,1).normalized, Vector2.zero };
            var inputs = new[] { new KeyboardState(Key.S, Key.LeftShift), new KeyboardState(Key.A), new KeyboardState(Key.D),
                new KeyboardState(Key.S, Key.D), new KeyboardState(Key.W, Key.A),
                new KeyboardState(Key.W, Key.D), new KeyboardState(Key.W, Key.S, Key.A, Key.D) };
            for (int i = 0; i < directions.Length; i++)
            {
                int beforeDirectionSteps = footsteps[mover.GetComponent<PlayerHealth>()].Count;
                for (float until = Time.time + 0.5f; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, inputs[i]);
                    yield return null;
                }
                Vector3 start = mover.transform.position;
                float began = Time.time;
                Transform left = mover.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = mover.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightFoot);
                Vector3 lastLeft = left.position, lastRight = right.position;
                int plantedSamples = 0;
                float footSeparation = 0f;
                for (float until = Time.time + 0.5f; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, inputs[i]);
                    yield return null;
                    float lower = Mathf.Min(left.position.y, right.position.y);
                    if ((left.position.y - lower < 0.02f && Vector3.Distance(left.position, lastLeft) < 0.025f) ||
                        (right.position.y - lower < 0.02f && Vector3.Distance(right.position, lastRight) < 0.025f)) plantedSamples++;
                    footSeparation = Mathf.Max(footSeparation, Mathf.Abs(left.position.y - right.position.y));
                    lastLeft = left.position; lastRight = right.position;
                }
                if (i == 0)
                {
                    Check(plantedSamples > 5 && footSeparation > 0.04f, "Backward gait plants a support foot and lifts the swing foot: planted=" + plantedSamples + " lift=" + footSeparation);
                }
                Vector3 displacement = mover.transform.InverseTransformDirection(mover.transform.position - start);
                Vector2 travelled = new Vector2(displacement.x, displacement.z);
                Check((mover.AnimationDirection - directions[i]).magnitude < 0.05f, "Input direction " + i);
                Check(directions[i] == Vector2.zero ? travelled.magnitude < 0.05f :
                    Vector2.Dot(travelled.normalized, directions[i]) > 0.99f && Mathf.Abs(travelled.magnitude / (Time.time - began) - mover.CurrentMovementSpeed) < 0.3f,
                    "Correct displacement and normalized speed " + i + ": " + travelled);
                foreach (var p in players)
                {
                    Check((p.GetComponent<PlayerMovement>().AnimationDirection - directions[i]).magnitude < 0.05f,
                        (p.isLocalPlayer ? "Local" : "Remote") + " direction replicated " + i);
                    Check(!p.GetComponent<Animator>().GetBool("IsSprinting"), "Side/back step does not use forward sprint " + i);
                    Check(Mathf.Abs(p.GetComponent<Collider>().bounds.min.y + 1f) < 0.04f, "Capsule remains on physical floor " + i);
                }
                if (!client) CapturePose(mover, "direction-" + i);
                // Hold this phase while the peer finishes its measurements.
                for (float until = Time.time + 0.5f; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, inputs[i]);
                    yield return null;
                }
                if (directions[i] != Vector2.zero)
                    Check(footsteps[mover.GetComponent<PlayerHealth>()].Count > beforeDirectionSteps, "Foot contacts during direction " + i);
            }
            foreach (var phase in new[] {
                new { keys = new KeyboardState(Key.A, Key.LeftShift), state = "Strafe Run Left", direction = -1f },
                new { keys = new KeyboardState(Key.D, Key.LeftShift), state = "Strafe Run Right", direction = 1f } })
            {
                int before = footsteps[mover.GetComponent<PlayerHealth>()].Count;
                for (float until = Time.time + 1.4f; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, phase.keys);
                    yield return null;
                }
                foreach (var p in players)
                {
                    var animator = p.GetComponent<Animator>();
                    Check(animator.GetCurrentAnimatorStateInfo(0).IsName(phase.state),
                        (p.isLocalPlayer ? "Local" : "Remote") + " uses " + phase.state);
                    if (p.isLocalPlayer)
                        Check(animator.GetBool("IsSprinting") &&
                              Mathf.Sign(p.GetComponent<PlayerMovement>().AnimationDirection.x) == phase.direction,
                              "Owner strafe run direction and sprint respond immediately");
                }
                Check(footsteps[mover.GetComponent<PlayerHealth>()].Count > before,
                    "Strafe running produces foot contacts");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            for (float until = Time.time + 0.5f; Time.time < until;)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                yield return null;
            }
            foreach (var p in players)
            {
                Check(p.GetComponent<PlayerMovement>().IsCrouching &&
                      p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Crouch Idle"),
                      (p.isLocalPlayer ? "Local" : "Remote") + " crouch idle");
                Check(Mathf.Abs(p.GetComponent<CapsuleCollider>().height - 1.2f) < 0.01f,
                      "Crouch capsule height replicated");
            }
            int crouchStepStart = footsteps[mover.GetComponent<PlayerHealth>()].Count;
            for (float until = Time.time + 1.3f; Time.time < until;)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift, Key.LeftCtrl));
                yield return null;
            }
            foreach (var p in players)
                Check(p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Crouch Walk") &&
                      !p.GetComponent<Animator>().GetBool("IsSprinting"),
                      (p.isLocalPlayer ? "Local" : "Remote") + " crouch walks without sprinting");
            Check(footsteps[mover.GetComponent<PlayerHealth>()].Count == crouchStepStart,
                "Crouch walking is silent");
            Check(PlayerNoise.Local != null && PlayerNoise.Local.Game <= 0.001f,
                "Crouch walking reports zero game noise");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return new WaitForSecondsRealtime(0.3f);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.position = mover.transform.position + Vector3.up * 0.68f;
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.3f);
            Check(mover.IsCrouching, "Ceiling blocks standing");
            Destroy(ceiling);
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.4f);
            foreach (var p in players)
                Check(!p.GetComponent<PlayerMovement>().IsCrouching &&
                      Mathf.Abs(p.GetComponent<CapsuleCollider>().height - 2f) < 0.01f,
                      (p.isLocalPlayer ? "Local" : "Remote") + " stands after ceiling clears");
            var movingBody = mover.GetComponent<Rigidbody>();
            // Teleporting for the airborne input check is outside locomotion;
            // exclude the network interpolation settling frames from ground QA.
            groundingPlayers = null;
            Vector3 groundedPosition = movingBody.position;
            movingBody.position = groundedPosition + Vector3.up * 3f;
            movingBody.linearVelocity = Vector3.zero;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            Check(!mover.IsCrouching, "Cannot crouch while airborne");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            movingBody.position = groundedPosition;
            movingBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForSecondsRealtime(0.5f);
            groundingPlayers = players;
            Vector3 wallStart = mover.transform.position;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = wallStart + mover.transform.forward * 2f;
            wall.transform.rotation = mover.transform.rotation;
            wall.transform.localScale = new Vector3(2f, 4f, 0.1f);
            for (float until = Time.time + 1f; Time.time < until;)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                yield return null;
            }
            float wallTravel = Vector3.Dot(mover.transform.position - wallStart, mover.transform.forward);
            Check(wallTravel > 1f && wallTravel < 1.5f, "Sprint capsule stops at a thin wall: travel=" + wallTravel);
            int blockedSteps = footsteps[mover.GetComponent<PlayerHealth>()].Count;
            for (float until = Time.time + 0.4f; Time.time < until;)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                yield return null;
            }
            Check(footsteps[mover.GetComponent<PlayerHealth>()].Count == blockedSteps, "No footsteps while blocked by a wall");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.2f);
            Destroy(wall);
        }
        foreach (var p in players)
            Check(groundingSamples[p] > 10 && worstPenetration[p] <= 0.005f,
                (p.isLocalPlayer ? "Local" : "Remote") + " locomotion mesh stays above physical floor: penetration=" + worstPenetration[p]);
        foreach (var p in players)
            Check(worstFloating[p] < 0.035f, (p.isLocalPlayer ? "Local" : "Remote") + " walking mesh stays grounded: gap=" + worstFloating[p]);
        groundingPlayers = null;
        if (groundingMesh != null) Destroy(groundingMesh);
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return new WaitForSecondsRealtime(1);
        var idleCounts = new Dictionary<PlayerHealth, int>();
        foreach (var p in players) idleCounts[p] = footsteps[p].Count;
        yield return new WaitForSecondsRealtime(0.5f);
        foreach (var p in players) Check(footsteps[p].Count == idleCounts[p], "No footsteps while idle");
        foreach (var p in players)
            Check(p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Idle"), (p.isLocalPlayer ? "Local" : "Remote") + " returns to Idle after releasing input");
        if (!client) foreach (var p in players) p.TakeDamage(999);
        yield return WaitForHealth(players, 1);
        foreach (var p in players)
            Check(p.IsDowned && p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Dying"), (p.isLocalPlayer ? "Local" : "Remote") + " downed animation still works");
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftCtrl));
        yield return null;
        foreach (var p in players) Check(!p.GetComponent<PlayerMovement>().IsCrouching, "Cannot crouch while downed");
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        if (!client) yield return new WaitForSecondsRealtime(1);
        if (!client) foreach (var p in players) p.Revive();
        yield return WaitForHealth(players, 0);
        foreach (var p in players)
            Check(!p.IsDowned && p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Idle"), (p.isLocalPlayer ? "Local" : "Remote") + " revive returns to Idle");
        if (!client) yield return new WaitForSecondsRealtime(1);
        if (!client) foreach (var p in players) p.ServerKill("Animation regression: direct death without downing");
        yield return WaitForHealth(players, 2);
        foreach (var p in players)
        {
            var animator = p.GetComponent<Animator>();
            Check(p.IsDead && !p.IsDowned && animator.GetCurrentAnimatorStateInfo(0).IsName("Dying"),
                (p.isLocalPlayer ? "Local" : "Remote") + " direct death enters Dying state");
        }
        foreach (var p in players)
        {
            Check(footsteps[p].Count == idleCounts[p], "No footsteps while downed/dead");
            File.WriteAllLines(Path.Combine(Application.dataPath, "../footsteps-" + (client ? "client" : "host") + "-" + p.netId + ".txt"), footsteps[p]);
        }
        File.WriteAllLines(Path.Combine(Application.dataPath, "../animation-results-" + (client ? "client" : "host") + ".txt"), results);
        yield return new WaitForSecondsRealtime(4);
        Application.Quit(failed ? 1 : 0);
    }
}
