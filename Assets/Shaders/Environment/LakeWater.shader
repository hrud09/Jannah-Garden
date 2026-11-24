// Garden lake water — stylised, transparent.
//
// The mobile pipeline has no camera depth texture (Mobile_RPAsset m_RequireDepthTexture: 0), so
// the usual depth-fade tricks — colour bands by depth, shoreline foam, soft edges — cannot read
// scene depth at runtime. Instead the lake's depth is *baked* once from the terrain heightmap
// into a small R8 texture mapped over the lake's world XZ rectangle (LakeWaterTools.cs,
// Jannah/Environment/Bake Lake Water).
//
// Look: toon water. Clear shallows you can see the lakebed through, stepped colour bands that
// get more opaque with depth, crisp hard-edged sun glints, a flat stylised sky reflection,
// animated cell caustics on the bed, and two hard foam lines along the shore (a solid edge and
// a ring that washes in and out).
//
// Transparency comes from the opaque texture (enabled on both PC_ and Mobile_RPAsset), which
// lets the bed be wobbled by the ripples; the pass still alpha-blends for the soft shore edge.
Shader "Jannah/Environment/Lake Water"
{
    Properties
    {
        [Header(Depth Bands)]
        // rgb = colour, a = opacity of that band (0 = fully see-through).
        _ShallowColor ("Shallow (rgb, a = opacity)", Color) = (0.45, 0.95, 0.88, 0.15)
        _MidColor ("Mid (rgb, a = opacity)", Color) = (0.16, 0.72, 0.80, 0.55)
        _DeepColor ("Deep (rgb, a = opacity)", Color) = (0.08, 0.42, 0.72, 0.85)
        _MidDepth ("Mid Band Starts (m)", Range(0.05, 4)) = 0.7
        _DeepDepth ("Deep Band Starts (m)", Range(0.1, 5)) = 2.0
        // 0 = hard toon steps between bands, 1 = smooth gradient.
        _BandSoftness ("Band Softness", Range(0.01, 1)) = 0.25

        [Header(Ripples)]
        [NoScaleOffset][Normal] _NormalA ("Normal A", 2D) = "bump" {}
        [NoScaleOffset][Normal] _NormalB ("Normal B", 2D) = "bump" {}
        // World metres per repeat of A and B.
        _NormalTiling ("Tiling (A m, B m, -, -)", Vector) = (12, 8, 0, 0)
        _NormalStrength ("Ripple Strength", Range(0, 2)) = 0.45
        _FlowA ("Flow A (xy m/s)", Vector) = (0.12, 0.05, 0, 0)
        _FlowB ("Flow B (xy m/s)", Vector) = (-0.06, 0.1, 0, 0)
        _DetailFadeDistance ("Ripple Fade Distance", Float) = 80
        _RefractionStrength ("Lakebed Wobble", Range(0, 0.06)) = 0.018

        [Header(Stylised Light)]
        _SkyColor ("Sky Reflection Colour", Color) = (0.75, 0.92, 1, 1)
        // Blend of flat sky colour (0) vs real reflection probe (1).
        _ProbeReflection ("Use Reflection Probe", Range(0, 1)) = 0.35
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 5
        _FresnelStrength ("Reflection Strength", Range(0, 1)) = 0.6
        _GlintColor ("Glint Colour", Color) = (1, 1, 1, 1)
        _GlintThreshold ("Glint Threshold", Range(0.8, 1)) = 0.975
        _GlintSoftness ("Glint Edge Softness", Range(0.0005, 0.02)) = 0.003
        // Ripple-pattern sparkles independent of the sun angle: crisp white flecks on the crests.
        _SparkleThreshold ("Crest Sparkle Threshold", Range(0.5, 1)) = 0.86
        _SparkleStrength ("Crest Sparkle Strength", Range(0, 1)) = 0.55

        [Header(Caustics)]
        [Toggle(_LAKE_CAUSTICS)] _CausticsOn ("Enable Caustics", Float) = 1
        _CausticColor ("Caustic Colour", Color) = (1, 1, 0.9, 1)
        _CausticScale ("Caustic Cell Size (m)", Range(0.3, 6)) = 1.6
        _CausticSpeed ("Caustic Speed", Range(0, 2)) = 0.6
        _CausticWidth ("Caustic Line Width", Range(0.01, 0.4)) = 0.12
        _CausticStrength ("Caustic Strength", Range(0, 2)) = 0.7
        // Caustics only show in water shallower than this.
        _CausticMaxDepth ("Caustic Max Depth (m)", Range(0.1, 5)) = 2.2

        [Header(Shore Foam)]
        [NoScaleOffset] _DepthMap ("Baked Depth (R = depth / Max Depth)", 2D) = "white" {}
        // xy: world XZ of the map's min corner, zw: 1 / world size. Written by the bake tool.
        _DepthRect ("Depth Map Rect", Vector) = (0, 0, 0.01, 0.01)
        _DepthMax ("Depth Map Max (m)", Float) = 5
        _FoamColor ("Foam Colour", Color) = (1, 1, 1, 1)
        _FoamEdge ("Edge Foam Depth (m)", Range(0, 1)) = 0.14
        _FoamRingDepth ("Wash Ring Reach (m)", Range(0, 2)) = 0.55
        _FoamRingWidth ("Wash Ring Width (m)", Range(0.01, 0.4)) = 0.07
        _FoamRingSpeed ("Wash Ring Speed", Range(0, 2)) = 0.35
        _FoamNoise ("Foam Wobble (m)", Range(0, 0.5)) = 0.12
        _EdgeFade ("Shore Edge Fade (m)", Range(0.01, 0.5)) = 0.05

        [Header(Fog)]
        _FogInfluence ("Scene Fog Influence", Range(0, 1)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LakeWater"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _LAKE_CAUSTICS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalA); SAMPLER(sampler_NormalA);
            TEXTURE2D(_NormalB); SAMPLER(sampler_NormalB);
            TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _MidColor;
                half4 _DeepColor;
                half _MidDepth;
                half _DeepDepth;
                half _BandSoftness;
                float4 _NormalTiling;
                half _NormalStrength;
                float4 _FlowA;
                float4 _FlowB;
                float _DetailFadeDistance;
                half _RefractionStrength;
                half4 _SkyColor;
                half _ProbeReflection;
                half _FresnelPower;
                half _FresnelStrength;
                half4 _GlintColor;
                half _GlintThreshold;
                half _GlintSoftness;
                half _SparkleThreshold;
                half _SparkleStrength;
                half4 _CausticColor;
                half _CausticScale;
                half _CausticSpeed;
                half _CausticWidth;
                half _CausticStrength;
                half _CausticMaxDepth;
                float4 _DepthRect;
                float _DepthMax;
                half4 _FoamColor;
                half _FoamEdge;
                half _FoamRingDepth;
                half _FoamRingWidth;
                half _FoamRingSpeed;
                half _FoamNoise;
                half _EdgeFade;
                half _FogInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                half fogFactor    : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // Animated Voronoi edge distance (F2 - F1): 0 on cell borders. Caustic lines are
            // where this is small.
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
                float3 posWS = i.positionWS;
                float t = _Time.y;
                float3 toCam = _WorldSpaceCameraPos - posWS;
                float camDist = length(toCam);
                half3 V = toCam / max(camDist, 1e-4);
                float2 xz = posWS.xz;

                // ---- Baked lake depth -------------------------------------------------------
                float2 depthUV = (xz - _DepthRect.xy) * _DepthRect.zw;
                half depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, depthUV).r * _DepthMax;

                // ---- Ripple normal ----------------------------------------------------------
                half detailFade = saturate(1.0 - camDist / max(_DetailFadeDistance, 1));
                half ripple = _NormalStrength * (0.3 + 0.7 * detailFade);
                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalA, sampler_NormalA, (xz + _FlowA.xy * t) / _NormalTiling.x));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalB, sampler_NormalB, (xz + _FlowB.xy * t) / _NormalTiling.y));
                half2 slope = (nA.xy + nB.xy) * ripple;
                half3 N = normalize(half3(slope.x, 1.0, slope.y));

                // ---- See-through lakebed ----------------------------------------------------
                float2 screenUV = i.screenPos.xy / i.screenPos.w;
                half wobble = _RefractionStrength * saturate(depth * 2.0) * saturate(1.0 - camDist / 120.0);
                half3 bed = SampleSceneColor(screenUV + slope * wobble);

            #if defined(_LAKE_CAUSTICS)
                // Wobble the caustic lookup by the ripples so the cells shimmer, then fade them
                // out with depth and distance.
                half edge = CausticEdges((xz + slope * 0.6) / _CausticScale, t * _CausticSpeed);
                half caustic = 1.0 - smoothstep(0.0, _CausticWidth, edge);
                caustic *= saturate(1.0 - depth / _CausticMaxDepth) * saturate(depth / 0.15) * detailFade;
                bed += _CausticColor.rgb * caustic * _CausticStrength * GetMainLight().color;
            #endif

                // ---- Stepped depth bands ----------------------------------------------------
                half s = _BandSoftness * 0.5;
                half toMid = smoothstep(_MidDepth - s, _MidDepth + s, depth);
                half toDeep = smoothstep(_DeepDepth - s, _DeepDepth + s, depth);
                half4 band = lerp(_ShallowColor, _MidColor, toMid);
                band = lerp(band, _DeepColor, toDeep);
                // Tint what is seen through the water by the band colour, then lay the band's own
                // colour over it by its opacity.
                half3 col = lerp(bed * lerp(1.0.xxx, band.rgb, 0.6), band.rgb, band.a);

                // ---- Stylised reflection ----------------------------------------------------
                half NdotV = saturate(dot(N, V));
                half fresnel = pow(1.0 - NdotV, _FresnelPower) * _FresnelStrength;
                half3 R = reflect(-V, N);
                R.y = abs(R.y);
                half3 probe = GlossyEnvironmentReflection(R, posWS, 0.15, 1.0);
                half3 sky = lerp(_SkyColor.rgb, probe, _ProbeReflection);
                col = lerp(col, sky, saturate(fresnel));

                // ---- Hard-edged glints ------------------------------------------------------
                Light sun = GetMainLight();
                half3 H = normalize(sun.direction + V);
                half NdotH = saturate(dot(N, H));
                half glint = smoothstep(_GlintThreshold - _GlintSoftness, _GlintThreshold + _GlintSoftness, NdotH);
                // Crest sparkles: where the two ripple layers agree on a steep slope.
                half crest = saturate(dot(nA.xy, nB.xy) * 2.0 + length(slope));
                half sparkle = step(_SparkleThreshold, crest) * _SparkleStrength * detailFade;
                col += _GlintColor.rgb * sun.color * max(glint, sparkle);

                // ---- Toon shore foam --------------------------------------------------------
                // Ripple noise pushes the foam lines in and out so they are not contour-perfect.
                half fd = depth + (nA.x + nB.y) * _FoamNoise;
                half edgeFoam = 1.0 - step(_FoamEdge, fd);
                // A ring that starts at the shore, travels out to _FoamRingDepth and fades.
                half ringPhase = frac(t * _FoamRingSpeed);
                half ringAt = ringPhase * _FoamRingDepth;
                half ring = (1.0 - step(_FoamRingWidth * (1.0 - ringPhase * 0.6), abs(fd - ringAt - _FoamEdge)));
                ring *= 1.0 - ringPhase;
                half foam = saturate(max(edgeFoam, ring)) * _FoamColor.a;
                half3 foamLit = _FoamColor.rgb * saturate(SampleSH(half3(0, 1, 0)) + sun.color * 0.75);
                col = lerp(col, foamLit, foam);

                // ---- Fog & alpha ------------------------------------------------------------
                col = lerp(col, MixFog(col, i.fogFactor), _FogInfluence);
                half alpha = saturate(depth / _EdgeFade);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
