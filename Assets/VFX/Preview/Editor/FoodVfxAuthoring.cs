using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PreThesis.Vfx;

public static class FoodVfxAuthoring
{
    private const string Popcorn = "Assets/VFX/PopcornMachine";
    private const string Drink = "Assets/VFX/DrinkDispenser";
    private const string Preview = "Assets/VFX/Preview";
    private const string ScenePath = "Assets/VFX/VFX_Preview.unity";

    [MenuItem("Tools/Pre-Thesis/VFX/Open Food Preview")]
    public static void OpenPreview()
    {
        if (!EditorApplication.isPlaying && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(ScenePath);
            var rig = UnityEngine.Object.FindAnyObjectByType<FoodVfxPreview>();
            if (rig != null) FoodVfxScenePreview.Frame(rig.transform);
        }
    }

    // Run in an isolated authoring project. Never replaces existing scene work.
    public static void Build()
    {
        try
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Food preview already exists.");
            foreach (string root in new[] { Popcorn, Drink, Preview })
                foreach (string folder in new[] { "Materials", "Prefabs" }) Directory.CreateDirectory(root + "/" + folder);
            Directory.CreateDirectory(Popcorn + "/Meshes");
            AssetDatabase.Refresh();
            MakePopcorn();
            MakeDrink();
            MakeScene();
            AssetDatabase.SaveAssets();
            Verify();
            File.WriteAllText("food-vfx-results.txt", "PASS: Food prefabs, particle motion, immediate cancellation, restart and preview renders.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("food-vfx-results.txt", "FAIL: " + e);
            EditorApplication.Exit(1);
        }
    }

    private static Material Material(string root, string name, Color color, bool particle = false, float softness = 0)
    {
        Shader shader = Shader.Find(particle ? "PreThesis/VFX/Food Particle" : "Universal Render Pipeline/Lit");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Invalid shader: " + name);
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        if (particle) material.SetFloat("_Softness", softness);
        else material.SetFloat("_Smoothness", .35f);
        AssetDatabase.CreateAsset(material, root + "/Materials/" + name + ".mat");
        return material;
    }

    private static ParticleSystem Particles(Transform parent, string name, Vector3 position, Material material,
        float rate, float lifetime, float minSize, float maxSize, Vector3 box, float rise, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = (uint)(41 + rate);
        var main = ps.main;
        main.duration = 3;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = lifetime;
        main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.maxParticles = 512;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = 0;
        velocity.y = rise;
        velocity.z = 0;
        var color = ps.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .7f), new GradientAlphaKey(0, 1) });
        color.color = gradient;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.maxParticleSize = 1;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static Mesh KernelMesh()
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh source = sphere.GetComponent<MeshFilter>().sharedMesh;
        var combines = new CombineInstance[4];
        for (int i = 0; i < combines.Length; i++)
        {
            Vector3 offset = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(i * 2.1f) * .28f, .18f, Mathf.Sin(i * 2.1f) * .28f);
            combines[i] = new CombineInstance { mesh = source, transform = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * (i == 0 ? .8f : .55f)) };
        }
        var mesh = new Mesh { name = "PoppedKernel" };
        mesh.CombineMeshes(combines);
        UnityEngine.Object.DestroyImmediate(sphere);
        AssetDatabase.CreateAsset(mesh, Popcorn + "/Meshes/PoppedKernel.asset");
        return mesh;
    }

    private static void MakePopcorn()
    {
        var steamMat = Material(Popcorn, "HotSteam", new Color(.94f, .96f, 1, .22f), true, 1);
        var kernelMat = Material(Popcorn, "GoldenKernel", new Color(1, .83f, .42f));
        var powderMat = Material(Popcorn, "SeasoningPowder", new Color(1, .48f, .07f, .9f), true, .1f);
        var root = new GameObject("PopcornMachine_VFX");
        var fx = root.AddComponent<PopcornMachineVfx>();
        fx.steam = Particles(root.transform, "01_HotSteam_Rises", new Vector3(-.3f, 1.03f, 0), steamMat,
            24, 1.5f, .18f, .38f, new Vector3(.72f, .02f, .36f), .48f, 0);
        var color = fx.steam.colorOverLifetime;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(.55f, .65f), new GradientAlphaKey(0, 1) });
        color.color = gradient;
        var size = fx.steam.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .45f, 1, 1.8f));
        var noise = fx.steam.noise;
        noise.enabled = true; noise.strength = .09f; noise.frequency = 1.2f; noise.scrollSpeed = .35f;
        fx.kernels = Particles(root.transform, "02_Kernels_SmallPops", new Vector3(-.3f, 1.035f, 0), kernelMat,
            15, .37f, .055f, .09f, new Vector3(.64f, .015f, .3f), .95f, .52f);
        var renderer = fx.kernels.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = KernelMesh();
        var rotation = fx.kernels.rotationOverLifetime;
        rotation.enabled = true; rotation.z = new ParticleSystem.MinMaxCurve(-5, 5);
        fx.seasoning = Particles(root.transform, "03_Seasoning_FallsIntoBucket", new Vector3(.65f, 1.65f, 0), powderMat,
            95, .52f, .008f, .022f, new Vector3(.16f, .01f, .13f), -.9f, .36f);
        fx.Cancel();
        PrefabUtility.SaveAsPrefabAsset(root, Popcorn + "/Prefabs/PopcornMachine_VFX.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static LineRenderer Stream(Transform root, string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.sharedMaterial = mat;
        line.startWidth = width;
        line.endWidth = width * .8f;
        line.numCapVertices = 4;
        line.positionCount = 2;
        line.SetPositions(new[] { new Vector3(0, 1.7f, 0), new Vector3(0, .78f, 0) });
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private static void MakeDrink()
    {
        // Lines need a flat unlit surface; the radial particle shader is reserved for droplets.
        var water = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "WaterStream" };
        water.SetColor("_BaseColor", new Color(.15f, .64f, .86f));
        AssetDatabase.CreateAsset(water, Drink + "/Materials/WaterStream.mat");
        var shine = new Material(water) { name = "StreamHighlight" };
        shine.SetColor("_BaseColor", new Color(.72f, .96f, 1));
        AssetDatabase.CreateAsset(shine, Drink + "/Materials/StreamHighlight.mat");
        var drops = Material(Drink, "SplashDroplets", new Color(.64f, .9f, 1, .95f), true, .15f);
        var root = new GameObject("DrinkDispenser_VFX");
        var fx = root.AddComponent<DrinkDispenserVfx>();
        fx.stream = Stream(root.transform, "01_ContinuousWaterStream", water, .043f);
        fx.highlight = Stream(root.transform, "02_WaterHighlight", shine, .009f);
        fx.fallingDroplets = Particles(root.transform, "03_FallingDroplets", fx.nozzle, drops,
            55, .27f, .012f, .025f, new Vector3(.03f, .02f, .03f), -3.4f, 0);
        fx.rimSplashes = Particles(root.transform, "04_CupRimSplashes", fx.cupRim, drops,
            38, .24f, .012f, .032f, new Vector3(.095f, .005f, .095f), .7f, .85f);
        var velocity = fx.rimSplashes.velocityOverLifetime;
        velocity.x = new ParticleSystem.MinMaxCurve(-.65f, .65f);
        velocity.y = new ParticleSystem.MinMaxCurve(.7f, .7f);
        velocity.z = new ParticleSystem.MinMaxCurve(-.65f, .65f);
        fx.Cancel();
        PrefabUtility.SaveAsPrefabAsset(root, Drink + "/Prefabs/DrinkDispenser_VFX.prefab");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static GameObject Prop(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void Label(string text, Vector3 pos, float size, Color color)
    {
        var go = new GameObject(text);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(8, 0, 0);
        var label = go.AddComponent<TextMesh>();
        label.text = text; label.fontSize = 64; label.characterSize = size;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
    }

    private static void Vessel(string name, float x, float bottom, float top, float radius, Material body, Material rim, Transform parent)
    {
        // Open vessels keep the particle landing surface readable.
        Prop(name + " bottom", PrimitiveType.Cylinder, new Vector3(x, bottom, 0), new Vector3(radius * 2, .018f, radius * 2), body, parent);
        for (int i = 0; i < 32; i++)
        {
            float angle = i * Mathf.PI * 2 / 32;
            var wall = Prop(name + " wall", PrimitiveType.Cube, new Vector3(x + Mathf.Sin(angle) * radius, (bottom + top) / 2, Mathf.Cos(angle) * radius),
                new Vector3(.055f, top - bottom, .022f), body, parent);
            wall.transform.rotation = Quaternion.Euler(0, i * 360f / 32, 0);
            var lip = Prop(name + " rim", PrimitiveType.Sphere, new Vector3(x + Mathf.Sin(angle) * radius, top, Mathf.Cos(angle) * radius),
                new Vector3(.056f, .024f, .035f), rim, parent);
            lip.transform.rotation = wall.transform.rotation;
        }
    }

    private static void MakeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.35f, .39f, .46f);
        RenderSettings.fog = false;
        var floor = Material(Preview, "StudioFloor", new Color(.035f, .05f, .07f));
        var stand = Material(Preview, "Counter", new Color(.1f, .14f, .18f));
        var metal = Material(Preview, "BrushedMetal", new Color(.4f, .48f, .55f));
        var red = Material(Preview, "PopcornRed", new Color(.6f, .085f, .06f));
        var white = Material(Preview, "CupIvory", new Color(.9f, .92f, .89f));
        var blue = Material(Preview, "DispenserBlue", new Color(.045f, .28f, .36f));
        var golden = AssetDatabase.LoadAssetAtPath<Material>(Popcorn + "/Materials/GoldenKernel.mat");
        Prop("Studio floor", PrimitiveType.Cube, new Vector3(0, -.16f, 0), new Vector3(30, .2f, 30), floor);
        var rig = new GameObject("Food Preview Controls").AddComponent<FoodVfxPreview>();
        rig.popcorn = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Popcorn + "/Prefabs/PopcornMachine_VFX.prefab"))).GetComponent<PopcornMachineVfx>();
        rig.popcorn.transform.position = new Vector3(-2, 0, 0);
        rig.drink = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Drink + "/Prefabs/DrinkDispenser_VFX.prefab"))).GetComponent<DrinkDispenserVfx>();
        rig.drink.transform.position = new Vector3(2, 0, 0);
        var popcornProps = new GameObject("Popcorn machine / preview props").transform;
        var drinkProps = new GameObject("Drink dispenser / preview props").transform;
        Prop("Popcorn counter", PrimitiveType.Cube, new Vector3(-2, .22f, 0), new Vector3(2.6f, .45f, 1.4f), stand, popcornProps);
        Prop("Machine base", PrimitiveType.Cube, new Vector3(-2.3f, .68f, 0), new Vector3(1.2f, .45f, .85f), red, popcornProps);
        Prop("Hot kettle", PrimitiveType.Cylinder, new Vector3(-2.3f, .96f, 0), new Vector3(.91f, .065f, .59f), metal, popcornProps);
        for (int i = 0; i < 40; i++)
        {
            float angle = i * 2.4f, radius = .33f * Mathf.Sqrt((i + .5f) / 40f);
            Prop("Resting popcorn", PrimitiveType.Sphere, new Vector3(-2.3f + Mathf.Cos(angle) * radius, 1.04f, Mathf.Sin(angle) * radius * .65f), Vector3.one * .064f, golden, popcornProps);
        }
        for (int side = -1; side <= 1; side += 2)
            Prop("Machine frame", PrimitiveType.Cube, new Vector3(-2.3f + side * .57f, 1.46f, .32f), new Vector3(.065f, 1.1f, .065f), metal, popcornProps);
        Prop("Machine canopy", PrimitiveType.Cube, new Vector3(-2.3f, 2.03f, 0), new Vector3(1.3f, .15f, .9f), red, popcornProps);
        Vessel("Popcorn bucket", -1.35f, .47f, .86f, .245f, red, white, popcornProps);
        Prop("Bucket popcorn surface", PrimitiveType.Cylinder, new Vector3(-1.35f, .83f, 0), new Vector3(.45f, .018f, .45f), golden, popcornProps);
        Prop("Seasoning hopper", PrimitiveType.Cylinder, new Vector3(-1.35f, 1.81f, 0), new Vector3(.23f, .14f, .23f), metal, popcornProps);
        Prop("Seasoning nozzle", PrimitiveType.Cylinder, new Vector3(-1.35f, 1.66f, 0), new Vector3(.07f, .04f, .07f), golden, popcornProps);
        Prop("Drink counter", PrimitiveType.Cube, new Vector3(2, .22f, 0), new Vector3(2.6f, .45f, 1.4f), stand, drinkProps);
        Prop("Dispenser housing", PrimitiveType.Cube, new Vector3(2, 1.23f, .44f), new Vector3(1.1f, 1.55f, .45f), blue, drinkProps);
        Prop("Nozzle arm", PrimitiveType.Cube, new Vector3(2, 1.77f, .16f), new Vector3(.24f, .15f, .45f), metal, drinkProps);
        Prop("Spout", PrimitiveType.Cylinder, new Vector3(2, 1.71f, 0), new Vector3(.1f, .055f, .1f), metal, drinkProps);
        Vessel("Drink cup", 2, .47f, .78f, .19f, white, metal, drinkProps);
        Prop("Water in cup", PrimitiveType.Cylinder, new Vector3(2, .758f, 0), new Vector3(.35f, .01f, .35f), AssetDatabase.LoadAssetAtPath<Material>(Drink + "/Materials/WaterStream.mat"), drinkProps);
        Label("01 / POPCORN MACHINE", new Vector3(-2, 2.55f, 0), .041f, new Color(1, .78f, .4f));
        Label("HOT STEAM  /  KERNEL POPS  /  SEASONING", new Vector3(-2, -.02f, -.86f), .020f, new Color(.8f, .85f, .9f));
        Label("02 / DRINK DISPENSER", new Vector3(2, 2.55f, 0), .041f, new Color(.5f, .85f, 1));
        Label("WATER STREAM  /  RIM SPLASH  /  INSTANT CANCEL", new Vector3(2, -.02f, -.86f), .019f, new Color(.8f, .85f, .9f));
        var camera = new GameObject("Food Preview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 3.7f, -9);
        camera.transform.LookAt(new Vector3(0, 1.15f, 0));
        camera.orthographic = true; camera.orthographicSize = 2.45f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .026f, .04f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 50;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        camera.gameObject.AddComponent<AudioListener>();
        var light = new GameObject("Studio key light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.65f;
        light.transform.rotation = Quaternion.Euler(35, -30, 0);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Simulate(GameObject root, float seconds)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(seconds, false, true, true);
    }
    private static void CheckClear(GameObject root)
    {
        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
            Assert(ps.particleCount == 0 && !ps.isPlaying, "Effect did not clear: " + ps.name);
    }
    private static float MeanY(ParticleSystem ps, bool velocity = false)
    {
        var particles = new ParticleSystem.Particle[ps.particleCount];
        int count = ps.GetParticles(particles);
        Assert(count > 0, "No particles: " + ps.name);
        float total = 0;
        foreach (var particle in particles) total += velocity ? particle.totalVelocity.y : particle.position.y;
        return total / count;
    }
    private static void Capture(Camera camera, string path)
    {
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        var target = new RenderTexture(1600, 900, 24);
        var oldActive = RenderTexture.active;
        camera.targetTexture = target;
        // The first URP render initializes the lighting and shader resources.
        camera.Render();
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); texture.Apply();
        int yellowFallback = 0;
        foreach (Color pixel in texture.GetPixels())
            if (pixel.r > .85f && pixel.g > .85f && pixel.b < .65f) yellowFallback++;
        Assert(yellowFallback < 1600 * 900 / 4, "Capture still uses compiling shader fallback.");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = oldActive;
        UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        ShaderUtil.allowAsyncCompilation = asyncCompilation;
    }
    private static void Verify()
    {
        var rig = UnityEngine.Object.FindAnyObjectByType<FoodVfxPreview>();
        Assert(rig != null && rig.popcorn != null && rig.drink != null, "Preview bindings missing.");
        rig.Replay();
        Simulate(rig.popcorn.gameObject, .7f);
        Simulate(rig.drink.gameObject, .7f);
        Assert(MeanY(rig.popcorn.steam) > .03f, "Steam must rise.");
        Assert(MeanY(rig.popcorn.kernels) > .01f && MeanY(rig.popcorn.kernels) < .2f, "Kernels must hop slightly.");
        Assert(MeanY(rig.popcorn.seasoning, true) < -.8f, "Seasoning must fall.");
        Assert(MeanY(rig.drink.fallingDroplets, true) < -3, "Water must fall.");
        Assert(rig.drink.rimSplashes.particleCount > 0 && rig.drink.stream.enabled, "Dispensing missing.");
        Directory.CreateDirectory("captures");
        var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        Capture(camera, "captures/food-preparing.png");
        rig.CancelDrink();
        CheckClear(rig.drink.gameObject);
        Assert(!rig.drink.IsDispensing && !rig.drink.stream.enabled && !rig.drink.highlight.enabled, "Stream survived cancel.");
        Capture(camera, "captures/drink-cancelled.png");
        rig.drink.BeginDispensing();
        Simulate(rig.drink.gameObject, .3f);
        Assert(rig.drink.rimSplashes.particleCount > 0 && rig.drink.stream.enabled, "Restart failed.");
        rig.drink.gameObject.SetActive(false);
        Assert(!rig.drink.gameObject.activeInHierarchy, "Dispenser did not disable.");
        // MonoBehaviour lifecycle callbacks do not run for ordinary objects in edit mode.
        // Cancellation is verified synchronously above; runtime OnDisable uses that same path.
        rig.drink.Cancel();
        CheckClear(rig.drink.gameObject);
        rig.drink.gameObject.SetActive(true);
        rig.Replay();
        rig.popcorn.Complete(); rig.drink.Complete();
        CheckClear(rig.popcorn.gameObject); CheckClear(rig.drink.gameObject);
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<ParticleSystemRenderer>())
            Assert(renderer.sharedMaterial != null && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Invalid particle material.");
        // Reload proves the authored scene/prefab references persist on disk.
        EditorSceneManager.OpenScene(ScenePath);
        rig = UnityEngine.Object.FindAnyObjectByType<FoodVfxPreview>();
        Assert(rig.popcorn.seasoning != null && rig.drink.rimSplashes != null, "Serialized references missing.");
        rig.Replay();
        Simulate(rig.popcorn.gameObject, .7f); Simulate(rig.drink.gameObject, .7f);
        Assert(rig.popcorn.steam.particleCount > 0 && rig.drink.rimSplashes.particleCount > 0, "Reload playback failed.");
    }

    public static void VerifyExisting()
    {
        EditorSceneManager.OpenScene(ScenePath);
        double readyAt = EditorApplication.timeSinceStartup + 2;
        EditorApplication.CallbackFunction callback = null;
        callback = () =>
        {
            if (EditorApplication.timeSinceStartup < readyAt) return;
            EditorApplication.update -= callback;
            try
            {
                Verify();
                File.WriteAllText("food-vfx-results.txt", "PASS: Food prefabs, particle motion, immediate cancellation, restart and preview renders.\n");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText("food-vfx-results.txt", "FAIL: " + e);
                EditorApplication.Exit(1);
            }
        };
        EditorApplication.update += callback;
    }
}
