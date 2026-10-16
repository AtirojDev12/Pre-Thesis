using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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

    private void Update()
    {
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
