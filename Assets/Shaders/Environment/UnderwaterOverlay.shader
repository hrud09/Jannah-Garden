// Full-screen underwater grade, drawn by UnderwaterEffect on a quad just past the near plane of the main
// camera (and only that camera). Slanted light shafts from the surface, a water-coloured tint and a deep
// vignette. Screen Space - Overlay UI draws after it, so the HUD is untouched.
Shader "Jannah/Environment/Underwater Overlay"
{
    Properties
    {
        _RayColor ("Light Shaft Colour", Color) = (0.75, 1, 0.95, 1)
        _RayStrength ("Light Shaft Strength", Range(0, 1)) = 0.28
        _RaySlant ("Light Shaft Slant", Range(-1, 1)) = 0.35
        _TintStrength ("Water Wash", Range(0, 1)) = 0.32
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.6
        _VignetteStart ("Vignette Start", Range(0, 1)) = 0.32
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UnderwaterOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Set globally by UnderwaterEffect.
            half4 _UnderwaterColor;
            half _UnderwaterWeight;
            half4 _UnderwaterParams; // x = look-up (-1..1), y = depth 0..1, z = daylight 0..1

            CBUFFER_START(UnityPerMaterial)
                half4 _RayColor;
                half _RayStrength;
                half _RaySlant;
                half _TintStrength;
                half _VignetteStrength;
                half _VignetteStart;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;
                half lookUp = _UnderwaterParams.x;
                half depth01 = _UnderwaterParams.y;
                half daylight = _UnderwaterParams.z;

                // Light shafts: a few slanted sine bands at unrelated frequencies, sharpened.
                float s = uv.x + uv.y * _RaySlant;
                half r = (sin(s * 23.0 + t * 0.70) * 0.5 + 0.5) * (sin(s * 41.0 - t * 0.45 + 1.3) * 0.5 + 0.5);
                r += (sin(s * 11.0 + t * 0.27 + 4.0) * 0.5 + 0.5) * 0.45;
                r = pow(saturate(r * 0.8), 3.0);
                r *= smoothstep(0.05, 1.0, uv.y);                 // they come down from the surface
                r *= 0.45 + 0.55 * saturate(lookUp + 0.35);        // strongest looking up into them
                r *= 1.0 - depth01 * 0.75;                         // and they die out with depth
                r *= daylight;
                half3 rays = _RayColor.rgb * r * _RayStrength;

                // Water absorbs colour even up close, so everything gets a wash of the water colour
                // (more of it with depth), then a darker vignette on top.
                half2 c = (uv - 0.5) * half2(1.25, 1.0);
                half v = smoothstep(_VignetteStart, 0.95, length(c)) * _VignetteStrength;
                half tint = saturate(_TintStrength * (0.75 + depth01 * 0.6));
                half3 water = _UnderwaterColor.rgb;
                half3 rgb = water * tint;                          // premultiplied wash
                rgb = rgb * (1.0 - v) + water * 0.4 * v;           // vignette over it
                half a = 1.0 - (1.0 - tint) * (1.0 - v);

                half w = _UnderwaterWeight;
                return half4((rgb + rays) * w, a * w);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
