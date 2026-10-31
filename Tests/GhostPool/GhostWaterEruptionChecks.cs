using System;
using System.IO;
using System.Linq;
using PreThesis.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

// Copy to Assets/Editor in an isolated project. GPU checks require -force-d3d11, not -nographics.
[InitializeOnLoad]
public static class GhostWaterEruptionChecks
{
    const string Running = "GhostWaterEruptionChecks.Running";
    const string GraphPath = "Assets/VFX/GhostPool/Graphs/Ghost_BlackWater_Splash.vfx";
    const string Output = "captures/water-evaporation";
    static GhostPoolPreview rig;
    static Camera camera;
    static VisualEffect[] effects;
    static int frame;
    static int maxAlive;
    static bool failed;

    static GhostWaterEruptionChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false))
                EditorApplication.delayCall += Setup;
        };
    }

    [MenuItem("Tools/Pre-Thesis/VFX/Verify Water Eruption")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        try
        {
            var shader = Shader.Find("PreThesis/VFX/Black Water");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Water shader imports");
            EditorSceneManager.OpenScene("Assets/VFX/VFX_Preview.unity");
            var switcher = UnityEngine.Object.FindAnyObjectByType<VfxPreviewSwitcher>();
            Require(switcher != null, "Combined VFX Preview exists");
            switcher.ShowGhost();
            Require(switcher.ghostPool != null, "Combined preview uses shared water rig");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Fail(e); }
    }

    static void Setup()
    {
        try
        {
            rig = UnityEngine.Object.FindAnyObjectByType<GhostPoolPreview>();
            Require(rig != null, "Ghost water stage active");
            camera = Camera.main;
            Require(camera != null, "Preview camera active");
            rig.enabled = false;
            rig.waterOnly = true;
            rig.manualWaterTime = true;
            rig.Replay();
            effects = rig.pools[0].transform.parent.parent.GetComponentsInChildren<VisualEffect>(true);
            foreach (var effect in effects) { effect.Reinit(); effect.Stop(); effect.pause = true; }
            // Replay resets the shared opening splash state before deterministic frame stepping.
            rig.Sample(.01f); rig.Sample(0);
            foreach (var pool in rig.pools)
            {
                Require(pool.GetComponent<MeshFilter>().sharedMesh.vertexCount == 4225, "Displaced surface has internal geometry");
                Require(pool.GetComponent<Collider>() == null, "Presentation creates no pool collider");
            }
            Require(GhostPoolSurface.SpreadAt(0) == 0 && GhostPoolSurface.SpreadAt(1) == 1, "Pool spread endpoints");
            CheckCoverage();
            frame = 0;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        try
        {
            float age = frame / 60f;
            if (frame == 450) rig.waterOnly = false;
            rig.Sample(age);
            if (frame == 54) foreach (var splash in rig.splashes) splash.Play();
            foreach (var effect in effects)
            {
                if (!effect.gameObject.activeInHierarchy) continue;
                effect.Simulate(1f / 60, 1);
                maxAlive = Mathf.Max(maxAlive, effect.aliveParticleCount);
            }
            if (frame == 18 || frame == 30 || frame == 42 || frame == 69 || frame == 96 || frame == 150 || frame == 387 || frame == 417 || frame == 450 || frame == 500 || frame == 550 || frame == 640)
                Capture("water-" + frame.ToString("000"));
            var vapor = rig.pools[0].GetComponent<GhostPoolSurface>().Evaporation;
            if (frame == 450)
                Require(vapor != null && !vapor.gameObject.activeInHierarchy,
                    "No evaporation smoke before the water disappears");
            if (frame == 500)
                Require(vapor.gameObject.activeInHierarchy && vapor.aliveParticleCount > 0,
                    "Evaporation produces live GPU smoke after the water disappears");
            if (frame == 640)
                Require(!vapor.gameObject.activeInHierarchy, "Vapor ends before the effect is cleaned up");
            if (frame == 96)
            {
                rig.waterOnly = false; rig.Sample(age); Capture("ghost-breach"); rig.waterOnly = true;
            }
            foreach (var message in ShaderUtil.GetShaderMessages(rig.pools[0].sharedMaterial.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            frame++;
            if (frame <= 660) return;
            Require(maxAlive > 0, "Native VFX Graph produces live GPU droplets");
            rig.Sample(0); rig.Sample(.4f);
            rig.waterEruption = false; rig.Sample(.4f);
            var properties = new MaterialPropertyBlock(); rig.pools[0].GetPropertyBlock(properties);
            Require(properties.GetFloat("_Eruption") == 0, "Comparison toggle disables surface eruption");
            rig.Sample(6.9f);
            Require(Vector3.Distance(rig.pools[0].transform.localScale, GhostPoolEffect.SurfaceSize) < .001f,
                "Water soaks in place without shrinking the footprint");
            rig.pools[0].GetPropertyBlock(properties);
            Require(properties.GetFloat("_Absorb") > 0 && properties.GetFloat("_Absorb") < 1, "Soaking is active during disappearance");
            Finish();
        }
        catch (Exception e) { Fail(e); }
    }

    static void CheckCoverage()
    {
        var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.transform.position = new Vector3(100, 0, 0);
        surface.transform.rotation = Quaternion.Euler(90, 0, 0);
        surface.transform.localScale = GhostPoolEffect.SurfaceSize;
        var material = new Material(rig.pools[0].sharedMaterial);
        surface.GetComponent<Renderer>().sharedMaterial = material;
        GhostPoolSurface.Create(surface.GetComponent<Renderer>(), null);
        var top = new GameObject("Water coverage probe").AddComponent<Camera>();
        top.transform.position = new Vector3(100, 3, 0);
        top.transform.rotation = Quaternion.Euler(90, 0, 0);
        top.orthographic = true; top.orthographicSize = 2;
        top.clearFlags = CameraClearFlags.SolidColor; top.backgroundColor = Color.magenta;
        top.nearClipPlane = .01f; top.farClipPlane = 10;
        var target = RenderTexture.GetTemporary(256, 256, 24);
        var previous = RenderTexture.active;
        top.targetTexture = target;
        int full = 0, partial = 0;
        try
        {
            // Compare with the same camera's empty frame, including the game's scanline/vignette filter.
            surface.SetActive(false);
            top.Render(); RenderTexture.active = target;
            var emptyImage = new Texture2D(256, 256, TextureFormat.RGB24, false);
            emptyImage.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); emptyImage.Apply();
            Color[] empty = emptyImage.GetPixels();
            UnityEngine.Object.DestroyImmediate(emptyImage);
            surface.SetActive(true);
            Func<Color, Color, bool> differs = (a, b) => Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b) > .12f;
            material.SetFloat("_Eruption", 1);
            material.SetFloat("_Phase", .5f);
            material.SetFloat("_Fade", 1);
            material.SetFloat("_Horror", 1);
            material.SetFloat("_Resolve", 1);
            foreach (float absorb in new[] { 0f, .5f, 1f })
            {
                material.SetFloat("_Absorb", absorb);
                top.Render(); RenderTexture.active = target;
                var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                int covered = image.GetPixels().Where((c, index) => differs(c, empty[index])).Count();
                if (absorb == 0)
                {
                    full = covered;
                    for (int y = 126; y <= 130; y++) for (int x = 126; x <= 130; x++)
                    {
                        Color c = image.GetPixel(x, y);
                        Require(differs(c, empty[y * 256 + x]), "Centre has continuous water coverage");
                    }
                }
                else if (absorb == .5f) partial = covered;
                else Require(covered == 0, "Fully absorbed water leaves no visible residue");
                File.WriteAllBytes(Output + "/coverage-" + absorb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            Require(full > 1000 && partial > 0 && partial < full, "Wet patches recede progressively during absorption");
        }
        finally
        {
            RenderTexture.active = previous; top.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(top.gameObject);
            UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    static void Capture(string name)
    {
        var target = RenderTexture.GetTemporary(1440, 900, 24);
        camera.targetTexture = target; camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(1440, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); image.Apply();
        File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        RenderTexture.active = previous; camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(target);
    }

    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Finish()
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(Running, false);
        File.WriteAllText(Output + "/results.txt", "PASS: no early smoke, live evaporation after water disappears, vapor tail cleanup, continuous centre coverage, absorption, fixed footprint, replay and GPU VFX. Maximum live particles: " + maxAlive);
        if (Application.isBatchMode) EditorApplication.Exit(0);
        else
        {
            rig.waterEruption = true; rig.waterOnly = false; rig.manualWaterTime = false;
            rig.enabled = true; rig.Replay();
            Debug.Log("[Water eruption] PASS. Preview is playing.");
        }
    }
    static void Fail(Exception e)
    {
        if (failed) return;
        failed = true; Debug.LogException(e);
        EditorApplication.update -= Tick;
        SessionState.SetBool(Running, false);
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/results.txt", "FAIL: " + e);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
