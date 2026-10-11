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
