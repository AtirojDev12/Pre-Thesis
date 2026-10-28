Shader "PreThesis/VFX/Item Impact"
{
    Properties { _Shape("Shape: volume / streak / ring", Float) = 0 _Distortion("Air refraction", Range(0,0.003)) = 0 }
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes {
                float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR;
                float3 cloudCenter:TEXCOORD1; float3 cloudRadii:TEXCOORD2; float2 cloudState:TEXCOORD3;
            };
            struct Varyings {
                float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; half3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2;
                nointerpolation float3 cloudCenter:TEXCOORD3; nointerpolation float3 cloudRadii:TEXCOORD4;
                nointerpolation float2 cloudState:TEXCOORD5;
            };
            CBUFFER_START(UnityPerMaterial)
                float _Shape, _Distortion;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.uv = input.uv; o.color = input.color;
                o.cloudCenter = input.cloudCenter; o.cloudRadii = input.cloudRadii; o.cloudState = input.cloudState;
                return o;
            }
            float Hash(float3 p)
            {
                p = frac(p * .1031); p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise3D(float3 p)
            {
                float3 cell = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x),
                                 lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x),
                                 lerp(Hash(cell + float3(0,1,1)), Hash(cell + 1), f.x), f.y), f.z);
            }
            float SmokeVolume(Varyings i, bool frontFace, out float3 normalWS)
            {
                float3 cameraOS = TransformWorldToObject(GetCameraPositionWS());
                float3 radii = max(i.cloudRadii, .0001);
                float3 origin = (cameraOS - i.cloudCenter) / radii;
                bool inside = all(abs(origin) < 1);
                // Draw one box surface per ray; the box is only a bound for the volume integral.
                if (inside == frontFace) discard;
                float3 direction = normalize(TransformWorldToObject(i.positionWS) - cameraOS);
                float3 ray = direction / radii;
                float a = dot(ray, ray), b = dot(origin, ray), c = dot(origin, origin) - 1;
                float discriminant = b * b - a * c;
                if (discriminant <= 0) discard;
                float entry = max(0, (-b - sqrt(discriminant)) / a);
                float exit = (-b + sqrt(discriminant)) / a;
                if (_CameraDepthTexture_TexelSize.z > 1)
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    float depth = SampleSceneDepth(screenUV);
                    #if !UNITY_REVERSED_Z
                        depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                    #endif
                    float3 opaqueWS = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                    exit = min(exit, dot(TransformWorldToObject(opaqueWS) - cameraOS, direction));
                }
                if (exit <= entry) discard;
                const int steps = 12;
                float stepLength = (exit - entry) / steps;
                float3 offset = i.cloudState.x + i.cloudState.y * float3(.6, -.8, .35);
                float transmission = 1;
                float3 normalSum = 0;
                [unroll] for (int s = 0; s < steps; s++)
                {
                    float3 p = origin + ray * (entry + (s + .5) * stepLength);
                    float noise = .65 * Noise3D(p * 3.5 + offset) + .35 * Noise3D(p * 7.4 + offset * 1.7);
                    float density = (1 - smoothstep(.08, 1, dot(p, p))) * smoothstep(.23, .72, noise);
                    float absorption = 1 - exp(-density * stepLength / max(.001, (radii.x + radii.y + radii.z) / 3) * i.color.a * 6);
                    normalSum += p / radii * transmission * absorption;
                    transmission *= 1 - absorption;
                }
                normalWS = TransformObjectToWorldNormal(normalize(normalSum + float3(0, .0001, 0)));
                return 1 - transmission;
            }
            half4 frag(Varyings i, FRONT_FACE_TYPE facing:FRONT_FACE_SEMANTIC):SV_Target
            {
                clip(i.color.a - .0001);
                float2 p = i.uv * 2 - 1;
                float r = length(p), angle = atan2(p.y, p.x);
                float wisps = .65 + .35 * sin(p.x * 13 + sin(p.y * 9)) * sin(p.y * 11);
                float mask = (1 - smoothstep(.08, 1, r)) * wisps;
                float3 normalWS = normalize(i.normalWS + float3(0, .00001, 0));
                float alpha = 0;
                if (_Shape < .5)
                    alpha = SmokeVolume(i, IS_FRONT_VFACE(facing, true, false), normalWS);
                if (_Shape > .5 && _Shape < 1.5)
                {
                    float taper = lerp(1, .08, i.uv.y);
                    mask = (1 - smoothstep(taper * .2, taper, abs(p.x))) * sin(i.uv.y * PI);
                }
                if (_Shape > 1.5)
                {
                    float edge = .80 + .025 * sin(angle * 7 + .6) + .02 * sin(angle * 13);
                    float broken = smoothstep(-.25, .65, sin(angle * 5 + .8) + .45 * sin(angle * 11));
                    mask = (1 - smoothstep(.015, .10, abs(r - edge))) * broken;
                }
                InputData lighting = (InputData)0;
                lighting.positionWS = i.positionWS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);
                lighting.vertexLighting = VertexLighting(i.positionWS, lighting.normalWS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = i.color.rgb;
                surface.occlusion = 1;
                surface.alpha = 1;
                half3 tint = UniversalFragmentBlinnPhong(lighting, surface).rgb;
                tint = MixFog(tint, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                if (_Shape > 1.5 && _Distortion > 0 && _CameraOpaqueTexture_TexelSize.z > 1)
                {
                    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    tint = lerp(tint, SampleSceneColor(screenUV + p * _Distortion * mask), .85);
                }
                return half4(tint, _Shape < .5 ? alpha : i.color.a * mask);
            }
            ENDHLSL
        }
    }
}
