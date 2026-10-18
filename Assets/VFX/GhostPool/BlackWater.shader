Shader "PreThesis/VFX/Black Water"
{
    Properties
    {
        _Fade ("Fade", Range(0,1)) = 1
        _Phase ("Phase", Float) = 0
        _Impact ("Impact", Float) = 0
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
                float _Fade, _Phase, _Impact;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; };
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.vertex.xyz); o.position=TransformWorldToHClip(o.world); o.uv=v.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float radius=length(p), angle=atan2(p.y,p.x);
                float edge=.79+.065*sin(angle*5+.7)+.035*sin(angle*9-1.2)+.018*sin(angle*17);
                float alpha=(1-smoothstep(edge-.028,edge,radius))*_Fade;
                clip(alpha-.001);
                float wave=sin(radius*42-_Phase*9)*exp(-radius*1.4)*(.015+.085*_Impact);
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
                color.a=alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
