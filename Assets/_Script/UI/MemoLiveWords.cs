using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 25 Nov 2026 (Mr.k): the LIVE Memo Book words: one short command in BIG blood letters that fill the page.
/// STYLE "Blood Seep" (default, MemoBookLook.liveStyle): blood soaks up through the paper. Each letter
/// appears as blotches that spread and join, pale and thin at first, darkening to deep red; its drips run
/// down at the end. EVERY word soaks up at the same time, slowly (Seep Together Seconds). The old words
/// dry away the same way (blotches shrink) while the new ones soak in. A dark halo bleeds into the paper.
/// STYLE "Pen": written BY HAND:
///   - letter after letter, in reading order (top line first, left to right),
///   - each letter drawn along its real pen strokes, from where people start writing it to where
///     they finish it (G starts at its top-right, curls round and ends with its bar; T = top bar, then
///     the stem; and so on),
///   - a short pause between words.
/// When the step changes, the old words are wiped in exactly the same way (same order, same pen
/// path) while the new ones are written.
/// How: each text sits under a stencil Mask whose picture is the pen's path so far (thick strokes),
/// so only the part of the blood letters the pen has passed shows. A finished letter shows whole
/// (drips too).
/// Timing and colour: Resources/Tuning/MemoBookLook (Pen Letter Seconds, Pen Word Gap, Blood Color).
/// Made and fed by WorksheetReader. LOCAL ONLY (UI).
/// </summary>
[DisallowMultipleComponent]
public sealed class MemoLiveWords : MonoBehaviour
{
    private sealed class Layer
    {
        public PenMask pen;
        public TextMeshProUGUI text;
        public readonly List<Letter> letters = new List<Letter>();
        public float length; // seconds until the last letter is done
    }

    public struct Letter
    {
        public int index;      // character index
        public float start;    // seconds after the change
        public float seconds;  // how long the pen takes
    }

    private Layer write, wipe;
    private string shown;
    private float startedAt = -1f;

    public static MemoLiveWords Create(RectTransform area)
    {
        var go = new GameObject("Live Words", typeof(RectTransform));
        go.transform.SetParent(area, false);
        Stretch((RectTransform)go.transform, 12f);
        MemoLiveWords words = go.AddComponent<MemoLiveWords>();
        words.wipe = words.MakeLayer("Wiped");
        words.write = words.MakeLayer("Written");
        return words;
    }

    private Layer MakeLayer(string name)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(transform, false);
        Stretch((RectTransform)holder.transform, 0f);
        PenMask pen = holder.AddComponent<PenMask>();
        pen.raycastTarget = false;
        Mask mask = holder.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var textGo = new GameObject("Words", typeof(RectTransform));
        textGo.transform.SetParent(holder.transform, false);
        Stretch((RectTransform)textGo.transform, 0f); // same rectangle = same local space as the pen
        TextMeshProUGUI t = textGo.AddComponent<TextMeshProUGUI>();
        t.raycastTarget = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = 30f;
        t.fontSizeMax = 150f;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.lineSpacing = -10f;
        t.text = "";
        pen.text = t;
        return new Layer { pen = pen, text = t };
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Shows these words. A change plays the wipe / write; <paramref name="instant"/> skips it.</summary>
    public void Show(string words, bool instant)
    {
        words = string.IsNullOrEmpty(words) ? "" : words.ToUpperInvariant();
        Style(write.text);
        Style(wipe.text);
        if (words == shown) return;

        // The words on the page now are wiped; the new words are written on top.
        Layer old = write;
        write = wipe;
        wipe = old;
        write.pen.transform.SetAsLastSibling();
        write.text.text = words;
        shown = words;
        if (string.IsNullOrEmpty(wipe.text.text) || instant) wipe.text.text = "";

        Plan(write);
        Plan(wipe);
        if (instant)
        {
            startedAt = -1f;
            write.pen.Draw(write.letters, float.MaxValue, true);
            wipe.pen.Draw(wipe.letters, float.MaxValue, false);
            return;
        }
        startedAt = Time.unscaledTime;
        Animate();
    }

    private static Material bloodMaterial;
    private static TMP_FontAsset bloodMaterialFor;

    private static void Style(TextMeshProUGUI t)
    {
        MemoBookLook look = MemoBookLook.Current;
        TMP_FontAsset font = MemoBookLook.BloodFont;
        if (font != null && t.font != font) t.font = font;
        if (t.color != Color.white) t.color = Color.white; // the gradient below carries the blood colours
        // Darker at the top, wetter red at the bottom (where the blood runs to).
        if (!t.enableVertexGradient)
        {
            t.enableVertexGradient = true;
            Color top = look.bloodColor * 0.7f, bottom = Color.Lerp(look.bloodColor, new Color(0.85f, 0.05f, 0.04f), 0.5f);
            top.a = bottom.a = 1f;
            t.colorGradient = new VertexGradient(top, top, bottom, bottom);
        }
        // A soft dark halo: the blood soaked into the paper around each letter.
        if (font != null && look.soakHalo.a > 0f)
        {
            if (bloodMaterial == null || bloodMaterialFor != font)
            {
                bloodMaterialFor = font;
                bloodMaterial = new Material(font.material) { name = "Blood Letters (soak halo)" };
                bloodMaterial.EnableKeyword("UNDERLAY_ON");
                if (bloodMaterial.HasProperty("_UnderlayColor")) bloodMaterial.SetColor("_UnderlayColor", look.soakHalo);
                if (bloodMaterial.HasProperty("_UnderlaySoftness")) bloodMaterial.SetFloat("_UnderlaySoftness", 0.7f);
                if (bloodMaterial.HasProperty("_UnderlayDilate")) bloodMaterial.SetFloat("_UnderlayDilate", 0.35f);
                if (bloodMaterial.HasProperty("_UnderlayOffsetY")) bloodMaterial.SetFloat("_UnderlayOffsetY", -0.25f);
            }
            if (t.fontSharedMaterial != bloodMaterial) t.fontSharedMaterial = bloodMaterial;
        }
    }

    private void Update()
    {
        if (startedAt >= 0f) Animate();
    }

    private void Animate()
    {
        float t = Time.unscaledTime - startedAt;
        write.pen.Draw(write.letters, t, true);
        wipe.pen.Draw(wipe.letters, t, false);
        if (Seep) Darken(write, t);
        if (t < Mathf.Max(write.length, wipe.length)) return;
        startedAt = -1f;
        wipe.text.text = "";
        if (Seep) Darken(write, float.MaxValue);
    }

    private static bool Seep => MemoBookLook.Current.liveStyle == MemoBookLook.LiveStyle.BloodSeep;

    /// <summary>Blood Seep: each letter goes from thin pale blood to the full blood colours as it soaks.</summary>
    private static void Darken(Layer layer, float t)
    {
        TextMeshProUGUI text = layer.text;
        if (string.IsNullOrEmpty(text.text) || layer.letters.Count == 0) return;
        if (text.havePropertiesChanged) text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        VertexGradient g = text.colorGradient;
        Color fresh = MemoBookLook.Current.freshBlood;
        foreach (Letter l in layer.letters)
        {
            if (l.index >= info.characterCount) continue;
            TMP_CharacterInfo ch = info.characterInfo[l.index];
            if (!ch.isVisible) continue;
            float k = Mathf.Clamp01((t - l.start) / Mathf.Max(0.01f, l.seconds));
            k = 1f - (1f - k) * (1f - k); // darkens fast at first, then slowly
            Color32[] colours = info.meshInfo[ch.materialReferenceIndex].colors32;
            int v = ch.vertexIndex;
            if (colours == null || v + 3 >= colours.Length) continue;
            colours[v] = Color.Lerp(fresh, g.bottomLeft, k);
            colours[v + 1] = Color.Lerp(fresh, g.topLeft, k);
            colours[v + 2] = Color.Lerp(fresh, g.topRight, k);
            colours[v + 3] = Color.Lerp(fresh, g.bottomRight, k);
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    /// <summary>When each letter starts and how long its pen takes (reading order, pause between words).</summary>
    private static void Plan(Layer layer)
    {
        layer.letters.Clear();
        layer.length = 0f;
        if (string.IsNullOrEmpty(layer.text.text)) return;
        layer.text.ForceMeshUpdate();
        TMP_TextInfo info = layer.text.textInfo;
        MemoBookLook look = MemoBookLook.Current;
        float perLetter = Mathf.Max(0.02f, look.penLetterSeconds);
        bool seep = look.liveStyle == MemoBookLook.LiveStyle.BloodSeep;
        float at = 0f;
        bool gapPending = false;
        for (int c = 0; c < info.characterCount; c++)
        {
            TMP_CharacterInfo ch = info.characterInfo[c];
            if (!ch.isVisible || char.IsWhiteSpace(ch.character))
            {
                if (layer.letters.Count > 0) gapPending = true;
                continue;
            }
            if (seep)
            {
                // 25 Nov: every letter soaks up at the same time, slowly.
                float soak = Mathf.Max(0.2f, look.seepTogetherSeconds);
                layer.letters.Add(new Letter { index = c, start = 0f, seconds = soak });
                layer.length = soak;
                continue;
            }
            if (gapPending) { at += look.penWordGap; gapPending = false; }
            float seconds = perLetter * Mathf.Clamp(PenStrokes.Length(ch.character) / 2.4f, 0.5f, 1.6f);
            layer.letters.Add(new Letter { index = c, start = at, seconds = seconds });
            at += seconds;
            layer.length = at;
        }
    }

    /// <summary>Clears the page (next Show writes from nothing).</summary>
    public void Clear()
    {
        shown = null;
        startedAt = -1f;
        foreach (Layer l in new[] { write, wipe })
        {
            if (l == null) continue;
            l.text.text = "";
            l.letters.Clear();
            l.length = 0f;
            l.pen.Draw(l.letters, 0f, true);
        }
    }
}

/// <summary>
/// 25 Nov: the stencil picture under one text: thick pen strokes along each letter's path, as far
/// as the pen has gone (writing), or what the pen has not wiped yet (wiping). Invisible itself.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PenMask : MaskableGraphic
{
    public TextMeshProUGUI text;

    private readonly List<MemoLiveWords.Letter> letters = new List<MemoLiveWords.Letter>();
    private float time;
    private bool writing = true;

    public void Draw(List<MemoLiveWords.Letter> plan, float seconds, bool isWriting)
    {
        letters.Clear();
        letters.AddRange(plan);
        time = seconds;
        writing = isWriting;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (text == null || letters.Count == 0) return;
        if (text.havePropertiesChanged) text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        foreach (MemoLiveWords.Letter l in letters)
        {
            if (l.index >= info.characterCount) continue;
            TMP_CharacterInfo ch = info.characterInfo[l.index];
            if (!ch.isVisible) continue;
            float k = Mathf.Clamp01((time - l.start) / Mathf.Max(0.01f, l.seconds));

            // The whole glyph (drips too): shown when written, or not wiped yet.
            Vector3[] verts = info.meshInfo[ch.materialReferenceIndex].vertices;
            int v = ch.vertexIndex;
            Rect whole = verts != null && v + 2 < verts.Length
                ? Rect.MinMaxRect(verts[v].x - 2f, verts[v].y - 2f, verts[v + 2].x + 2f, verts[v + 2].y + 2f)
                : Rect.MinMaxRect(ch.bottomLeft.x, ch.bottomLeft.y, ch.topRight.x, ch.topRight.y);
            if (writing ? k >= 1f : k <= 0f) { Quad(vh, whole); continue; }
            if (writing ? k <= 0f : k >= 1f) continue;

            // Part way. The letter body = from the baseline to the top of the glyph.
            float bottom = ch.baseLine, top = Mathf.Max(ch.topRight.y, bottom + 1f);
            Rect body = Rect.MinMaxRect(ch.bottomLeft.x, bottom, ch.topRight.x, top);
            if (MemoBookLook.Current.liveStyle == MemoBookLook.LiveStyle.BloodSeep)
            {
                // Writing: blotches grow, then the drips run down. Wiping: the same, backwards (it dries away).
                float s = writing ? k : 1f - k;
                Seep(vh, body, whole, s, l.index * 7919 + ch.character);
                continue;
            }
            float brush = body.height * 0.45f;
            if (writing) PenStrokes.Draw(vh, ch.character, body, 0f, k, brush, this);
            else PenStrokes.Draw(vh, ch.character, body, k, 1f, brush, this);
        }
    }

    /// <summary>Blood Seep: 4 blotches spread out from spots inside the letter, then the drips run down.</summary>
    private void Seep(VertexHelper vh, Rect body, Rect whole, float s, int seed)
    {
        float size = Mathf.Max(body.width, body.height);
        float grow = Mathf.Clamp01(s / 0.8f);               // the blotches: first 80 %
        for (int i = 0; i < 4; i++)
        {
            float delay = Hash(seed, i * 3 + 2) * 0.35f;      // spots appear one after another
            float g = Mathf.Clamp01((grow - delay) / (1f - delay));
            g = 1f - (1f - g) * (1f - g) * (1f - g);         // fast at first, slows as it soaks
            if (g <= 0f) continue;
            Vector2 centre = new Vector2(body.xMin + (0.15f + 0.7f * Hash(seed, i * 3)) * body.width,
                                         body.yMin + (0.15f + 0.7f * Hash(seed, i * 3 + 1)) * body.height);
            Blob(vh, centre, g * size * 0.75f, seed + i * 31);
        }
        // The drips below the baseline run down at the end.
        float drip = Mathf.Clamp01((s - 0.55f) / 0.45f);
        if (drip > 0f && whole.yMin < body.yMin)
        {
            float bottom = Mathf.Lerp(body.yMin, whole.yMin, drip);
            Quad(vh, Rect.MinMaxRect(whole.xMin, bottom, whole.xMax, body.yMin + 1f));
        }
    }

    /// <summary>A round, wobbly blotch.</summary>
    private void Blob(VertexHelper vh, Vector2 centre, float radius, int seed)
    {
        const int sides = 18;
        int start = vh.currentVertCount;
        Color32 c = Color.white;
        vh.AddVert(centre, c, Vector2.zero);
        float p1 = Hash(seed, 1) * 6.283f, p2 = Hash(seed, 2) * 6.283f;
        for (int i = 0; i <= sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            float r = radius * (1f + 0.18f * Mathf.Sin(a * 3f + p1) + 0.1f * Mathf.Sin(a * 5f + p2));
            vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, c, Vector2.zero);
            if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    /// <summary>0..1, the same every time for the same numbers.</summary>
    private static float Hash(int seed, int n)
    {
        unchecked
        {
            uint h = (uint)(seed * 374761393 + n * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }

    private void Quad(VertexHelper vh, Rect r)
    {
        int i = vh.currentVertCount;
        Color32 c = Color.white;
        vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }

    /// <summary>A thick line from a to b (with square caps so the joints overlap).</summary>
    public void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        if (len < 0.001f) d = Vector2.right; else d /= len;
        Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
        Vector2 cap = d * (width * 0.5f);
        a -= cap;
        b += cap;
        int i = vh.currentVertCount;
        Color32 c = Color.white;
        vh.AddVert(a - n, c, Vector2.zero);
        vh.AddVert(a + n, c, Vector2.zero);
        vh.AddVert(b + n, c, Vector2.zero);
        vh.AddVert(b - n, c, Vector2.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }
}

/// <summary>
/// 25 Nov: how people WRITE each capital letter / digit: the strokes in order, each from where the pen
/// starts to where it stops. Points are in the letter box: x 0 = left, 1 = right; y 0 = baseline, 1 = top.
/// </summary>
public static class PenStrokes
{
    private static readonly Dictionary<char, Vector2[][]> table = Build();
    private static readonly Dictionary<char, float> lengths = new Dictionary<char, float>();

    private static Vector2[] S(params float[] xy)
    {
        var pts = new Vector2[xy.Length / 2];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        return pts;
    }

    private static Dictionary<char, Vector2[][]> Build()
    {
        Vector2[] ring = S(0.5f, 1f, 0.15f, 0.88f, 0f, 0.5f, 0.15f, 0.12f, 0.5f, 0f, 0.85f, 0.12f, 1f, 0.5f, 0.85f, 0.88f, 0.5f, 1f);
        Vector2[] bowlP = S(0f, 1f, 0.7f, 1f, 0.95f, 0.85f, 0.95f, 0.6f, 0.7f, 0.45f, 0f, 0.45f);
        return new Dictionary<char, Vector2[][]>
        {
            ['A'] = new[] { S(0f, 0f, 0.5f, 1f, 1f, 0f), S(0.22f, 0.4f, 0.78f, 0.4f) },
            ['B'] = new[] { S(0f, 1f, 0f, 0f), S(0f, 1f, 0.65f, 1f, 0.85f, 0.88f, 0.85f, 0.62f, 0.65f, 0.52f, 0f, 0.52f), S(0.65f, 0.52f, 0.92f, 0.4f, 0.92f, 0.12f, 0.65f, 0f, 0f, 0f) },
            ['C'] = new[] { S(0.95f, 0.82f, 0.75f, 1f, 0.35f, 1f, 0.08f, 0.8f, 0f, 0.5f, 0.08f, 0.2f, 0.35f, 0f, 0.75f, 0f, 0.95f, 0.18f) },
            ['D'] = new[] { S(0f, 1f, 0f, 0f), S(0f, 1f, 0.55f, 1f, 0.9f, 0.78f, 1f, 0.5f, 0.9f, 0.22f, 0.55f, 0f, 0f, 0f) },
            ['E'] = new[] { S(0f, 1f, 0f, 0f), S(0f, 1f, 1f, 1f), S(0f, 0.5f, 0.8f, 0.5f), S(0f, 0f, 1f, 0f) },
            ['F'] = new[] { S(0f, 1f, 0f, 0f), S(0f, 1f, 1f, 1f), S(0f, 0.5f, 0.8f, 0.5f) },
            ['G'] = new[] { S(0.95f, 0.82f, 0.75f, 1f, 0.35f, 1f, 0.08f, 0.8f, 0f, 0.5f, 0.08f, 0.2f, 0.35f, 0f, 0.75f, 0f, 1f, 0.2f, 1f, 0.45f, 0.55f, 0.45f) },
            ['H'] = new[] { S(0f, 1f, 0f, 0f), S(1f, 1f, 1f, 0f), S(0f, 0.5f, 1f, 0.5f) },
            ['I'] = new[] { S(0.5f, 1f, 0.5f, 0f) },
            ['J'] = new[] { S(1f, 1f, 1f, 0.25f, 0.8f, 0f, 0.3f, 0f, 0f, 0.25f) },
            ['K'] = new[] { S(0f, 1f, 0f, 0f), S(1f, 1f, 0f, 0.45f), S(0.3f, 0.6f, 1f, 0f) },
            ['L'] = new[] { S(0f, 1f, 0f, 0f, 1f, 0f) },
            ['M'] = new[] { S(0f, 0f, 0f, 1f, 0.5f, 0.35f, 1f, 1f, 1f, 0f) },
            ['N'] = new[] { S(0f, 0f, 0f, 1f, 1f, 0f, 1f, 1f) },
            ['O'] = new[] { ring },
            ['P'] = new[] { S(0f, 1f, 0f, 0f), bowlP },
            ['Q'] = new[] { ring, S(0.6f, 0.3f, 1f, -0.05f) },
            ['R'] = new[] { S(0f, 1f, 0f, 0f), bowlP, S(0.45f, 0.45f, 1f, 0f) },
            ['S'] = new[] { S(0.95f, 0.85f, 0.7f, 1f, 0.3f, 1f, 0.05f, 0.82f, 0.1f, 0.6f, 0.5f, 0.5f, 0.9f, 0.4f, 0.95f, 0.18f, 0.7f, 0f, 0.3f, 0f, 0.05f, 0.15f) },
            ['T'] = new[] { S(0f, 1f, 1f, 1f), S(0.5f, 1f, 0.5f, 0f) },
            ['U'] = new[] { S(0f, 1f, 0f, 0.3f, 0.2f, 0.05f, 0.5f, 0f, 0.8f, 0.05f, 1f, 0.3f, 1f, 1f) },
            ['V'] = new[] { S(0f, 1f, 0.5f, 0f, 1f, 1f) },
            ['W'] = new[] { S(0f, 1f, 0.25f, 0f, 0.5f, 0.65f, 0.75f, 0f, 1f, 1f) },
            ['X'] = new[] { S(0f, 1f, 1f, 0f), S(1f, 1f, 0f, 0f) },
            ['Y'] = new[] { S(0f, 1f, 0.5f, 0.5f), S(1f, 1f, 0.5f, 0.5f, 0.5f, 0f) },
            ['Z'] = new[] { S(0f, 1f, 1f, 1f, 0f, 0f, 1f, 0f) },
            ['0'] = new[] { ring },
            ['1'] = new[] { S(0.25f, 0.8f, 0.55f, 1f, 0.55f, 0f) },
            ['2'] = new[] { S(0.05f, 0.78f, 0.25f, 0.97f, 0.6f, 1f, 0.9f, 0.85f, 0.9f, 0.6f, 0f, 0f, 1f, 0f) },
            ['3'] = new[] { S(0.05f, 0.85f, 0.35f, 1f, 0.75f, 1f, 0.95f, 0.8f, 0.75f, 0.55f, 0.4f, 0.52f), S(0.75f, 0.52f, 1f, 0.3f, 0.85f, 0.05f, 0.5f, 0f, 0.05f, 0.12f) },
            ['4'] = new[] { S(0.75f, 0f, 0.75f, 1f, 0f, 0.3f, 1f, 0.3f) },
            ['5'] = new[] { S(0.95f, 1f, 0.1f, 1f, 0.05f, 0.55f, 0.5f, 0.62f, 0.9f, 0.45f, 0.95f, 0.2f, 0.7f, 0f, 0.3f, 0f, 0.05f, 0.12f) },
            ['6'] = new[] { S(0.9f, 0.9f, 0.6f, 1f, 0.25f, 0.9f, 0.05f, 0.55f, 0.05f, 0.25f, 0.3f, 0f, 0.7f, 0f, 0.95f, 0.25f, 0.9f, 0.5f, 0.6f, 0.62f, 0.25f, 0.55f, 0.05f, 0.35f) },
            ['7'] = new[] { S(0f, 1f, 1f, 1f, 0.35f, 0f) },
            ['8'] = new[] { S(0.85f, 0.82f, 0.5f, 1f, 0.15f, 0.82f, 0.2f, 0.6f, 0.5f, 0.52f, 0.85f, 0.38f, 0.9f, 0.15f, 0.5f, 0f, 0.1f, 0.15f, 0.15f, 0.38f, 0.5f, 0.52f, 0.8f, 0.6f, 0.85f, 0.82f) },
            ['9'] = new[] { S(0.95f, 0.65f, 0.7f, 0.45f, 0.3f, 0.45f, 0.05f, 0.7f, 0.3f, 1f, 0.7f, 1f, 0.95f, 0.75f, 0.95f, 0.3f, 0.7f, 0f, 0.3f, 0f, 0.1f, 0.1f) },
            [':'] = new[] { S(0.5f, 0.75f, 0.5f, 0.6f), S(0.5f, 0.3f, 0.5f, 0.15f) },
            ['.'] = new[] { S(0.5f, 0.15f, 0.5f, 0f) },
            [','] = new[] { S(0.55f, 0.15f, 0.4f, -0.1f) },
            ['!'] = new[] { S(0.5f, 1f, 0.5f, 0.3f), S(0.5f, 0.12f, 0.5f, 0f) },
            ['?'] = new[] { S(0.05f, 0.8f, 0.3f, 1f, 0.7f, 1f, 0.95f, 0.8f, 0.5f, 0.45f, 0.5f, 0.3f), S(0.5f, 0.12f, 0.5f, 0f) },
            ['-'] = new[] { S(0.1f, 0.5f, 0.9f, 0.5f) },
            ['\''] = new[] { S(0.5f, 1f, 0.5f, 0.7f) },
            ['/'] = new[] { S(0f, 0f, 1f, 1f) },
            ['('] = new[] { S(0.8f, 1f, 0.3f, 0.75f, 0.2f, 0.5f, 0.3f, 0.25f, 0.8f, 0f) },
            [')'] = new[] { S(0.2f, 1f, 0.7f, 0.75f, 0.8f, 0.5f, 0.7f, 0.25f, 0.2f, 0f) },
        };
    }

    private static readonly Vector2[][] Unknown = { new[] { new Vector2(0f, 0.5f), new Vector2(1f, 0.5f) } };

    private static Vector2[][] Of(char c) => table.TryGetValue(char.ToUpperInvariant(c), out Vector2[][] s) ? s : Unknown;

    /// <summary>Total pen path length in letter boxes (for the timing).</summary>
    public static float Length(char c)
    {
        if (lengths.TryGetValue(c, out float l)) return l;
        l = 0f;
        foreach (Vector2[] stroke in Of(c))
            for (int i = 1; i < stroke.Length; i++) l += Vector2.Distance(stroke[i - 1], stroke[i]);
        lengths[c] = l;
        return l;
    }

    /// <summary>Draws the part of the pen path between <paramref name="from"/> and <paramref name="to"/> (0..1 of its length).</summary>
    public static void Draw(VertexHelper vh, char c, Rect box, float from, float to, float brush, PenMask pen)
    {
        Vector2[][] strokes = Of(c);
        float total = 0f;
        foreach (Vector2[] stroke in strokes)
            for (int i = 1; i < stroke.Length; i++) total += Vector2.Distance(Map(box, stroke[i - 1]), Map(box, stroke[i]));
        if (total <= 0f) return;
        float a = from * total, b = to * total, walked = 0f;
        foreach (Vector2[] stroke in strokes)
            for (int i = 1; i < stroke.Length; i++)
            {
                Vector2 p0 = Map(box, stroke[i - 1]), p1 = Map(box, stroke[i]);
                float len = Vector2.Distance(p0, p1);
                float s0 = walked, s1 = walked + len;
                walked = s1;
                if (s1 <= a || s0 >= b || len <= 0f) continue;
                float t0 = Mathf.Clamp01((a - s0) / len), t1 = Mathf.Clamp01((b - s0) / len);
                pen.Line(vh, Vector2.Lerp(p0, p1, t0), Vector2.Lerp(p0, p1, t1), brush);
            }
    }

    // The path runs along the middle of the thick blood strokes, not on the glyph's outer edge.
    private static Vector2 Map(Rect box, Vector2 p) =>
        new Vector2(box.xMin + (0.12f + p.x * 0.76f) * box.width, box.yMin + (0.08f + p.y * 0.84f) * box.height);
}
