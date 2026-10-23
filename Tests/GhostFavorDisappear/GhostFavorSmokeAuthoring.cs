using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;
using PreThesis.Vfx;

public static class GhostFavorSmokeAuthoring
{
    public static void Run()
    {
        if (!Application.isBatchMode || !Application.dataPath.Replace('\\', '/').Contains("/.utmp/"))
            throw new Exception("Run in the isolated regression project.");
        BakeSmokeTexture();
        foreach (string previewPath in new[] { "Assets/VFX/VFX_Preview.unity", "Assets/VFX/GhostManifestation/VFX_Preview.unity" })
        {
            EditorSceneManager.OpenScene(previewPath);
            var switcher = UnityEngine.Object.FindAnyObjectByType<VfxPreviewSwitcher>();
            if (switcher == null || switcher.favor == null || switcher.favorStage == null ||
                !switcher.favorStage.activeSelf || switcher.favor.smoke.visualEffectAsset == null)
                throw new Exception("Missing active Ghost Favor stage: " + previewPath);
            switcher.ShowFood(); switcher.ShowGhost(); switcher.ShowFavor();
            if (switcher.foodStage.activeSelf || switcher.ghostStage.activeSelf || !switcher.favorStage.activeSelf)
                throw new Exception("Preview stage switching failed: " + previewPath);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/Map/Cinema_GamePlay.unity");
        var bootstrap = UnityEngine.Object.FindAnyObjectByType<PopcornMinigameBootstrap>();
        var configured = bootstrap.GetComponent<GhostFavorDisappearVfx>();
        if (configured == null || new SerializedObject(configured).FindProperty("smokeGraph").objectReferenceValue == null)
            throw new Exception("Cinema must contain an explicitly assigned smoke graph.");
        var item = (Transform)typeof(PopcornMinigameBootstrap).GetField("ghostStation", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bootstrap);
        var jar = UnityEngine.Object.Instantiate(item.gameObject);
        jar.name = "Ghost Favor test jar";
        jar.transform.SetParent(null, true);
        foreach (var script in jar.GetComponentsInChildren<MonoBehaviour>()) UnityEngine.Object.DestroyImmediate(script);
        PrefabUtility.SaveAsPrefabAsset(jar, "Assets/GhostFavorSmokeTestJar.prefab");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // A plain URP renderer makes the texture readable without the game's pixel filter.
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/GhostFavorSmokeTestRenderer.asset");
        if (rendererData == null) { rendererData = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rendererData, "Assets/GhostFavorSmokeTestRenderer.asset"); }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/GhostFavorSmokeTestPipeline.asset");
        if (pipeline == null) { pipeline = UniversalRenderPipelineAsset.Create(rendererData); AssetDatabase.CreateAsset(pipeline, "Assets/GhostFavorSmokeTestPipeline.asset"); }
        pipeline.renderScale = 1; pipeline.msaaSampleCount = 1;
        GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
        jar = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GhostFavorSmokeTestJar.prefab"));
        jar.transform.position = Vector3.zero;
        Bounds bounds = new Bounds(); bool found = false;
        foreach (var r in jar.GetComponentsInChildren<Renderer>()) { if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds); }
        var runner = new GameObject("Ghost Favor disappearance checks").AddComponent<GhostFavorSmokeChecks>();
        runner.item = jar.transform;
        var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        counter.transform.position = new Vector3(bounds.center.x, bounds.min.y - .08f, bounds.center.z);
        counter.transform.localScale = new Vector3(6, .15f, 5);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GhostFavorSmokeTestCounter.mat");
        if (material == null) {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, "Assets/GhostFavorSmokeTestCounter.mat");
        }
        material.SetColor("_BaseColor", new Color(.55f, .58f, .6f));
        counter.GetComponent<Renderer>().sharedMaterial = material;
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .25f);
        camera.transform.position = bounds.center + new Vector3(1.7f, 1.1f, -2.8f) * size;
        camera.transform.LookAt(bounds.center + Vector3.up * size * .3f);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
        camera.nearClipPlane = .01f;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        var light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.eulerAngles = new Vector3(40, -35, 0); light.intensity = 2;
        RenderSettings.ambientLight = Color.gray;
        EditorSceneManager.SaveScene(scene, "Assets/GhostFavorSmokeTest.unity");
        AssetDatabase.SaveAssets();
        EditorApplication.EnterPlaymode();
    }

    public static void BakeSmokeTexture()
    {
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        var lobes = new[] { new Vector3(-.30f,-.05f,.38f), new Vector3(.04f,.25f,.40f),
            new Vector3(.32f,.08f,.31f), new Vector3(.06f,-.27f,.36f), new Vector3(-.27f,.34f,.22f) };
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        {
            Vector2 p = new Vector2((x + .5f) / 128 - 1, (y + .5f) / 128 - 1);
            Vector2 warp = new Vector2(Mathf.PerlinNoise(p.x * 3 + 20, p.y * 3 + 31),
                Mathf.PerlinNoise(p.x * 3 + 61, p.y * 3 + 7)) - Vector2.one * .5f;
            Vector2 q = p + warp * .19f;
            float cloud = 0;
            foreach (Vector3 lobe in lobes)
            {
                float d = Vector2.Distance(q, new Vector2(lobe.x, lobe.y)) / lobe.z;
                cloud = Mathf.Max(cloud, 1 - Mathf.SmoothStep(.20f, 1.3f, d));
            }
            float noise = .52f * Mathf.PerlinNoise(p.x * 4 + 8, p.y * 4 + 13)
                + .28f * Mathf.PerlinNoise(p.x * 9 + 24, p.y * 9 + 3)
                + .14f * Mathf.PerlinNoise(p.x * 21 + 11, p.y * 21 + 40)
                + .06f * Mathf.PerlinNoise(p.x * 44 + 7, p.y * 44 + 8);
            float alpha = cloud * Mathf.SmoothStep(.18f, .78f, noise);
            texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        texture.Apply();
        const string path = "Assets/Resources/GhostFavorSmoke.png";
        System.IO.File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset("Assets/Resources/GhostFavorDisappear.vfx", ImportAssetOptions.ForceUpdate);
    }
}
