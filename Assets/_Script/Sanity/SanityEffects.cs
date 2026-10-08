using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 8 Oct (Mr.k). Everything sanity does to YOUR OWN screen. Added at runtime by
/// PlayerSanity on the owner only (no prefab edit, no scene edit).
///
///   - Sanity bar (bottom-left) + "chanting" / "cannot recover" text
///   - Eat / feed progress bar (centre)
///   - SHAKEN: dark screen edges.  BREAKING: darker edges that pulse like a heartbeat
///   - BREAKING: your friends look like ghosts (only on YOUR screen)
///   - Sanity 0: jumpscare flash (+ optional picture / sound from SanitySettings)
///
/// Visual only. It never changes sanity; the server does that.
/// </summary>
[DisallowMultipleComponent]
public sealed class SanityEffects : MonoBehaviour
{
    private static readonly Color CalmColor = new Color(0.55f, 0.8f, 1f);
    private static readonly Color ShakenColor = new Color(1f, 0.8f, 0.3f);
    private static readonly Color BreakingColor = new Color(1f, 0.25f, 0.25f);

    private PlayerSanity sanity;
    private GameObject root;
    private Image vignette;
    private RectTransform barFill;
    private Image barFillImage;
    private Text barLabel;
    private GameObject useRoot;
    private RectTransform useFill;
    private Text useLabel;
    private Image flash;
    private AudioSource audioSource;
    private float flashUntil;
    private float shownVignette;
    private string shownText;

    private void Awake() => sanity = GetComponent<PlayerSanity>();

    private void OnEnable()
    {
        if (sanity != null) sanity.JumpscareReceived += OnJumpscare;
    }

    private void OnDisable()
    {
        if (sanity != null) sanity.JumpscareReceived -= OnJumpscare;
        ClearHallucinations();
    }

    private void OnDestroy()
    {
        ClearHallucinations();
        if (root != null) Destroy(root);
    }

    private void LateUpdate()
    {
        if (sanity == null) return;
        bool show = sanity.InPlay || (MatchDirector.Instance != null && !PlayerInventory.InLobby && sanity.Value < PlayerSanity.Max);
        if (root == null) { if (!show) return; Build(); }
        if (root.activeSelf != show) root.SetActive(show);
        if (!show) { ClearHallucinations(); return; }

        SanitySettings s = SanitySettings.Current;
        SanityLevel level = sanity.Level;

        // Bar
        float fraction = sanity.Fraction;
        barFill.anchorMax = new Vector2(fraction, 1f);
        Color colour = level == SanityLevel.Breaking ? BreakingColor : level == SanityLevel.Shaken ? ShakenColor : CalmColor;
        if (level == SanityLevel.Breaking) colour.a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
        barFillImage.color = colour;
        string text = "SANITY " + Mathf.CeilToInt(sanity.Value)
            + (sanity.IsChanting ? (sanity.BlocksGain ? "   chanting (cannot recover now)" : "   chanting...") : "")
            + (!sanity.IsChanting && sanity.BlocksGain ? "   cannot recover" : "")
            + (!sanity.IsChanting && !sanity.BlocksGain && sanity.Value < PlayerSanity.Max && sanity.InPlay ? $"   [{s.chantKey}] chant" : "");
        if (text != shownText) { shownText = text; barLabel.text = text; }

        // Eat / feed progress
        bool using_ = sanity.UseProgress > 0f;
        if (useRoot.activeSelf != using_) useRoot.SetActive(using_);
        if (using_)
        {
            useFill.anchorMax = new Vector2(sanity.UseProgress, 1f);
            string label = sanity.UseIsFeeding ? "Feeding your friend..." : "Eating...";
            if (useLabel.text != label) useLabel.text = label;
        }

        // Vignette
        float target = level == SanityLevel.Breaking ? s.vignetteBreaking : level == SanityLevel.Shaken ? s.vignetteShaken : 0f;
        if (level == SanityLevel.Breaking) target *= 0.85f + 0.15f * Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.time * 2.6f)), 6f);
        shownVignette = Mathf.MoveTowards(shownVignette, target, Time.deltaTime * 0.8f);
        vignette.color = new Color(0f, 0f, 0f, shownVignette);
        vignette.enabled = shownVignette > 0.001f;

        // Jumpscare flash
        if (flash.enabled)
        {
            float left = flashUntil - Time.time;
            if (left <= 0f) flash.enabled = false;
            else
            {
                Color c = flash.color;
                c.a = Mathf.Clamp01(left / Mathf.Max(0.05f, s.jumpscareSeconds) * 1.4f);
                flash.color = c;
            }
        }

        // Friends as ghosts
        if (level == SanityLevel.Breaking && sanity.InPlay) UpdateHallucinations(s);
        else ClearHallucinations();
    }

    private void OnJumpscare()
    {
        if (root == null) Build();
        SanitySettings s = SanitySettings.Current;
        flash.sprite = s.jumpscareImage;
        flash.color = s.jumpscareImage != null ? Color.white : new Color(0.6f, 0f, 0f, 1f);
        flash.preserveAspect = s.jumpscareImage != null;
        flash.enabled = true;
        flashUntil = Time.time + s.jumpscareSeconds;
        if (s.jumpscareSound != null) audioSource.PlayOneShot(s.jumpscareSound);
    }

    // ---- Friends look like ghosts (this screen only) ---------------------------

    private sealed class Haunted
    {
        public PlayerSanity friend;
        public SkinnedMeshRenderer[] bodies;
        public GameObject visual;
    }

    private readonly List<Haunted> haunted = new List<Haunted>();
    private float hauntScan;
    private MaterialPropertyBlock shadowBlock;

    private void UpdateHallucinations(SanitySettings s)
    {
        if (Time.time < hauntScan) return;
        hauntScan = Time.time + 1f;

        // Drop friends who left / died.
        for (int i = haunted.Count - 1; i >= 0; i--)
        {
            if (haunted[i].friend == null || !haunted[i].friend.InPlay)
            {
                Restore(haunted[i]);
                haunted.RemoveAt(i);
            }
        }

        for (int i = 0; i < PlayerSanity.All.Count; i++)
        {
            PlayerSanity friend = PlayerSanity.All[i];
            if (friend == null || friend == sanity || !friend.InPlay || IsHaunted(friend)) continue;
            haunted.Add(Haunt(friend, s));
        }
    }

    private bool IsHaunted(PlayerSanity friend)
    {
        for (int i = 0; i < haunted.Count; i++) if (haunted[i].friend == friend) return true;
        return false;
    }

    private Haunted Haunt(PlayerSanity friend, SanitySettings s)
    {
        var h = new Haunted { friend = friend, bodies = friend.GetComponentsInChildren<SkinnedMeshRenderer>(true) };
        if (s.hallucinationPrefab != null)
        {
            h.visual = SpawnVisualOnly(s.hallucinationPrefab, friend.transform);
            foreach (SkinnedMeshRenderer body in h.bodies) if (body != null) body.enabled = false;
        }
        else
        {
            if (shadowBlock == null)
            {
                shadowBlock = new MaterialPropertyBlock();
                shadowBlock.SetColor("_BaseColor", Color.black); // URP Lit / Unlit
                shadowBlock.SetColor("_Color", Color.black);     // Built-in / most custom shaders
            }
            foreach (SkinnedMeshRenderer body in h.bodies) if (body != null) body.SetPropertyBlock(shadowBlock);
        }
        return h;
    }

    /// <summary>A copy of the prefab with every script, collider and network part removed: pure visuals.</summary>
    private static GameObject SpawnVisualOnly(GameObject prefab, Transform parent)
    {
        var holder = new GameObject("Hallucination (local)");
        holder.SetActive(false); // children Instantiated under an inactive parent do not run Awake
        holder.transform.SetParent(parent, false);
        GameObject copy = Instantiate(prefab, holder.transform, false);
        copy.transform.localPosition = Vector3.zero;
        copy.transform.localRotation = Quaternion.identity;
        foreach (MonoBehaviour script in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(script);
        foreach (Mirror.NetworkIdentity id in copy.GetComponentsInChildren<Mirror.NetworkIdentity>(true)) DestroyImmediate(id);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Rigidbody b in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(b);
        foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        holder.SetActive(true);
        return holder;
    }

    private void Restore(Haunted h)
    {
        if (h.visual != null) Destroy(h.visual);
        if (h.bodies == null) return;
        foreach (SkinnedMeshRenderer body in h.bodies)
        {
            if (body == null) continue;
            body.SetPropertyBlock(null);
            body.enabled = true;
        }
    }

    private void ClearHallucinations()
    {
        for (int i = 0; i < haunted.Count; i++) Restore(haunted[i]);
        haunted.Clear();
    }

    // ---- UI (built in code) ------------------------------------------------------

    private void Build()
    {
        SanitySettings s = SanitySettings.Current;
        root = new GameObject("Sanity Screen");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 44;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        audioSource = root.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        vignette = NewImage("Vignette", root.transform, Color.clear);
        vignette.sprite = MakeVignetteSprite();
        Stretch(vignette.rectTransform);

        // Sanity bar
        Image back = NewImage("Sanity Bar", root.transform, new Color(0f, 0f, 0f, 0.55f));
        RectTransform backRt = back.rectTransform;
        backRt.anchorMin = backRt.anchorMax = backRt.pivot = Vector2.zero;
        backRt.anchoredPosition = s.barPosition;
        backRt.sizeDelta = s.barSize;
        barFillImage = NewImage("Fill", backRt, CalmColor);
        barFill = barFillImage.rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = Vector2.one;
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;
        barLabel = NewText("Label", backRt, "SANITY", 18, TextAnchor.LowerLeft);
        RectTransform labelRt = barLabel.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.pivot = new Vector2(0f, 0f);
        labelRt.anchoredPosition = new Vector2(0f, 2f);
        labelRt.sizeDelta = new Vector2(400f, 24f);

        // Eat / feed progress (centre, under the crosshair)
        Image useBack = NewImage("Use Progress", root.transform, new Color(0f, 0f, 0f, 0.6f));
        useRoot = useBack.gameObject;
        RectTransform useRt = useBack.rectTransform;
        useRt.anchorMin = useRt.anchorMax = new Vector2(0.5f, 0.5f);
        useRt.anchoredPosition = new Vector2(0f, -70f);
        useRt.sizeDelta = new Vector2(220f, 10f);
        Image useFillImage = NewImage("Fill", useRt, new Color(0.55f, 1f, 0.6f));
        useFill = useFillImage.rectTransform;
        useFill.anchorMin = Vector2.zero;
        useFill.anchorMax = new Vector2(0f, 1f);
        useFill.offsetMin = useFill.offsetMax = Vector2.zero;
        useLabel = NewText("Label", useRt, "", 18, TextAnchor.LowerCenter);
        RectTransform useLabelRt = useLabel.rectTransform;
        useLabelRt.anchorMin = new Vector2(0.5f, 1f);
        useLabelRt.anchorMax = new Vector2(0.5f, 1f);
        useLabelRt.pivot = new Vector2(0.5f, 0f);
        useLabelRt.sizeDelta = new Vector2(400f, 24f);
        useRoot.SetActive(false);

        // Jumpscare (on top of everything)
        var flashCanvasGo = new GameObject("Sanity Jumpscare", typeof(RectTransform));
        flashCanvasGo.transform.SetParent(root.transform, false);
        var flashCanvas = flashCanvasGo.AddComponent<Canvas>();
        flashCanvas.overrideSorting = true;
        flashCanvas.sortingOrder = 300;
        Stretch((RectTransform)flashCanvasGo.transform);
        flash = NewImage("Flash", flashCanvasGo.transform, Color.clear);
        Stretch(flash.rectTransform);
        flash.enabled = false;
    }

    private static Sprite MakeVignetteSprite()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f; // 0 centre, 1 corner
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.95f, d));
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text NewText(string name, Transform parent, string value, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.text = value;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        return text;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
