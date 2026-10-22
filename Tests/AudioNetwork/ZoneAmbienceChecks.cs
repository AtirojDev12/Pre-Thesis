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
            var root = GameObject.Find("Zone 1 Ambience");
            Check(root != null, "Scene contains the authored ambience root");
            var zone = root.GetComponent<AmbientZone>();
            var controller = root.GetComponent<AmbientController>();
            Check(zone.clip.name == "OldAirCordinator" && zone.occasionalClip.name == "old_loudspeaker_horror_crackle_extended", "Both clips resolve");
            Check(zone.Contains(new Vector3(-16, 1.7f, 2)), "Ticket and popcorn area included");
            Check(zone.Contains(new Vector3(-35.58f, 1.7f, 1.64f)), "Men toilet included");
            Check(zone.Contains(new Vector3(-35.68f, 1.7f, 12.92f)), "Women toilet included");
            Check(!zone.Contains(new Vector3(-50, 1.7f, 58)), "Screen rooms excluded");
            Check(!zone.Contains(new Vector3(36, 1.7f, 80)), "Staff room excluded");
            for (float x = -35; x < -16; x += .1f)
                Check(zone.Contains(new Vector3(x, 1.7f, 2)), "No ambience gap along lobby/toilet crossing " + x);
            zone.additionalAreas[0].enabled = false;
            Check(!zone.Contains(new Vector3(-35.58f, 1.7f, 1.64f)), "Disabled extra area is ignored");
            zone.additionalAreas[0].enabled = true;
            Call(controller, "AddLayer", zone, zone.clip);
            var layers = (IList)Get(controller, "layers");
            var layer = layers[0];
            var loop = (AudioSource)Get(layer, "source");
            var occasional = (AudioSource)Get(layer, "occasional");
            Check(loop.loop && !occasional.loop && loop != occasional, "Loop and occasional sounds have separate sources");
            Check(loop.clip == zone.clip && loop.spatialBlend == 0, "Room loop uses the air clip in 2D");
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
