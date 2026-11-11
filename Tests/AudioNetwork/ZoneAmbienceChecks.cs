using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in the isolated project used by Run-AudioNetworkChecks.ps1.
public static class ZoneAmbienceChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int count;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Debug.Log("PASS " + name);
        count++;
    }
    static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
            var controller = UnityEngine.Object.FindFirstObjectByType<AmbientController>();
            Check(controller != null, "Scene contains the authored ambience controller");
            var zone = controller.GetComponent<AmbientZone>();
            Check(zone.clip.name == "OldAirCordinator", "Ticket/popcorn loop resolves");
            Check(zone.Contains(new Vector3(-16, 1.7f, 2)), "Ticket and popcorn area included");
            Check(!zone.Contains(new Vector3(-35.58f, 1.7f, 1.64f)), "Toilets are their own zones now");
            Check(!zone.Contains(new Vector3(-50, 1.7f, 58)), "Screen rooms excluded");
            Check(!zone.Contains(new Vector3(36, 1.7f, 80)), "Staff room excluded");
            int controllers = UnityEngine.Object.FindObjectsByType<AmbientController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Check(controllers == 1, "Exactly one AmbientController in the scene (" + controllers + ")");
            var all = UnityEngine.Object.FindObjectsByType<AmbientZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var z in all) Check(z.clip != null, "Zone has a loop clip: " + z.name);
            Check(zone.Weight(new Vector3(-16, 1.7f, 2)) == 1, "Weight is 1 inside the box");
            float edge = zone.Weight(new Vector3(-16, 1.7f, 14.88f + zone.blendDistance * 0.5f));
            Check(edge > 0.4f && edge < 0.6f, "Weight is about half at half the blend distance outside");
            Call(controller, "Register", zone);
            var occasionals = (IList)Get(controller, "occasionals");
            var layer = occasionals[0];
            var loops = (IList)Get(controller, "loops");
            var loop = (AudioSource)Get(loops[0], "source");
            var occasional = (AudioSource)Get(layer, "source");
            Check(loop.loop && !occasional.loop && loop != occasional, "Loop and occasional sounds have separate sources");
            Check(loop.clip == zone.clip && loop.spatialBlend == 0, "Room loop uses the air clip in 2D");
            var twin = new GameObject("Twin zone", typeof(BoxCollider)).AddComponent<AmbientZone>();
            twin.clip = zone.clip;
            Call(controller, "Register", twin);
            Check(loops.Count == 1, "Zones with the same clip share one looping source (no restart)");
            UnityEngine.Object.DestroyImmediate(twin.gameObject);
            // The scene no longer authors an occasional clip; use one in-memory for the timer checks.
            zone.occasionalClip = zone.clip;
            Call(controller, "UpdateOccasional", layer, true);
            float wait = (float)Get(layer, "wait");
            Check(wait >= 49 && wait <= 100, "First entry waits for a quiet interval");
            Call(controller, "UpdateOccasional", layer, true);
            Check((float)Get(layer, "wait") <= wait, "Moving within shared zone does not restart the timer");
            zone.burstChance = 1;
            zone.burstExtraPlays = new Vector2Int(2, 2);
            Set(layer, "wait", -1f);
            Call(controller, "UpdateOccasional", layer, true);
            Check((int)Get(layer, "extraPlays") == 2, "Burst begins with two extra plays");
            Check((float)Get(layer, "wait") >= 3 && (float)Get(layer, "wait") <= 8, "Burst uses short gaps");
            for (int i = 1; i >= 0; i--)
            {
                occasional.Stop();
                Set(layer, "wait", -1f);
                Call(controller, "UpdateOccasional", layer, true);
                Check((int)Get(layer, "extraPlays") == i, "Burst consumes one extra play");
            }
            Check((float)Get(layer, "wait") >= 50 && (float)Get(layer, "wait") <= 100, "Burst returns to long quiet interval");
            Call(controller, "UpdateOccasional", layer, false);
            Call(controller, "UpdateOccasional", layer, true);
            Check((int)Get(layer, "extraPlays") == 0 && (float)Get(layer, "wait") >= 49, "Reentry starts fresh without an immediate event");
            Call(controller, "OnDisable");
            Check(!loop.isPlaying && !occasional.isPlaying && !(bool)Get(layer, "active"), "Disabling stops both sources and resets state");
            File.WriteAllText("zone-ambience-results.txt", "PASS " + count + " checks\n");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("zone-ambience-results.txt", "FAIL " + ex + "\n");
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }
}
