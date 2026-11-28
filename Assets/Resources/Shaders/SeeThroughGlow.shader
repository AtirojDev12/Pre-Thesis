// 17 Nov 2026 (Mr.k, Task 5): a flat glowing colour drawn ON TOP of walls (ZTest Always).
// Used by SeeThroughGlow for the projector glow before a film change.
// 28 Nov 2026 (Atiroj): _ZTest picks it per material. Default Always (mops: through walls).
// The projector now uses LessEqual: walls hide its glow, an icon (FilmProjectorIcon) shows
// where it is instead. Offset pulls the copy a hair forward so it does not flicker on the mesh.
// It lives in Resources so it is always in the build.
Shader "Hidden/PreThesis/SeeThroughGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.95, 0.62, 0.55)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "SeeThroughGlow"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest [_ZTest]
            ZWrite Off
            Offset -1, -1
            Cull Back
            Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // Brighter at the edges, so the shape reads clearly through walls.
                half rim = 1.0h - saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half a = _Color.a * (0.45h + 0.55h * rim);
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
