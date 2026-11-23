using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE CLICK: Tools > Pre-Thesis > Scenes > Menu: Add VCR Background (A)
///
/// Adds the background of menu design "A — VCR Play Screen" to MainMenu.unity:
///   Blue Screen   flat VCR blue (#1A2A9E)
///   Tracking Band white streaky band that rolls down the screen
///   Noise         grain that jumps 24 times a second
///
/// It is its own canvas ("VCR Background") in Screen Space - Camera on the
/// scene's Main Camera, so the game's Retro Horror Filter (PC_Renderer) draws
/// its scanlines / vignette / pixelation over it. The Main Menu UI stays an
/// overlay on top and is not changed. The textures it needs are made once in
/// Assets/UI Skins/VCR Background/.
///
/// Safe to run again: it only replaces "VCR Background". Undo works (Ctrl+Z).
/// </summary>
public static class VcrBackgroundSetup
{
    private const string MenuPath = "Tools/Pre-Thesis/Scenes/Menu: Add VCR Background (A)";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string MenuRootName = "Main Menu UI";
    private const string RootName = "VCR Background";
    private const string TextureFolder = "Assets/UI Skins/VCR Background";

    private static readonly Color VcrBlue = new Color32(0x1A, 0x2A, 0x9E, 0xFF);

    [MenuItem(MenuPath, false, 34)]
    private static void Add()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("VCR Background", "Stop Play mode first.", "OK");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }

        GameObject existing = FindRoot(scene, RootName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("VCR Background",
                    "'" + RootName + "' is already in the scene.\n\nRebuild it? (Only that object is replaced.)",
                    "Rebuild", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        Textures tex = EnsureTextures();

        // ---- Canvas on the scene camera (so the Retro Horror Filter covers it) --
        Camera cam = FindSceneCamera(scene);
        if (cam == null)
        {
            EditorUtility.DisplayDialog("VCR Background", "No camera found in " + scene.name + ". Nothing was added.", "OK");
            return;
        }
        GameObject menuRoot = FindRoot(scene, MenuRootName);
        DeleteOldTexture("VCR_Scanlines.png"); // the game's filter does these now
        DeleteOldTexture("VCR_Vignette.png");

        var root = new GameObject(RootName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Add VCR Background");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        // Just past the near clip: in front of every 3D object, so nothing pokes through.
        canvas.planeDistance = cam.nearClipPlane + 0.05f;
        canvas.sortingOrder = -100; // behind any other camera canvas
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: the background never blocks clicks.

        if (menuRoot != null) root.transform.SetSiblingIndex(menuRoot.transform.GetSiblingIndex());

        var area = (RectTransform)root.transform;

        // ---- Layers, back to front -------------------------------------------
        Image blue = NewLayer<Image>("Blue Screen", area);
        blue.color = VcrBlue;

        RawImage band = NewLayer<RawImage>("Tracking Band", area);
        band.texture = tex.band;
        band.color = new Color(1f, 1f, 1f, 0.35f);
        var bandRt = band.rectTransform;
        bandRt.anchorMin = new Vector2(0f, 1f);
        bandRt.anchorMax = new Vector2(1f, 1f);
        bandRt.pivot = new Vector2(0.5f, 1f);
        bandRt.sizeDelta = new Vector2(80f, 82f); // 80 wider so the wobble never shows an edge
        bandRt.anchoredPosition = new Vector2(0f, -380f);

        RawImage grain = NewLayer<RawImage>("Noise", area);
        grain.texture = tex.noise;
        grain.color = new Color(1f, 1f, 1f, 0.09f);
        grain.uvRect = new Rect(0f, 0f, 1920f / 512f, 1080f / 512f);

        VcrBackground vcr = root.AddComponent<VcrBackground>();
        vcr.Setup(area, bandRt, null, grain);
        EditorUtility.SetDirty(vcr);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("[VcrBackgroundSetup] Added '" + RootName + "' on camera '" + cam.name +
                  "' (Retro Horror Filter covers it). Main Menu UI was not changed. Save the scene to keep it.");
    }

    // =========================================================================
    // Layers
    // =========================================================================

    private static T NewLayer<T>(string name, RectTransform parent) where T : Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        T g = go.AddComponent<T>();
        g.raycastTarget = false;
        return g;
    }

    private static Camera FindSceneCamera(Scene scene)
    {
        Camera first = null;
        foreach (GameObject r in scene.GetRootGameObjects())
        foreach (Camera c in r.GetComponentsInChildren<Camera>(true))
        {
            if (c.CompareTag("MainCamera")) return c;
            if (first == null) first = c;
        }
        return first;
    }

    private static void DeleteOldTexture(string file)
    {
        string path = TextureFolder + "/" + file;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) AssetDatabase.DeleteAsset(path);
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject r in scene.GetRootGameObjects())
            if (r.name == name) return r;
        return null;
    }

    // =========================================================================
    // Textures (made once, kept as PNG so they can be swapped by an artist)
    // =========================================================================

    private struct Textures
    {
        public Texture2D band, noise;
    }

    private static Textures EnsureTextures()
    {
        if (!AssetDatabase.IsValidFolder(TextureFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/UI Skins")) AssetDatabase.CreateFolder("Assets", "UI Skins");
            AssetDatabase.CreateFolder("Assets/UI Skins", "VCR Background");
        }

        return new Textures
        {
            band = Ensure("VCR_TrackingBand.png", MakeBand, TextureWrapMode.Clamp, FilterMode.Bilinear),
            noise = Ensure("VCR_Noise.png", MakeNoise, TextureWrapMode.Repeat, FilterMode.Point),
        };
    }

    private static Texture2D Ensure(string file, System.Func<Texture2D> make, TextureWrapMode wrap, FilterMode filter)
    {
        string path = TextureFolder + "/" + file;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
        {
            Texture2D t = make();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.wrapMode = wrap;
            ti.filterMode = filter;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // White streaks, strongest in the middle row, fading up and down.
    private static Texture2D MakeBand()
    {
        const int w = 512, h = 64;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var rng = new System.Random(13);
        var column = new float[w];
        for (int x = 0; x < w; x++)
        {
            float streak = Mathf.PerlinNoise(x * 0.09f, 1.7f);
            streak = Mathf.Clamp01((streak - 0.35f) * 2.2f);
            if (rng.NextDouble() < 0.12) streak = Mathf.Max(streak, 0.6f + (float)rng.NextDouble() * 0.4f);
            column[x] = streak;
        }
        for (int y = 0; y < h; y++)
        {
            float v = Mathf.Sin(Mathf.PI * (y + 0.5f) / h);
            v = Mathf.Pow(v, 1.6f);
            for (int x = 0; x < w; x++)
            {
                float jitter = 0.75f + (float)rng.NextDouble() * 0.25f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(column[x] * v * jitter)));
            }
        }
        t.Apply();
        return t;
    }

    private static Texture2D MakeNoise()
    {
        const int s = 256;
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var rng = new System.Random(1996);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float g = (float)rng.NextDouble();
            t.SetPixel(x, y, new Color(g, g, g, 1f));
        }
        t.Apply();
        return t;
    }
}
