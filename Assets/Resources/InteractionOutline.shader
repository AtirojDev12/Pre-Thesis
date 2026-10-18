Shader "Popcorn/InteractionOutline"
{
    Properties
    {
        _BaseMap ("Opacity map", 2D) = "white" {}
        _BaseColor ("Base color", Color) = (1,1,1,1)
        _SelectionCutoff ("Opacity cutoff", Float) = -1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "SelectionNormals"
            Cull [_Cull] ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D_X_FLOAT(_SelectionSceneDepth);
            float4 _BaseMap_ST, _BaseColor, _SelectionHeight;
            float _SelectionCutoff;
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                clip(alpha - _SelectionCutoff);
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneDepth = SAMPLE_TEXTURE2D_X(_SelectionSceneDepth, sampler_PointClamp, screenUV).r;
                float sceneEye = LinearEyeDepth(sceneDepth, _ZBufferParams);
                float fragmentEye = -TransformWorldToView(input.positionWS).z;
                // A small tolerance avoids fighting the scene's own opaque depth.
                clip(sceneEye + max(0.002, fragmentEye * 0.0001) - fragmentEye);
                float height = saturate((input.positionWS.y - _SelectionHeight.x) * _SelectionHeight.y);
                return half4(normalize(input.normalWS) * 0.5 + 0.5, 1 + height);
            }
            ENDHLSL
        }
    }
}
