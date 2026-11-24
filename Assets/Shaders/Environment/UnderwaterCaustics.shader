// Dancing caustic light on the lakebed, seen from under water. Drawn by UnderwaterEffect on a mesh it
// builds over the submerged terrain (vertices lifted a few centimetres, uv.x = water depth there), so it
// needs no depth texture — mobile has none (see Mobile_RPAsset). Additive, faded with depth and fog.
Shader "Jannah/Environment/Underwater Caustics"
{
    Properties
    {
        _CausticColor ("Caustic Colour", Color) = (0.85, 1, 0.95, 1)
        _CausticScale ("Cell Size A (m)", Range(0.3, 6)) = 1.1
        _CausticScale2 ("Cell Size B (m)", Range(0.3, 6)) = 1.9
        _CausticSpeed ("Speed", Range(0, 2)) = 0.55
        _CausticWidth ("Line Width", Range(0.01, 0.4)) = 0.1
        _CausticStrength ("Strength", Range(0, 3)) = 0.55
        _FadeDepth ("Fades Out By Depth (m)", Range(0.5, 12)) = 6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UnderwaterCaustics"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half _UnderwaterWeight;

            CBUFFER_START(UnityPerMaterial)
                half4 _CausticColor;
                half _CausticScale;
                half _CausticScale2;
                half _CausticSpeed;
                half _CausticWidth;
                half _CausticStrength;
                half _FadeDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half depth        : TEXCOORD1;
                half fogFactor    : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.depth = v.uv.x;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Animated Voronoi edge distance (F2 - F1), same as the lake's own caustics.
            half CausticEdges(float2 p, float t)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float f1 = 8.0, f2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = Hash22(cell + g);
                    o = 0.5 + 0.4 * sin(t + 6.2831 * o);
                    float d = length(g + o - f);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) { f2 = d; }
                }
                return f2 - f1;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _CausticSpeed;
                float2 xz = i.positionWS.xz;

                // Two layers at different scales and drift; their overlap gives the bright knots.
                half a = 1.0 - smoothstep(0.0, _CausticWidth, CausticEdges(xz / _CausticScale, t));
                half b = 1.0 - smoothstep(0.0, _CausticWidth * 1.3,
                                          CausticEdges(xz / _CausticScale2 + float2(17.3, 4.1), t * 0.8 + 2.0));
                half caustic = a * 0.65 + b * 0.45 + a * b * 0.8;

                half fade = saturate(1.0 - i.depth / _FadeDepth) * saturate(i.depth / 0.25);
                half3 col = _CausticColor.rgb * caustic * _CausticStrength * fade * GetMainLight().color;
                col *= _UnderwaterWeight;

                // Additive: fog takes it to black, not to the fog colour.
                col = MixFogColor(col, half3(0, 0, 0), i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
