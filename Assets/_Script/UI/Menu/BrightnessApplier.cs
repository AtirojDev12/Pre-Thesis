using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the Brightness setting to the game world.
///
/// Creates itself at startup (no scene setup needed): one global URP Volume,
/// kept across scene loads, with a Color Adjustments override whose Post
/// Exposure follows GameSettings.Brightness. The middle of the slider (0.5)
/// is exposure 0 — exactly how the artists lit the scene.
///
/// Brightness only reaches cameras that render post-processing, so this also
/// turns "Post Processing" on for the active camera. The player camera had it
/// off, and there is no other Volume in the gameplay scene, so nothing else
/// changes visually.
///
/// UI is not affected (Screen Space - Overlay canvases draw after post), so a
/// dark setting never makes the menus unreadable.
/// </summary>
public sealed class BrightnessApplier : MonoBehaviour
{
    private static BrightnessApplier instance;

    private Volume volume;
    private ColorAdjustments colorAdjustments;
    private Camera[] cameraBuffer = new Camera[8];
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;

        var go = new GameObject("[Brightness]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BrightnessApplier>();
    }

    private void Awake()
    {
        // Default layer: every URP camera's Volume Mask includes it by default.
        gameObject.layer = 0;

        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1000f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Runtime Brightness";
        colorAdjustments = profile.Add<ColorAdjustments>(false); // false: only exposure is overridden, never a map's own grading
        volume.sharedProfile = profile;

        Apply();
    }

    private void OnEnable()
    {
        GameSettings.Changed += Apply;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        GameSettings.Changed -= Apply;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => nextScan = 0f;

    private void Update()
    {
        // The player camera is spawned by the network after the scene loads,
        // and may not be tagged MainCamera, so scan every enabled camera twice
        // a second. GetAllCameras fills a reused buffer: no garbage.
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.5f;

        // GetAllCameras throws if the buffer is smaller than the camera count.
        if (cameraBuffer.Length < Camera.allCamerasCount)
            cameraBuffer = new Camera[Camera.allCamerasCount * 2];

        int count = Camera.GetAllCameras(cameraBuffer);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cameraBuffer[i];
            if (cam == null || cam.targetTexture != null) continue;

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null && data.renderType == CameraRenderType.Base && !data.renderPostProcessing)
                data.renderPostProcessing = true;
        }
    }

    private void Apply()
    {
        if (colorAdjustments == null) return;
        colorAdjustments.postExposure.Override(GameSettings.BrightnessExposure);
    }
}
