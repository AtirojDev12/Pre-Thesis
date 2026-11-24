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
/// 24 Nov 2026: NOISE BURSTS (the rhythm of an old horror-game title screen). Most of the
/// time the grain is faint; then, at random moments, a short heavy burst of static
/// flickers on and off, the blue picture dips darker and the tracking band skips to a new
/// spot. Sometimes the burst comes twice in a row. Every number is in the Inspector.
/// Turn "Bursts" off for the old steady grain.
///
/// 24 Nov 2026: CLARITY CYCLE (separate from the bursts), over and over:
///   CLEAR (no grain at all, a few seconds)
///   -> GLITCH: static for only 1-2 frames while the picture jumps sideways (Jitter Targets)
///   -> AFTER: a little grain for a moment  -> quick fade back to CLEAR
/// The bursts still happen on top of it whenever they like. With the cycle on, "Calm Alpha"
/// is not used (the cycle decides the grain between bursts).
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

    [Header("Noise bursts (24 Nov)")]
    [Tooltip("Off = the old steady grain (Noise object as set in the scene).")]
    [SerializeField] private bool bursts = true;
    [Tooltip("Grain strength between bursts (alpha of the Noise layer).")]
    [SerializeField, Range(0f, 1f)] private float calmAlpha = 0.03f;
    [Tooltip("Grain strength during a burst: a random value between X and Y each time.")]
    [SerializeField] private Vector2 burstAlpha = new Vector2(0.35f, 0.65f);
    [Tooltip("Quiet time between bursts, in seconds: random between X and Y.")]
    [SerializeField] private Vector2 calmSeconds = new Vector2(1.5f, 5f);
    [Tooltip("Length of one burst, in seconds: random between X and Y.")]
    [SerializeField] private Vector2 burstSeconds = new Vector2(0.08f, 0.3f);
    [Tooltip("Chance (0-1) that a burst comes back once more right after a tiny gap.")]
    [SerializeField, Range(0f, 1f)] private float doubleChance = 0.35f;
    [Tooltip("Gap before the repeat burst, in seconds: random between X and Y.")]
    [SerializeField] private Vector2 doubleGapSeconds = new Vector2(0.06f, 0.18f);
    [Tooltip("During a burst the static switches on/off this often (seconds, random between X and Y).")]
    [SerializeField] private Vector2 flickerStep = new Vector2(0.02f, 0.06f);
    [Tooltip("Grain jumps per second during a burst.")]
    [SerializeField] private float burstGrainFps = 60f;

    [Header("Clarity cycle (24 Nov): clear -> 1-2 frame glitch -> a little grain -> clear")]
    [Tooltip("Off = the grain between bursts stays at Calm Alpha.")]
    [SerializeField] private bool clarityCycle = true;
    [Tooltip("Grain while the picture is CLEAR (0 = none at all).")]
    [SerializeField, Range(0f, 1f)] private float clearAlpha = 0f;
    [Tooltip("How long the picture stays clear, in seconds: random between X and Y.")]
    [SerializeField] private Vector2 clearSeconds = new Vector2(4f, 8f);
    [Tooltip("Glitch length in FRAMES: random between X and Y (1-2 = a blink).")]
    [SerializeField] private Vector2Int glitchFrames = new Vector2Int(1, 2);
    [Tooltip("Grain strength during the glitch frames.")]
    [SerializeField, Range(0f, 1f)] private float glitchAlpha = 0.35f;
    [Tooltip("How far the picture jumps sideways during the glitch, in canvas pixels (random left or right).")]
    [SerializeField, Min(0f)] private float jitterPixels = 14f;
    [Tooltip("What jumps sideways during the glitch (the menu pages). Empty = only the grain and band jump.")]
    [SerializeField] private RectTransform[] jitterTargets = new RectTransform[0];
    [Tooltip("Grain right after the glitch (0 = straight back to clear).")]
    [SerializeField, Range(0f, 1f)] private float afterAlpha = 0.05f;
    [Tooltip("How long that grain stays, in seconds: random between X and Y.")]
    [SerializeField] private Vector2 afterSeconds = new Vector2(0.2f, 0.5f);
    [Tooltip("Seconds to fade from that grain back to Clear.")]
    [SerializeField, Min(0f)] private float fadeToClearSeconds = 0.3f;

    [Header("Burst: picture and band")]
    [Tooltip("Optional: the blue screen image. It dips darker during a burst.")]
    [SerializeField] private Graphic flickerTarget;
    [Tooltip("How bright the picture stays during a burst (1 = no dip).")]
    [SerializeField, Range(0f, 1f)] private float flickerBrightness = 0.7f;
    [Tooltip("The tracking band jumps to a random height when a burst starts.")]
    [SerializeField] private bool bandSkips = true;

    [Header("Burst: sound (optional)")]
    [Tooltip("Sound Library id played at the start of each burst group. Empty = no sound.")]
    [SerializeField] private string burstSoundId = "";

    /// <summary>The script drives the Noise layer's strength (bursts and/or the clarity cycle).</summary>
    private bool Animated => bursts || clarityCycle;

    private float trackT;
    private float grainTimer;
    private Vector2 lastSize;

    // bursts
    private bool inBurst;
    private float stateLeft;      // seconds until the next burst / until this burst ends
    private int repeatsLeft;      // repeat bursts still to come in this group
    private float flickLeft;
    private bool flickOn;
    private float currentBurstAlpha;
    private Color flickerBase;
    private bool flickerBaseKnown;
    private bool noiseWasActive;

    // clarity cycle
    private enum Clarity { Clear, Glitch, After, FadeToClear }
    private Clarity clarity;
    private float clarityTime;     // seconds spent in the current step
    private float clarityLength;   // length of the current step
    private int glitchFramesLeft;
    private bool jittering;

    /// <summary>How far the menu pages are pushed sideways right now (0 outside a glitch frame).</summary>
    public static Vector2 PageJitter { get; private set; }
    private float jitterX;         // sideways jump of this glitch frame
    private Vector2[] jitterBase = new Vector2[0];
    private VcrTapeTransition pageTransition;

    private void OnEnable()
    {
        trackT = Random.value;
        lastSize = Vector2.zero;

        if (flickerTarget != null)
        {
            flickerBase = flickerTarget.color;
            flickerBaseKnown = true;
        }
        if (Animated && noise != null)
        {
            noiseWasActive = noise.gameObject.activeSelf;
            if (!noiseWasActive) noise.gameObject.SetActive(true); // bursts / cycle need the Noise layer
            SetNoiseAlpha(calmAlpha);
        }
        inBurst = false;
        repeatsLeft = 0;
        stateLeft = RandomIn(calmSeconds);
        SetClarity(Clarity.Clear);
        if (Animated && noise != null) SetNoiseAlpha(BaseAlpha());
    }

    private void OnDisable()
    {
        EndJitter();
        if (flickerTarget != null && flickerBaseKnown) flickerTarget.color = flickerBase;
        if (Animated && noise != null && !noiseWasActive) noise.gameObject.SetActive(false);
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

        if (Animated && noise != null)
        {
            if (clarityCycle) UpdateClarity(dt);
            if (bursts) UpdateBursts(dt);
            if (!inBurst) SetNoiseAlpha(BaseAlpha());
        }

        // Tracking band: from just above the top to just below the bottom.
        if (trackingBand != null && trackingSeconds > 0.01f)
        {
            trackT += dt / trackingSeconds;
            if (trackT > 1f) trackT -= 1f;
            float h = trackingBand.rect.height;
            float y = Mathf.Lerp(h, -size.y - h, trackT);
            trackingBand.anchoredPosition = new Vector2(jitterX, y); // straight down; jumps sideways only on a glitch frame
        }

        // Grain: jump to a random spot a few times a second, like tape noise.
        bool fast = (bursts && inBurst) || (clarityCycle && clarity == Clarity.Glitch);
        float fps = fast ? burstGrainFps : grainFps;
        if (noise != null && fps > 0f)
        {
            grainTimer += dt;
            if (grainTimer >= 1f / fps)
            {
                grainTimer = 0f;
                Rect r = noise.uvRect;
                r.x = Random.value;
                r.y = Random.value;
                noise.uvRect = r;
            }
        }
    }

    // ---- Bursts ---------------------------------------------------------------------

    private void UpdateBursts(float dt)
    {
        stateLeft -= dt;

        if (!inBurst)
        {
            if (stateLeft <= 0f) StartBurst(repeatsLeft == 0);
            return;
        }

        // Inside a burst: hard on/off flicker of the static and the picture.
        flickLeft -= dt;
        if (flickLeft <= 0f)
        {
            flickOn = !flickOn;
            flickLeft = RandomIn(flickerStep);
            ApplyBurstLook(flickOn);
        }

        if (stateLeft <= 0f) EndBurst();
    }

    private void StartBurst(bool firstOfGroup)
    {
        inBurst = true;
        stateLeft = RandomIn(burstSeconds);
        currentBurstAlpha = RandomIn(burstAlpha);
        flickOn = true;
        flickLeft = RandomIn(flickerStep);
        ApplyBurstLook(true);

        if (firstOfGroup)
        {
            if (bandSkips) trackT = Random.value;
            if (!string.IsNullOrEmpty(burstSoundId)) AudioManager.Instance?.PlayUI(burstSoundId);
        }
    }

    private void EndBurst()
    {
        inBurst = false;
        SetNoiseAlpha(BaseAlpha());
        if (flickerTarget != null && flickerBaseKnown) flickerTarget.color = flickerBase;

        if (repeatsLeft > 0)
        {
            repeatsLeft--;
            stateLeft = RandomIn(doubleGapSeconds);
        }
        else
        {
            repeatsLeft = Random.value < doubleChance ? 1 : 0;
            stateLeft = RandomIn(calmSeconds);
        }
    }

    private void ApplyBurstLook(bool on)
    {
        float baseA = BaseAlpha();
        SetNoiseAlpha(on ? Mathf.Max(baseA, currentBurstAlpha) : Mathf.Max(baseA, currentBurstAlpha * 0.35f));
        if (flickerTarget != null && flickerBaseKnown)
        {
            float k = on ? flickerBrightness : Mathf.Lerp(flickerBrightness, 1f, 0.5f);
            flickerTarget.color = new Color(flickerBase.r * k, flickerBase.g * k, flickerBase.b * k, flickerBase.a);
        }
    }

    // ---- Clarity cycle --------------------------------------------------------------

    private void SetClarity(Clarity step)
    {
        if (clarity == Clarity.Glitch && step != Clarity.Glitch) EndJitter();
        clarity = step;
        clarityTime = 0f;
        clarityLength = step == Clarity.Clear ? RandomIn(clearSeconds)
                      : step == Clarity.After ? RandomIn(afterSeconds)
                      : step == Clarity.FadeToClear ? fadeToClearSeconds
                      : 0f;
        if (step == Clarity.Glitch)
        {
            glitchFramesLeft = Random.Range(Mathf.Max(1, Mathf.Min(glitchFrames.x, glitchFrames.y)),
                                            Mathf.Max(1, Mathf.Max(glitchFrames.x, glitchFrames.y)) + 1);
            StartJitter();
        }
    }

    private void UpdateClarity(float dt)
    {
        if (clarity == Clarity.Glitch)
        {
            // counted in frames, not seconds: a real 1-2 frame blink
            glitchFramesLeft--;
            if (glitchFramesLeft <= 0) { SetClarity(afterAlpha > 0f ? Clarity.After : Clarity.FadeToClear); return; }
            Jitter(); // a new sideways jump on the next glitch frame
            return;
        }

        clarityTime += dt;
        if (clarityTime < clarityLength) return;
        switch (clarity)
        {
            case Clarity.Clear: SetClarity(Clarity.Glitch); break;
            case Clarity.After: SetClarity(Clarity.FadeToClear); break;
            default: SetClarity(Clarity.Clear); break;
        }
    }

    // ---- Glitch jump ----------------------------------------------------------------

    private void StartJitter()
    {
        if (pageTransition == null) pageTransition = FindFirstObjectByType<VcrTapeTransition>();
        if (jitterBase.Length != jitterTargets.Length) jitterBase = new Vector2[jitterTargets.Length];
        for (int i = 0; i < jitterTargets.Length; i++)
            if (jitterTargets[i] != null) jitterBase[i] = jitterTargets[i].anchoredPosition;
        jittering = true;
        Jitter();
    }

    private void Jitter()
    {
        jitterX = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.4f, 1f) * jitterPixels;
        PageJitter = Vector2.zero;
        if (noise != null)
        {
            Rect r = noise.uvRect;
            r.x = Random.value;
            noise.uvRect = r;
        }
        // The page-change effect moves the pages too: leave them alone while it runs.
        if (pageTransition != null && pageTransition.IsPlaying) return;
        for (int i = 0; i < jitterTargets.Length; i++)
            if (jitterTargets[i] != null) jitterTargets[i].anchoredPosition = jitterBase[i] + new Vector2(jitterX, 0f);
        PageJitter = new Vector2(jitterX, 0f);
    }

    private void EndJitter()
    {
        jitterX = 0f;
        if (!jittering) return;
        jittering = false;
        bool pagesMoved = PageJitter != Vector2.zero;
        PageJitter = Vector2.zero;
        if (!pagesMoved) return;
        for (int i = 0; i < jitterTargets.Length && i < jitterBase.Length; i++)
            if (jitterTargets[i] != null) jitterTargets[i].anchoredPosition = jitterBase[i];
    }

    /// <summary>Grain strength between bursts: from the clarity cycle, or Calm Alpha.</summary>
    private float BaseAlpha()
    {
        if (!clarityCycle) return calmAlpha;
        float t = clarityLength > 0.0001f ? Mathf.Clamp01(clarityTime / clarityLength) : 1f;
        switch (clarity)
        {
            case Clarity.Clear: return clearAlpha;
            case Clarity.Glitch: return glitchAlpha;
            case Clarity.After: return afterAlpha;
            default: return Mathf.Lerp(afterAlpha, clearAlpha, Mathf.SmoothStep(0f, 1f, t));
        }
    }

    private void SetNoiseAlpha(float a)
    {
        if (noise == null) return;
        Color c = noise.color;
        c.a = a;
        noise.color = c;
    }

    private static float RandomIn(Vector2 range) => Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));

    // ---- Tiling -----------------------------------------------------------------------

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
