using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 23 Nov 2026 (Mr.k): MOCKUP bodies for two map-rule ghosts (they must NOT look like the NPC ghosts).
///   Guard = the 3 AM flashlight hunter: a security guard ghost. Navy uniform, cap with a badge,
///           gold chest badge, "SECURITY" on the back, red glowing eyes, flashlight in the right hand.
///   VIP   = the VIP guest ghost: tall, pale white face, black suit with gold trim, gold bow tie,
///           top hat with a gold band, white gloves, cane. Has a "Standing" and a "Seated" pose.
///
/// Built from Unity primitives at runtime (no colliders), so it works with no art at all.
/// REAL MODEL LATER: put a prefab at Resources/MapRuleMockups/Guard Ghost (or VIP Ghost).
/// It is used instead. Feet at the prefab root, facing +Z. For the VIP, children named
/// "Standing" and "Seated" are switched on/off (optional). For the guard, a child named "Lens"
/// is where the flashlight beam starts (optional; also found under "Standing").
/// </summary>
public static class MapMockups
{
    public const string ResourceFolder = "MapRuleMockups/";
    public const string GuardName = "Guard Ghost";
    public const string VipName = "VIP Ghost";
    public const string StandingName = "Standing";
    public const string SeatedName = "Seated";
    public const string LensName = "Lens";

    // Colours.
    private static readonly Color Navy = new Color(0.07f, 0.09f, 0.17f);
    private static readonly Color ShirtBlue = new Color(0.38f, 0.45f, 0.55f);
    private static readonly Color Black = new Color(0.025f, 0.025f, 0.03f);
    private static readonly Color Gold = new Color(0.92f, 0.70f, 0.20f);
    private static readonly Color GuardSkin = new Color(0.50f, 0.58f, 0.54f);
    private static readonly Color VipSkin = new Color(0.86f, 0.86f, 0.90f);
    private static readonly Color White = new Color(0.92f, 0.92f, 0.90f);
    private static readonly Color Grey = new Color(0.30f, 0.30f, 0.32f);
    private static readonly Color RedEye = new Color(1f, 0.15f, 0.08f);
    private static readonly Color Lamp = new Color(1f, 0.95f, 0.75f);

    private static readonly Dictionary<long, Material> materials = new Dictionary<long, Material>();
    private static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
    private static Material baseMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { materials.Clear(); meshes.Clear(); baseMaterial = null; }

    /// <summary>Is this body one of the mockups?</summary>
    public static bool Handles(MapWalker.Body body) => body == MapWalker.Body.Guard || body == MapWalker.Body.Vip;

    /// <summary>Builds the body under <paramref name="parent"/> (feet at the parent, facing its forward).</summary>
    public static GameObject Build(MapWalker.Body body, Transform parent)
    {
        string name = body == MapWalker.Body.Guard ? GuardName : VipName;
        GameObject prefab = Resources.Load<GameObject>(ResourceFolder + name);
        GameObject root;
        if (prefab != null)
        {
            root = Object.Instantiate(prefab, parent, false);
            root.name = name;
            foreach (Collider c in root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        }
        else
        {
            root = new GameObject(name + " (mockup)");
            root.transform.SetParent(parent, false);
            if (body == MapWalker.Body.Guard) BuildGuard(root.transform);
            else BuildVip(root.transform);
        }
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        SetSeated(root, false);
        return root;
    }

    /// <summary>Shows the "Seated" child and hides "Standing" (or back). Does nothing if they are missing.</summary>
    public static void SetSeated(GameObject root, bool seated)
    {
        if (root == null) return;
        Transform standing = root.transform.Find(StandingName);
        Transform sitting = root.transform.Find(SeatedName);
        if (standing == null || sitting == null) return;
        standing.gameObject.SetActive(!seated);
        sitting.gameObject.SetActive(seated);
    }

    /// <summary>The part that bobs while walking ("Standing" when there is one, else the root).</summary>
    public static Transform BobPart(GameObject root)
    {
        if (root == null) return null;
        Transform standing = root.transform.Find(StandingName);
        return standing != null ? standing : root.transform;
    }

    // ---- Guard (flashlight hunter) ---------------------------------------------------------------

    private static void BuildGuard(Transform root)
    {
        Transform s = Group(root, StandingName);
        Group(root, SeatedName).gameObject.SetActive(false); // the guard never sits (empty, keeps the API the same)

        // Legs + shoes.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = 0.11f * side;
            Part(s, PrimitiveType.Capsule, "Leg", new Vector3(x, 0.47f, 0f), new Vector3(0.17f, 0.45f, 0.17f), Navy);
            Part(s, PrimitiveType.Cube, "Shoe", new Vector3(x, 0.05f, 0.05f), new Vector3(0.15f, 0.1f, 0.28f), Black);
        }
        // Body.
        Part(s, PrimitiveType.Cube, "Hips", new Vector3(0f, 0.92f, 0f), new Vector3(0.42f, 0.2f, 0.24f), Navy);
        Part(s, PrimitiveType.Cube, "Belt", new Vector3(0f, 0.99f, 0f), new Vector3(0.44f, 0.06f, 0.26f), Black);
        Part(s, PrimitiveType.Cube, "Buckle", new Vector3(0f, 0.99f, 0.135f), new Vector3(0.07f, 0.05f, 0.012f), Gold);
        Part(s, PrimitiveType.Cube, "Jacket", new Vector3(0f, 1.27f, 0f), new Vector3(0.46f, 0.54f, 0.26f), Navy);
        Part(s, PrimitiveType.Cube, "Collar", new Vector3(0f, 1.48f, 0.131f), new Vector3(0.13f, 0.1f, 0.012f), ShirtBlue);
        Part(s, PrimitiveType.Cube, "Tie", new Vector3(0f, 1.36f, 0.134f), new Vector3(0.05f, 0.2f, 0.012f), Black);
        Part(s, PrimitiveType.Cube, "Chest Badge", new Vector3(-0.13f, 1.38f, 0.136f), new Vector3(0.07f, 0.08f, 0.012f), Gold);
        for (int side = -1; side <= 1; side += 2)
            Part(s, PrimitiveType.Cube, "Epaulette", new Vector3(0.21f * side, 1.53f, 0f), new Vector3(0.09f, 0.025f, 0.16f), Gold);
        Text(s, "SECURITY", new Vector3(0f, 1.36f, -0.136f), Quaternion.Euler(0f, 180f, 0f), new Vector2(0.4f, 0.1f), new Color(1f, 0.85f, 0.2f));

        // Head.
        Part(s, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 1.57f, 0f), new Vector3(0.1f, 0.04f, 0.1f), GuardSkin);
        Part(s, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.7f, 0f), new Vector3(0.24f, 0.27f, 0.24f), GuardSkin);
        for (int side = -1; side <= 1; side += 2)
        {
            Part(s, PrimitiveType.Sphere, "Eye Socket", new Vector3(0.05f * side, 1.72f, 0.1f), new Vector3(0.06f, 0.045f, 0.03f), Black);
            Part(s, PrimitiveType.Sphere, "Eye", new Vector3(0.05f * side, 1.72f, 0.113f), new Vector3(0.03f, 0.022f, 0.012f), RedEye, true);
        }
        Part(s, PrimitiveType.Cube, "Mouth", new Vector3(0f, 1.62f, 0.112f), new Vector3(0.07f, 0.015f, 0.01f), Black);
        // Cap.
        Part(s, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 1.835f, -0.01f), new Vector3(0.27f, 0.045f, 0.27f), Navy);
        Part(s, PrimitiveType.Cylinder, "Cap Band", new Vector3(0f, 1.805f, -0.01f), new Vector3(0.275f, 0.014f, 0.275f), Black);
        Part(s, PrimitiveType.Cube, "Cap Brim", new Vector3(0f, 1.80f, 0.14f), new Vector3(0.22f, 0.02f, 0.12f), Black);
        Part(s, PrimitiveType.Cube, "Cap Badge", new Vector3(0f, 1.845f, 0.126f), new Vector3(0.05f, 0.04f, 0.012f), Gold);

        // Left arm hangs, right arm points the flashlight forward.
        Limb(s, "Arm L", new Vector3(-0.3f, 1.48f, 0f), new Vector3(-0.32f, 0.92f, 0.02f), 0.12f, Navy);
        Part(s, PrimitiveType.Sphere, "Hand L", new Vector3(-0.32f, 0.88f, 0.02f), Vector3.one * 0.1f, GuardSkin);
        Limb(s, "Arm R", new Vector3(0.3f, 1.48f, 0f), new Vector3(0.28f, 1.28f, 0.46f), 0.12f, Navy);
        Part(s, PrimitiveType.Sphere, "Hand R", new Vector3(0.28f, 1.28f, 0.47f), Vector3.one * 0.1f, GuardSkin);
        Limb(s, "Flashlight", new Vector3(0.28f, 1.30f, 0.42f), new Vector3(0.28f, 1.27f, 0.66f), 0.055f, Black);
        Limb(s, "Flashlight Head", new Vector3(0.28f, 1.27f, 0.65f), new Vector3(0.28f, 1.265f, 0.71f), 0.085f, Grey);
        Transform lens = Part(s, PrimitiveType.Cylinder, LensName, Vector3.zero, Vector3.one, Lamp, true);
        lens.name = LensName;
        SetLimb(lens, new Vector3(0.28f, 1.265f, 0.708f), new Vector3(0.28f, 1.264f, 0.716f), 0.075f);
    }

    // ---- VIP -----------------------------------------------------------------------------------

    private static void BuildVip(Transform root)
    {
        Transform s = Group(root, StandingName);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = 0.1f * side;
            Part(s, PrimitiveType.Capsule, "Leg", new Vector3(x, 0.5f, 0f), new Vector3(0.16f, 0.48f, 0.16f), Black);
            Part(s, PrimitiveType.Cube, "Shoe", new Vector3(x, 0.04f, 0.05f), new Vector3(0.14f, 0.08f, 0.3f), Black);
        }
        Part(s, PrimitiveType.Cube, "Coat Front", new Vector3(0f, 0.92f, 0.02f), new Vector3(0.44f, 0.2f, 0.22f), Black);
        Part(s, PrimitiveType.Cube, "Coat Tails", new Vector3(0f, 0.82f, -0.1f), new Vector3(0.44f, 0.42f, 0.05f), Black);
        VipUpper(s, Vector3.zero);
        // Cane in the right hand.
        Limb(s, "Cane", new Vector3(0.34f, 1.06f, 0.15f), new Vector3(0.37f, 0.01f, 0.22f), 0.03f, Black);
        Part(s, PrimitiveType.Sphere, "Cane Knob", new Vector3(0.34f, 1.09f, 0.15f), Vector3.one * 0.065f, Gold);

        // Seated: hips on a cinema seat (top about 0.46 m), thighs forward, shins down.
        Transform d = Group(root, SeatedName);
        Part(d, PrimitiveType.Cube, "Hips", new Vector3(0f, 0.53f, -0.1f), new Vector3(0.42f, 0.14f, 0.3f), Black);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = 0.1f * side;
            Limb(d, "Thigh", new Vector3(x, 0.53f, -0.14f), new Vector3(x, 0.54f, 0.32f), 0.17f, Black);
            Limb(d, "Shin", new Vector3(x, 0.53f, 0.33f), new Vector3(x, 0.07f, 0.36f), 0.15f, Black);
            Part(d, PrimitiveType.Cube, "Shoe", new Vector3(x, 0.04f, 0.42f), new Vector3(0.14f, 0.08f, 0.3f), Black);
        }
        VipUpper(d, new Vector3(0f, -0.41f, -0.15f));
    }

    /// <summary>Torso, arms, head, hat. Designed standing (torso bottom at 1.0 m), moved by <paramref name="o"/>.</summary>
    private static void VipUpper(Transform p, Vector3 o)
    {
        Part(p, PrimitiveType.Cube, "Suit", o + new Vector3(0f, 1.33f, 0f), new Vector3(0.46f, 0.66f, 0.25f), Black);
        Part(p, PrimitiveType.Cube, "Shirt", o + new Vector3(0f, 1.47f, 0.126f), new Vector3(0.13f, 0.36f, 0.012f), White);
        for (int side = -1; side <= 1; side += 2)
            Part(p, PrimitiveType.Cube, "Gold Lapel", o + new Vector3(0.085f * side, 1.47f, 0.128f), new Vector3(0.025f, 0.36f, 0.012f), Gold);
        Part(p, PrimitiveType.Cube, "Bow Tie", o + new Vector3(0f, 1.62f, 0.132f), new Vector3(0.13f, 0.05f, 0.02f), Gold);
        Part(p, PrimitiveType.Sphere, "Button 1", o + new Vector3(0f, 1.38f, 0.133f), Vector3.one * 0.025f, Gold);
        Part(p, PrimitiveType.Sphere, "Button 2", o + new Vector3(0f, 1.28f, 0.133f), Vector3.one * 0.025f, Gold);
        Part(p, PrimitiveType.Cube, "Pocket Square", o + new Vector3(-0.15f, 1.52f, 0.128f), new Vector3(0.06f, 0.035f, 0.012f), Gold);
        for (int side = -1; side <= 1; side += 2)
            Part(p, PrimitiveType.Cube, "Shoulder", o + new Vector3(0.2f * side, 1.65f, 0f), new Vector3(0.12f, 0.03f, 0.25f), Black);

        Part(p, PrimitiveType.Cylinder, "Neck", o + new Vector3(0f, 1.70f, 0f), new Vector3(0.1f, 0.04f, 0.1f), VipSkin);
        Part(p, PrimitiveType.Sphere, "Head", o + new Vector3(0f, 1.84f, 0f), new Vector3(0.23f, 0.29f, 0.23f), VipSkin);
        for (int side = -1; side <= 1; side += 2)
        {
            Part(p, PrimitiveType.Sphere, "Eye Hole", o + new Vector3(0.05f * side, 1.86f, 0.096f), new Vector3(0.06f, 0.045f, 0.03f), Black);
            Part(p, PrimitiveType.Sphere, "Eye Glint", o + new Vector3(0.05f * side, 1.86f, 0.11f), Vector3.one * 0.018f, Gold, true);
        }
        Part(p, PrimitiveType.Cube, "Smile", o + new Vector3(0f, 1.76f, 0.104f), new Vector3(0.11f, 0.012f, 0.01f), Black);
        // Top hat.
        Part(p, PrimitiveType.Cylinder, "Hat Brim", o + new Vector3(0f, 1.975f, 0f), new Vector3(0.36f, 0.008f, 0.36f), Black);
        Part(p, PrimitiveType.Cylinder, "Hat", o + new Vector3(0f, 2.11f, 0f), new Vector3(0.22f, 0.13f, 0.22f), Black);
        Part(p, PrimitiveType.Cylinder, "Hat Band", o + new Vector3(0f, 2.01f, 0f), new Vector3(0.226f, 0.025f, 0.226f), Gold);
        // Arms + white gloves.
        Limb(p, "Arm L", o + new Vector3(-0.29f, 1.62f, 0f), o + new Vector3(-0.31f, 1.08f, 0.02f), 0.12f, Black);
        Part(p, PrimitiveType.Sphere, "Glove L", o + new Vector3(-0.31f, 1.04f, 0.02f), Vector3.one * 0.1f, White);
        Limb(p, "Arm R", o + new Vector3(0.29f, 1.62f, 0f), o + new Vector3(0.33f, 1.12f, 0.12f), 0.12f, Black);
        Part(p, PrimitiveType.Sphere, "Glove R", o + new Vector3(0.33f, 1.08f, 0.13f), Vector3.one * 0.1f, White);
        for (int side = -1; side <= 1; side += 2)
            Part(p, PrimitiveType.Cube, "Cufflink", o + new Vector3(side < 0 ? -0.31f : 0.33f, side < 0 ? 1.12f : 1.16f, side < 0 ? 0.02f : 0.12f),
                Vector3.one * 0.035f, Gold);
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool glow = false)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshFilter>().sharedMesh = MeshOf(type);
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = MaterialOf(color, glow);
        r.shadowCastingMode = glow ? ShadowCastingMode.Off : ShadowCastingMode.On;
        return go.transform;
    }

    /// <summary>A capsule (or cylinder for thin things) from one point to another.</summary>
    private static void Limb(Transform parent, string name, Vector3 from, Vector3 to, float thickness, Color color)
    {
        Transform t = Part(parent, thickness < 0.1f ? PrimitiveType.Cylinder : PrimitiveType.Capsule, name, Vector3.zero, Vector3.one, color);
        SetLimb(t, from, to, thickness);
    }

    private static void SetLimb(Transform t, Vector3 from, Vector3 to, float thickness)
    {
        Vector3 dir = to - from;
        float length = Mathf.Max(0.001f, dir.magnitude);
        t.localPosition = (from + to) * 0.5f;
        t.localRotation = Quaternion.FromToRotation(Vector3.up, dir / length);
        t.localScale = new Vector3(thickness, length * 0.5f, thickness);
    }

    private static void Text(Transform parent, string text, Vector3 pos, Quaternion rot, Vector2 size, Color color)
    {
        var go = new GameObject("Text " + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.rectTransform.sizeDelta = size;
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 2f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>The built-in primitive meshes (taken once from a temporary primitive).</summary>
    private static Mesh MeshOf(PrimitiveType type)
    {
        if (meshes.TryGetValue(type, out Mesh mesh) && mesh != null) return mesh;
        GameObject temp = GameObject.CreatePrimitive(type);
        mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);
        meshes[type] = mesh;
        return mesh;
    }

    /// <summary>
    /// 28 Nov 2026: a URP Lit material that also works in a BUILD.
    /// Do NOT take the material of a CreatePrimitive object: in the Editor it is URP Lit, but in a
    /// build it is the old built-in "Default-Material" (Standard shader), which URP draws magenta.
    /// Resources/CustomerBody.mat is URP Lit and always ships with the build (it is in Resources).
    /// </summary>
    public static Material NewLitMaterial()
    {
        if (baseMaterial == null) baseMaterial = Resources.Load<Material>("CustomerBody");
        if (baseMaterial != null) return new Material(baseMaterial);
        Debug.LogError("[MapMockups] Missing Resources/CustomerBody material; the mockups may be magenta in a build.");
        return new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    private static Material MaterialOf(Color color, bool glow)
    {
        Color32 c = color;
        long key = ((long)c.r << 24) | ((long)c.g << 16) | ((long)c.b << 8) | (glow ? 1L : 0L);
        if (materials.TryGetValue(key, out Material m) && m != null) return m;
        m = NewLitMaterial();
        m.name = "Mockup " + ColorUtility.ToHtmlStringRGB(color) + (glow ? " Glow" : "");
        m.color = color;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glow ? 0f : 0.35f);
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * 3f);
        }
        materials[key] = m;
        return m;
    }
}
