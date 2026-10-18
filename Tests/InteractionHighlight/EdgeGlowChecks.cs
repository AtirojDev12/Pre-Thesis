// Installed into an isolated Unity project; never included in the game build.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class EdgeGlowChecks
{
    static readonly List<string> results = new List<string>();
    static Camera camera;
    static RenderTexture target;
    static InteractionOutline outline;
    static GameObject cube;
    static Material originalMaterial;
    static int ticks;
    static UniversalRenderPipelineAsset pipeline;

    public static void Run()
    {
        try
        {
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var feature = ScriptableObject.CreateInstance<InteractionEdgeGlowFeature>();
            renderer.rendererFeatures.Add(feature);
            feature.Create();
            AssetDatabase.CreateAsset(renderer, "Assets/TestRenderer.asset");
            AssetDatabase.AddObjectToAsset(feature, renderer);
            pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_RendererDataList").arraySize = 1;
            serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(pipeline, "Assets/TestPipeline.asset");
            pipeline.msaaSampleCount = 1;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            camera = new GameObject("Test Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(3, 2.5f, -4);
            camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.045f, 0.045f, 1);
            camera.fieldOfView = 42;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            target.Create();
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            originalMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            originalMaterial.SetColor("_BaseColor", new Color(0.18f, 0.36f, 0.48f, 1));
            cube.GetComponent<Renderer>().sharedMaterial = originalMaterial;
            outline = cube.AddComponent<InteractionOutline>();
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }

    static void Tick()
    {
        if (++ticks < 12) return;
        EditorApplication.update -= Tick;
        try
        {
            foreach (string name in new[] { "Popcorn/InteractionOutline", "Popcorn/InteractionEdgeGlow" })
            {
                Shader shader = Shader.Find(name);
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), name + " compiles");
            }
            outline.SetVisible(false);
            Color32[] baseline = Capture("before");
            outline.SetVisible(true, camera);
            Color32[] highlighted = Capture("hover");
            int changed = Changed(baseline, highlighted);
            Check(changed > 500 && changed < 18000, "edges change while face interiors remain intact: " + changed + " pixels");
            // Central projected cube corner is an interior crease, far from its silhouette.
            Vector3 corner = camera.WorldToViewportPoint(new Vector3(0.5f, 0.5f, -0.5f));
            int interiorChanges = 0;
            int cx = Mathf.RoundToInt(corner.x * 512), cy = Mathf.RoundToInt(corner.y * 512);
            for (int y = cy - 8; y <= cy + 8; y++)
                for (int x = cx - 8; x <= cx + 8; x++)
                    if (Delta(baseline[y * 512 + x], highlighted[y * 512 + x]) > 20) interiorChanges++;
            Check(interiorChanges > 10, "interior cube crease is highlighted: " + interiorChanges + " pixels");
            Check(cube.GetComponent<Renderer>().sharedMaterial == originalMaterial, "authored material preserved");
            typeof(InteractionOutline).GetField("selectedAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(outline, Time.unscaledTime - 0.35f);
            Color32[] lowerBand = Capture("sweep-low");
            typeof(InteractionOutline).GetField("selectedAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(outline, Time.unscaledTime - 0.85f);
            Color32[] upperBand = Capture("sweep-high");
            Check(Changed(lowerBand, upperBand) > 100, "sweep moves along edges while hovered");
            Check(upperBand.Select((pixel, i) => WhiteBlend(baseline[i], pixel)).All(value => value),
                "sweep and resting edges share one white hue");
            double upwardMotion = 0;
            for (int i = 0; i < upperBand.Length; i++)
                upwardMotion += (upperBand[i].r + upperBand[i].g + upperBand[i].b - lowerBand[i].r - lowerBand[i].g - lowerBand[i].b) * (i / 512 - 256);
            Check(upwardMotion > 0, "light band travels upward along the model");
            outline.SetVisible(false);
            Check(Changed(baseline, Capture("leave")) == 0, "leaving hover restores original image");
            var other = new GameObject("Other Camera").AddComponent<Camera>();
            outline.SetVisible(true, other);
            Check(Changed(baseline, Capture("other-camera")) == 0, "selection is local to owning camera");
            outline.SetVisible(true, camera);
            cube.SetActive(false);
            var list = new List<Renderer>();
            InteractionOutline.CollectRenderers(camera, list);
            Check(list.Count == 0, "disabled selection unregisters");
            cube.SetActive(true);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = camera.transform.position * 0.45f;
            wall.transform.localScale = Vector3.one * 2.3f;
            wall.GetComponent<Renderer>().sharedMaterial = originalMaterial;
            outline.SetVisible(false);
            Color32[] occluded = Capture("occluded-before");
            outline.SetVisible(true, camera);
            Check(Changed(occluded, Capture("occluded-hover")) == 0, "opaque wall occludes edge lighting");
            wall.SetActive(false);
            QualitySettings.antiAliasing = 4;
            pipeline.msaaSampleCount = 4;
            outline.SetVisible(false);
            Color32[] msaaBaseline = Capture("before-msaa");
            outline.SetVisible(true, camera);
            Check(Changed(msaaBaseline, Capture("hover-msaa")) > 500, "MSAA render path draws selection");
            foreach (string rendererName in new[] { "PC_Renderer", "Mobile_Renderer" })
            {
                var authoredRenderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/" + rendererName + ".asset");
                Check(authoredRenderer != null && authoredRenderer.rendererFeatures.Count(f => f is InteractionEdgeGlowFeature && f.isActive) == 1,
                    rendererName + " has one enabled edge glow feature");
                if (rendererName != "PC_Renderer") continue;
                var authoredPipeline = Object.Instantiate(pipeline);
                var settings = new SerializedObject(authoredPipeline);
                settings.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = authoredRenderer;
                settings.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(authoredPipeline, "Assets/AuthoredPipeline.asset");
                QualitySettings.renderPipeline = authoredPipeline;
                GraphicsSettings.defaultRenderPipeline = authoredPipeline;
                outline.SetVisible(false);
                Color32[] authoredBaseline = Capture("pc-before");
                outline.SetVisible(true, camera);
                Check(Changed(authoredBaseline, Capture("pc-hover")) > 500, "actual PC renderer with retro filter draws readable hover edges");
            }
            outline.SetVisible(false);
            cube.SetActive(false);
            var makerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Maker/Popcorn_Maker.prefab");
            Check(makerPrefab != null, "actual Popcorn Maker prefab imports");
            var maker = Object.Instantiate(makerPrefab);
            maker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Renderer[] parts = maker.GetComponentsInChildren<Renderer>();
            Bounds makerBounds = parts[0].bounds;
            foreach (Renderer part in parts) makerBounds.Encapsulate(part.bounds);
            maker.transform.localScale *= 1.8f / Mathf.Max(makerBounds.size.x, makerBounds.size.y, makerBounds.size.z);
            makerBounds = parts[0].bounds;
            foreach (Renderer part in parts) makerBounds.Encapsulate(part.bounds);
            maker.transform.position -= makerBounds.center;
            Check(parts.SelectMany(part => part.sharedMaterials).Any(mat => mat != null && mat.renderQueue >= 3000),
                "Popcorn Maker reproduces transparent material setup");
            Color32[] makerBefore = Capture("maker-before");
            var makerOutline = maker.AddComponent<InteractionOutline>();
            makerOutline.SetVisible(true, camera);
            Check(Changed(makerBefore, Capture("maker-hover")) > 200, "actual transparent Popcorn Maker highlights");
            makerOutline.SetVisible(false);
            Check(Changed(makerBefore, Capture("maker-leave")) == 0, "Popcorn Maker restores original appearance after hover");
            maker.SetActive(false);
            cube.SetActive(true);
            var invisible = new Material(originalMaterial);
            invisible.renderQueue = 3000;
            invisible.SetColor("_BaseColor", new Color(1, 1, 1, 0));
            invisible.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            invisible.SetFloat("_Surface", 1);
            invisible.SetFloat("_ZWrite", 0);
            invisible.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            invisible.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            cube.GetComponent<Renderer>().sharedMaterial = invisible;
            outline.SetVisible(false);
            Color32[] invisibleBefore = Capture("transparent-before");
            outline.SetVisible(true, camera);
            Check(Changed(invisibleBefore, Capture("transparent-hover")) == 0, "fully transparent pixels do not produce solid outlines");
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }

    static Color32[] Capture(string name)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.GetFullPath("../" + name + ".png"), image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        Object.DestroyImmediate(image);
        RenderTexture.active = null;
        return pixels;
    }
    static int Delta(Color32 a, Color32 b) => Math.Abs(a.r-b.r) + Math.Abs(a.g-b.g) + Math.Abs(a.b-b.b);
    static bool WhiteBlend(Color32 before, Color32 after)
    {
        if (Delta(before, after) <= 20) return true;
        float r = (after.r - before.r) / (float)Math.Max(1, 255 - before.r);
        float g = (after.g - before.g) / (float)Math.Max(1, 255 - before.g);
        float b = (after.b - before.b) / (float)Math.Max(1, 255 - before.b);
        // White is alpha-blended over the original colored surface, not replacing it.
        return Mathf.Max(r, g, b) - Mathf.Min(r, g, b) < 0.15f;
    }
    static int Changed(Color32[] a, Color32[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++) if (Delta(a[i], b[i]) > 20) count++;
        return count;
    }
    static void Check(bool condition, string message) => results.Add((condition ? "PASS " : "FAIL ") + message);
    static void Finish(Exception error)
    {
        if (error != null) results.Add("FAIL " + error);
        File.WriteAllLines("../results.txt", results);
        EditorApplication.Exit(results.Exists(line => line.StartsWith("FAIL")) ? 1 : 0);
    }
}
