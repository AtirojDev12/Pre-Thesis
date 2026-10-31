Shader "PreThesis/VFX/Black Water"
{
    Properties
    {
        _Fade ("Fade", Range(0,1)) = 1
        _Phase ("Phase", Float) = 0
        _Impact ("Impact", Float) = 0
        _Resolve ("Resolve", Range(0,1)) = 0
        _Horror ("Resolve atmosphere", Range(0,1)) = 0
        _Eruption ("Water eruption", Range(0,2)) = 0
        _Absorb ("Soak into floor", Range(0,1)) = 0
        _Opacity ("Water opacity", Range(0,1)) = .94
        _EdgeSoftness ("Soft wet edge", Range(.04,.3)) = .18
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
                float _Fade, _Phase, _Impact, _Resolve, _Horror, _Mist, _Eruption;
                float _Absorb, _Opacity, _EdgeSoftness;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; float2 uv:TEXCOORD1; float4 color:COLOR; };
            float Pulse(float time, float start, float duration)
            {
                float t=saturate((time-start)/duration);
                return sin(t*3.14159265)*sin(t*3.14159265);
            }
            float WaterHeight(float2 p)
            {
                // atan2(0,0) is undefined on some GPUs and can remove the centre triangles.
                float r=length(p), a=atan2(p.y,p.x+0.00001);
                float edge=1-smoothstep(.60,.78,r);
                float opening=Pulse(_Phase,.08,.85);
                float breach=Pulse(_Phase,.82,1.05);
                float crownRadius=.14+.22*saturate((_Phase-.12)/.75)
                    +.045*sin(a*3+1.2)+.025*sin(a*7-_Phase*2);
                float crown=exp(-pow((r-crownRadius)/.065,2));
                float fingers=.035+.965*pow(saturate(.5+.5*sin(a*7+.6*sin(a*3))),4);
                float mound=exp(-r*r*42)*(.28*opening+.16*breach);
                float surge=crown*fingers*(.65*opening+.40*breach);
                float ring=sin(r*36-_Phase*12)*(.001+.004*(opening+breach));
                // Ripple troughs must stay above the floor, including at maximum height.
                return max(0,mound+surge+ring*r)*edge*_Eruption*(1-_Absorb);
            }
            V Vert(A v)
            {
                V o;
                if (_Mist < .5) v.vertex.z-=WaterHeight(v.uv*2-1);
                o.world=TransformObjectToWorld(v.vertex.xyz);
                o.position=TransformWorldToHClip(o.world); o.uv=v.uv; o.color=v.color; return o;
            }
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
                float radius=length(p), angle=atan2(p.y,p.x+0.00001);
                float agitation=_Eruption*(Pulse(_Phase,.08,.85)+Pulse(_Phase,.82,1.05));
                float edge=.79+.065*sin(angle*5+.7)+.035*sin(angle*9-1.2)+.018*sin(angle*17)
                    +.018*agitation*sin(angle*23-_Phase*10);
                float wetEdge=1-smoothstep(edge-_EdgeSoftness,edge+.025,radius);
                float pores=saturate(Noise(p*7)*.6+Noise(p*17+3.1)*.2
                    +.2*(1-saturate(radius/.8)));
                // Uneven absorption leaves soft wet patches in place rather than shrinking the quad.
                float absorbed=smoothstep(pores*.45,.55+pores*.45,_Absorb);
                float alpha=wetEdge*(1-absorbed)*_Fade*_Opacity;
                clip(alpha-.001);
                float still=1-smoothstep(.8,1,_Resolve);
                float waveTime=_Phase*lerp(1,still,_Horror*(1-saturate(_Eruption)));
                // Positive time makes phase fronts travel inward, against the original outward ripples.
                float wave=lerp(sin(radius*42-waveTime*9),sin(radius*42+waveTime*7),_Horror)
                    *exp(-radius*1.4)*(.015+.085*_Impact)*lerp(1,still,_Horror);
                float3 normal=normalize(float3(p.x*wave,1,p.y*wave));
                float3 geometryNormal=normalize(cross(ddy(i.world),ddx(i.world)));
                geometryNormal*=geometryNormal.y < 0 ? -1 : 1;
                normal=normalize(lerp(normal,geometryNormal,saturate(_Eruption)));
                float chop=(Noise(p*18+_Phase*.7)-.5)*.08*_Eruption;
                normal=normalize(normal+float3(chop,0,chop*.7));
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
                // Broken, restrained silver rims describe moving water without a glowing portal ring.
                float crest=saturate(WaterHeight(p)*5);
                float ripples=pow(saturate(sin(radius*48-_Phase*13)),18)
                    *exp(-radius*1.6)*saturate(agitation+.12*_Eruption);
                float brokenHighlight=smoothstep(.36,.74,Noise(p*23+_Phase*.4));
                color.rgb+=half3(.11,.15,.17)*(crest*.30*brokenHighlight+ripples*.08);
                color.a=alpha;
                return color;
            }
            ENDHLSL
        }
    }
}
