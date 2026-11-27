using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

/// <summary>
/// 16 Oct 2026: LOCAL helpers shared by the zone rules. Nothing here changes
/// game state; it only shows things on THIS PC.
///   - ShowJumpscare: the same tagged "JumpscareUI" overlay the ghosts use.
///   - Toast: a short line of text (rule broken / hint). Can be turned off per rule.
///   - IsLookingAt: "is my camera looking at this point, with nothing in between?"
///   - KeepOutOfMyWay: a look-target collider never blocks the local player's body.
///   - PlaceholderClip: a generated sound so mockups make noise before real audio exists.
/// </summary>
public sealed class RuleFx : MonoBehaviour
{
    private static RuleFx instance;
    private GameObject jumpscareUI;
    private Coroutine hideJumpscare;
    private TMP_Text toastText;
    private CanvasGroup toastGroup;
    private float toastUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        clips.Clear();
    }

    private static RuleFx Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("Rule FX (local)");
                instance = go.AddComponent<RuleFx>();
            }
            return instance;
        }
    }

    // ---- Jumpscare ------------------------------------------------------------

    /// <summary>LOCAL. Shows the jumpscare overlay on this screen for a few seconds.</summary>
    public static void ShowJumpscare(float seconds)
    {
        RuleFx fx = Instance;
        PlayerGhostWarningController.SuppressLocal(seconds);
        if (fx.jumpscareUI == null) fx.jumpscareUI = FindJumpscareUI();
        if (fx.jumpscareUI == null)
        {
            Debug.LogWarning("[RuleFx] No loaded object tagged JumpscareUI; showing a text scare instead.");
            Toast("!!!", seconds);
            return;
        }
        fx.jumpscareUI.SetActive(true);
        if (fx.hideJumpscare != null) fx.StopCoroutine(fx.hideJumpscare);
        fx.hideJumpscare = fx.StartCoroutine(fx.HideJumpscareLater(seconds));
    }

    /// <summary>
    /// LOCAL. 27 Nov 2026: plays a frame-by-frame jumpscare (JumpscareAnimation) on this
    /// screen. No animation (or no clip / frames in it) = the old JumpUi picture for
    /// <paramref name="fallbackSeconds"/>.
    /// </summary>
    public static void ShowJumpscare(JumpscareAnimation animation, float fallbackSeconds)
    {
        if (animation == null || !animation.CanPlay)
        {
            ShowJumpscare(fallbackSeconds);
            return;
        }
        Instance.PlayAnimation(animation);
    }

    private IEnumerator HideJumpscareLater(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (jumpscareUI != null) jumpscareUI.SetActive(false);
        hideJumpscare = null;
    }

    private static GameObject FindJumpscareUI()
    {
        GameObject fallback = null;
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (!obj.scene.IsValid() || !obj.scene.isLoaded) continue;
            try { if (!obj.CompareTag("JumpscareUI")) continue; }
            catch (UnityException) { return null; } // the tag does not exist in this project
            if (obj.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) return obj;
            if (fallback == null) fallback = obj;
        }
        return fallback;
    }

    // ---- Toast ----------------------------------------------------------------

    /// <summary>LOCAL. One short line in the middle-top of the screen.</summary>
    public static void Toast(string text, float seconds = 3f)
    {
        if (string.IsNullOrEmpty(text)) return;
        RuleFx fx = Instance;
        if (fx.toastText == null) fx.BuildToast();
        fx.toastText.text = text;
        fx.toastUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
        fx.toastGroup.alpha = 1f;
    }

    private void BuildToast()
    {
        var root = new GameObject("Rule Toast", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 52;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        toastGroup = root.GetComponent<CanvasGroup>();
        toastGroup.blocksRaycasts = false;
        toastGroup.interactable = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(root.transform, false);
        RectTransform rt = (RectTransform)textGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -190f);
        rt.sizeDelta = new Vector2(1200f, 80f);
        toastText = textGo.AddComponent<TextMeshProUGUI>();
        toastText.fontSize = 30f;
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.color = new Color(0.95f, 0.35f, 0.3f);
        toastText.raycastTarget = false;
        toastText.textWrappingMode = TextWrappingModes.Normal;
    }

    // ---- Animated jumpscare (27 Nov 2026) -----------------------------------------
    //
    // Its own full-screen canvas (built on first use), so it does not depend on the
    // GAME HUD being loaded and never changes the shared JumpUi picture.
    // A Clip is played through a PlayableGraph on an Animator on the "Frame" Image,
    // stepped by hand with unscaled time (so pause / time scale never freeze it).

    private const int AnimationSortingOrder = 400; // above the HUD (its highest canvas is 300)
    private GameObject animRoot;
    private CanvasGroup animGroup;
    private Image animBackground;
    private RectTransform animFrameRect;
    private Image animFrame;
    private AspectRatioFitter animFitter;
    private Animator animAnimator;
    private PlayableGraph animGraph;
    private AnimationClipPlayable animClipPlayable;
    private JumpscareAnimation playing;
    private float playStart;
    private Sprite fittedSprite;
    private bool soundPlayed;

    private void PlayAnimation(JumpscareAnimation animation)
    {
        if (animRoot == null) BuildAnimation();
        PlayerGhostWarningController.SuppressLocal(animation.TotalSeconds);
        DestroyGraph();
        playing = animation;
        playStart = Time.unscaledTime;
        fittedSprite = null;
        soundPlayed = false;

        animFrame.sprite = null;
        animBackground.color = animation.backgroundColor;
        animGroup.alpha = 1f;
        animRoot.SetActive(true);

        if (animation.clip != null)
        {
            animGraph = PlayableGraph.Create("Jumpscare Animation");
            animGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            animClipPlayable = AnimationClipPlayable.Create(animGraph, animation.clip);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(animGraph, "Jumpscare", animAnimator);
            output.SetSourcePlayable(animClipPlayable);
            animGraph.Play();
        }
        UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        JumpscareAnimation a = playing;
        float t = Time.unscaledTime - playStart;
        if (a == null || t >= a.TotalSeconds) { StopAnimation(); return; }
        float animTime = Mathf.Min(t, a.PlaySeconds); // during the fade: stay on the last frame

        if (animGraph.IsValid())
        {
            animClipPlayable.SetTime(animTime);
            animGraph.Evaluate(); // the clip writes animFrame.sprite
        }
        else
        {
            Sprite sprite = a.SpriteAt(a.FrameIndexAt(animTime));
            if (sprite != null) animFrame.sprite = sprite;
        }

        if (animFrame.sprite != fittedSprite)
        {
            fittedSprite = animFrame.sprite;
            if (fittedSprite != null) ApplyFit(a.fit, fittedSprite);
        }
        animFrame.enabled = animFrame.sprite != null; // an empty Image would be a white box

        if (!soundPlayed && t >= a.soundAtSeconds)
        {
            soundPlayed = true;
            if (a.sound != null && AudioManager.Instance != null)
                AudioManager.Instance.PlayClip(a.sound, SoundCategory.Sfx, a.volume, Vector3.zero, false, 1f, 2f, default);
        }

        bool shake = a.shakePixels > 0f && t >= a.shakeFromSeconds;
        animFrameRect.anchoredPosition = shake ? Random.insideUnitCircle * a.shakePixels : Vector2.zero;
        // A little bigger while shaking so the moving picture never shows its edges.
        float grow = shake && a.fit == JumpscareAnimation.Fit.FillScreen ? 1f + a.shakePixels * 2f / 1080f : 1f;
        animFrameRect.localScale = new Vector3(grow, grow, 1f);

        float fadeStart = a.TotalSeconds - a.fadeOutSeconds;
        animGroup.alpha = a.fadeOutSeconds > 0f && t > fadeStart
            ? Mathf.Clamp01((a.TotalSeconds - t) / a.fadeOutSeconds)
            : 1f;
    }

    private void StopAnimation()
    {
        playing = null;
        DestroyGraph();
        if (animRoot != null) animRoot.SetActive(false);
        if (animFrame != null) animFrame.sprite = null;
    }

    private void DestroyGraph()
    {
        if (animGraph.IsValid()) animGraph.Destroy();
    }

    private void OnDestroy() => DestroyGraph();

    private void ApplyFit(JumpscareAnimation.Fit fit, Sprite sprite)
    {
        if (fit == JumpscareAnimation.Fit.Stretch)
        {
            animFitter.enabled = false;
            animFrameRect.anchorMin = Vector2.zero;
            animFrameRect.anchorMax = Vector2.one;
            animFrameRect.sizeDelta = Vector2.zero;
            return;
        }
        animFitter.aspectRatio = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
        animFitter.aspectMode = fit == JumpscareAnimation.Fit.FitInside
            ? AspectRatioFitter.AspectMode.FitInParent
            : AspectRatioFitter.AspectMode.EnvelopeParent;
        animFitter.enabled = true;
    }

    private void BuildAnimation()
    {
        animRoot = new GameObject("Jumpscare Animation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        animRoot.transform.SetParent(transform, false);
        Canvas canvas = animRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = AnimationSortingOrder;
        CanvasScaler scaler = animRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        animGroup = animRoot.GetComponent<CanvasGroup>();
        animGroup.blocksRaycasts = false;
        animGroup.interactable = false;

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(animRoot.transform, false);
        RectTransform bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        animBackground = bg.GetComponent<Image>();
        animBackground.raycastTarget = false;

        var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter), typeof(Animator));
        frame.transform.SetParent(animRoot.transform, false);
        animFrameRect = (RectTransform)frame.transform;
        animFrame = frame.GetComponent<Image>();
        animFrame.raycastTarget = false;
        animFitter = frame.GetComponent<AspectRatioFitter>();
        animAnimator = frame.GetComponent<Animator>();
        animAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        animRoot.SetActive(false);
    }

    private void Update()
    {
        if (playing != null) UpdateAnimation();
        if (toastGroup == null || toastGroup.alpha <= 0f) return;
        float left = toastUntil - Time.unscaledTime;
        toastGroup.alpha = Mathf.Clamp01(left / 0.6f);
    }

    // ---- Looking ----------------------------------------------------------------

    /// <summary>
    /// LOCAL. Is <paramref name="eye"/> looking at <paramref name="point"/> (inside
    /// <paramref name="maxAngle"/> degrees, closer than <paramref name="maxDistance"/>)
    /// with no wall in between? Hits on <paramref name="targetRoot"/> or on the
    /// player's own body do not count as walls.
    /// </summary>
    public static bool IsLookingAt(Transform eye, Vector3 point, float maxAngle, float maxDistance,
        Transform targetRoot, Transform ownBody)
    {
        if (eye == null) return false;
        Vector3 to = point - eye.position;
        float distance = to.magnitude;
        if (distance < 0.05f || distance > maxDistance) return false;
        Vector3 dir = to / distance;
        if (Vector3.Angle(eye.forward, dir) > maxAngle) return false;

        RaycastHit[] hits = Physics.RaycastAll(eye.position, dir, distance - 0.1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (targetRoot != null && t.IsChildOf(targetRoot)) continue;
            if (ownBody != null && t.IsChildOf(ownBody)) continue;
            return false; // a wall, a seat back...
        }
        return true;
    }

    /// <summary>LOCAL. The camera of my own player, or null.</summary>
    public static Camera LocalCamera()
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        return me != null ? me.GetComponentInChildren<Camera>() : null;
    }

    // ---- Colliders ----------------------------------------------------------------

    /// <summary>
    /// LOCAL. Look-target colliders must be solid (the interaction ray ignores
    /// triggers) but must never stop my body walking. Movement is owner-side, so
    /// ignoring my own body is enough. Call again after a collider is re-enabled
    /// (Unity forgets the ignore pair when a collider turns off).
    /// </summary>
    public static void KeepOutOfMyWay(Collider[] own)
    {
        PlayerHealth me = PlayerHealth.LocalInstance;
        if (me == null || own == null) return;
        foreach (Collider body in me.GetComponentsInChildren<Collider>(true))
            foreach (Collider c in own)
                if (body != null && c != null && body.enabled && c.enabled) Physics.IgnoreCollision(c, body, true);
    }

    // ---- Mockup size (2 Nov, Mr.k: room ghosts as tall as a player) -------------------------

    /// <summary>
    /// Scales a mockup look (pivot at its feet / seat) so its top is <paramref name="height"/>
    /// metres above <paramref name="baseY"/>. Uses mesh bounds, so it works on hidden objects too.
    /// </summary>
    public static void ScaleToHeight(Transform look, float baseY, float height)
    {
        if (look == null || height <= 0f || !TopAbove(look, baseY, out float top)) return;
        look.localScale *= height / top;
    }

    private static bool TopAbove(Transform look, float baseY, out float top)
    {
        top = float.MinValue;
        bool any = false;
        foreach (MeshFilter mf in look.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;
            Bounds b = mesh.bounds;
            Matrix4x4 toWorld = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                float y = toWorld.MultiplyPoint3x4(corner).y;
                if (y > top) top = y;
                any = true;
            }
        }
        top -= baseY;
        return any && top > 0.05f;
    }

    // ---- Placeholder sound ------------------------------------------------------------

    public enum PlaceholderSound { Crying, Whisper, Hum }

    private static readonly Dictionary<PlaceholderSound, AudioClip> clips = new Dictionary<PlaceholderSound, AudioClip>();

    /// <summary>A generated looping clip for mockups (replace with real audio in the prefab).</summary>
    public static AudioClip PlaceholderClip(PlaceholderSound kind)
    {
        if (clips.TryGetValue(kind, out AudioClip existing) && existing != null) return existing;
        const int rate = 22050;
        int length = rate * 3;
        var data = new float[length];
        var random = new System.Random(7 + (int)kind);
        float phase = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float sample;
            switch (kind)
            {
                case PlaceholderSound.Crying:
                {
                    // Sobs: a wavering voice in short bursts.
                    float burst = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI / 0.55f));
                    float gate = (t % 1.5f) < 1.1f ? 1f : 0f;
                    float pitch = 300f + 60f * Mathf.Sin(t * 9f) - 40f * (t % 0.55f);
                    phase += 2f * Mathf.PI * pitch / rate;
                    sample = (Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2f) * 0.2f) * burst * burst * gate * 0.35f;
                    break;
                }
                case PlaceholderSound.Whisper:
                    sample = ((float)random.NextDouble() * 2f - 1f) * 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 5f));
                    break;
                default:
                    phase += 2f * Mathf.PI * 70f / rate;
                    sample = Mathf.Sin(phase) * 0.15f * (0.7f + 0.3f * Mathf.Sin(t * 1.3f));
                    break;
            }
            data[i] = sample;
        }
        AudioClip clip = AudioClip.Create("Placeholder " + kind, length, 1, rate, false);
        clip.SetData(data, 0);
        clips[kind] = clip;
        return clip;
    }
}
