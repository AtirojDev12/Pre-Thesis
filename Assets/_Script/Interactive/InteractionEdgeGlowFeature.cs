using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>Depth-tested hover edges, including hard surface creases. Requires URP Render Graph.</summary>
public sealed class InteractionEdgeGlowFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader normalsShader;
    [SerializeField] private Shader compositeShader;
    [SerializeField] private Color edgeColor = Color.white;
    [SerializeField, Range(1f, 5f)] private float edgeWidth = 1.5f;
    [SerializeField, Range(2f, 12f)] private float glowWidth = 4f;
    [SerializeField, Range(0f, 1f)] private float glowOpacity = 0.22f;
    [SerializeField, Range(0.01f, 1f)] private float creaseThreshold = 0.18f;
    [SerializeField, Min(0.1f)] private float sweepDuration = 1.25f;
    [SerializeField, Min(0f)] private float sweepPause = 0.5f;
    [SerializeField, Range(0.02f, 0.5f)] private float sweepWidth = 0.18f;
    [SerializeField, Range(0.1f, 1f)] private float baseBrightness = 0.5f;

    private Material normalsMaterial;
    private Material compositeMaterial;
    private SelectionPass selectionPass;
    private CompositePass compositePass;
    private readonly List<Renderer> selected = new List<Renderer>();

    public override void Create()
    {
        selectionPass?.DisposeMaterials();
        CoreUtils.Destroy(normalsMaterial);
        CoreUtils.Destroy(compositeMaterial);
        if (normalsShader == null) normalsShader = Resources.Load<Shader>("InteractionOutline");
        if (compositeShader == null) compositeShader = Resources.Load<Shader>("InteractionEdgeGlow");
        if (normalsShader == null || compositeShader == null) return;
        normalsMaterial = CoreUtils.CreateEngineMaterial(normalsShader);
        compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
        selectionPass = new SelectionPass(normalsMaterial, selected)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
        selectionPass.ConfigureInput(ScriptableRenderPassInput.Depth);
        compositePass = new CompositePass(compositeMaterial)
        {
            // Keep edges readable after the game's pixelation/vignette filter.
            renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPostProcessing + 1)
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (normalsMaterial == null || compositeMaterial == null) return;
        Camera camera = renderingData.cameraData.camera;
        if (camera.cameraType != CameraType.Game) return;
        InteractionOutline.CollectRenderers(camera, selected);
        // Keep depth/intermediate requirements stable between hover states. Otherwise
        // a fullscreen filter can suddenly start rendering only while hovering.
        renderer.EnqueuePass(selectionPass);
        if (selected.Count == 0) return;
        compositeMaterial.SetColor("_EdgeColor", edgeColor);
        compositeMaterial.SetFloat("_EdgeWidth", edgeWidth);
        compositeMaterial.SetFloat("_GlowWidth", Mathf.Max(edgeWidth, glowWidth));
        compositeMaterial.SetFloat("_GlowOpacity", glowOpacity);
        compositeMaterial.SetFloat("_CreaseThreshold", creaseThreshold);
        float elapsed = Mathf.Max(0f, Time.unscaledTime - InteractionOutline.SelectionStartedAt(camera));
        float duration = Mathf.Max(0.1f, sweepDuration);
        float phase = elapsed % (duration + Mathf.Max(0f, sweepPause));
        compositeMaterial.SetFloat("_SweepPosition", Mathf.Lerp(-sweepWidth, 1f + sweepWidth, Mathf.Clamp01(phase / duration)));
        compositeMaterial.SetFloat("_SweepWidth", sweepWidth);
        compositeMaterial.SetFloat("_BaseBrightness", baseBrightness);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(normalsMaterial);
        CoreUtils.Destroy(compositeMaterial);
        selectionPass?.DisposeMaterials();
    }

    private sealed class SelectionData : ContextItem
    {
        public TextureHandle texture;
        public override void Reset() => texture = TextureHandle.nullHandle;
    }

    private sealed class SelectionPass : ScriptableRenderPass
    {
        private readonly Material material;
        private readonly List<Renderer> sources;
        private readonly List<Material> sourceMaterials = new List<Material>();
        private readonly Dictionary<Material, Material> overrides = new Dictionary<Material, Material>();
        private struct Draw
        {
            public Renderer renderer;
            public int submesh;
            public Material material;
        }
        private sealed class PassData
        {
            public TextureHandle sceneDepth;
            public List<Draw> draws;
        }

        public void DisposeMaterials()
        {
            foreach (Material copy in overrides.Values) CoreUtils.Destroy(copy);
            overrides.Clear();
        }

        private Material CaptureMaterial(Material source, Bounds bounds)
        {
            if (!overrides.TryGetValue(source, out Material copy) || copy == null)
            {
                copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                overrides[source] = copy;
            }
            string map = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            Texture texture = source.HasProperty(map) ? source.GetTexture(map) : null;
            copy.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
            copy.SetTextureScale("_BaseMap", source.HasProperty(map) ? source.GetTextureScale(map) : Vector2.one);
            copy.SetTextureOffset("_BaseMap", source.HasProperty(map) ? source.GetTextureOffset(map) : Vector2.zero);
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            copy.SetColor("_BaseColor", color);
            bool cutout = source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f || source.IsKeywordEnabled("_ALPHATEST_ON");
            bool transparent = source.renderQueue >= (int)RenderQueue.Transparent;
            copy.SetFloat("_SelectionCutoff", cutout && source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : transparent ? 0.05f : -1f);
            copy.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f);
            copy.SetVector("_SelectionHeight", new Vector4(bounds.min.y, 1f / Mathf.Max(bounds.size.y, 0.001f), 0, 0));
            return copy;
        }

        public SelectionPass(Material material, List<Renderer> sources)
        {
            this.material = material;
            this.sources = sources;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraDepthTexture.IsValid() || sources.Count == 0) return;
            // A direct-to-target depth buffer can be an imported back buffer with no
            // graph descriptor. Camera data remains valid for that path as well.
            var descriptor = new TextureDesc(frameData.Get<UniversalCameraData>().cameraTargetDescriptor);
            descriptor.name = "Interaction selection normals";
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.clearBuffer = true;
            descriptor.clearColor = Color.clear;
            descriptor.bindTextureMS = false;
            descriptor.filterMode = FilterMode.Point;
            TextureHandle mask = graph.CreateTexture(descriptor);
            var depthDescriptor = new TextureDesc(descriptor);
            depthDescriptor.name = "Interaction selection depth";
            depthDescriptor.colorFormat = GraphicsFormat.None;
            depthDescriptor.depthBufferBits = DepthBits.Depth24;
            TextureHandle selectionDepth = graph.CreateTexture(depthDescriptor);
            frameData.GetOrCreate<SelectionData>().texture = mask;

            using (var builder = graph.AddRasterRenderPass<PassData>("Interaction selection", out var data))
            {
                data.sceneDepth = resources.cameraDepthTexture;
                data.draws = new List<Draw>();
                Bounds bounds = sources[0].bounds;
                foreach (Renderer source in sources) if (source != null) bounds.Encapsulate(source.bounds);
                foreach (Renderer source in sources)
                {
                    if (source == null) continue;
                    source.GetSharedMaterials(sourceMaterials);
                    for (int submesh = 0; submesh < sourceMaterials.Count; submesh++)
                    {
                        Material authored = sourceMaterials[submesh];
                        if (authored == null) continue;
                        data.draws.Add(new Draw { renderer = source, submesh = submesh, material = CaptureMaterial(authored, bounds) });
                    }
                }
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(selectionDepth, AccessFlags.Write);
                builder.UseTexture(data.sceneDepth, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture("_SelectionSceneDepth", pass.sceneDepth);
                    foreach (Draw draw in pass.draws)
                        if (draw.renderer != null) context.cmd.DrawRenderer(draw.renderer, draw.material, draw.submesh, 0);
                });
            }
        }
    }

    private sealed class CompositePass : ScriptableRenderPass
    {
        private readonly Material material;
        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
        }
        public CompositePass(Material material) => this.material = material;

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            TextureHandle mask = frameData.GetOrCreate<SelectionData>().texture;
            if (!mask.IsValid()) return;
            var resources = frameData.Get<UniversalResourceData>();
            using (var builder = graph.AddRasterRenderPass<PassData>("Interaction edge glow", out var data))
            {
                data.source = mask;
                data.material = material;
                builder.UseTexture(mask, AccessFlags.Read);
                // Blend directly, including when the active color is the back buffer.
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData pass, RasterGraphContext context) =>
                    Blitter.BlitTexture(context.cmd, pass.source, new Vector4(1, 1, 0, 0), pass.material, 0));
            }
        }
    }
}
