Shader "Popcorn/InteractionEdgeGlow"
{
    Properties
    {
        _EdgeColor ("Edge color", Color) = (1, 1, 1, 1)
        _EdgeWidth ("Core width in pixels", Float) = 1.5
        _GlowWidth ("Glow width in pixels", Float) = 4
        _GlowOpacity ("Glow opacity", Range(0, 1)) = 0.22
        _CreaseThreshold ("Normal difference threshold", Range(0.01, 1)) = 0.18
        _SweepPosition ("Sweep position", Float) = -0.18
        _SweepWidth ("Sweep width", Float) = 0.18
        _BaseBrightness ("Resting edge brightness", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, Zero One
        Pass
        {
            Name "EdgeGlowComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _EdgeColor;
            float _EdgeWidth, _GlowWidth, _GlowOpacity, _CreaseThreshold;
            float _SweepPosition, _SweepWidth, _BaseBrightness;

            half4 Selection(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            }
            float Difference(half4 center, half4 neighbor)
            {
                float centerMask = step(0.5, center.a);
                float neighborMask = step(0.5, neighbor.a);
                float silhouette = abs(centerMask - neighborMask);
                float normalDifference = length(center.rgb - neighbor.rgb);
                float crease = smoothstep(_CreaseThreshold, _CreaseThreshold + 0.12, normalDifference);
                float height = (centerMask > 0 ? center.a : neighbor.a) - 1;
                float band = 1 - smoothstep(0, max(_SweepWidth, 0.001), abs(height - _SweepPosition));
                return max(silhouette, crease * centerMask * neighborMask) * lerp(_BaseBrightness, 1, band);
            }
            float Edges(float2 uv, half4 center, float radius)
            {
                float2 stepUV = _BlitTexture_TexelSize.xy * radius;
                float edge = 0;
                edge = max(edge, Difference(center, Selection(uv + float2(stepUV.x, 0))));
                edge = max(edge, Difference(center, Selection(uv - float2(stepUV.x, 0))));
                edge = max(edge, Difference(center, Selection(uv + float2(0, stepUV.y))));
                edge = max(edge, Difference(center, Selection(uv - float2(0, stepUV.y))));
                edge = max(edge, Difference(center, Selection(uv + stepUV * 0.7071)));
                edge = max(edge, Difference(center, Selection(uv - stepUV * 0.7071)));
                edge = max(edge, Difference(center, Selection(uv + float2(stepUV.x, -stepUV.y) * 0.7071)));
                edge = max(edge, Difference(center, Selection(uv + float2(-stepUV.x, stepUV.y) * 0.7071)));
                return edge;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 center = Selection(input.texcoord);
                float core = Edges(input.texcoord, center, _EdgeWidth);
                float innerGlow = Edges(input.texcoord, center, lerp(_EdgeWidth, _GlowWidth, 0.5));
                float outerGlow = Edges(input.texcoord, center, _GlowWidth);
                float alpha = saturate(core + _GlowOpacity * (innerGlow * 0.65 + outerGlow * 0.35));
                // Flat face interiors have zero alpha, preserving the original material.
                return half4(_EdgeColor.rgb, alpha * _EdgeColor.a);
            }
            ENDHLSL
        }
    }
}
