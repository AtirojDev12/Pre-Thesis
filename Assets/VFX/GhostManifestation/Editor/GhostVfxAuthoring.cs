using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PreThesis.Vfx;

/// <summary>Explicit authoring command only; never runs on import or changes gameplay scenes.</summary>
public static class GhostVfxAuthoring
{
    private const string Root = "Assets/VFX/GhostManifestation";

    [MenuItem("Tools/Pre-Thesis/VFX/Open Ghost Preview")]
    public static void OpenPreview()
    {
        if (EditorApplication.isPlaying) return;
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Root + "/VFX_Preview.unity");
    }

    // Invoked in an isolated authoring project. Existing authored files are never overwritten.
    public static void Build()
    {
        try
        {
            if (File.Exists(Root + "/VFX_Preview.unity"))
                throw new InvalidOperationException("Preview already exists. Build only in a fresh authoring workspace.");
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/AuthoringRenderer.asset");
                var pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, "Assets/AuthoringPipeline.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
            }
            var shader = Shader.Find("PreThesis/VFX/Ghost Smoke");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Smoke shader failed to compile.");
            var smoke = new Material(shader) { name = "GhostSmoke" };
            smoke.SetFloat("_Density", 1.35f);
            AssetDatabase.CreateAsset(smoke, Root + "/Materials/GhostSmoke.mat");
            MakeEffect(true, smoke);
            MakeEffect(false, smoke);
            MakeScene();
            AssetDatabase.SaveAssets();
            ValidateAndCapture();
            File.WriteAllText("vfx-results.txt", "PASS: Prefabs, scene, shader, particle simulation and preview captures verified.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("vfx-results.txt", "FAIL: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static void MakeEffect(bool appearing, Material material)
    {
        string name = appearing ? "Ghost_Appear" : "Ghost_Disappear";
        var root = new GameObject(name);
        // A zero-emission parent gives Unity's native preview controls one root to play.
        var parent = root.AddComponent<ParticleSystem>();
        parent.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = parent.main;
        main.duration = .1f;
        main.startLifetime = .01f;
        main.loop = false;
        main.playOnAwake = true;
        var emission = parent.emission;
        emission.enabled = false;
        root.GetComponent<ParticleSystemRenderer>().enabled = false;

        var body = Layer(root, "01_BodyMist", material, 95, .0f, appearing ? 1.55f : 1.9f,
            .55f, 1.05f, new Color(.34f,.41f,.43f,.68f), appearing ? -.68f : .70f,
            appearing ? .05f : .26f, new Vector3(.8f,.90f,.65f));
        body.transform.localPosition = new Vector3(0,.95f,0);
        var veil = Layer(root, "02_TornVeil", material, 42, appearing ? .55f : .04f, 1.65f,
            .40f, .85f, new Color(.56f,.64f,.63f,.35f), appearing ? -.26f : .52f,
            .42f, new Vector3(.44f,.8f,.40f));
        veil.transform.localPosition = new Vector3(0,.95f,0);
        var floor = Layer(root, "03_GroundBreath", material, 36, 0f, 2.25f,
            .5f, .95f, new Color(.34f,.40f,.40f,.32f), appearing ? -.35f : .55f,
            .025f, new Vector3(.85f,.035f,.85f));
        floor.transform.localPosition = new Vector3(0,.12f,0);
        var ash = Layer(root, "04_Ash", material, 48, appearing ? .15f : .05f, 2.0f,
            .035f, .095f, new Color(.64f,.69f,.67f,.65f), appearing ? -.45f : .85f,
            .32f, new Vector3(.65f,.85f,.55f));
        ash.transform.localPosition = new Vector3(0,.95f,0);
        var noise = ash.noise;
        noise.strength = .18f;
        PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static ParticleSystem Layer(GameObject root, string name, Material material, short count,
        float delay, float lifetime, float sizeMin, float sizeMax, Color tint, float radial, float rise, Vector3 shapeScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = (uint)(100 + count);
        var main = ps.main;
        main.duration = .1f;
        main.loop = false;
        main.playOnAwake = true;
        main.startDelay = delay;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime*.75f,lifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin,sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI,Mathf.PI);
        main.startColor = tint;
        main.maxParticles = count + 8;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f,count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 1f;
        shape.radiusThickness = radial < 0 ? .18f : 1f;
        shape.scale = shapeScale;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.radial = new ParticleSystem.MinMaxCurve(radial, AnimationCurve.EaseInOut(0,1,1,.15f));
        velocity.y = rise;
        velocity.orbitalY = radial < 0 ? .28f : -.16f;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white,0), new GradientColorKey(Color.white,1) },
            new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(1,.16f),new GradientAlphaKey(.72f,.55f),new GradientAlphaKey(0,1) });
        color.color = gradient;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0,.55f),new Keyframe(.35f,1f),new Keyframe(1,1.45f)));
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-.4f,.4f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = .10f;
        noise.frequency = .65f;
        noise.scrollSpeed = .2f;
        noise.quality = ParticleSystemNoiseQuality.Medium;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 2f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static Material Solid(string name, Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", .12f);
        AssetDatabase.CreateAsset(mat, Root + "/Materials/" + name + ".mat");
        return mat;
    }

    private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void MakeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.26f,.29f,.32f);
        RenderSettings.fog = false;
        var floor = Solid("PreviewFloor",new Color(.065f,.075f,.085f));
        var stand = Solid("PreviewPlinth",new Color(.13f,.16f,.18f));
        var reference = Solid("PreviewReference",new Color(.26f,.32f,.33f));
        Primitive("Studio floor",PrimitiveType.Cube,new Vector3(0,-.2f,0),new Vector3(30,.2f,30),floor);
        var rig = new GameObject("Preview controls (preview scene only)").AddComponent<GhostVfxPreview>();
        for (int i=0;i<2;i++)
        {
            float x = i==0 ? -1.85f : 1.85f;
            Primitive(i==0 ? "Appear plinth" : "Disappear plinth",PrimitiveType.Cylinder,new Vector3(x,-.06f,0),new Vector3(2.75f,.04f,2.75f),stand);
            string path = Root + "/Prefabs/" + (i==0 ? "Ghost_Appear" : "Ghost_Disappear") + ".prefab";
            var fx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            fx.transform.position = new Vector3(x,0,0);
            var dummy = new GameObject(i==0 ? "Appear size reference" : "Disappear size reference");
            Primitive("Body (placeholder)",PrimitiveType.Capsule,new Vector3(x,.85f,0),new Vector3(.43f,.65f,.34f),reference).transform.SetParent(dummy.transform,true);
            Primitive("Head (placeholder)",PrimitiveType.Sphere,new Vector3(x,1.64f,0),new Vector3(.33f,.38f,.32f),reference).transform.SetParent(dummy.transform,true);
            if (i==0) { rig.appear=fx.GetComponent<ParticleSystem>(); rig.appearReference=dummy; }
            else { rig.disappear=fx.GetComponent<ParticleSystem>(); rig.disappearReference=dummy; }
        }
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0,3.0f,-8);
        camera.transform.LookAt(new Vector3(0,.85f,0));
        camera.orthographic = true;
        camera.orthographicSize = 2.8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.024f,.030f,.038f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 50;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        var light = new GameObject("Soft studio light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(.77f,.85f,1);
        light.transform.rotation = Quaternion.Euler(45,-25,0);
        EditorSceneManager.SaveScene(scene,Root + "/VFX_Preview.unity");
    }

    public static void ValidateAndCapture()
    {
        var rig = UnityEngine.Object.FindAnyObjectByType<GhostVfxPreview>();
        var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (rig == null || camera == null) throw new Exception("Preview is incomplete.");
        Directory.CreateDirectory("captures");
        foreach (float time in new[] { .4f,.85f,1.3f,2.1f })
        {
            rig.appear.Simulate(time,true,true,true);
            rig.disappear.Simulate(time,true,true,true);
            rig.appearReference.SetActive(time>=.85f);
            rig.disappearReference.SetActive(time<.22f);
            var target = new RenderTexture(1280,720,24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(1280,720,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,1280,720),0,0);
            texture.Apply();
            float low = 1f, high = 0f;
            foreach (Color pixel in texture.GetPixels())
            {
                low = Mathf.Min(low, pixel.grayscale);
                high = Mathf.Max(high, pixel.grayscale);
            }
            if (high-low < .06f) throw new Exception("Preview capture is blank.");
            File.WriteAllBytes("captures/ghost-" + time.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture) + ".png",texture.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
        foreach (var effect in new[] { rig.appear,rig.disappear })
        {
            effect.Simulate(.8f,true,true,true);
            int alive = 0;
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>())
            {
                if (system.main.loop) throw new Exception("Effect must be one-shot.");
                alive += system.particleCount;
            }
            if (alive<50) throw new Exception("Expected a visible particle burst.");
            effect.Simulate(5f,true,true,true);
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>())
            {
                Debug.Log($"[GhostVFX] {effect.name}/{system.name}: time={system.time}, particles={system.particleCount}, alive={system.IsAlive(false)}");
                if (system.particleCount != 0) throw new Exception("Particles remained after five seconds: " + system.name);
            }
        }
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<ParticleSystemRenderer>())
            if (renderer.enabled && (renderer.sharedMaterial == null || ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader)))
                throw new Exception("Particle material is missing or invalid.");
    }

    public static void VerifyExisting()
    {
        EditorSceneManager.OpenScene(Root + "/VFX_Preview.unity");
        EditorApplication.QueuePlayerLoopUpdate();
        double readyAt = EditorApplication.timeSinceStartup + 2.0;
        EditorApplication.CallbackFunction verify = null;
        verify = () =>
        {
            if (EditorApplication.timeSinceStartup < readyAt) return;
            EditorApplication.update -= verify;
            FinishVerification();
        };
        EditorApplication.update += verify;
    }

    private static void FinishVerification()
    {
        try
        {
            ValidateAndCapture();
            File.WriteAllText("vfx-results.txt", "PASS: Prefabs, scene, shader, particle simulation and preview captures verified.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("vfx-results.txt", "FAIL: " + e);
            EditorApplication.Exit(1);
        }
    }
}
