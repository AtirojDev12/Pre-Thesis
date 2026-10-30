using System;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Copy into Assets/Editor of the isolated audio-network project.
public static class ProjectorAudioChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static object Get(object o, string f) => o.GetType().GetField(f, Flags).GetValue(o);
    static void Set(object o, string f, object v) => o.GetType().GetField(f, Flags).SetValue(o, v);
    static void Call(object o, string m, params object[] a) => o.GetType().GetMethod(m, Flags).Invoke(o, a);
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("PASS " + message);
        checks++;
    }
    static void Tick(ProjectorAudio audio, string state)
    {
        Call(audio, "LateUpdate");
        Check(Get(audio, "playback").ToString() == state, "Playback state: " + state);
    }
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
            var all = Object.FindObjectsByType<ProjectorAudio>(FindObjectsSortMode.None);
            Check(all.Length == 2, "Both projectors are configured");
            ProjectorAudio ghost = null, normal = null;
            foreach (var a in all)
            {
                Check(((AudioClip)Get(a,"runningClip")).name == "Projector_Running_Indoor_Smooth_Loop_10s", "Correct running clip");
                Check(((AudioClip)Get(a,"filmChangeClip")).name == "Film_Reel_Change_Indoor_5s", "Correct change clip");
                Check((float)Get(a,"runningVolume") > (float)Get(a,"filmChangeVolume"), "Running volume exceeds changing volume");
                if (Get(a,"filmChore") != null) ghost = a; else normal = a;
            }
            Check(ghost != null && normal != null && ghost.transform.parent.name == "GhostScreenRoom", "Only Ghost Room follows the film chore");
            var runClip = (AudioClip)Get(ghost,"runningClip");
            var changeClip = (AudioClip)Get(ghost,"filmChangeClip");
            Check(Mathf.Abs(runClip.length-10)<.01f && Mathf.Abs(changeClip.length-5)<.01f, "Imported lengths are 10 and 5 seconds");
            Check(runClip.preloadAudioData && changeClip.preloadAudioData, "Clips preload to avoid delayed starts");

            // Isolate transition checks from match/scene startup, scoring and EOS.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var loadingChoreObject = new GameObject("Uninitialized remote film chore");
            loadingChoreObject.SetActive(false);
            loadingChoreObject.AddComponent<NetworkIdentity>();
            var loadingChore = loadingChoreObject.AddComponent<ZoneChoreSpot>();
            var loadingAudioObject = new GameObject("Always-active projector audio");
            var loadingAudio = loadingAudioObject.AddComponent<ProjectorAudio>();
            Set(loadingAudio, "filmChore", loadingChore);
            var connectionState = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
            var previousConnectionState = connectionState.GetValue(null);
            try
            {
                connectionState.SetValue(null, Enum.Parse(connectionState.FieldType, "Connecting"));
                Check(loadingChore.netIdentity == null, "Loading fixture reproduces scene identity before Awake");
                Tick(loadingAudio, "Silent");
            }
            finally
            {
                connectionState.SetValue(null, previousConnectionState);
                Object.DestroyImmediate(loadingAudioObject);
                Object.DestroyImmediate(loadingChoreObject);
            }
            var go = new GameObject("Test film chore");
            go.AddComponent<NetworkIdentity>();
            var chore = go.AddComponent<ZoneChoreSpot>();
            var audio = go.AddComponent<ProjectorAudio>();
            Set(audio,"filmChore",chore);
            Set(audio,"runningClip",runClip);
            Set(audio,"filmChangeClip",changeClip);
            Tick(audio,"Running");
            var source = (AudioSource)Get(audio,"source");
            Check(source.loop && source.clip == runClip && source.spatialBlend == 1, "Running is a 3D loop");
            Check(source.minDistance == 2 && source.maxDistance == 15 && source.dopplerLevel == 0, "Spatial attenuation configured");
            Check(source.GetComponent<SoundCategoryVolume>().Category == SoundCategory.Sfx, "Uses SFX settings");

            Call(chore,"SetNeeded",true);
            Tick(audio,"Silent");
            var worker = new GameObject("Worker");
            Call(chore,"OnInteracted",worker);
            Check(chore.IsWorking, "Accepted interaction starts work");
            Tick(audio,"Changing");
            Check(!source.loop && source.clip == changeClip, "Change plays once");
            Check(Mathf.Abs((float)Get(source.GetComponent<SoundCategoryVolume>(),"baseVolume")-.35f)<.001f, "Change has lower base volume");
            var epoch = Get(audio,"changeStartedAt");
            source.Stop();
            Tick(audio,"Changing");
            Check(epoch.Equals(Get(audio,"changeStartedAt")), "Finished one-shot does not change its epoch");
            Call(chore,"StopWork");
            Tick(audio,"Silent");
            Call(chore,"OnInteracted",worker);
            Tick(audio,"Changing");
            Set(chore,"workStart",chore.WorkStartedAt-1);
            Tick(audio,"Changing");
            Check(Get(audio,"changeStartedAt").Equals(chore.WorkStartedAt), "A new work epoch restarts playback");

            var replica = new GameObject("Observer");
            replica.AddComponent<NetworkIdentity>();
            var remoteChore = replica.AddComponent<ZoneChoreSpot>();
            using (var writer = NetworkWriterPool.Get())
            {
                chore.OnSerialize(writer,true);
                using (var reader = NetworkReaderPool.Get(writer.ToArraySegment())) remoteChore.OnDeserialize(reader,true);
            }
            Check(remoteChore.Needed && remoteChore.IsWorking && remoteChore.WorkStartedAt == chore.WorkStartedAt, "Mirror initial snapshot restores ongoing work");
            var remoteAudio = replica.AddComponent<ProjectorAudio>();
            Set(remoteAudio,"filmChore",remoteChore);
            Set(remoteAudio,"runningClip",runClip);
            Set(remoteAudio,"filmChangeClip",changeClip);
            Tick(remoteAudio,"Changing");
            Check(Get(remoteAudio,"changeStartedAt").Equals(chore.WorkStartedAt), "Observer uses original start time");

            Call(chore,"StopWork");
            Call(chore,"SetNeeded",false);
            Tick(audio,"Running");
            Check(source.loop && source.clip == runClip, "Completion resumes the running loop");
            Check(Mathf.Abs((float)Get(source.GetComponent<SoundCategoryVolume>(),"baseVolume")-.85f)<.001f, "Running has higher base volume");
            NetworkMode.SessionEnding = true;
            Tick(audio,"Silent");
            NetworkMode.SessionEnding = false;
            Tick(audio,"Running");
            Call(audio,"OnDisable");
            Check(Get(audio,"playback").ToString() == "Silent", "Disable resets playback");
            File.WriteAllText("projector-audio-results.txt", "PASS " + checks + " checks\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText("projector-audio-results.txt", "FAIL " + e + "\n");
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
