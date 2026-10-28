Shader "PreThesis/VFX/Black Water"
{
    Properties
    {
        _Fade ("Fade", Range(0,1)) = 1
        _Phase ("Phase", Float) = 0
        _Impact ("Impact", Float) = 0
        _Resolve ("Resolve", Range(0,1)) = 0
        _Horror ("Resolve atmosphere", Range(0,1)) = 0
        [HideInInspector] _Mist ("Smoke billboards", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Fade, _Phase, _Impact, _Resolve, _Horror, _Mist;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; float4 color:COLOR; };
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.vertex.xyz); o.position=TransformWorldToHClip(o.world); o.uv=v.uv; o.color=v.color; return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                    lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                if (_Mist > .5)
                {
                    float noise=Noise(i.uv*5)*.65+Noise(i.uv*11+4.7)*.35;
                    float soft=saturate((1-length(p))*2.1);
                    return half4(i.color.rgb,soft*soft*smoothstep(.18,.78,noise)*i.color.a);
                }
                float radius=length(p), angle=atan2(p.y,p.x);
                float edge=.79+.065*sin(angle*5+.7)+.035*sin(angle*9-1.2)+.018*sin(angle*17);
                float alpha=(1-smoothstep(edge-.028,edge,radius))*_Fade;
                clip(alpha-.001);
                float still=1-smoothstep(.8,1,_Resolve);
                float waveTime=_Phase*lerp(1,still,_Horror);
                // Positive time makes phase fronts travel inward, against the original outward ripples.
                float wave=lerp(sin(radius*42-waveTime*9),sin(radius*42+waveTime*7),_Horror)
                    *exp(-radius*1.4)*(.015+.085*_Impact)*lerp(1,still,_Horror);
                float3 normal=normalize(float3(p.x*wave,1,p.y*wave));
                InputData input=(InputData)0;
                input.positionWS=i.world;
                input.normalWS=normal;
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);
                input.shadowCoord=TransformWorldToShadowCoord(i.world);
                input.bakedGI=SampleSH(normal);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.position);
                input.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=half3(.006,.008,.011);
                surface.metallic=.25;
                surface.smoothness=.96;
                surface.normalTS=half3(0,0,1);
                surface.occlusion=1;
                surface.alpha=alpha;
                half4 color=UniversalFragmentPBR(input,surface);
                // Thin grazing highlights keep the black surface readable in a dark room.
                float rim=pow(1-saturate(dot(normal,input.viewDirectionWS)),5);
                color.rgb+=half3(.055,.075,.085)*rim;
                // Frost stays inside the existing pool quad; no floor material or decal changes.
                float veins=pow(saturate(Noise(p*38)+Noise(p*71)*.4-.5),3);
                float frost=smoothstep(edge-.19,edge-.025,radius)*veins
                    *smoothstep(.3,.9,_Resolve)*_Horror;
                color.rgb=lerp(color.rgb,half3(.38,.49,.55),saturate(frost*2));
                color.a=alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
