// Copied into the isolated test project only; exercised in an exported Windows player.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;

public sealed class CustomerBuildProbe : MonoBehaviour
{
    public GameObject playerPrefab;
    private readonly List<string> results = new List<string>();
    private bool failed;

    private void Check(bool pass, string label)
    {
        results.Add((pass ? "PASS " : "FAIL ") + label);
        failed |= !pass;
    }

    private IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
        bool offline = Array.IndexOf(Environment.GetCommandLineArgs(), "--offline") >= 0;
        if (offline)
            UnityEngine.SceneManagement.SceneManager.LoadScene("Cinema_GamePlay");
        else
        {
            var root = new GameObject("Customer Build Host");
            var transport = root.AddComponent<kcp2k.KcpTransport>();
            transport.Port = 17995;
            var manager = root.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.playerPrefab = playerPrefab;
            manager.onlineScene = "Assets/Scenes/Map/Cinema_GamePlay.unity";
            // Server active before the scene loads: reproduces build startup ordering.
            manager.StartHost();
        }

        float deadline = Time.realtimeSinceStartup + 40f;
        TicketMinigame ticket = null;
        CounterSlot popcorn = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            ticket = FindAnyObjectByType<TicketMinigame>();
            popcorn = FindAnyObjectByType<CounterSlot>();
            if (ticket != null && popcorn != null &&
                ticket.VisibleState.stage == TicketCustomerStage.Waiting &&
                popcorn.ActiveCustomer != null && popcorn.ActiveCustomer.CanInteract()) break;
            yield return null;
        }
        Check(ticket != null && ticket.isActiveAndEnabled, "Ticket system survives exported scene startup");
        Check(ticket != null && ticket.VisibleState.stage == TicketCustomerStage.Waiting, "Ticket customer spawns and reaches counter naturally");
        Check(popcorn != null && popcorn.ActiveCustomer != null && popcorn.ActiveCustomer.CanInteract(), "Popcorn customer spawns and reaches counter naturally");
        var material = Resources.Load<Material>("CustomerBody");
        Check(material != null && material.shader != null && material.shader.isSupported &&
            material.shader.name == "Universal Render Pipeline/Lit", "URP customer material and shader survive build stripping");
        foreach (string name in new[] { "Human Ticket Customer", "Ghost Ticket Customer", "Human Customer", "Ghost Customer" })
        {
            var customer = GameObject.Find(name);
            if (customer == null) continue;
            var renderer = customer.GetComponent<Renderer>();
            Check(renderer.sharedMaterial == material, name + " uses explicit URP material");
            CheckPixels(renderer, name);
        }
        Application.logMessageReceived -= OnLog;
        File.WriteAllLines(Path.Combine(Application.dataPath, "../customer-results-" + (offline ? "offline" : "host") + ".txt"), results);
        Application.Quit(failed ? 1 : 0);
    }

    private void CheckPixels(Renderer renderer, string label)
    {
        var root = new GameObject("Customer material test camera");
        var camera = root.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 31;
        camera.transform.position = renderer.bounds.center + Vector3.back * 3f;
        camera.transform.LookAt(renderer.bounds.center);
        int layer = renderer.gameObject.layer;
        renderer.gameObject.layer = 31;
        var lightRoot = new GameObject("Customer material test light");
        var light = lightRoot.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.cullingMask = 1 << 31;
        var render = new RenderTexture(128, 128, 24);
        camera.targetTexture = render;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = render;
        var texture = new Texture2D(128, 128, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
        texture.Apply();
        Color center = texture.GetPixel(64, 64);
        Check(center.maxColorComponent > .05f && !(center.r > .7f && center.b > .7f && center.g < .2f),
            label + " renders visible, non-magenta pixels: " + center);
        File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + label.Replace(" ", "-") + ".png"), texture.EncodeToPNG());
        RenderTexture.active = previous;
        renderer.gameObject.layer = layer;
        Destroy(root);
        Destroy(lightRoot);
        Destroy(render);
        Destroy(texture);
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if ((type == LogType.Exception || type == LogType.Error) &&
            (stack.Contains("TicketMinigame") || stack.Contains("TicketNetSync") || message.Contains("[Customers]")))
            Check(false, "Customer runtime error: " + message + " " + stack);
    }
}
