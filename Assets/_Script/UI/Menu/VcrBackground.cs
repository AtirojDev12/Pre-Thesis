using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main menu background, design "A — VCR Play Screen": the blue VCR screen
/// with a tracking band rolling down and moving grain. Scanlines / vignette come
/// from the game's Retro Horror Filter (its canvas renders through the camera).
///
/// Built and wired by Tools > Pre-Thesis > Scenes > Menu: Add VCR Background (A).
/// It sits on its own canvas ("VCR Background") under the Main Menu UI, so it
/// never touches the menu itself. Turn the GameObject off to get the old look back.
/// The Scanlines slot is optional (empty = not used).
///
/// Every layer is a plain Image / RawImage, so colors and strengths can be
/// changed in the Inspector. This script only animates them.
/// </summary>
[DisallowMultipleComponent]
public class VcrBackground : MonoBehaviour
{
    [Header("Layers (wired by the setup tool)")]
    [SerializeField] private RectTransform area;
    [SerializeField] private RectTransform trackingBand;
    [SerializeField] private RawImage scanlines;
    [SerializeField] private RawImage noise;

    [Header("Tracking band")]
    [Tooltip("Seconds for the band to roll from top to bottom.")]
    [SerializeField] private float trackingSeconds = 7f;

    [Header("Scanlines")]
    [Tooltip("Height of one dark + one clear line, in canvas units (1080 high).")]
    [SerializeField] private float scanlinePeriod = 4f;

    [Header("Grain")]
    [Tooltip("Grain pixel size in canvas units.")]
    [SerializeField] private float grainSize = 2f;
    [Tooltip("How many times per second the grain jumps.")]
    [SerializeField] private float grainFps = 24f;

    private float trackT;
    private float grainTimer;
    private Vector2 lastSize;

    private void OnEnable()
    {
        trackT = Random.value;
        lastSize = Vector2.zero;
    }

    private void LateUpdate()
    {
        if (area == null) return;
        Vector2 size = area.rect.size;
        if (size.x < 1f || size.y < 1f) return; // canvas not laid out yet
        if (size != lastSize)
        {
            lastSize = size;
            FitTiling(size);
        }

        float dt = Time.unscaledDeltaTime; // keeps running if timeScale is 0

        // Tracking band: from just above the top to just below the bottom.
        if (trackingBand != null && trackingSeconds > 0.01f)
        {
            trackT += dt / trackingSeconds;
            if (trackT > 1f) trackT -= 1f;
            float h = trackingBand.rect.height;
            float y = Mathf.Lerp(h, -size.y - h, trackT);
            trackingBand.anchoredPosition = new Vector2(0f, y); // straight down, no sideways shake
        }

        // Grain: jump to a random spot a few times a second, like tape noise.
        if (noise != null && grainFps > 0f)
        {
            grainTimer += dt;
            if (grainTimer >= 1f / grainFps)
            {
                grainTimer = 0f;
                Rect r = noise.uvRect;
                r.x = Random.value;
                r.y = Random.value;
                noise.uvRect = r;
            }
        }
    }

    private void FitTiling(Vector2 size)
    {
        if (scanlines != null && scanlinePeriod > 0.01f)
            scanlines.uvRect = new Rect(0f, 0f, 1f, size.y / scanlinePeriod);

        if (noise != null && noise.texture != null && grainSize > 0.01f)
        {
            Rect r = noise.uvRect;
            r.width = size.x / (noise.texture.width * grainSize);
            r.height = size.y / (noise.texture.height * grainSize);
            noise.uvRect = r;
        }
    }

    // Called by the setup tool after wiring.
    public void Setup(RectTransform areaRect, RectTransform band, RawImage lines, RawImage grain)
    {
        area = areaRect;
        trackingBand = band;
        scanlines = lines;
        noise = grain;
        if (area != null && area.rect.width > 1f && area.rect.height > 1f) FitTiling(area.rect.size);
    }
}
