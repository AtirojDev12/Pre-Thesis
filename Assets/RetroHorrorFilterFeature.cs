using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

// ---------------------------------------------------------------------------
// Retro Horror Filter — URP Renderer Feature (Unity 6 / URP 17 Render Graph)
//
// SETUP:
// 1. Put this script + RetroHorrorFilter.shader anywhere in Assets (they can
//    be in different folders, Unity finds the shader by its "Hidden/..." name).
// 2. Select your URP Renderer asset (Project Settings > Graphics > the
//    Renderer Data asset, usually "Universal Renderer Data").
// 3. Click "Add Renderer Feature" > "Retro Horror Filter Feature".
// 4. Tweak the settings on the feature itself, or reference it at runtime via
//    RetroHorrorFilterController to animate values, e.g. spike grain/flicker
//    when a ghost jumpscares.
//
// Written against Unity 6000.x (URP 17) using the Render Graph API
// (RecordRenderGraph). This does NOT work on older URP versions that only
// have Configure/Execute — use the Compatibility Mode version for those.
// ---------------------------------------------------------------------------

[System.Serializable]
public class RetroHorrorFilterSettings
{
    public bool enabled = true;

    [Header("Pixelation")]
    [Range(1f, 16f)] public float pixelBlockSize = 1f; // 1 = off

    [Header("Scanlines")]
    [Range(0f, 1f)] public float scanlineIntensity = 0.25f;
    [Range(50f, 800f)] public float scanlineCount = 240f;

    [Header("Chromatic Aberration")]
    [Range(0f, 5f)] public float chromaticAberration = 0.6f;

    [Header("Vignette")]
    [Range(0f, 1f)] public float vignetteIntensity = 0.5f;
    [Range(0f, 1f)] public float vignetteSmoothness = 0.4f;

    [Header("Grain / VHS Noise")]
    [Range(0f, 1f)] public float grainIntensity = 0.08f;
    [Range(1f, 10f)] public float grainSize = 2f;
    [Range(0f, 1f)] public float flickerIntensity = 0.05f;

    [Header("Color")]
    [Range(0f, 1f)] public float desaturation = 0.3f;
    public Color colorTint = new Color(0.85f, 1.0f, 0.82f); // sickly pale-green tint
}

public class RetroHorrorFilterFeature : ScriptableRendererFeature
{
    public RetroHorrorFilterSettings settings = new RetroHorrorFilterSettings();
    public Shader shader;

    private Material _material;
    private RetroHorrorFilterPass _pass;

    private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
    private static readonly int ScanlineIntensityId = Shader.PropertyToID("_ScanlineIntensity");
    private static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");
    private static readonly int ChromaticAberrationId = Shader.PropertyToID("_ChromaticAberration");
    private static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
    private static readonly int VignetteSmoothnessId = Shader.PropertyToID("_VignetteSmoothness");
    private static readonly int GrainIntensityId = Shader.PropertyToID("_GrainIntensity");
    private static readonly int GrainSizeId = Shader.PropertyToID("_GrainSize");
    private static readonly int ColorTintId = Shader.PropertyToID("_ColorTint");
    private static readonly int DesaturationId = Shader.PropertyToID("_Desaturation");
    private static readonly int Time01Id = Shader.PropertyToID("_Time01");
    private static readonly int FlickerIntensityId = Shader.PropertyToID("_FlickerIntensity");

    public override void Create()
    {
        if (shader == null)
        {
            shader = Shader.Find("Hidden/RetroHorrorFilter");
        }

        if (shader == null)
        {
            Debug.LogWarning("RetroHorrorFilterFeature: shader not found. Assign it manually on the feature.");
            return;
        }

        _material = CoreUtils.CreateEngineMaterial(shader);
        _pass = new RetroHorrorFilterPass(_material)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.enabled || _material == null || _pass == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        _material.SetFloat(PixelSizeId, settings.pixelBlockSize);
        _material.SetFloat(ScanlineIntensityId, settings.scanlineIntensity);
        _material.SetFloat(ScanlineCountId, settings.scanlineCount);
        _material.SetFloat(ChromaticAberrationId, settings.chromaticAberration);
        _material.SetFloat(VignetteIntensityId, settings.vignetteIntensity);
        _material.SetFloat(VignetteSmoothnessId, settings.vignetteSmoothness);
        _material.SetFloat(GrainIntensityId, settings.grainIntensity);
        _material.SetFloat(GrainSizeId, settings.grainSize);
        _material.SetVector(ColorTintId, settings.colorTint);
        _material.SetFloat(DesaturationId, settings.desaturation);
        _material.SetFloat(Time01Id, Time.time);
        _material.SetFloat(FlickerIntensityId, settings.flickerIntensity);

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_material);
    }

    // -----------------------------------------------------------------
    // Render Graph pass (Unity 6 / URP 17). Grabs the active camera color
    // texture, blits it through our material into a new temp texture, then
    // hands that back to the graph as the new camera color.
    private class RetroHorrorFilterPass : ScriptableRenderPass
    {
        private readonly Material _mat;
        private const string PassName = "Retro Horror Filter";

        public RetroHorrorFilterPass(Material mat)
        {
            _mat = mat;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            // Can't blit-and-rebind when the active target is the back buffer
            // (e.g. certain XR / final blit configurations) — skip safely.
            if (resourceData.isActiveTargetBackBuffer)
            {
                return;
            }

            TextureHandle source = resourceData.activeColorTexture;

            TextureDesc destDesc = renderGraph.GetTextureDesc(source);
            destDesc.name = "_RetroHorrorTemp";
            destDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destDesc);

            RenderGraphUtils.BlitMaterialParameters blitParams = new(source, destination, _mat, 0);
            renderGraph.AddBlitPass(blitParams, PassName);

            // Feed the filtered result back as the camera's color texture so
            // anything rendered after this pass (e.g. UI) sees the filtered image.
            resourceData.cameraColor = destination;
        }
    }
}

// ---------------------------------------------------------------------------
// Optional: runtime controller to animate the filter, e.g. spike glitch
// intensity during a jumpscare. Attach to any GameObject and assign the
// URP Renderer Data asset in the Inspector.
// ---------------------------------------------------------------------------
public class RetroHorrorFilterController : MonoBehaviour
{
    [SerializeField] private UniversalRendererData rendererData;
    private RetroHorrorFilterFeature _feature;

    private void Awake()
    {
        if (rendererData == null) return;
        foreach (var f in rendererData.rendererFeatures)
        {
            if (f is RetroHorrorFilterFeature rf)
            {
                _feature = rf;
                break;
            }
        }
    }

    /// <summary>Call this when a ghost jumpscares the player for a burst of glitch/noise.</summary>
    public void TriggerJumpscareGlitch(float duration = 0.6f)
    {
        if (_feature == null) return;
        StopAllCoroutines();
        StartCoroutine(GlitchRoutine(duration));
    }

    private System.Collections.IEnumerator GlitchRoutine(float duration)
    {
        var s = _feature.settings;
        float baseGrain = s.grainIntensity;
        float baseFlicker = s.flickerIntensity;
        float baseChroma = s.chromaticAberration;

        s.grainIntensity = 0.4f;
        s.flickerIntensity = 0.5f;
        s.chromaticAberration = 3.5f;

        yield return new WaitForSeconds(duration);

        s.grainIntensity = baseGrain;
        s.flickerIntensity = baseFlicker;
        s.chromaticAberration = baseChroma;
    }
}
