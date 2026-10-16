using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Pre-Thesis > Rules > Build Mockup Prefabs (missing only)   (16 Oct 2026, Mr.k)
///
/// Makes simple placeholder PREFABS for every prop the Screen Room + Toilet rules
/// need, in Assets/Prefab/RuleMockups (materials in .../Materials).
///
/// FOR THE DESIGNER: open a prefab and replace its look with the real model.
/// Keep the prefab ROOT (and for RM_GhostAudience the children named "Seated"
/// and "Standing"). Every copy in the scene updates.
///
/// SAFE: a prefab or material that already exists is NEVER overwritten, so your
/// replacements stay. Delete a prefab and run this again to get the mockup back.
/// </summary>
public static class RuleMockupBuilder
{
    public const string Folder = "Assets/Prefab/RuleMockups";
    private const string MatFolder = Folder + "/Materials";

    public const string GhostAudience = "RM_GhostAudience";
    public const string ShadowSitter = "RM_ShadowSitter";
    public const string GhostMovie = "RM_GhostMovie";
    public const string RuleGhost = "RM_RuleGhost";
    public const string MirrorCrack = "RM_MirrorCrack";
    public const string MirrorShattered = "RM_MirrorShattered";
    public const string ClosedStallDoor = "RM_ClosedStallDoor";
    public const string CryingSound = "RM_CryingSound";
    public const string Smoke = "RM_Smoke";
    public const string ToiletLamp = "RM_ToiletLamp";
    public const string LightSwitch = "RM_LightSwitch";
    public const string BlackBox = "RM_BlackBox";
    public const string BlackBoxShelf = "RM_BlackBoxShelf";
    public const string BoxDropSpot = "RM_BoxDropSpot";
    public const string KnockSpot = "RM_KnockSpot";

    [MenuItem("Tools/Pre-Thesis/Rules/Build Mockup Prefabs (missing only)", false, 60)]
    private static void Menu()
    {
        int made = BuildMissing(out _);
        EditorUtility.DisplayDialog("Rule mockups",
            made > 0 ? $"Made {made} mockup prefab(s) in {Folder}.\nExisting prefabs were not touched."
                     : $"All mockup prefabs already exist in {Folder}. Nothing changed.", "OK");
    }

    /// <summary>Creates the missing prefabs. Returns how many were made; <paramref name="prefabs"/> = all of them by name.</summary>
    public static int BuildMissing(out Dictionary<string, GameObject> prefabs)
    {
        EnsureFolder("Assets/Prefab");
        EnsureFolder(Folder);
        EnsureFolder(MatFolder);
        prefabs = new Dictionary<string, GameObject>();
        int made = 0;
        made += Make(prefabs, GhostAudience, BuildGhostAudience);
        made += Make(prefabs, ShadowSitter, BuildShadowSitter);
        made += Make(prefabs, GhostMovie, BuildGhostMovie);
        made += Make(prefabs, RuleGhost, BuildRuleGhost);
        made += Make(prefabs, MirrorCrack, BuildMirrorCrack);
        made += Make(prefabs, MirrorShattered, BuildMirrorShattered);
        made += Make(prefabs, ClosedStallDoor, BuildClosedStallDoor);
        made += Make(prefabs, CryingSound, BuildCryingSound);
        made += Make(prefabs, Smoke, BuildSmoke);
        made += Make(prefabs, ToiletLamp, BuildToiletLamp);
        made += Make(prefabs, LightSwitch, BuildLightSwitch);
        made += Make(prefabs, BlackBox, BuildBlackBox);
        made += Make(prefabs, BlackBoxShelf, BuildBlackBoxShelf);
        made += Make(prefabs, BoxDropSpot, BuildBoxDropSpot);
        made += Make(prefabs, KnockSpot, BuildKnockSpot);
        AssetDatabase.SaveAssets();
        return made;
    }

    private static int Make(Dictionary<string, GameObject> prefabs, string name, System.Func<GameObject> build)
    {
        string path = $"{Folder}/{name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) { prefabs[name] = existing; return 0; }
        GameObject temp = build();
        temp.name = name;
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        prefabs[name] = saved;
        return 1;
    }

    // ---- Prefabs --------------------------------------------------------------------------------

    // Seated: pivot = the seat surface. Standing: pivot = the floor under the feet.
    private static GameObject BuildGhostAudience()
    {
        var root = new GameObject();
        Material pale = Mat("RM_GhostPale", new Color(0.72f, 0.76f, 0.82f));
        Material eyes = Mat("RM_GhostEyes", new Color(0.3f, 0f, 0f), false, new Color(1.4f, 0.05f, 0.05f));

        GameObject seated = Child(root, "Seated");
        Part(seated, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.35f, 0f), new Vector3(0.45f, 0.35f, 0.4f), pale);
        Part(seated, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.85f, 0f), Vector3.one * 0.3f, pale);
        Eyes(seated, 0.88f, 0.14f, eyes);

        GameObject standing = Child(root, "Standing");
        Part(standing, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.45f, 0.8f, 0.4f), pale);
        Part(standing, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.7f, 0f), Vector3.one * 0.3f, pale);
        Eyes(standing, 1.73f, 0.14f, eyes);
        standing.SetActive(false);
        return root;
    }

    private static GameObject BuildShadowSitter()
    {
        var root = new GameObject();
        Material shadow = Mat("RM_Shadow", new Color(0f, 0f, 0f, 0.75f), true);
        Part(root, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.35f, 0f), new Vector3(0.45f, 0.35f, 0.4f), shadow);
        Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.85f, 0f), Vector3.one * 0.3f, shadow);
        BoxCollider box = root.AddComponent<BoxCollider>(); // the "Hold E: sit" target
        box.center = new Vector3(0f, 0.55f, 0f);
        box.size = new Vector3(0.7f, 1.1f, 0.7f);
        return root;
    }

    // A 1 x 1 picture; the setup tool scales it to the screen. Double-sided.
    private static GameObject BuildGhostMovie()
    {
        var root = new GameObject();
        Material movie = Mat("RM_GhostMovie", new Color(0.15f, 0.02f, 0.02f), false, new Color(0.9f, 0.08f, 0.06f), true);
        Material white = Mat("RM_GhostMovieFace", new Color(0.9f, 0.9f, 0.9f), false, new Color(1.2f, 1.2f, 1.2f), true);
        Part(root, PrimitiveType.Quad, "Picture", Vector3.zero, Vector3.one, movie);
        Part(root, PrimitiveType.Quad, "Eye L", new Vector3(-0.12f, 0.12f, -0.01f), new Vector3(0.07f, 0.1f, 1f), white);
        Part(root, PrimitiveType.Quad, "Eye R", new Vector3(0.12f, 0.12f, -0.01f), new Vector3(0.07f, 0.1f, 1f), white);
        Part(root, PrimitiveType.Quad, "Mouth", new Vector3(0f, -0.15f, -0.01f), new Vector3(0.18f, 0.22f, 1f), white);
        return root;
    }

    private static GameObject BuildRuleGhost()
    {
        var root = new GameObject();
        Material dark = Mat("RM_GhostDark", new Color(0.05f, 0.05f, 0.06f, 0.85f), true);
        Material eyes = Mat("RM_GhostEyes", new Color(0.3f, 0f, 0f), false, new Color(1.4f, 0.05f, 0.05f));
        Part(root, PrimitiveType.Capsule, "Body", new Vector3(0f, 1.0f, 0f), new Vector3(0.55f, 1.0f, 0.45f), dark);
        Part(root, PrimitiveType.Sphere, "Head", new Vector3(0f, 2.05f, 0f), Vector3.one * 0.38f, dark);
        Part(root, PrimitiveType.Capsule, "Arm L", new Vector3(-0.38f, 1.1f, 0.15f), new Vector3(0.12f, 0.55f, 0.12f), dark).transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
        Part(root, PrimitiveType.Capsule, "Arm R", new Vector3(0.38f, 1.1f, 0.15f), new Vector3(0.12f, 0.55f, 0.12f), dark).transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
        Eyes(root, 2.1f, 0.18f, eyes);
        GlowLight(root, new Vector3(0f, 2f, 0.3f), new Color(1f, 0.15f, 0.1f), 3f, 0.8f);
        return root;
    }

    // On the glass: local X/Y = the mirror plane, +Z = out of the mirror.
    private static GameObject BuildMirrorCrack()
    {
        var root = new GameObject();
        Material crack = Mat("RM_Crack", new Color(0.08f, 0.08f, 0.08f));
        var random = new System.Random(5);
        for (int i = 0; i < 9; i++)
        {
            float angle = i * 40f + (float)random.NextDouble() * 20f;
            float length = 0.18f + (float)random.NextDouble() * 0.28f;
            GameObject line = Part(root, PrimitiveType.Cube, "Crack " + (i + 1), Vector3.zero, new Vector3(length, 0.012f, 0.004f), crack);
            line.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            line.transform.localPosition = line.transform.localRotation * new Vector3(length * 0.5f, 0f, 0f) + new Vector3(0f, 0f, 0.006f);
        }
        return root;
    }

    private static GameObject BuildMirrorShattered()
    {
        var root = new GameObject();
        Material hole = Mat("RM_MirrorHole", new Color(0.02f, 0.02f, 0.02f));
        Material glass = Mat("RM_Glass", new Color(0.7f, 0.85f, 0.95f, 0.55f), true);
        Part(root, PrimitiveType.Cube, "Broken Glass", new Vector3(0f, 0f, 0.004f), new Vector3(0.6f, 0.8f, 0.004f), hole);
        var random = new System.Random(9);
        for (int i = 0; i < 12; i++)
        {
            Vector3 at = new Vector3((float)random.NextDouble() - 0.5f, -0.75f - (float)random.NextDouble() * 0.4f, 0.1f + (float)random.NextDouble() * 0.5f);
            float s = 0.05f + (float)random.NextDouble() * 0.1f;
            GameObject shard = Part(root, PrimitiveType.Cube, "Shard " + (i + 1), at, new Vector3(s, 0.005f, s * 1.4f), glass);
            shard.transform.localRotation = Quaternion.Euler((float)random.NextDouble() * 30f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 30f);
        }
        return root;
    }

    // Pivot = door centre. Local X = door width, Y = up, Z = thickness.
    private static GameObject BuildClosedStallDoor()
    {
        var root = new GameObject();
        Material door = Mat("RM_StallDoor", new Color(0.32f, 0.3f, 0.28f));
        Material red = Mat("RM_Occupied", new Color(0.4f, 0f, 0f), false, new Color(1.2f, 0.05f, 0.05f));
        Part(root, PrimitiveType.Cube, "Door", Vector3.zero, new Vector3(1f, 1f, 0.05f), door);
        Part(root, PrimitiveType.Cube, "Occupied", new Vector3(0.35f, 0.1f, 0.035f), new Vector3(0.08f, 0.04f, 0.02f), red);
        return root;
    }

    private static GameObject BuildCryingSound()
    {
        var root = new GameObject();
        AudioSource source = root.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 14f;
        source.volume = 0.8f;
        return root;
    }

    // Pivot = stall door centre; smoke starts near the top of the door.
    private static GameObject BuildSmoke()
    {
        var root = new GameObject();
        var emitter = new GameObject("Smoke");
        emitter.transform.SetParent(root.transform, false);
        emitter.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        emitter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up
        ParticleSystem ps = emitter.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        main.startColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        main.gravityModifier = -0.03f;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 14f;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.15f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.25f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
        emitter.GetComponent<ParticleSystemRenderer>().sharedMaterial = SmokeMaterial();
        return root;
    }

    // Pivot = ceiling point. Has its own light (not building power).
    private static GameObject BuildToiletLamp()
    {
        var root = new GameObject();
        root.AddComponent<IndependentLight>();
        Material shade = Mat("RM_LampShade", new Color(0.85f, 0.85f, 0.8f));
        Material glow = Mat("RM_LampGlow", new Color(1f, 0.95f, 0.8f), false, new Color(2.2f, 2f, 1.6f));
        Part(root, PrimitiveType.Cylinder, "Shade", new Vector3(0f, -0.03f, 0f), new Vector3(0.5f, 0.03f, 0.5f), shade);
        Part(root, PrimitiveType.Cylinder, "Lit", new Vector3(0f, -0.065f, 0f), new Vector3(0.42f, 0.008f, 0.42f), glow);
        GlowLight(root, new Vector3(0f, -0.3f, 0f), new Color(1f, 0.93f, 0.8f), 8f, 2.2f);
        return root;
    }

    // Pivot = plate centre on the wall, +Z = out of the wall. Root collider = the E target.
    private static GameObject BuildLightSwitch()
    {
        var root = new GameObject();
        Material plate = Mat("RM_SwitchPlate", new Color(0.9f, 0.9f, 0.88f));
        Material nub = Mat("RM_SwitchNub", new Color(0.15f, 0.15f, 0.15f));
        Part(root, PrimitiveType.Cube, "Plate", Vector3.zero, new Vector3(0.09f, 0.13f, 0.015f), plate);
        Part(root, PrimitiveType.Cube, "Toggle", new Vector3(0f, 0f, 0.015f), new Vector3(0.025f, 0.05f, 0.02f), nub);
        Part(root, PrimitiveType.Cylinder, "Post", new Vector3(0f, -0.66f, -0.03f), new Vector3(0.05f, 0.66f, 0.05f), nub);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, 0.03f);
        box.size = new Vector3(0.25f, 0.3f, 0.12f);
        return root;
    }

    private static GameObject BuildBlackBox()
    {
        var root = new GameObject();
        Material black = Mat("RM_BlackBox", new Color(0.03f, 0.03f, 0.03f));
        Material band = Mat("RM_BlackBoxBand", new Color(0.35f, 0.05f, 0.05f));
        Part(root, PrimitiveType.Cube, "Box", Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f), black);
        Part(root, PrimitiveType.Cube, "Band", Vector3.zero, new Vector3(0.31f, 0.04f, 0.31f), band);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(0.36f, 0.28f, 0.36f);
        return root;
    }

    // Pivot = floor. Top at 0.8 m.
    private static GameObject BuildBlackBoxShelf()
    {
        var root = new GameObject();
        Material wood = Mat("RM_Shelf", new Color(0.35f, 0.25f, 0.18f));
        Part(root, PrimitiveType.Cube, "Top", new Vector3(0f, 0.78f, 0f), new Vector3(0.55f, 0.04f, 0.4f), wood);
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.24f : 0.24f;
            float z = i < 2 ? -0.17f : 0.17f;
            Part(root, PrimitiveType.Cube, "Leg " + (i + 1), new Vector3(x, 0.38f, z), new Vector3(0.04f, 0.76f, 0.04f), wood);
        }
        return root;
    }

    // Pivot = floor. Shown only while the "Ghost food" rule waits.
    private static GameObject BuildBoxDropSpot()
    {
        var root = new GameObject();
        Material hole = Mat("RM_DropHole", new Color(0.01f, 0.01f, 0.01f));
        Material ring = Mat("RM_DropRing", new Color(0.3f, 0.02f, 0.02f), false, new Color(0.6f, 0.03f, 0.03f));
        Part(root, PrimitiveType.Cylinder, "Ring", new Vector3(0f, 0.01f, 0f), new Vector3(0.6f, 0.01f, 0.6f), ring);
        Part(root, PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0.02f, 0f), new Vector3(0.48f, 0.01f, 0.48f), hole);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.3f, 0f);
        box.size = new Vector3(0.7f, 0.6f, 0.7f);
        return root;
    }

    // Pivot = stall door centre. A knob + an E target covering the door.
    private static GameObject BuildKnockSpot()
    {
        var root = new GameObject();
        Material knob = Mat("RM_Knob", new Color(0.6f, 0.55f, 0.4f));
        Part(root, PrimitiveType.Sphere, "Knob", new Vector3(0.3f, 0f, 0.06f), Vector3.one * 0.06f, knob);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, 0.06f);
        box.size = new Vector3(0.8f, 1.6f, 0.12f);
        return root;
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // looks only; colliders are added on purpose
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    private static void Eyes(GameObject parent, float y, float spread, Material mat)
    {
        Part(parent, PrimitiveType.Sphere, "Eye L", new Vector3(-spread * 0.5f, y, 0.13f), Vector3.one * 0.05f, mat);
        Part(parent, PrimitiveType.Sphere, "Eye R", new Vector3(spread * 0.5f, y, 0.13f), Vector3.one * 0.05f, mat);
    }

    private static void GlowLight(GameObject parent, Vector3 localPos, Color color, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.AddComponent<IndependentLight>(); // building power does not switch it
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    private static Material Mat(string name, Color color, bool transparent = false, Color? emission = null, bool twoSided = false)
    {
        string path = $"{MatFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { name = name };
        m.SetColor("_BaseColor", color);
        m.color = color;
        if (transparent) MakeTransparent(m);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        if (twoSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)CullMode.Off);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static void MakeTransparent(Material m)
    {
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // URP: Transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);     // Alpha
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Material SmokeMaterial()
    {
        string path = $"{MatFolder}/RM_Smoke.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(shader) { name = "RM_Smoke" };
        MakeTransparent(m);
        m.SetColor("_BaseColor", new Color(0.75f, 0.75f, 0.75f, 0.6f));
        Texture2D dot = SoftDotTexture();
        if (dot != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", dot);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Texture2D SoftDotTexture()
    {
        string path = $"{MatFolder}/RM_SoftDot.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        if (AssetImporter.GetAtPath(path) is TextureImporter importer)
        {
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
