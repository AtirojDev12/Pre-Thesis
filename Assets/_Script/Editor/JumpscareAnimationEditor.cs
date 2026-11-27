using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 27 Nov 2026: Inspector for JumpscareAnimation.
///   - Clip + "Make clip from frames": builds / updates an Animation Clip (Image sprite)
///     from the Frames list and its timing.
///   - Frames folder + "Load frames": fills the list from a folder in one click
///     (name order, and switches the pictures to Sprite if they are not yet).
///   - Frame list with a thumbnail, drag to reorder, own time per frame.
///   - Preview: Play button and a time slider (plays the Clip if set, else the list).
/// </summary>
[CustomEditor(typeof(JumpscareAnimation))]
public sealed class JumpscareAnimationEditor : Editor
{
    private ReorderableList list;
    private DefaultAsset folder;
    private float previewTime;
    private bool previewPlaying;
    private double previewStart;
    private bool pendingLoad;
    private bool pendingResetTimes;
    private bool pendingMakeClip;

    private void OnEnable()
    {
        list = new ReorderableList(serializedObject, serializedObject.FindProperty("frames"), true, true, true, true)
        {
            elementHeight = 46f,
            drawHeaderCallback = r => EditorGUI.LabelField(r, "Frames (top plays first)   Seconds 0 = auto from Frames Per Second"),
            drawElementCallback = DrawFrame,
        };
        EditorApplication.update += Tick;
    }

    private void OnDisable() => EditorApplication.update -= Tick;

    private void Tick()
    {
        if (!previewPlaying || target == null) return;
        var anim = (JumpscareAnimation)target;
        previewTime = (float)(EditorApplication.timeSinceStartup - previewStart);
        if (previewTime >= anim.TotalSeconds)
        {
            previewTime = anim.TotalSeconds;
            previewPlaying = false;
        }
        Repaint();
    }

    public override void OnInspectorGUI()
    {
        var anim = (JumpscareAnimation)target;
        serializedObject.Update();

        string source = anim.clip != null
            ? $"Plays the Animation Clip \"{anim.clip.name}\" ({anim.clip.length:0.00} s)"
            : $"Plays the Frames list ({anim.FrameCount} frames {anim.FramesSeconds:0.00} s + hold {anim.holdLastFrameSeconds:0.00} s)";
        EditorGUILayout.HelpBox(
            $"{source} + fade {anim.fadeOutSeconds:0.00} s = {anim.TotalSeconds:0.00} s total.\n" +
            "To use it: drag this file into a Rule Ghost's \"Jumpscare Animation\" slot (and tick Jumpscare On Touch).",
            MessageType.Info);

        // ---- Clip ----
        EditorGUILayout.LabelField("Animation Clip", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("clip"));
        using (new EditorGUI.DisabledScope(!anim.HasFrames))
            if (GUILayout.Button(anim.clip != null ? "Update clip from frames (overwrites its keys)" : "Make clip from frames"))
                pendingMakeClip = true;

        // ---- Quick fill ----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Frames", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            folder = (DefaultAsset)EditorGUILayout.ObjectField(
                new GUIContent("Frames folder", "Drag a folder of frame pictures here from the Project window."),
                folder, typeof(DefaultAsset), false);
            using (new EditorGUI.DisabledScope(folder == null))
                if (GUILayout.Button("Load frames", GUILayout.Width(95))) pendingLoad = true;
        }
        if (GUILayout.Button("Set every frame's Seconds to auto (0)")) pendingResetTimes = true;

        list.DoLayoutList();
        DrawPropertiesExcluding(serializedObject, "m_Script", "frames", "clip");
        serializedObject.ApplyModifiedProperties();

        DrawPreview(anim);

        // Changes that write the asset directly run after ApplyModifiedProperties,
        // otherwise the serialized copy above would overwrite them.
        if (pendingLoad)
        {
            pendingLoad = false;
            LoadFolder(anim);
            serializedObject.Update();
            GUIUtility.ExitGUI();
        }
        if (pendingMakeClip)
        {
            pendingMakeClip = false;
            MakeClip(anim);
            serializedObject.Update();
            GUIUtility.ExitGUI();
        }
        if (pendingResetTimes)
        {
            pendingResetTimes = false;
            Undo.RecordObject(anim, "Reset frame times");
            for (int i = 0; i < anim.FrameCount; i++)
            {
                JumpscareAnimation.Frame f = anim.frames[i];
                f.seconds = 0f;
                anim.frames[i] = f;
            }
            EditorUtility.SetDirty(anim);
            serializedObject.Update();
        }
    }

    // ---- Frame row ----------------------------------------------------------------

    private void DrawFrame(Rect rect, int index, bool active, bool focused)
    {
        SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
        SerializedProperty sprite = element.FindPropertyRelative("sprite");
        SerializedProperty seconds = element.FindPropertyRelative("seconds");

        rect.y += 3f;
        float h = rect.height - 6f;
        var thumb = new Rect(rect.x, rect.y, h * 16f / 9f, h);
        var s = sprite.objectReferenceValue as Sprite;
        EditorGUI.DrawRect(thumb, new Color(0f, 0f, 0f, 0.35f));
        if (s != null && s.texture != null) GUI.DrawTexture(thumb, s.texture, ScaleMode.ScaleToFit);

        float x = thumb.xMax + 6f;
        float w = rect.xMax - x;
        EditorGUI.LabelField(new Rect(x, rect.y, 34f, 18f), "#" + (index + 1), EditorStyles.boldLabel);
        EditorGUI.PropertyField(new Rect(x + 34f, rect.y, w - 34f, 18f), sprite, GUIContent.none);

        float y2 = rect.y + 21f;
        EditorGUI.LabelField(new Rect(x, y2, 56f, 18f), "Seconds");
        EditorGUI.PropertyField(new Rect(x + 56f, y2, 55f, 18f), seconds, GUIContent.none);
        if (seconds.floatValue <= 0f)
        {
            float fps = serializedObject.FindProperty("framesPerSecond").floatValue;
            EditorGUI.LabelField(new Rect(x + 116f, y2, w - 116f, 18f),
                $"auto ({1f / Mathf.Max(1f, fps):0.000} s)", EditorStyles.miniLabel);
        }
    }

    // ---- Preview ------------------------------------------------------------------

    private void DrawPreview(JumpscareAnimation anim)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(anim.clip != null ? "Preview (Animation Clip)" : "Preview (Frames list)", EditorStyles.boldLabel);
        float total = Mathf.Max(0.01f, anim.TotalSeconds);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(previewPlaying ? "Stop" : "Play", GUILayout.Width(60)))
            {
                previewPlaying = !previewPlaying;
                if (previewPlaying) previewStart = EditorApplication.timeSinceStartup;
            }
            EditorGUI.BeginChangeCheck();
            previewTime = EditorGUILayout.Slider(Mathf.Min(previewTime, total), 0f, total);
            if (EditorGUI.EndChangeCheck()) previewPlaying = false;
        }

        float animTime = Mathf.Min(previewTime, anim.PlaySeconds);
        Sprite sprite;
        string frameText;
        if (anim.clip != null)
        {
            ObjectReferenceKeyframe[] keys = SpriteKeys(anim.clip);
            int k = KeyAt(keys, animTime);
            sprite = k >= 0 ? keys[k].value as Sprite : null;
            frameText = keys.Length > 0 ? $"Key {k + 1} / {keys.Length}" : "This clip has no sprite keys (press Update clip from frames)";
        }
        else
        {
            int index = anim.FrameIndexAt(animTime);
            sprite = index >= 0 ? anim.SpriteAt(index) : null;
            frameText = index >= 0 ? $"Frame {index + 1} / {anim.FrameCount}" : "No frames yet";
        }

        Rect box = GUILayoutUtility.GetAspectRect(16f / 9f, GUILayout.MaxWidth(520f));
        EditorGUI.DrawRect(box, anim.backgroundColor);

        float fadeStart = anim.TotalSeconds - anim.fadeOutSeconds;
        float alpha = anim.fadeOutSeconds > 0f && previewTime > fadeStart
            ? Mathf.Clamp01((anim.TotalSeconds - previewTime) / anim.fadeOutSeconds)
            : 1f;
        bool shaking = anim.shakePixels > 0f && previewTime >= anim.shakeFromSeconds;
        Vector2 offset = shaking && previewPlaying ? Random.insideUnitCircle * anim.shakePixels * (box.width / 1920f) : Vector2.zero;

        if (sprite != null && sprite.texture != null)
        {
            ScaleMode mode = anim.fit == JumpscareAnimation.Fit.Stretch ? ScaleMode.StretchToFill
                : anim.fit == JumpscareAnimation.Fit.FitInside ? ScaleMode.ScaleToFit
                : ScaleMode.ScaleAndCrop;
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.BeginClip(box);
            GUI.DrawTexture(new Rect(offset.x, offset.y, box.width, box.height), sprite.texture, mode);
            GUI.EndClip();
            GUI.color = old;
        }
        else
        {
            GUI.Label(box, "Nothing to show", EditorStyles.centeredGreyMiniLabel);
        }

        string info = $"{frameText}   {previewTime:0.00} s";
        if (shaking) info += "   [shake]";
        if (anim.sound == null) info += "   (no sound set)";
        else if (previewTime >= anim.soundAtSeconds) info += "   [sound playing]";
        EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
    }

    private static readonly EditorCurveBinding SpriteBinding = EditorCurveBinding.PPtrCurve("", typeof(Image), "m_Sprite");

    private static ObjectReferenceKeyframe[] SpriteKeys(AnimationClip clip)
    {
        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, SpriteBinding);
        if (keys != null && keys.Length > 0) return keys;
        // A clip made by hand may animate the sprite on another path: use the first sprite curve found.
        foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            if (b.propertyName == "m_Sprite") return AnimationUtility.GetObjectReferenceCurve(clip, b) ?? new ObjectReferenceKeyframe[0];
        return new ObjectReferenceKeyframe[0];
    }

    private static int KeyAt(ObjectReferenceKeyframe[] keys, float time)
    {
        int found = keys.Length > 0 ? 0 : -1;
        for (int i = 0; i < keys.Length; i++) if (keys[i].time <= time + 0.0001f) found = i;
        return found;
    }

    // ---- Make the clip ---------------------------------------------------------------

    private static void MakeClip(JumpscareAnimation anim)
    {
        if (!anim.HasFrames) return;

        string clipPath = anim.clip != null
            ? AssetDatabase.GetAssetPath(anim.clip)
            : Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(anim)), anim.name + ".anim").Replace('\\', '/');
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        bool isNew = clip == null;
        if (isNew) clip = new AnimationClip { frameRate = 60f };
        else Undo.RecordObject(clip, "Update jumpscare clip");

        var keys = new List<ObjectReferenceKeyframe>();
        float t = 0f;
        for (int i = 0; i < anim.FrameCount; i++)
        {
            Sprite s = anim.frames[i].sprite;
            if (s != null) keys.Add(new ObjectReferenceKeyframe { time = t, value = s });
            t += anim.FrameSeconds(i);
        }
        // One more key at the end: the last frame stays for its own time + the hold.
        keys.Add(new ObjectReferenceKeyframe { time = t + anim.holdLastFrameSeconds, value = anim.SpriteAt(anim.FrameCount - 1) });

        AnimationUtility.SetObjectReferenceCurve(clip, SpriteBinding, keys.ToArray());
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        if (isNew) AssetDatabase.CreateAsset(clip, clipPath);
        else EditorUtility.SetDirty(clip);

        Undo.RecordObject(anim, "Set jumpscare clip");
        anim.clip = clip;
        EditorUtility.SetDirty(anim);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Jumpscare Animation] {(isNew ? "Made" : "Updated")} {clipPath}: {keys.Count - 1} frames, {clip.length:0.00} s.", clip);
    }

    // ---- Load a folder --------------------------------------------------------------

    private void LoadFolder(JumpscareAnimation anim)
    {
        string path = AssetDatabase.GetAssetPath(folder);
        if (!AssetDatabase.IsValidFolder(path))
        {
            EditorUtility.DisplayDialog("Jumpscare Animation", "That is not a folder. Drag a folder from the Project window.", "OK");
            return;
        }

        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(p).Replace('\\', '/') == path) paths.Add(p); // this folder only, not sub-folders
        }
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("Jumpscare Animation", $"No pictures found in {path}.", "OK");
            return;
        }
        paths.Sort((a, b) => EditorUtility.NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));

        if (anim.FrameCount > 0 && !EditorUtility.DisplayDialog("Jumpscare Animation",
                $"Replace the {anim.FrameCount} frames with the {paths.Count} pictures in {path}?", "Replace", "Cancel"))
            return;

        int converted = 0;
        foreach (string p in paths)
        {
            if (AssetImporter.GetAtPath(p) is TextureImporter importer && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
                converted++;
            }
        }

        Undo.RecordObject(anim, "Load jumpscare frames");
        anim.frames = new List<JumpscareAnimation.Frame>();
        foreach (string p in paths)
            anim.frames.Add(new JumpscareAnimation.Frame { sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p), seconds = 0f });
        EditorUtility.SetDirty(anim);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Jumpscare Animation] {anim.name}: loaded {paths.Count} frames from {path}" +
                  (converted > 0 ? $" ({converted} pictures switched to Sprite). Press 'Make clip from frames' next." : ". Press 'Make clip from frames' next."), anim);
    }
}
