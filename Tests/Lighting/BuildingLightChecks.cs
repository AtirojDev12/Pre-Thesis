using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildingLightChecks
{
    static readonly List<string> results = new List<string>();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        if (!pass) throw new Exception(label);
    }
    static object Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    static Light Lamp(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent);
        return go.AddComponent<Light>();
    }
    static void SelectManager(BuildingLightManager manager) => typeof(BuildingLightManager)
        .GetProperty("Instance").SetValue(null, manager);

    public static void Run()
    {
        int code = 0;
        try
        {
            results.AddRange(GhostPerceptionChecks.RunChecks());
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            NetworkMode.SessionEnding = false;
            var manager = new GameObject("Building Lights").AddComponent<BuildingLightManager>();
            Call(manager.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
            Call(manager, "Awake");
            Light first = Lamp("Original lamp");
            Light disabled = Lamp("Initially disabled lamp"); disabled.enabled = false;
            Light inactive = Lamp("Inactive lamp"); inactive.gameObject.SetActive(false);
            var camera = new GameObject("Player Camera").AddComponent<Camera>();
            Light eye = Lamp("Eyes Adjusted Light", camera.transform);
            var flashlight = new GameObject("Flashlight").AddComponent<FlashlightVisual>();
            Light beam = Lamp("Floor Beam", flashlight.transform);
            var exempt = new GameObject("Emergency lamp").AddComponent<IndependentLight>();
            Light emergency = Lamp("Emergency", exempt.transform);
            manager.RefreshLights();
            Check(manager.SetLights(false), "Switch works without any GhostManager");
            Check(!first.enabled && !inactive.enabled, "All normal lamps including inactive objects switch off");
            Check(eye.enabled && beam.enabled && emergency.enabled, "Camera, flashlight and independent lights stay on");
            Light added = Lamp("New lamp while power is off");
            manager.RefreshLights();
            Check(!added.enabled, "New lamp is discovered and follows existing power state");
            inactive.gameObject.SetActive(true);
            Check(!inactive.enabled, "Reactivated lamp remains off");
            Check(manager.SetLights(true) && first.enabled && added.enabled && inactive.enabled,
                "Power on restores all participating lamps");
            Check(!disabled.enabled, "Intentionally disabled lamps stay disabled");
            Check(!manager.SetLights(true), "Repeated same-state request is rejected");

            Light roomA = Lamp("Room A");
            Light roomB = Lamp("Room B");
            var networkWriteGuard = manager.zoneStates.IsWritable;
            manager.zoneStates.IsWritable = () => throw new Exception("Offline switch touched Mirror's write guard");
            typeof(BuildingLightManager).GetField("zones", Private).SetValue(manager, new[] {
                new BuildingLightManager.LightZone { zoneId = "A", lightRoots = new[] { roomA.transform } },
                new BuildingLightManager.LightZone { zoneId = "B", additionalLights = new[] { roomB } }
            });
            manager.RefreshLights();
            Check(manager.SetZoneLights("A", false) && !roomA.enabled && roomB.enabled && first.enabled,
                "Zone A turns off without affecting Zone B or ungrouped lights");
            Check(!manager.SetZoneLights("missing", false), "Unknown zone cannot toggle building power");
            Light newRoomLamp = Lamp("Added under A", roomA.transform);
            manager.RefreshLights();
            Check(!newRoomLamp.enabled, "New lights under a zone root follow that zone");
            var zoneSwitch = new GameObject("Zone switch").AddComponent<LightSwitchInteractable>();
            typeof(LightSwitchInteractable).GetField("zoneId", Private).SetValue(zoneSwitch, "A");
            zoneSwitch.Interact(null);
            Check(roomA.enabled && newRoomLamp.enabled && roomB.enabled && BuildingLightManager.LightsOn,
                "Zone switch restores only its own circuit");
            manager.SetZoneLights("A", false);
            manager.SetLights(false);
            manager.SetLights(true);
            Check(!roomA.enabled && roomB.enabled, "Master power restoration preserves individual zone state");
            Check(manager.zoneStates.Count == 0, "Offline interactions never mutate the network dictionary");
            manager.zoneStates.IsWritable = networkWriteGuard;
            // Model server authority for the woven serialization test without opening a transport.
            var activeServer = typeof(NetworkServer).GetProperty("active");
            var serverIdentity = typeof(NetworkIdentity).GetProperty("isServer");
            var recording = manager.zoneStates.IsRecording;
            BuildingLightManager replica = null;
            try
            {
                activeServer.SetValue(null, true);
                serverIdentity.SetValue(manager.netIdentity, true);
                manager.zoneStates.IsRecording = () => true;
                manager.OnStartServer();
                Check(manager.ZoneLightsOn("A"), "Online session does not inherit offline preview state");
                Check(manager.SetZoneLights("A", false) && !manager.zoneStates["A"],
                    "Authoritative server still writes zone state into Mirror");
                var zoneInitial = new NetworkWriter();
                manager.OnSerialize(zoneInitial, true);
                replica = new GameObject("Zone state replica").AddComponent<BuildingLightManager>();
                Call(replica.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
                typeof(BuildingLightManager).GetField("zones", Private).SetValue(replica,
                    typeof(BuildingLightManager).GetField("zones", Private).GetValue(manager));
                replica.OnDeserialize(new NetworkReader(zoneInitial.ToArraySegment()), true);
                Check(!replica.ZoneLightsOn("A"), "Late join receives independent zone off state");
                Check(!replica.SetZoneLights("A", true), "Non-server replica cannot change zone power");
                manager.SetZoneLights("A", true);
                var zoneDelta = new NetworkWriter();
                manager.OnSerialize(zoneDelta, false);
                replica.OnDeserialize(new NetworkReader(zoneDelta.ToArraySegment()), false);
                Check(replica.ZoneLightsOn("A"), "Live network update restores the replicated zone state");
            }
            finally
            {
                activeServer.SetValue(null, false);
                serverIdentity.SetValue(manager.netIdentity, false);
                manager.zoneStates.IsRecording = recording;
                if (replica != null) UnityEngine.Object.DestroyImmediate(replica.gameObject);
            }

            var switchObject = new GameObject("Switch").AddComponent<LightSwitchInteractable>();
            Check(switchObject.CanInteract(), "World switch is usable without a ghost");
            switchObject.Interact(null);
            Check(!BuildingLightManager.LightsOn && !first.enabled, "World switch routes to building power");
            Check(switchObject.GetInteractionPrompt().Contains("Turn lights on"), "Prompt follows independent power");
            var ghost = new GameObject("Ghost manager").AddComponent<GhostManager>();
            Call(ghost, "Awake");
            var arrival = (IEnumerator)Call(ghost, "GhostArrivalSequence");
            Check(arrival.MoveNext() && !BuildingLightManager.LightsOn, "Ghost arrival leaves power unchanged");
            Check(arrival.MoveNext() && !BuildingLightManager.LightsOn, "Ghost countdown leaves power unchanged");
            ghost.ResetManagerAfterJumpscare();
            Call(ghost, "DespawnGhost");
            Check(!BuildingLightManager.LightsOn && !first.enabled, "Ghost reset and despawn never restore power");
            UnityEngine.Object.DestroyImmediate(ghost.gameObject);
            LightController.ServerSetLights(true);
            Check(first.enabled, "Scripted/keyboard power API works after removing the ghost manager");

            // Use Mirror's woven serializer, including the initial-state path used by late joins.
            manager.SetLights(false);
            var initial = new NetworkWriter();
            manager.OnSerialize(initial, true);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/LightingTestSource.unity");
            var clientScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(clientScene);
            SelectManager(null);
            var client = new GameObject("Guest power").AddComponent<BuildingLightManager>();
            Call(client.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
            Call(client, "Awake");
            Light guestLamp = Lamp("Guest lamp");
            client.OnDeserialize(new NetworkReader(initial.ToArraySegment()), true);
            Check(!BuildingLightManager.LightsOn && !guestLamp.enabled, "Late join receives off state and applies it to lamps");
            typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, ConnectState.Connected);
            Check(!client.SetLights(true) && !guestLamp.enabled, "Guest cannot change authoritative power directly");
            typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, ConnectState.None);
            SelectManager(manager);
            manager.SetLights(true);
            var delta = new NetworkWriter();
            manager.OnSerialize(delta, false);
            SelectManager(client);
            client.OnDeserialize(new NetworkReader(delta.ToArraySegment()), false);
            Check(BuildingLightManager.LightsOn && guestLamp.enabled, "Live replicated state restores guest lamps");
            SelectManager(manager);
            manager.SetLights(false);
            Check(guestLamp.enabled, "Manager only controls lights in its own scene");
            UnityEngine.Object.DestroyImmediate(client.gameObject);
            EditorSceneManager.CloseScene(clientScene, true);
            SelectManager(manager);

            foreach (string path in new[] { "Assets/Scenes/Map/Cinema.unity", "Assets/Scenes/Map/Cinema_GamePlay.unity",
                "Assets/Floderไอนน/NewEnemyplus.unity", "Assets/Scenes/Z1_EnemyTest.unity" })
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var managers = new List<BuildingLightManager>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    managers.AddRange(root.GetComponentsInChildren<BuildingLightManager>(true));
                Check(managers.Count == 1, path + " contains one independent manager");
                var power = managers[0];
                Check(power.GetComponent<NetworkIdentity>().sceneId != 0 && power.transform.parent == null
                    && power.GetComponent<GhostManager>() == null, path + " has separate networked power object");
                if (path.EndsWith("Cinema_GamePlay.unity"))
                {
                    Check(power.Zones.Count == 6, "Gameplay scene contains six configured lighting zones");
                    foreach (var zone in power.Zones)
                        Check(zone.areaSize.x > 0 && zone.areaSize.y > 0 && zone.areaSize.z > 0,
                            zone.zoneId + " has a configured ghost detection area");
                    foreach (string switchName in new[] { "SwichLIghtScreenRoom", "LGhostScreenRoom", "SLStraffRoom", "SLMaleToilet", "SLFemaleToilet", "Light Swich ticketAndPopcorn" })
                    {
                        GameObject go = GameObject.Find(switchName);
                        var sceneSwitch = go != null ? go.GetComponent<LightSwitchInteractable>() : null;
                        string id = sceneSwitch != null ? (string)typeof(LightSwitchInteractable).GetField("zoneId", Private).GetValue(sceneSwitch) : null;
                        Check(sceneSwitch != null && power.HasZone(id) && go.GetComponent<Collider>() != null &&
                            go.GetComponent<NetworkIdentity>().sceneId != 0, switchName + " is a networked collider switch assigned to a valid zone");
                    }
                }
            }
        }
        catch (Exception ex) { results.Add("FAIL " + ex); code = 1; }
        finally
        {
            File.WriteAllLines("building-light-results.txt", results);
            EditorApplication.Exit(code);
        }
    }
}
