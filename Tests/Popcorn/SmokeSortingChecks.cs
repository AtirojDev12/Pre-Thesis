using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PreThesis.Vfx;

// Run in the isolated regression project, using the actual counter-side viewpoint.
public static class SmokeSortingChecks
{
    static Color[] Capture(Camera camera, string path)
    {
        var target = new RenderTexture(1280, 720, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        var pixels = texture.GetPixels();
        camera.targetTexture = null;
        RenderTexture.active = previous;
        UnityEngine.Object.DestroyImmediate(texture);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return pixels;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var fx = UnityEngine.Object.FindAnyObjectByType<PopcornMachineVfx>();
        var renderer = fx.steam.GetComponent<ParticleSystemRenderer>();
        var camera = new GameObject("Smoke inspection camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(-21.13f,1.81f,10.96f);
        camera.transform.LookAt(fx.steam.transform.position);
        camera.fieldOfView = 75;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 150;
        var lines = new System.Collections.Generic.List<string>();
        foreach (float time in new[]{ .3f, 1.3f, 2.3f })
        {
            fx.steam.Simulate(time, false, true, true);
            renderer.sortingOrder = 0;
            var before = Capture(camera, "sorting-before-" + time + ".png");
            fx.SendMessage("Awake", SendMessageOptions.RequireReceiver);
            var after = Capture(camera, "sorting-after-" + time + ".png");
            int brighter = 0;
            for (int i=0; i<before.Length; i++)
                if (after[i].grayscale > before[i].grayscale + .03f) brighter++;
            lines.Add("t="+time+" visible smoke pixels restored="+brighter+" particles="+fx.steam.particleCount);
            if (time > 1 && brighter < 1000) throw new Exception("Sorting change did not restore smoke at "+time+"; "+brighter);
        }
        File.WriteAllLines("smoke-sorting-results.txt", lines);
    }
}
