using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 5 Oct (Mr.k). REAL darkness when the lights go off.
///
/// Before: turning the lights off (L, or the ghost's flicker) only disabled the
/// lamps. The rest of the scene lighting stayed, so the room went grey but you
/// could still see everything, far away included.
///
/// Now, while GhostManager says the lights are off, on THIS machine only
/// (every machine does the same, from the same synced light state):
///   1. Ambient light and reflections drop to almost nothing.
///   2. Black fog swallows anything far away.
///   3. A weak, short "eyes adjusted" light around your own camera lets you see
///      only what is close. Further = darker, like a real dark room.
///   Flashlights (FlashlightController) still reach far, so they matter.
/// When the lights come back, the scene's own settings are put back exactly.
///
/// HOW TO TUNE (numbers or sliders)
///   - Play the map: an object "[Darkness]" is created automatically. Select it and
///     move the sliders while playing to try values live.
///   - To KEEP values for a map: Tools > Pre-Thesis > Darkness: Add To Open Scene,
///     set the sliders there, save the scene. A map with its own DarknessController
///     uses it instead of the automatic one.
///
/// Visuals only. Who the ghost can see is still TimedGhost's job.
/// </summary>
[DisallowMultipleComponent]
public sealed class DarknessController : MonoBehaviour
{
    [Header("How far you can see with NO flashlight")]
    [Tooltip("Metres. Beyond this the room is black (unless a flashlight or lamp lights it).")]
    [Range(1f, 30f)] public float visibleDistance = 6f;
    [Tooltip("How bright close things are in the dark. 0 = pitch black.")]
    [Range(0f, 3f)] public float nearBrightness = 0.6f;
    [Tooltip("Colour of what you can still see. Blue-grey reads as night.")]
    public Color nearColor = new Color(0.62f, 0.68f, 0.85f);

    [Header("Black fog (hides far things, flashlights too)")]
    public bool useFog = true;
    [Tooltip("Metres where the fog is fully black. Keep it LONGER than the best flashlight's range (20 m) or the flashlight looks short.")]
    [Range(5f, 100f)] public float fogDistance = 28f;

    [Header("Scene lighting in the dark")]
    [Tooltip("Ambient light left over. 0 = none.")]
    [Range(0f, 0.3f)] public float ambientLeft = 0.01f;
    [Tooltip("Reflections left over (shiny surfaces). 0 = none.")]
    [Range(0f, 1f)] public float reflectionsLeft = 0.05f;

    [Header("Speed")]
    [Tooltip("Seconds to go dark when the lights die. Short = a real power cut.")]
    [Range(0f, 3f)] public float secondsToDark = 0.1f;
    [Tooltip("Seconds to come back when the lights return.")]
    [Range(0f, 5f)] public float secondsToLight = 0.6f;

    [Header("Test")]
    [Tooltip("Force darkness on, to tune without pressing L.")]
    public bool previewDark;

    /// <summary>0 = lit, 1 = fully dark. Read-only for other scripts.</summary>
    public float Darkness => darkness;

    private float darkness;
    private bool applied;
    private Light eyeLight;
    private Camera eyeCamera;
    private float nextCameraScan;

    // The scene's own settings, put back when the lights return.
    private bool origFog;
    private FogMode origFogMode;
    private Color origFogColor;
    private float origFogDensity, origFogStart, origFogEnd;
    private UnityEngine.Rendering.AmbientMode origAmbientMode;
    private Color origAmbientLight, origAmbientSky, origAmbientEquator, origAmbientGround;
    private float origAmbientIntensity, origReflection;
    private bool captured;

    // ---- Automatic setup: every scene with a GhostManager gets one ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isBatchMode || !scene.IsValid()) return;
        GameObject[] roots = scene.GetRootGameObjects();
        bool hasGhostManager = false;
        foreach (GameObject root in roots)
        {
            if (root.GetComponentInChildren<DarknessController>(true) != null) return; // the map has its own
            if (root.GetComponentInChildren<GhostManager>(true) != null) hasGhostManager = true;
        }
        if (!hasGhostManager) return; // menu, lobby without lights: nothing to darken

        var go = new GameObject("[Darkness]");
        SceneManager.MoveGameObjectToScene(go, scene); // dies with the map, restores on the way out
        go.AddComponent<DarknessController>();
    }

    // ---- Runtime --------------------------------------------------------------

    private void OnEnable()
    {
        Capture();
        darkness = 0f;
    }

    private void OnDisable()
    {
        Restore();
        if (eyeLight != null) Destroy(eyeLight.gameObject);
        eyeLight = null;
    }

    private void Capture()
    {
        origFog = RenderSettings.fog;
        origFogMode = RenderSettings.fogMode;
        origFogColor = RenderSettings.fogColor;
        origFogDensity = RenderSettings.fogDensity;
        origFogStart = RenderSettings.fogStartDistance;
        origFogEnd = RenderSettings.fogEndDistance;
        origAmbientMode = RenderSettings.ambientMode;
        origAmbientLight = RenderSettings.ambientLight;
        origAmbientSky = RenderSettings.ambientSkyColor;
        origAmbientEquator = RenderSettings.ambientEquatorColor;
        origAmbientGround = RenderSettings.ambientGroundColor;
        origAmbientIntensity = RenderSettings.ambientIntensity;
        origReflection = RenderSettings.reflectionIntensity;
        captured = true;
    }

    private void Update()
    {
        bool wantDark = previewDark || (GhostManager.Instance != null && !GhostManager.Instance.areLightsOnCurrently);
        float seconds = wantDark ? secondsToDark : secondsToLight;
        float step = seconds <= 0f ? 1f : Time.unscaledDeltaTime / seconds;
        darkness = Mathf.MoveTowards(darkness, wantDark ? 1f : 0f, step);

        if (darkness <= 0f) { Restore(); return; }
        Apply(darkness);
    }

    private void Apply(float t)
    {
        applied = true;

        // 1. Ambient + reflections: Flat ambient so one colour controls it.
        Color litAmbient = origAmbientMode == UnityEngine.Rendering.AmbientMode.Flat ? origAmbientLight
            : origAmbientMode == UnityEngine.Rendering.AmbientMode.Trilight ? origAmbientEquator
            : origAmbientSky * origAmbientIntensity;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(litAmbient, Color.white * ambientLeft, t);
        RenderSettings.reflectionIntensity = Mathf.Lerp(origReflection, reflectionsLeft, t);

        // 2. Black fog. Keeps the scene's fog mode when it has one.
        if (useFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogColor = Color.Lerp(origFog ? origFogColor : Color.black, Color.black, t);
            if (origFog && origFogMode == FogMode.Linear)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = Mathf.Lerp(origFogStart, fogDistance * 0.3f, t);
                RenderSettings.fogEndDistance = Mathf.Lerp(origFogEnd, Mathf.Min(origFogEnd, fogDistance), t);
            }
            else
            {
                FogMode mode = origFog ? origFogMode : FogMode.ExponentialSquared;
                RenderSettings.fogMode = mode;
                // Density that leaves ~2% visible at fogDistance.
                float target = mode == FogMode.Exponential ? 3.91f / fogDistance : 1.98f / fogDistance;
                float from = origFog ? origFogDensity : 0f;
                RenderSettings.fogDensity = Mathf.Lerp(from, Mathf.Max(from, target), t);
            }
        }
        else RestoreFog();

        // 3. The "eyes adjusted" light around the camera that is drawing the game.
        UpdateEyeLight(t);
    }

    private void UpdateEyeLight(float t)
    {
        if (Time.unscaledTime >= nextCameraScan || eyeCamera == null || !eyeCamera.isActiveAndEnabled)
        {
            nextCameraScan = Time.unscaledTime + 0.5f;
            Camera cam = FindViewCamera();
            if (cam != eyeCamera)
            {
                eyeCamera = cam;
                if (eyeLight != null) Destroy(eyeLight.gameObject);
                eyeLight = null;
            }
        }
        if (eyeCamera == null) return;

        if (eyeLight == null)
        {
            var go = new GameObject("Eyes Adjusted Light");
            go.transform.SetParent(eyeCamera.transform, false);
            eyeLight = go.AddComponent<Light>();
            eyeLight.type = LightType.Point;
            eyeLight.shadows = LightShadows.None;
            eyeLight.renderMode = LightRenderMode.ForcePixel; // smooth falloff, not per-vertex
        }
        eyeLight.range = visibleDistance;
        eyeLight.color = nearColor;
        eyeLight.intensity = nearBrightness * t;
        eyeLight.enabled = nearBrightness > 0f;
    }

    /// <summary>The camera the player is looking through (player, spectator...).</summary>
    private static Camera FindViewCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled && main.targetTexture == null) return main;
        Camera best = null;
        foreach (Camera cam in Camera.allCameras)
            if (cam.targetTexture == null && (best == null || cam.depth > best.depth)) best = cam;
        return best;
    }

    private void Restore()
    {
        if (eyeLight != null && eyeLight.enabled) eyeLight.enabled = false;
        if (!applied || !captured) return;
        applied = false;
        RenderSettings.ambientMode = origAmbientMode;
        RenderSettings.ambientLight = origAmbientLight;
        RenderSettings.ambientSkyColor = origAmbientSky;
        RenderSettings.ambientEquatorColor = origAmbientEquator;
        RenderSettings.ambientGroundColor = origAmbientGround;
        RenderSettings.ambientIntensity = origAmbientIntensity;
        RenderSettings.reflectionIntensity = origReflection;
        RestoreFog();
    }

    private void RestoreFog()
    {
        RenderSettings.fog = origFog;
        RenderSettings.fogMode = origFogMode;
        RenderSettings.fogColor = origFogColor;
        RenderSettings.fogDensity = origFogDensity;
        RenderSettings.fogStartDistance = origFogStart;
        RenderSettings.fogEndDistance = origFogEnd;
    }
}
