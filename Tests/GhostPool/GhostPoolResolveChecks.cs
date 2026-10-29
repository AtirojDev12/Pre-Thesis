using System;
using System.IO;
using PreThesis.Vfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GhostPoolResolveChecks
{
    const string Running = "GhostPoolResolveChecks.Running";
    static string output;
    static int frame;
    static Camera camera;
    static Renderer pool;
    static GhostPoolAtmosphere smoke;
    static Material water;
    static readonly float[] resolves = { 0, .3f, .6f, 1 };

    static GhostPoolResolveChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Running, false)) Setup();
        };
    }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Running, true);
        EditorApplication.EnterPlaymode();
    }

    static void Setup()
    {
        output = Path.GetFullPath("captures/ghost-pool-resolve");
        Directory.CreateDirectory(output);
        try
        {
            var shader = Shader.Find("PreThesis/VFX/Black Water");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader imports without errors");
            Require(GhostPoolAtmosphere.ResolveAt(0, 1.25f, false) == 0, "Appear starts unresolved");
            Require(GhostPoolAtmosphere.ResolveAt(3, 1.25f, false) == 1, "Appear reaches full resolve");
            Require(GhostPoolAtmosphere.ResolveAt(0, 1.25f, true) == 1, "Submerge starts resolved");
            Require(GhostPoolAtmosphere.ResolveAt(3, 1.25f, true) == 0, "Submerge ends unresolved");
            Require(!float.IsNaN(GhostPoolAtmosphere.ResolveAt(1, 0, false)), "Zero travel is safe");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.24f, .28f, .34f);
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0, -.12f, 0);
            floor.transform.localScale = new Vector3(12, .2f, 12);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor", new Color(.2f, .22f, .24f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.transform.position = new Vector3(0, 3, 4);
            backdrop.transform.localScale = new Vector3(12, 6, .1f);
            backdrop.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.transform.rotation = Quaternion.Euler(90, 0, 0);
            surface.transform.localScale = GhostPoolEffect.SurfaceSize;
            pool = surface.GetComponent<Renderer>();
            water = new Material(shader);
            pool.sharedMaterial = water;
            smoke = GhostPoolAtmosphere.Create(pool);
            Require(smoke != null, "Local atmosphere created from serialized shader reference");
            Require(smoke.GetComponentsInChildren<Collider>().Length == 0, "Atmosphere has no colliders");
            camera = new GameObject("Capture camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 3, -5);
            camera.transform.LookAt(new Vector3(0, .8f, 0));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .07f, .085f);
            camera.fieldOfView = 45;
            EditorApplication.update += Tick;
        }
        catch (Exception e) { Fail(e); }
    }

    static void Tick()
    {
        try
        {
            float resolve = resolves[Mathf.Min(frame / 2, resolves.Length - 1)];
            var properties = new MaterialPropertyBlock();
            properties.SetFloat("_Horror", 1); properties.SetFloat("_Resolve", resolve);
            properties.SetFloat("_Phase", 1.5f); properties.SetFloat("_Impact", .5f);
            pool.SetPropertyBlock(properties);
            smoke.Sample(1.5f, resolve, 1);
            var target = RenderTexture.GetTemporary(1000, 800, 24);
            camera.targetTexture = target;
            camera.Render();
            if (frame % 2 == 1)
            {
                RenderTexture.active = target;
                var image = new Texture2D(1000, 800, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1000, 800), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(output, "resolve-" + resolve.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                RenderTexture.active = null;
            }
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            var mesh = smoke.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh.vertexCount == 288 && mesh.triangles.Length == 432, "Smoke geometry budget");
            Require(mesh.bounds.size.y > 1.5f, "Smoke has vertical volume");
            foreach (var message in ShaderUtil.GetShaderMessages(water.shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            frame++;
            if (frame < resolves.Length * 2) return;
            EditorApplication.update -= Tick;
            SessionState.SetBool(Running, false);
            File.WriteAllText(Path.Combine(output, "results.txt"), "PASS: shader compilation, resolve endpoints, zero travel, geometry budget, vertical volume and collider isolation. Rendered 0.0 / 0.3 / 0.6 / 1.0.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Fail(e); }
    }

    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Fail(Exception e)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(Running, false);
        Debug.LogException(e);
        File.WriteAllText(Path.Combine(output, "results.txt"), "FAIL: " + e);
        EditorApplication.Exit(1);
    }
}
