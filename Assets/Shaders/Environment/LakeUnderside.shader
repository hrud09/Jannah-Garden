// The lake surface seen from below, drawn by UnderwaterEffect only while the camera is under water.
//
// The lake's own shader culls back faces, so from underneath the surface simply vanished and the sky
// showed straight through. This draws the same mesh front-face-culled: looking up steeply you see out
// through Snell's window (bright, rippling, with the sun smeared across it); at shallower angles the
// surface is a total-internal-reflection mirror of the water body itself, which is what makes it read
// as a ceiling of water rather than a hole.
Shader "Jannah/Environment/Lake Underside"
{
    Properties
    {
        [NoScaleOffset][Normal] _NormalA ("Normal A", 2D) = "bump" {}
        [NoScaleOffset][Normal] _NormalB ("Normal B", 2D) = "bump" {}
        _NormalTiling ("Tiling (A m, B m, -, -)", Vector) = (12, 8, 0, 0)
        _NormalStrength ("Ripple Strength", Range(0, 2)) = 0.7
        _FlowA ("Flow A (xy m/s)", Vector) = (0.12, 0.05, 0, 0)
        _FlowB ("Flow B (xy m/s)", Vector) = (-0.06, 0.1, 0, 0)

        _WindowColor ("Window Colour (sky seen from below)", Color) = (0.78, 0.96, 1, 1)
        _WindowBrightness ("Window Brightness", Range(0, 3)) = 1.35
        _WindowCos ("Window Edge (cos of critical angle)", Range(0.3, 0.95)) = 0.66
        _WindowSoftness ("Window Edge Softness", Range(0.01, 0.3)) = 0.09
        _MirrorTint ("Mirror Tint (x water colour)", Range(0, 2)) = 1.05
        _SunSpot ("Sun Spot Strength", Range(0, 4)) = 1.6
        _SparkleStrength ("Ripple Sparkle", Range(0, 1)) = 0.35

        [NoScaleOffset] _DepthMap ("Baked Depth (R = depth / Max Depth)", 2D) = "white" {}
        _DepthRect ("Depth Map Rect", Vector) = (0, 0, 0.01, 0.01)
        _DepthMax ("Depth Map Max (m)", Float) = 5
        _EdgeFade ("Shore Edge Fade (m)", Range(0.01, 0.5)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-11"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LakeUnderside"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);

            // Set globally by UnderwaterEffect.
            half4 _UnderwaterColor;

            CBUFFER_START(UnityPerMaterial)
                float4 _NormalTiling;
                half _NormalStrength;
                float4 _FlowA;
                float4 _FlowB;
                half4 _WindowColor;
                half _WindowBrightness;
                half _WindowCos;
                half _WindowSoftness;
                half _MirrorTint;
                half _SunSpot;
                half _SparkleStrength;
                float4 _DepthRect;
                float _DepthMax;
                half _EdgeFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fogFactor    : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 posWS = i.positionWS;
                float t = _Time.y;
                float2 xz = posWS.xz;

                float3 toSurface = posWS - _WorldSpaceCameraPos;
                half3 V = normalize(toSurface);

                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, (xz + _FlowA.xy * t) / _NormalTiling.x));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, (xz + _FlowB.xy * t) / _NormalTiling.y));
                half2 slope = (nA.xy + nB.xy) * _NormalStrength;

                // Ripples bend the view ray, which is what makes the window's edge and the sun wobble.
                half3 Vr = normalize(V + half3(slope.x, 0, slope.y) * 0.6);

                half window = smoothstep(_WindowCos - _WindowSoftness, _WindowCos + _WindowSoftness, Vr.y);

                Light sun = GetMainLight();
                half3 skyLight = SampleSH(half3(0, 1, 0)) + sun.color * 0.6;
                half3 windowCol = _WindowColor.rgb * skyLight * _WindowBrightness;
                windowCol *= 0.85 + 0.3 * (nA.x + nB.y);

                // The sun through the surface: a bright, ripple-broken smear.
                half sunDot = saturate(dot(Vr, sun.direction));
                windowCol += sun.color * (pow(sunDot, 48.0) * _SunSpot + pow(sunDot, 6.0) * 0.25);

                // Crest sparkles where the ripple layers agree.
                half crest = saturate(dot(nA.xy, nB.xy) * 2.0 + length(slope));
                windowCol += sun.color * step(0.82, crest) * _SparkleStrength;

                // Outside the window: total internal reflection of the water body.
                half3 mirror = _UnderwaterColor.rgb * _MirrorTint * (0.9 + 0.2 * nA.y);

                half3 col = lerp(mirror, windowCol, window);
                col = MixFog(col, i.fogFactor);

                float2 depthUV = (xz - _DepthRect.xy) * _DepthRect.zw;
                half depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, depthUV).r * _DepthMax;
                half alpha = saturate(depth / _EdgeFade);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
