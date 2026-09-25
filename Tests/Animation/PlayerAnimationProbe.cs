using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class PlayerAnimationProbe : MonoBehaviour
{
    public GameObject prefab;
    readonly List<string> results = new List<string>();
    bool failed;
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
        if (NetworkClient.localPlayer != null)
        {
            foreach (var p in players) p.GetComponent<Rigidbody>().useGravity = false;
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
                foreach (var p in players) angles[p] = 0;
                var start = NetworkClient.localPlayer.transform.position; float phaseStart = Time.time; int samples = 0; int remoteSprintFrames = 0;
                for (float until = Time.time + 3; Time.time < until;)
                {
                    InputSystem.QueueStateEvent(keyboard, sprint ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
                    yield return null;
                    foreach (var p in players)
                    {
                        if (sprint && !p.isLocalPlayer && Time.time - phaseStart > 1f) { samples++; if (p.GetComponent<Animator>().GetBool("IsSprinting")) remoteSprintFrames++; }
                        angles[p] += Quaternion.Angle(last[p], bones[p].localRotation);
                        last[p] = bones[p].localRotation;
                    }
                }
                if (sprint) Check(samples > 0 && remoteSprintFrames >= samples * 0.98f, "Remote sprint stays active between network snapshots: " + remoteSprintFrames + "/" + samples);
                Check(Vector3.Distance(start, NetworkClient.localPlayer.transform.position) > 1, "Local movement " + sprint);
                foreach (var p in players)
                {
                    var animator = p.GetComponent<Animator>();
                    string role = p.isLocalPlayer ? "local" : "remote";
                    Check(angles[p] > 20, role + " animated " + (sprint ? "sprint" : "walk") + " boneDelta=" + angles[p] + " Speed=" + animator.GetFloat("Speed") + " sprint=" + animator.GetBool("IsSprinting") + " state=" + animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
                }
            }
        }
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        yield return new WaitForSecondsRealtime(1);
        foreach (var p in players)
            Check(p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Idle"), (p.isLocalPlayer ? "Local" : "Remote") + " returns to Idle after releasing input");
        if (!client) foreach (var p in players) p.TakeDamage(999);
        yield return WaitForHealth(players, 1);
        foreach (var p in players)
            Check(p.IsDowned && p.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Dying"), (p.isLocalPlayer ? "Local" : "Remote") + " downed animation still works");
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
        File.WriteAllLines(Path.Combine(Application.dataPath, "../animation-results-" + (client ? "client" : "host") + ".txt"), results);
        yield return new WaitForSecondsRealtime(4);
        Application.Quit(failed ? 1 : 0);
    }
}



