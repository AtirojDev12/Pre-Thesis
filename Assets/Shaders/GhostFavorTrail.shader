Shader "PreThesis/VFX/Ghost Favor Blood Trail"
{
    Properties
    {
        _BloodTexture("Optional blood stamp (RGBA)", 2D) = "white" {}
        _BloodColor("Blood tint", Color) = (.3,.012,.025,.9)
        [HDR] _SweepColor("Sweep tint", Color) = (1,.2,.12,1)
        _MinimumVisibility("Minimum visibility", Range(0,1)) = .2
        _SweepIntensity("Sweep intensity", Float) = 1.4
        _SweepDuration("Sweep duration", Float) = 1.5
        _SweepPause("Pause between sweeps", Float) = 9
        _SweepWidth("Sweep width (metres)", Float) = 1.8
        _VisibleDistance("Visible distance", Float) = 28
        _DistanceFade("Distance fade", Float) = 5
        [HideInInspector] _UseBloodTexture("Use texture", Float) = 0
        [HideInInspector] _Opacity("Lifecycle fade", Float) = 1
        [HideInInspector] _TrailTime("Shared route time", Float) = 0
        [HideInInspector] _RouteLength("Route length", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "BloodTrail"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BloodTexture); SAMPLER(sampler_BloodTexture);
            CBUFFER_START(UnityPerMaterial)
                float4 _BloodColor, _SweepColor;
                float _MinimumVisibility, _SweepIntensity, _SweepDuration, _SweepPause, _SweepWidth;
                float _VisibleDistance, _DistanceFade, _UseBloodTexture, _Opacity, _TrailTime, _RouteLength;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 route : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 route : TEXCOORD3;
                half fog : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.route = input.route;
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                float seed = input.route.y * 31.7;
                float wobble = sin(uv.y * 26 + seed) * .018 + sin(uv.y * 51 - seed) * .009;
                float drop = length((uv - float2(.49 + wobble,.3)) / float2(.24,.26));
                float tailWidth = lerp(.15,.018,saturate((uv.y - .32) / .6));
                float tail = max(abs(uv.x - .5 - wobble) / tailWidth, abs(uv.y - .56) / .39);
                float smallDrop = length((uv - float2(.78,.66)) / float2(.055,.095));
                float shape = min(drop, min(tail, smallDrop));
                float edge = max(fwidth(shape),.025);
                float procedural = 1 - smoothstep(1 - edge,1 + edge,shape);
                half4 stamp = SAMPLE_TEXTURE2D(_BloodTexture, sampler_BloodTexture, uv);
                float mask = lerp(procedural, stamp.a, _UseBloodTexture);
                float distanceFade = saturate((_VisibleDistance - distance(input.positionWS, GetCameraPositionWS())) / max(.1,_DistanceFade));
                float alpha = mask * _BloodColor.a * _Opacity * distanceFade;
                clip(alpha - .008);
                float elapsed = fmod(_TrailTime, max(.3,_SweepDuration + _SweepPause));
                float head = elapsed / max(.2,_SweepDuration);
                float width = _SweepWidth / max(.1,_RouteLength);
                float pulse = (1 - smoothstep(0,max(.001,width),abs(input.route.x - head))) * step(elapsed,_SweepDuration);
                Light mainLight = GetMainLight();
                half3 illumination = SampleSH(normalize(input.normalWS)) + mainLight.color * saturate(dot(input.normalWS, mainLight.direction));
                illumination = max(illumination, _MinimumVisibility.xxx);
                half3 tint = _BloodColor.rgb * lerp(half3(1,1,1),stamp.rgb,_UseBloodTexture);
                half3 color = tint * illumination + _SweepColor.rgb * pulse * _SweepIntensity;
                return half4(MixFog(color,input.fog),alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
