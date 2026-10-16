using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GhostPerceptionChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object target, Type owner, string name, object value) => owner.GetField(name, Hidden).SetValue(target, value);
    static object Call(TimedGhost ghost, string name, params object[] args) => typeof(TimedGhost).GetMethod(name, Hidden).Invoke(ghost, args);

    public static List<string> RunChecks()
    {
        var results = new List<string>();
        void Check(bool pass, string label)
        {
            if (!pass) throw new Exception(label);
            results.Add("PASS " + label);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        NetworkMode.SessionEnding = false;
        var power = new GameObject("Power").AddComponent<BuildingLightManager>();
        typeof(BuildingLightManager).GetMethod("Awake", Hidden).Invoke(power, null);
        Set(power, typeof(BuildingLightManager), "zones", new[] {
            new BuildingLightManager.LightZone { zoneId = "A", areaCenter = new Vector3(0, 1, 5), areaSize = new Vector3(10, 10, 10) },
            new BuildingLightManager.LightZone { zoneId = "B", areaCenter = new Vector3(20, 1, 5), areaSize = new Vector3(10, 10, 10) }
        });
        var ghost = new GameObject("Ghost").AddComponent<TimedGhost>();
        typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Hidden).Invoke(ghost.GetComponent<NetworkIdentity>(), null);
        var playerObject = new GameObject("Player", typeof(NetworkIdentity), typeof(PlayerHealth), typeof(PlayerNoise));
        PlayerHealth player = playerObject.GetComponent<PlayerHealth>();
        PlayerNoise noise = playerObject.GetComponent<PlayerNoise>();
        player.transform.position = new Vector3(0, 0, 4);
        PlayerRegistry.Invalidate();

        Check((bool)Call(ghost, "CanPerceive", player), "Lit player in front is visible");
        Call(ghost, "CheckForPlayer");
        Vector3 lastSeen = (Vector3)typeof(TimedGhost).GetField("lastKnownPosition", Hidden).GetValue(ghost);
        power.SetZoneLights("A", false);
        Check(BuildingLightManager.LightsOn && !power.LightsOnAt(player.transform.position) &&
            power.LightsOnAt(new Vector3(20, 0, 4)), "Only the player's switched-off zone becomes dark");
        player.transform.position = new Vector3(0, 0, 1); // inside the old automatic 3 m detection radius
        Check(!(bool)Call(ghost, "CanPerceive", player), "Silent movement and proximity do not reveal a player in darkness");
        Call(ghost, "CheckForPlayer");
        Check((Enemy_Abstract_Class.EnemyState)typeof(Enemy_Abstract_Class).GetField("currentState", Hidden).GetValue(ghost)
            == Enemy_Abstract_Class.EnemyState.Search, "Losing perception ends the live chase");
        Check((Vector3)typeof(TimedGhost).GetField("lastKnownPosition", Hidden).GetValue(ghost) == lastSeen,
            "Search keeps the last seen position instead of following silent movement");

        typeof(PlayerNoise).GetProperty("ServerMic").SetValue(noise, 0.5f);
        Check((bool)Call(ghost, "CanPerceive", player), "Loud player is audible in a dark zone");
        player.transform.position = new Vector3(0, 0, 9);
        Check(!(bool)Call(ghost, "CanPerceive", player), "Microphone hearing is limited by hearing radius");
        typeof(PlayerNoise).GetProperty("ServerMic").SetValue(noise, 0f);
        power.SetZoneLights("A", true);
        player.transform.position = new Vector3(0, 0, 4);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1.2f, 2);
        wall.transform.localScale = new Vector3(4, 4, 0.2f);
        Physics.SyncTransforms();
        Check(!(bool)Call(ghost, "CanPerceive", player), "Solid wall blocks vision even with an empty obstacle mask");
        UnityEngine.Object.DestroyImmediate(wall);
        Physics.SyncTransforms();
        Check((bool)Call(ghost, "CanPerceive", player), "Vision returns after the obstruction is removed");
        player.transform.position = new Vector3(0, 0, -4);
        Check(!(bool)Call(ghost, "CanPerceive", player), "Silent player behind the ghost is outside its view cone");

        var connection = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = connection.GetValue(null);
        try
        {
            // Populate netIdentity, as Mirror normally does when spawning the ghost.
            typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Hidden).Invoke(ghost.GetComponent<NetworkIdentity>(), null);
            connection.SetValue(null, ConnectState.Connected);
            Vector3 saved = (Vector3)typeof(TimedGhost).GetField("lastKnownPosition", Hidden).GetValue(ghost);
            ghost.HearSound(Vector3.one, 10f);
            Call(ghost, "CheckForPlayer");
            Check((Vector3)typeof(TimedGhost).GetField("lastKnownPosition", Hidden).GetValue(ghost) == saved,
                "Client puppet cannot run detection or react to external noise");
        }
        finally { connection.SetValue(null, previous); }
        return results;
    }
}
