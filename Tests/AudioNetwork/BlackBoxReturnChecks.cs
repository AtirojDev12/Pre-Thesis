using System;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BlackBoxReturnChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int count;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Debug.Log("PASS " + message); }
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static void Property(object obj, string name, object value) => obj.GetType().GetProperty(name, Flags).SetValue(obj, value);
    static void Server(bool active) => typeof(NetworkServer).GetProperty("active").SetValue(null, active);
    public static void Run()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
            var boxes = UnityEngine.Object.FindObjectsByType<BlackBoxCarry>(FindObjectsSortMode.None);
            Check(boxes.Length == 2, "Both toilet boxes exist");
            var player = new GameObject("Carrier", typeof(NetworkIdentity), typeof(PlayerHealth));
            var other = new GameObject("Other player", typeof(NetworkIdentity), typeof(PlayerHealth));
            var health = player.GetComponent<PlayerHealth>();
            Call(player.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
            Call(other.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
            typeof(PlayerHealth).GetProperty("LocalInstance").SetValue(null, health);
            foreach (var carry in boxes)
            {
                Call(carry.GetComponent<NetworkIdentity>(), "InitializeNetworkBehaviours");
                Call(carry, "Awake");
                Call(carry.GetComponent<RuleInteractable>(), "Awake");
                var box = (Transform)Get(carry, "box");
                Vector3 home = box.position;
                Quaternion rotation = box.rotation;
                Call(carry, "OnPickedUp", player);
                Check(carry.ServerIsCarrier(player) && !carry.ServerIsCarrier(other), "Only the actual offline carrier owns the box");
                Check(BlackBoxCarry.UsesLocalDiscardInput, "Carried box reserves the discard input");
                Check(!(bool)Call(carry, "TryReturnHome", other), "Noncarrier cannot return another player's box");
                box.position += Vector3.one * 10;
                Call(carry, "RequestReturnHome");
                Check(carry.CarrierId == 0 && carry.GetComponent<RuleInteractable>().Usable, "Discard releases the carrier and enables pickup");
                Check(Vector3.Distance(home, box.position) < .001f && Quaternion.Angle(rotation, box.rotation) < .01f, "Discard restores the original position and rotation immediately");
                Check(BlackBoxCarry.UsesLocalDiscardInput, "Same press remains reserved after return");
                Check(!(bool)Call(carry, "TryReturnHome", player), "Duplicate return is ignored");
                Call(carry, "OnPickedUp", player);
                Set(health, "isDowned", true);
                Check(!(bool)Call(carry, "TryReturnHome", player), "Downed sender cannot issue a manual return");
                Set(health, "isDowned", false);
                carry.ServerConsume();
                Check(!(bool)Call(carry, "TryReturnHome", player) && !box.gameObject.activeSelf, "Consumed box cannot be restored with discard");
                carry.ServerReturnHome();
                Check(box.gameObject.activeSelf && carry.GetComponent<RuleInteractable>().Usable, "Rule reset still restores the consumed box");
            }
            var online = boxes[0];
            Property(player.GetComponent<NetworkIdentity>(), "netId", 501u);
            Property(other.GetComponent<NetworkIdentity>(), "netId", 502u);
            Property(online.netIdentity, "isServer", true);
            Server(true);
            Set(online, "carrierId", 501u);
            Check(!(bool)Call(online, "TryReturnHome", other), "Server rejects a different network carrier");
            Check((bool)Call(online, "TryReturnHome", player) && online.CarrierId == 0, "Server accepts the current network carrier");
            Property(online.netIdentity, "isServer", false);
            Set(online, "carrierId", 501u);
            Check(!(bool)Call(online, "TryReturnHome", player), "Nonauthoritative copy cannot change state");
            Server(false);
            File.WriteAllText("black-box-results.txt", "PASS " + count + " checks\n");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Server(false);
            File.WriteAllText("black-box-results.txt", "FAIL " + ex + "\n");
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }
}
