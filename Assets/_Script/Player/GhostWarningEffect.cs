using UnityEngine;
using UnityEngine.UI;

/// <summary>Local screen overlay only. Never modifies a shared renderer or material.</summary>
[DisallowMultipleComponent]
public sealed class GhostWarningEffect : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float duration = 1f;
    [SerializeField, Min(0.01f)] private float attackTime = 0.15f;
    [SerializeField, Range(0f, 20f)] private float fovReduction = 6f;
    [SerializeField, Range(0f, 1f)] private float maximumDarkness = 0.65f;
    [Tooltip("How far the dark border extends toward the centre.")]
    [SerializeField, Range(0.05f, 0.8f)] private float borderWidth = 0.35f;
    [SerializeField, Range(0.1f, 4f)] private float softness = 1.5f;
    private RawImage image;
    private GameObject overlay;
    private Texture2D texture;
    private float startedAt;
    private bool playing;
    public bool IsPlaying => playing && Time.time - startedAt < duration;
    private float Strength
    {
        get
        {
            if (!IsPlaying) return 0f;
            float elapsed = Time.time - startedAt;
            float attack = Mathf.Clamp(attackTime, 0.01f, duration * 0.9f);
            float value = elapsed < attack ? elapsed / attack : 1f - (elapsed - attack) / (duration - attack);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
        }
    }
    public float FovReduction => Strength * fovReduction;
    public void Play()
    {
        if (IsPlaying) return;
        if (overlay == null) CreateOverlay();
        startedAt = Time.time;
        playing = true;
        overlay.SetActive(true);
    }
    private void LateUpdate()
    {
        if (!playing) return;
        if (!IsPlaying) { Cancel(); return; }
        image.color = new Color(1f, 1f, 1f, Strength * maximumDarkness);
    }
    public void Cancel()
    {
        playing = false;
        if (overlay != null) overlay.SetActive(false);
    }
    private void CreateOverlay()
    {
        overlay = new GameObject("Local Ghost Warning", typeof(RectTransform), typeof(Canvas));
        overlay.transform.SetParent(transform, false);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var border = new GameObject("Soft Black Border", typeof(RectTransform), typeof(RawImage));
        border.transform.SetParent(overlay.transform, false);
        image = border.GetComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.clear;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        RebuildTexture();
    }
    private void RebuildTexture()
    {
        if (texture != null) Destroy(texture);
        const int size = 256;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float nx = Mathf.Abs(2f * x / (size - 1) - 1f);
            float ny = Mathf.Abs(2f * y / (size - 1) - 1f);
            float edge = Mathf.Max(nx, ny);
            float alpha = Mathf.Pow(Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(1f - borderWidth, 1f, edge)), softness);
            pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        image.texture = texture;
    }
    private void OnValidate()
    {
        if (Application.isPlaying && image != null) RebuildTexture();
    }
    private void OnDisable() => Cancel();
    private void OnDestroy()
    {
        if (overlay != null) Destroy(overlay);
        if (texture != null) Destroy(texture);
    }
}
