using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Run only in the isolated project. Captures real Mirror serialized batches;
// host delivery uses Mirror's local connection, remote recipients use capture connections.
public static class AudioNetworkChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> results = new List<string>();
    static int hostCount;
    static WorldSoundMessage hostMessage;
    static void Check(bool pass, string name)
    {
        results.Add((pass ? "PASS " : "FAIL ") + name);
        if (!pass) throw new Exception(name);
    }
    static void Call(object obj, string name, params object[] args) =>
        obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    static void Set(object obj, string name, object value) =>
        obj.GetType().GetField(name, Private).SetValue(obj, value);
    static void ServerActive(bool value) => typeof(NetworkServer).GetProperty("active").SetValue(null, value);
    static void FlushHost() => Call(NetworkClient.connection, "Update");
    static bool HasReceiver() => ((System.Collections.IDictionary)typeof(NetworkClient)
        .GetField("handlers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
        .Contains(NetworkMessageId<WorldSoundMessage>.Id);

    sealed class CaptureConnection : NetworkConnectionToClient
    {
        public readonly List<WorldSoundMessage> messages = new List<WorldSoundMessage>();
        public CaptureConnection(int id, bool ready) : base(id) { isReady = ready; }
        public void Flush() => typeof(NetworkConnection).GetMethod("Update", Private).Invoke(this, null);
        protected override void SendToTransport(ArraySegment<byte> segment, int channelId = Channels.Reliable)
        {
            Check(channelId == Channels.Reliable, "World audio uses reliable delivery");
            var unbatcher = new Unbatcher();
            Check(unbatcher.AddBatch(segment), "Mirror accepts audio batch");
            while (unbatcher.GetNextMessage(out var message, out double timestamp))
            using (var reader = NetworkReaderPool.Get(message))
            {
                Check(NetworkMessages.UnpackId(reader, out ushort id) && id == NetworkMessageId<WorldSoundMessage>.Id,
                    "Audio message has correct Mirror identifier");
                messages.Add(reader.Read<WorldSoundMessage>());
            }
        }
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new Exception("Use an isolated batch project.");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var transportObject = new GameObject("Test transport");
            Transport.active = transportObject.AddComponent<kcp2k.KcpTransport>();
            Call(Transport.active, "Awake");
            ServerActive(true);
            NetworkClient.ConnectHost();
            NetworkClient.connection.isAuthenticated = true;
            NetworkServer.localConnection.isReady = true;
            NetworkServer.connections.Add(NetworkServer.localConnection.connectionId, NetworkServer.localConnection);
            NetworkAudioRelay.RegisterClient();
            Check(HasReceiver(), "Client registers audio receiver");
            NetworkClient.ReplaceHandler<WorldSoundMessage>(message => { hostCount++; hostMessage = message; });
            var guest = new CaptureConnection(10, true);
            var loading = new CaptureConnection(11, false);
            NetworkServer.connections.Add(guest.connectionId, guest);
            NetworkServer.connections.Add(loading.connectionId, loading);
            Vector3 position = new Vector3(2, 3, 4);
            foreach (string id in new[] { "LightSwitch_On", "LightSwitch_Off", "Ticket_MovieClick", "Ticket_SubmitClick" })
            {
                int before = hostCount;
                NetworkAudioRelay.Play(id, position);
                FlushHost(); guest.Flush(); loading.Flush();
                Check(hostCount == before + 1 && hostMessage.id == id && hostMessage.position == position,
                    id + " reaches host exactly once with source position");
                Check(guest.messages.Count == hostCount && guest.messages[hostCount - 1].id == id &&
                    guest.messages[hostCount - 1].position == position && guest.messages[hostCount - 1].clipIndex == 0,
                    id + " reaches ready guest exactly once with correct clip");
                Check(loading.messages.Count == 0, "Loading guest receives no one-shot audio");
            }
            loading.isReady = true;
            loading.Flush();
            Check(loading.messages.Count == 0, "Joining guest does not replay old clicks");
            int sent = hostCount;
            ServerActive(false);
            NetworkAudioRelay.Play("LightSwitch_On", position);
            ServerActive(true);
            NetworkMode.SessionEnding = true;
            NetworkAudioRelay.Play("LightSwitch_On", position);
            NetworkMode.SessionEnding = false;
            FlushHost(); guest.Flush(); loading.Flush();
            Check(hostCount == sent && guest.messages.Count == sent && loading.messages.Count == 0,
                "Client-only and ending-session calls cannot broadcast audio");

            // Buttons remain local even while a host and guest connection exist.
            var pausePrefab = Resources.Load<GameObject>("UI/PauseMenu");
            var pause = UnityEngine.Object.Instantiate(pausePrefab);
            Call(pause.GetComponent<PauseMenuController>(), "Awake");
            var emitters = pause.GetComponentsInChildren<UISoundEmitter>(true);
            Check(emitters.Length == pause.GetComponentsInChildren<Button>(true).Length,
                "Every pause/settings button has exactly one sound emitter");
            foreach (var emitter in emitters)
            {
                Check(!emitter.HoverEnabled, "In-game button hover is disabled");
                Call(emitter, "Clicked");
            }
            var menuButton = new GameObject("Main menu button", typeof(RectTransform), typeof(Button), typeof(UISoundEmitter));
            Check(menuButton.GetComponent<UISoundEmitter>().HoverEnabled, "Main menu hover remains enabled by default");
            FlushHost(); guest.Flush(); loading.Flush();
            Check(hostCount == sent && guest.messages.Count == sent && loading.messages.Count == 0,
                "Pause/settings clicks never send network messages");
            NetworkAudioRelay.UnregisterClient();
            Check(!HasReceiver(), "Client removes audio receiver on stop");
            results.Add("NOTE Remote guest capture uses real Mirror batching/serialization, not a second running game. Audible playback is not tested in batch mode.");
            File.WriteAllLines("audio-network-results.txt", results);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            results.Add("FAIL " + exception);
            File.WriteAllLines("audio-network-results.txt", results);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
