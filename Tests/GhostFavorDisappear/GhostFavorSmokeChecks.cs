using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using PreThesis.Vfx;

public sealed class GhostFavorSmokeChecks : MonoBehaviour
{
    public Transform item;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string Output => Path.GetFullPath("../ghost-favor-smoke");
    private GhostFavorRecovery recovery;
    private GhostFavorDisappearVfx smoke;
    private float started;
    private string results = "";
    private int redPixels;
    private int emberPixels;

    private void Check(bool condition, string description)
    {
        results += (condition ? "PASS " : "FAIL ") + description + "\n";
        File.WriteAllText(Output + "-results.txt", results);
        if (!condition) throw new Exception(description);
    }

    private IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        // Fixed capture steps keep GPU appearance checks stable in a busy batch editor.
        Time.captureFramerate = 60;
        NetworkMode.SessionEnding = false;
        var point = new GameObject("Relocation destination").transform;
        point.position = Vector3.right * 20;
        recovery = gameObject.AddComponent<GhostFavorRecovery>();
        recovery.Configure(item, new[] { point });
        smoke = GetComponent<GhostFavorDisappearVfx>();
        Bounds initialBounds = item.GetComponentInChildren<Renderer>().bounds;
        foreach (var renderer in item.GetComponentsInChildren<Renderer>()) initialBounds.Encapsulate(renderer.bounds);
        Debug.Log("Ghost Favor bounds: " + initialBounds + "; pivot: " + item.position);
        yield return null;
        Capture("00-before");
        recovery.ServerOnGhostAppeared();
        started = Time.time;
        GhostFavorState state = recovery.VisibleState;
        Check(state.displaced && state.disappearanceId == 1, "Successful relocation emits one disappearance event");
        using (var writer = NetworkWriterPool.Get())
        {
            writer.Write(state);
            var restored = new NetworkReader(writer.ToArraySegment()).Read<GhostFavorState>();
            Check(restored.disappearanceId == state.disappearanceId && restored.disappearedAt == state.disappearedAt,
                "Mirror round trip preserves event ID and server timestamp");
        }
        yield return null;
        VisualEffect effect = FindFirstObjectByType<VisualEffect>();
        Debug.Log("Smoke position: " + effect.transform.position + "; scale: " + effect.transform.localScale);
        Check(effect != null && effect.visualEffectAsset != null, "Relocation automatically starts the assigned VFX Graph");
        Check(effect.transform.parent == null && Vector3.Distance(effect.transform.position, initialBounds.center) < .1f,
            "Smoke stays detached at the original seasoning position");
        recovery.ServerOnGhostAppeared();
        Check(recovery.VisibleState.disappearanceId == 1, "Already displaced seasoning does not create duplicate events");
        for (int i = 0; i < 3; i++) smoke.Observe(state);
        Check(FindObjectsByType<VisualEffect>(FindObjectsSortMode.None).Length == 1, "Repeated network snapshots do not duplicate the effect");
        yield return new WaitUntil(() => Time.time - started >= .27f);
        Check(Capture("01-burst") > 300, "Rendered puff has a readable dark core");
        Check(redPixels > 100, "Crimson wisps are visible around the black smoke (pixels: " + redPixels + ")");
        Check(emberPixels > 0, "Bright red embers are visible (pixels: " + emberPixels + ")");
        Check(effect.aliveParticleCount > 0, "GPU smoke particles spawn");
        yield return new WaitUntil(() => Time.time - started >= 1.1f);
        Capture("02-hold-1s");
        Check(redPixels > 30, "Red accents remain visible after one second");
        Check(effect != null && effect.aliveParticleCount > 0, "Smoke remains visible before the final fade");
        yield return new WaitUntil(() => Time.time - started >= 1.8f);
        Capture("03-fade");
        yield return new WaitUntil(() => Time.time - started >= 2.15f);
        Capture("04-clear");
        Check(effect == null, "Smoke and embers clean up within the two-second duration");
        state.disappearanceId = 2;
        state.disappearedAt = Time.timeAsDouble - 20;
        smoke.Observe(state);
        Check(FindFirstObjectByType<VisualEffect>() == null, "Expired snapshots do not replay smoke for late joiners");
        state.disappearanceId = 3;
        state.disappearedAt = Time.timeAsDouble - .8;
        smoke.Observe(state);
        yield return null;
        Check(FindFirstObjectByType<VisualEffect>() != null, "Recent late observer resumes remaining smoke");
        var resumed = FindAnyObjectByType<VisualEffect>();
        var old = state; old.disappearanceId = 1;
        smoke.Observe(old);
        yield return null;
        Check(FindObjectsByType<VisualEffect>().Length == 1 && resumed != null,
            "Older snapshots cannot replace a newer effect");
        smoke.enabled = false;
        yield return null;
        Check(FindFirstObjectByType<VisualEffect>() == null, "Disabling the owner cleans up detached smoke");
        yield return ServerChecks();
        yield return PreviewChecks();
        File.WriteAllText(Output + "-results.txt", results);
        UnityEditor.EditorApplication.Exit(0);
    }

    private IEnumerator PreviewChecks()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects()) if (root != gameObject) root.SetActive(false);
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode("Assets/VFX/VFX_Preview.unity",
            new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Additive));
        yield return null;
        var switcher = FindAnyObjectByType<VfxPreviewSwitcher>();
        Check(switcher != null && switcher.favor != null && switcher.favorStage.activeSelf,
            "VFX_Preview opens with the Ghost Favor stage selected");
        var preview = switcher.favor; preview.autoReplay = false; preview.Replay();
        float began = Time.time;
        yield return new WaitUntil(() => Time.time - began >= .8f);
        Check(Array.TrueForAll(preview.jarRenderers, renderer => !renderer.enabled) && preview.smoke.aliveParticleCount > 0,
            "Preview jar disappears and plays the same gameplay graph");
        Capture("05-preview", preview.GetComponentInChildren<Camera>());
        yield return new WaitUntil(() => Time.time - began >= 2.65f);
        Check(!preview.smoke.gameObject.activeInHierarchy, "Preview effect also stops after two seconds including fade");
        Capture("06-preview-clear", preview.GetComponentInChildren<Camera>());
        Check(redPixels == 0, "Preview has no red particles left on screen after two seconds");
        switcher.ShowFood(); yield return null;
        Check(switcher.foodStage.activeSelf && !switcher.favorStage.activeSelf, "Food button hides Ghost Favor and its camera");
        switcher.ShowGhost(); yield return null;
        Check(switcher.ghostStage.activeSelf && !switcher.foodStage.activeSelf && !switcher.favorStage.activeSelf,
            "Ghost black-water button still selects its original stage");
        switcher.ShowFavor(); yield return null;
        Check(!switcher.ghostStage.activeSelf && switcher.favorStage.activeSelf &&
            Array.TrueForAll(preview.jarRenderers, renderer => renderer.enabled), "Ghost Favor button restores the jar for replay");
    }

    private IEnumerator ServerChecks()
    {
        Time.captureFramerate = 0;
        recovery.enabled = false;
        var networkObject = new GameObject("Ghost Favor host check");
        var transport = networkObject.AddComponent<kcp2k.KcpTransport>(); transport.Port = 17996;
        var manager = networkObject.AddComponent<NetworkManager>();
        manager.transport = transport; manager.autoCreatePlayer = false;
        manager.StartHost();
        for (int frame = 0; frame < 120 && !NetworkClient.ready; frame++) yield return null;
        Check(NetworkServer.active && NetworkClient.ready, "Real Mirror host connects and becomes ready");
        // Assemble before Awake so Mirror discovers the authored behaviour indices.
        var syncObject = new GameObject("Authoritative seasoning state"); syncObject.SetActive(false);
        syncObject.AddComponent<NetworkIdentity>();
        var server = syncObject.AddComponent<PopcornNetSync>();
        syncObject.SetActive(true);
        NetworkServer.Spawn(syncObject);
        var owner = new GameObject("Host seasoning recovery");
        var hostRecovery = owner.AddComponent<GhostFavorRecovery>();
        var jar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        jar.transform.position = new Vector3(0, 1, 0); jar.transform.localScale = Vector3.one * .3f;
        var destination = new GameObject("Host relocation point").transform; destination.position = Vector3.right * 12;
        hostRecovery.Configure(jar.transform, new[] { destination });
        hostRecovery.ServerOnGhostAppeared();
        yield return null;
        var eventOne = server.GhostFavor;
        Check(eventOne.displaced && eventOne.disappearanceId == 1 && Math.Abs(eventOne.disappearedAt - NetworkTime.time) < .5,
            "Server publishes exactly one event using the synchronized network clock");
        Check(FindObjectsByType<VisualEffect>().Length == 1, "Host renders one local effect for its server event");
        var guestObject = new GameObject("Remote snapshot replica"); guestObject.SetActive(false);
        guestObject.AddComponent<NetworkIdentity>(); var guest = guestObject.AddComponent<PopcornNetSync>();
        var singleton = typeof(PopcornNetSync).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        singleton.SetValue(null, null);
        guestObject.SetActive(true);
        singleton.SetValue(null, server);
        guestObject.SetActive(false);
        var writer = new NetworkWriter(); server.OnSerialize(writer, true);
        guest.OnDeserialize(new NetworkReader(writer.ToArraySegment()), true);
        Check(guest.GhostFavor.disappearanceId == eventOne.disappearanceId && guest.GhostFavor.disappearedAt == eventOne.disappearedAt,
            "Generated initial SyncVar snapshot delivers event ID and timestamp to a joining client");
        var rejected = eventOne; rejected.disappearanceId = 999;
        guest.SetGhostFavorState(rejected);
        Check(guest.GhostFavor.disappearanceId == 1 && server.GhostFavor.disappearanceId == 1,
            "A client replica cannot write authoritative disappearance state");
        var guestView = new GameObject("Remote presentation");
        var remoteSmoke = guestView.AddComponent<GhostFavorDisappearVfx>(); remoteSmoke.Configure(hostRecovery);
        remoteSmoke.Observe(guest.GhostFavor); yield return null;
        Check(FindObjectsByType<VisualEffect>().Length == 2, "Host and deserialized remote observer each render exactly once");
        var home = eventOne; home.displaced = false; home.position = hostRecovery.HomePosition;
        typeof(GhostFavorRecovery).GetField("state", Private).SetValue(hostRecovery, home);
        hostRecovery.ServerOnGhostAppeared();
        var eventTwo = server.GhostFavor;
        var delta = new NetworkWriter(); server.OnSerialize(delta, false);
        guest.OnDeserialize(new NetworkReader(delta.ToArraySegment()), false);
        Check(eventTwo.disappearanceId == 2 && guest.GhostFavor.disappearanceId == 2 && guest.GhostFavor.disappearedAt == eventTwo.disappearedAt,
            "Subsequent server relocation reaches observers through the generated delta snapshot");
        remoteSmoke.Observe(guest.GhostFavor);
        remoteSmoke.Observe(guest.GhostFavor);
        yield return null;
        Check(FindObjectsByType<VisualEffect>().Length == 2, "Repeated host and remote updates preserve one effect per observer");
        var late = new GameObject("Expired network observer");
        var lateSmoke = late.AddComponent<GhostFavorDisappearVfx>(); lateSmoke.Configure(hostRecovery);
        var expired = eventTwo; expired.disappearedAt = NetworkTime.time - 2.1;
        lateSmoke.Observe(expired);
        Check(FindObjectsByType<VisualEffect>().Length == 2, "Joining after two seconds does not replay expired network smoke");
        NetworkMode.SessionEnding = true;
        var next = eventTwo; next.disappearanceId++;
        lateSmoke.Observe(next);
        Check(FindObjectsByType<VisualEffect>().Length == 2, "Session shutdown cannot start another effect");
        owner.SetActive(false); guestView.SetActive(false); late.SetActive(false);
        manager.StopHost();
        Destroy(owner); Destroy(guestView); Destroy(late); Destroy(guestObject); Destroy(jar); Destroy(destination.gameObject); Destroy(networkObject);
        yield return null;
        NetworkMode.SessionEnding = false;
    }

    private void OnLog(string message, string stack, LogType kind)
    {
        if (kind != LogType.Exception && kind != LogType.Error) return;
        File.AppendAllText(Output + "-results.txt", "FAIL " + message + "\n" + stack);
        UnityEditor.EditorApplication.Exit(1);
    }

    private int Capture(string label, Camera camera = null)
    {
        var target = RenderTexture.GetTemporary(960, 640, 24);
        RenderPipeline.SubmitRenderRequest(camera != null ? camera : Camera.main, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(960, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
        File.WriteAllBytes(Output + "-" + label + ".png", image.EncodeToPNG());
        int dark = 0;
        redPixels = 0; emberPixels = 0;
        foreach (var pixel in image.GetPixels())
        {
            if (pixel.r < .33f && pixel.g < .35f && pixel.b < .38f) dark++;
            if (pixel.r > .25f && pixel.r > pixel.g * 1.4f && pixel.r > pixel.b * 1.3f) redPixels++;
            if (pixel.r > .7f && pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2) emberPixels++;
        }
        Destroy(image); RenderTexture.active = null; RenderTexture.ReleaseTemporary(target);
        return dark;
    }
}
