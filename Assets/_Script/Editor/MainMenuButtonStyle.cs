using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ONE CLICK: Tools > Pre-Thesis > Scenes > Menu: Style Main Menu Buttons (VCR)
///
/// The five main menu buttons (Create Room, Room Browser, Quick Join, Settings, Quit),
/// design "A — VCR Play Screen":
///   - every label is 38 (the size PLAY had) and does NOT grow on hover;
///   - on hover the bar behind the label is white (#F2F2F2), the label turns
///     VCR blue (#1A2A9E) and a solid white block (VHS cursor) shows in place of '>';
///   - Pressed looks the same as hover (no flicker on click);
///   - out of hover everything is hidden by default in the scene, so states with no
///     animation of their own (Disabled while EOS connects, Selected) never show
///     a stray '>' / bar.
///
/// Changes: label size/colour, bar RGB + alpha, '>' text and a "Cursor Block" image in
/// MainMenu.unity; the "Normal" / "Highlighted" / "Pressed" clips inside each button's
/// .controller. Safe to run again.
/// </summary>
public static class MainMenuButtonStyle
{
    private const string MenuPath = "Tools/Pre-Thesis/Scenes/Menu: Style Main Menu Buttons (VCR)";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string PanelPath = "Main Menu UI/Main Panel/";
    private const float Size = 38f;

    private static readonly string[] Buttons =
        { "Create Room Button", "Room Browser Button", "Quick Join Button", "Settings Button", "Quit Button" };

    private static readonly Color BarWhite = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
    private static readonly Color VcrBlue = new Color32(0x1A, 0x2A, 0x9E, 0xFF);

    [MenuItem(MenuPath, false, 37)]
    private static void Style()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Main Menu Buttons", "Stop Play mode first.", "OK");
            return;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainMenuScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        }

        var log = new List<string>();
        foreach (string name in Buttons)
        {
            GameObject button = GameObject.Find(PanelPath + name);
            if (button == null) { log.Add("- " + name + ": not found, skipped"); continue; }
            log.Add(StyleButton(button));
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[MainMenuButtonStyle] Done. Save the scene to keep the scene part.\n" + string.Join("\n", log));
    }

    private static string StyleButton(GameObject button)
    {
        var animator = button.GetComponent<Animator>();
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null) return "- " + button.name + ": no Animator controller, skipped";

        AnimationClip normal = null, highlighted = null;
        foreach (AnimationClip c in controller.animationClips)
        {
            if (c == null) continue;
            if (c.name == "Normal") normal = c;
            else if (c.name == "Highlighted") highlighted = c;
        }
        if (normal == null || highlighted == null) return "- " + button.name + ": Normal/Highlighted clip missing, skipped";

        // The label the clips already animate (m_fontSize on "Text (TMP)").
        EditorCurveBinding sizeBinding = default;
        bool found = false;
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(highlighted))
            if (b.propertyName == "m_fontSize") { sizeBinding = b; found = true; break; }
        if (!found) return "- " + button.name + ": no font size curve, skipped";

        Transform labelT = button.transform.Find(sizeBinding.path);
        TMP_Text label = labelT != null ? labelT.GetComponent<TMP_Text>() : null;
        if (label == null) return "- " + button.name + ": label '" + sizeBinding.path + "' not found, skipped";

        AnimationClip pressed = null;
        foreach (AnimationClip c in controller.animationClips)
            if (c != null && c.name == "Pressed") pressed = c;

        // The bar the clips fade in (m_Color.a on an Image) and the '>' arrow (m_fontColor.a on another TMP).
        Image bar = null;
        EditorCurveBinding barBinding = default;
        TMP_Text arrow = null;
        string arrowPath = null;
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(highlighted))
        {
            if (b.path.EndsWith(BlockName)) continue;
            Transform t = button.transform.Find(b.path);
            if (t == null) continue;
            if (b.propertyName == "m_Color.a" && b.type == typeof(Image)) { bar = t.GetComponent<Image>(); barBinding = b; }
            else if (b.propertyName == "m_fontColor.a" && b.path != sizeBinding.path) { arrow = t.GetComponent<TMP_Text>(); arrowPath = b.path; }
        }

        // ---- '>' becomes a white block -------------------------------------------
        Image block = arrow != null ? MakeBlock(arrow) : null;

        // ---- Scene: size 38, bar white (its alpha stays animated) -----------------
        Color normalText = label.color;
        if (IsBlue(normalText)) normalText = Color.white; // ran before: never keep blue as "normal"
        Undo.RecordObject(label, "Style Main Menu Buttons");
        label.fontSize = Size;
        label.color = normalText;
        EditorUtility.SetDirty(label);
        if (bar != null)
        {
            Undo.RecordObject(bar, "Style Main Menu Buttons");
            bar.color = new Color(BarWhite.r, BarWhite.g, BarWhite.b, 0f); // hidden until hover
            EditorUtility.SetDirty(bar);
        }

        // ---- Clips ----------------------------------------------------------------
        SetSize(normal, sizeBinding);
        SetSize(highlighted, sizeBinding);
        SetColor(normal, sizeBinding, normalText);
        SetColor(highlighted, sizeBinding, VcrBlue);
        EditorCurveBinding blockBinding = default;
        if (block != null)
        {
            blockBinding = EditorCurveBinding.FloatCurve(arrowPath + "/" + BlockName, typeof(Image), "m_Color.a");
            SetCurve(normal, blockBinding, 0f);
            SetCurve(highlighted, blockBinding, 1f);
        }
        if (pressed != null) // look the same as hover while the mouse is down
        {
            SetSize(pressed, sizeBinding);
            SetColor(pressed, sizeBinding, VcrBlue);
            if (bar != null) SetCurve(pressed, barBinding, 1f);
            if (block != null) SetCurve(pressed, blockBinding, 1f);
        }

        return "- " + button.name + ": size " + Size + ", hover = white bar + blue text + white block" +
               (bar == null ? " (no bar found)" : "") + (block == null ? " (no '>' found)" : "");
    }

    private const string BlockName = "Cursor Block";
    private const string FitPath = "Tools/Pre-Thesis/Scenes/Menu: Fit Cursor Blocks To Highlight";

    /// Every button's white block: as tall as its highlight bar, and with the same width and
    /// gap to the bar as the Create Room (PLAY) block has now. Tune PLAY's block by hand,
    /// then run this to copy it to the others.
    [MenuItem(FitPath, false, 38)]
    private static void FitBlocks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Cursor Blocks", "Stop Play mode first.", "OK");
            return;
        }
        if (!GetBarAndBlock(Buttons[0], out Transform tButton, out RectTransform tBar, out RectTransform tBlock))
        {
            EditorUtility.DisplayDialog("Cursor Blocks", "Create Room Button needs a bar and a '" + BlockName +
                "'. Run 'Menu: Style Main Menu Buttons (VCR)' first.", "OK");
            return;
        }
        Bounds(tButton, tBar, out Vector2 barMin, out _);
        Bounds(tButton, tBlock, out Vector2 blockMin, out Vector2 blockMax);
        float gap = barMin.x - blockMax.x;
        float width = blockMax.x - blockMin.x;

        int done = 0;
        foreach (string name in Buttons)
        {
            if (!GetBarAndBlock(name, out Transform button, out RectTransform bar, out RectTransform block)) continue;
            Bounds(button, bar, out Vector2 bMin, out Vector2 bMax);
            // Wanted rect, in the button's own space.
            Vector3 lo = new Vector3(bMin.x - gap - width, bMin.y, 0f);
            Vector3 hi = new Vector3(bMin.x - gap, bMax.y, 0f);

            // Into the block's parent space.
            Transform parent = block.parent;
            Vector3 pLo = parent.InverseTransformPoint(button.TransformPoint(lo));
            Vector3 pHi = parent.InverseTransformPoint(button.TransformPoint(hi));

            Undo.RecordObject(block, "Fit Cursor Blocks");
            block.localScale = Vector3.one;
            block.localRotation = Quaternion.identity;
            block.pivot = new Vector2(0.5f, 0.5f);
            block.sizeDelta = new Vector2(Mathf.Abs(pHi.x - pLo.x), Mathf.Abs(pHi.y - pLo.y));
            Vector3 c = (pLo + pHi) * 0.5f;
            block.localPosition = new Vector3(c.x, c.y, 0f);
            EditorUtility.SetDirty(block);
            done++;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[MainMenuButtonStyle] " + done + " cursor block(s): as tall as the highlight, width " +
                  width.ToString("0.#") + " and gap " + gap.ToString("0.#") + " (copied from PLAY). Save the scene to keep it.");
    }

    private static bool GetBarAndBlock(string name, out Transform button, out RectTransform bar, out RectTransform block)
    {
        bar = block = null;
        GameObject go = GameObject.Find(PanelPath + name);
        button = go != null ? go.transform : null;
        if (button == null) return false;
        foreach (RectTransform rt in button.GetComponentsInChildren<RectTransform>(true))
            if (rt.name == BlockName) block = rt;

        var animator = button.GetComponent<Animator>();
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller != null)
            foreach (AnimationClip c in controller.animationClips)
            {
                if (c == null || c.name != "Highlighted") continue;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(c))
                {
                    if (b.propertyName != "m_Color.a" || b.type != typeof(Image) || b.path.EndsWith(BlockName)) continue;
                    var t = button.Find(b.path) as RectTransform;
                    if (t != null) bar = t;
                }
            }
        return bar != null && block != null;
    }

    /// A rect's corners in another transform's space (min / max).
    private static void Bounds(Transform space, RectTransform rt, out Vector2 min, out Vector2 max)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 w in corners)
        {
            Vector3 p = space.InverseTransformPoint(w);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
    }

    /// '>' text is cleared and a white block (hidden, alpha 0) is put where the glyph was.
    private static Image MakeBlock(TMP_Text arrow)
    {
        Transform existing = arrow.transform.Find(BlockName);
        Image block = existing != null ? existing.GetComponent<Image>() : null;

        if (block == null)
        {
            // Size and place it from the '>' glyph before the text is cleared.
            Vector2 center = Vector2.zero, size = new Vector2(22f, 30f);
            arrow.ForceMeshUpdate();
            if (arrow.textInfo != null && arrow.textInfo.characterCount > 0)
            {
                TMP_CharacterInfo ci = arrow.textInfo.characterInfo[0];
                float h = Mathf.Max(8f, ci.topRight.y - ci.bottomLeft.y);
                size = new Vector2(Mathf.Round(h * 0.75f), Mathf.Round(h));
                center = new Vector2((ci.bottomLeft.x + ci.topRight.x) * 0.5f, (ci.bottomLeft.y + ci.topRight.y) * 0.5f);
            }

            var go = new GameObject(BlockName, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Style Main Menu Buttons");
            go.layer = arrow.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(arrow.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Glyph coords are relative to the text's pivot; convert to its centre.
            var art = (RectTransform)arrow.transform;
            Vector2 pivotToCenter = (new Vector2(0.5f, 0.5f) - art.pivot) * art.rect.size;
            rt.anchoredPosition = center - pivotToCenter;
            rt.sizeDelta = size;
            block = go.AddComponent<Image>();
            block.raycastTarget = false;
        }

        Undo.RecordObject(block, "Style Main Menu Buttons");
        block.color = new Color(BarWhite.r, BarWhite.g, BarWhite.b, 0f); // hidden until hover
        EditorUtility.SetDirty(block);

        Undo.RecordObject(arrow, "Style Main Menu Buttons");
        arrow.text = string.Empty;
        EditorUtility.SetDirty(arrow);
        return block;
    }

    private static void SetCurve(AnimationClip clip, EditorCurveBinding binding, float value)
    {
        Undo.RecordObject(clip, "Style Main Menu Buttons");
        AnimationUtility.SetEditorCurve(clip, binding, Constant(value));
        EditorUtility.SetDirty(clip);
    }

    private static void SetSize(AnimationClip clip, EditorCurveBinding sizeBinding)
    {
        Undo.RecordObject(clip, "Style Main Menu Buttons");
        foreach (string prop in new[] { "m_fontSize", "m_fontSizeBase" })
        {
            EditorCurveBinding b = sizeBinding;
            b.propertyName = prop;
            AnimationUtility.SetEditorCurve(clip, b, Constant(Size));
        }
        EditorUtility.SetDirty(clip);
    }

    private static void SetColor(AnimationClip clip, EditorCurveBinding sizeBinding, Color color)
    {
        Undo.RecordObject(clip, "Style Main Menu Buttons");
        string[] props = { "m_fontColor.r", "m_fontColor.g", "m_fontColor.b", "m_fontColor.a" };
        float[] values = { color.r, color.g, color.b, color.a };
        for (int i = 0; i < props.Length; i++)
        {
            EditorCurveBinding b = sizeBinding; // same object + component type as the size curve
            b.propertyName = props[i];
            AnimationUtility.SetEditorCurve(clip, b, Constant(values[i]));
        }
        EditorUtility.SetDirty(clip);
    }

    private static AnimationCurve Constant(float v) => new AnimationCurve(new Keyframe(0f, v));

    private static bool IsBlue(Color c) =>
        Mathf.Abs(c.r - VcrBlue.r) < 0.02f && Mathf.Abs(c.g - VcrBlue.g) < 0.02f && Mathf.Abs(c.b - VcrBlue.b) < 0.02f;
}
