Shader "Hidden/RetroHorrorFilter"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "RetroHorrorFilterPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Blit.hlsl provides: Attributes/Varyings, the fullscreen-triangle Vert(),
            // _BlitTexture, sampler_LinearClamp/sampler_PointClamp, and _BlitTexture_TexelSize.
            // This is the required include for URP 17 / Unity 6 Render Graph blit passes.
            // NOTE: must include Core.hlsl (not just Common.hlsl) first — Blit.hlsl needs
            // the TEXTURE2D_X / XR texture macros that only Core.hlsl pulls in.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Effect parameters (set from C#)
            float _PixelSize;          // e.g. 4 = chunky pixel blocks. 1 or less = off
            float _ScanlineIntensity;  // 0-1
            float _ScanlineCount;      // number of lines across screen height
            float _ChromaticAberration;// pixel offset amount
            float _VignetteIntensity;  // 0-1
            float _VignetteSmoothness; // 0-1
            float _GrainIntensity;     // 0-1
            float _GrainSize;          // grain scale
            float3 _ColorTint;         // multiply tint, e.g. (0.85,1.0,0.82) sickly pale-green
            float _Desaturation;       // 0-1, 1 = fully grayscale before tint
            float _Time01;             // running time (seconds), passed from C#
            float _FlickerIntensity;   // 0-1 brightness flicker (VHS)

            float rand(float2 co)
            {
                return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _BlitTexture_TexelSize.zw; // (width, height) in pixels

                // ---- Pixelation ----
                if (_PixelSize > 1.0)
                {
                    float2 blockSize = _PixelSize / texelSize;
                    uv = floor(uv / blockSize) * blockSize + blockSize * 0.5;
                }

                // ---- Chromatic Aberration ----
                float2 caOffset = (uv - 0.5) * _ChromaticAberration * 0.01;
                float r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - caOffset).r;
                float g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                float b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + caOffset).b;
                float3 col = float3(r, g, b);

                // ---- Desaturation ----
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, luma.xxx, _Desaturation);

                // ---- Color Tint ----
                col *= _ColorTint;

                // ---- Scanlines ----
                float scanline = sin(uv.y * _ScanlineCount * PI * 2.0) * 0.5 + 0.5;
                col *= lerp(1.0, scanline, _ScanlineIntensity);

                // ---- Film Grain ----
                float2 grainUV = uv * texelSize / max(_GrainSize, 0.001) + _Time01 * 60.0;
                float grain = rand(grainUV) - 0.5;
                col += grain * _GrainIntensity;

                // ---- VHS Brightness Flicker ----
                float flicker = 1.0 + (rand(float2(_Time01 * 10.0, 0.0)) - 0.5) * _FlickerIntensity;
                col *= flicker;

                // ---- Vignette ----
                float2 vigUV = uv - 0.5;
                float vig = length(vigUV) * 1.414213;
                vig = smoothstep(1.0, 1.0 - _VignetteSmoothness, vig * _VignetteIntensity + (1.0 - _VignetteIntensity));
                col *= saturate(vig + (1.0 - _VignetteIntensity));

                return float4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
