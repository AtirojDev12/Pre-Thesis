Shader "PreThesis/VFX/Food Particle"
{
    Properties { _BaseColor("Tint", Color) = (1,1,1,1) _Softness("Softness", Range(0,1)) = 0.5 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Softness;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _BaseColor;
                return output;
            }
            half4 frag(Varyings input):SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float radius = length(p);
                float mask = 1 - smoothstep(lerp(.85,.02,_Softness),1,radius);
                float wisps = .78 + .22 * sin(p.x * 12 + sin(p.y * 9)) * sin(p.y * 11);
                return half4(input.color.rgb, input.color.a * mask * lerp(1,wisps,_Softness));
            }
            ENDHLSL
        }
    }
}
