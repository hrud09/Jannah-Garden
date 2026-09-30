// Fake-volumetric cloud puffs for the garden horizon bank.
//
// Real raymarched volumetrics are out of reach here: the mobile pipeline runs at 0.8 render
// scale with no depth texture (Mobile_RPAsset m_RequireDepthTexture: 0), so there is neither
// the fill budget for a march nor a depth buffer to march against. Instead each puff is a
// single billboard whose *shading* is volumetric: density comes from a noise texture, and
// light transport is approximated with Beer-Lambert extinction plus a powder term and a
// forward-scattering lobe. That is 2 texture fetches and 2 exp() per pixel, and it reads as
// a lit three-dimensional puff rather than a flat decal.
//
// Geometry (an octagon per puff, packed into one mesh per layer) is built by CloudBank.cs.
Shader "Jannah/Environment/Volumetric Cloud Bank"
{
    Properties
    {
        [NoScaleOffset] _CloudNoise ("Cloud Noise (R: density, G: detail)", 2D) = "white" {}

        [Header(Shape)]
        // Below 1 so a puff samples only a window of the noise: the analytic radial falloff owns
        // the silhouette and the noise erodes it, which is what gives distinct lobes instead of
        // a fine all-over mottle.
        _NoiseTiling ("Noise Tiling", Float) = 0.45
        _DetailTiling ("Detail Tiling", Float) = 1.3
        _DetailStrength ("Detail Erosion", Range(0, 1)) = 0.35
        _Coverage ("Coverage Threshold", Range(0, 1)) = 0.15
        _DensityScale ("Density Contrast", Range(0.5, 8)) = 3.0
        // Above 1 so a puff's mid-density body reaches full alpha and only the rim feathers.
        // At exactly 1 the bank stays translucent everywhere and the skybox reads through it.
        _Opacity ("Opacity", Range(0, 3)) = 1.8
        // Above 1 thins and sharpens the edge, below 1 fattens it into haze.
        _SoftEdge ("Edge Falloff", Range(0.25, 4)) = 1.1

        [Header(Volumetric Lighting)]
        _LightTint ("Lit Colour", Color) = (1, 0.99, 0.96, 1)
        _ShadowTint ("Shadowed Colour", Color) = (0.72, 0.77, 0.86, 1)
        _Absorption ("Absorption (Beer)", Range(0, 8)) = 2.0
        _SelfShadowStep ("Self Shadow Step", Range(0, 0.5)) = 0.12
        _PowderStrength ("Powder", Range(0, 1)) = 0.5
        _SilverLining ("Silver Lining", Range(0, 3)) = 0.6
        _SilverPower ("Silver Lining Tightness", Range(1, 64)) = 12

        [Header(Motion)]
        _WindScroll ("Wind Scroll (xy shape, zw detail)", Vector) = (0.004, 0.0015, -0.006, 0.002)
        // x: vertical amplitude, y: vertical speed, z: horizontal amplitude, w: horizontal speed.
        _Sway ("Sway (yAmp, ySpeed, xzAmp, xzSpeed)", Vector) = (0.6, 0.25, 0, 0)
        // Oscillating tilt about the view axis. Reads as the puff churning rather than spinning,
        // because it swings back and forth instead of rotating continuously.
        _Spin ("Spin (amplitude radians, speed)", Vector) = (0, 0, 0, 0)
        // Self-advection: the noise field displaces its own lookup, so the interior boils
        // instead of sliding rigidly past. This is what separates smoke from a scrolling decal.
        [Toggle(CLOUD_TURBULENCE)] _TurbulenceOn ("Enable Turbulence", Float) = 0
        _Turbulence ("Turbulence Amount", Range(0, 0.5)) = 0.14
        _TurbulenceSpeed ("Turbulence Speed", Range(0, 2)) = 0.35

        [Header(Fading)]
        // x: distance at which the puff is fully invisible, y: distance at which it is fully on.
        // Keeps a drifting mist card from filling the screen when the player walks into it, which
        // is both an artefact and the single worst overdraw case for this shader.
        _NearFade ("Near Fade (zero at x, full at y)", Vector) = (0, 0, 0, 0)
        // Softens the puff's lower edge so where it meets a hillside there is no hard cut.
        _BottomFade ("Bottom Fade", Range(0, 1)) = 0

        [Header(Projection)]
        _VerticalBillboard ("Camera Facing (0 = upright)", Range(0, 1)) = 0.25
        _FogInfluence ("Scene Fog Influence", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "DisableBatching" = "True"
        }

        // ZWrite off / ZTest on: terrain in front still occludes the bank, which is where most of
        // the fill saving comes from - the hills cover the bottom half of the wall. Cull Off so a
        // puff is valid from either side while the billboard basis flips.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            // Drops the powder + silver-lining terms for the low quality tier.
            #pragma shader_feature_local_fragment _ CLOUD_CHEAP
            #pragma shader_feature_local_fragment _ CLOUD_TURBULENCE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_CloudNoise);
            SAMPLER(sampler_CloudNoise);

            CBUFFER_START(UnityPerMaterial)
                float  _NoiseTiling;
                float  _DetailTiling;
                float  _DetailStrength;
                float  _Coverage;
                float  _DensityScale;
                float  _Opacity;
                float  _SoftEdge;
                half4  _LightTint;
                half4  _ShadowTint;
                float  _Absorption;
                float  _SelfShadowStep;
                float  _PowderStrength;
                float  _SilverLining;
                float  _SilverPower;
                float4 _WindScroll;
                float4 _Sway;
                float4 _Spin;
                float  _Turbulence;
                float  _TurbulenceSpeed;
                float4 _NearFade;
                float  _BottomFade;
                float  _VerticalBillboard;
                float  _FogInfluence;
            CBUFFER_END

            struct Attributes
            {
                // Puff centre in object space. Every vertex of a puff shares it; the corner is
                // carried separately so the expansion happens in view-aligned world space.
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                // xy: corner offset in world units, z: per-puff random phase, w: per-puff noise offset
                float4 corner     : TEXCOORD1;
                // rgb: per-puff tint, a: per-puff density (the radial "denser at the edges" ramp)
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                // xy: noise uv origin for this puff, zw: light direction in billboard space
                float4 noiseUV    : TEXCOORD1;
                half4  color      : TEXCOORD2;
                float3 viewDirWS  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 centreWS = TransformObjectToWorld(input.positionOS);

                float phase = input.corner.z * 6.2831853;

                // Drift: a slow bob keyed off the per-puff phase so the bank never looks frozen.
                centreWS.y += sin(_Time.y * _Sway.y + phase) * _Sway.x;

                // Horizontal wander. Each puff gets its own heading from its phase, so the field
                // shears and mixes rather than sliding as one rigid sheet.
                float2 heading = float2(cos(phase * 2.3), sin(phase * 2.3));
                centreWS.xz += heading * sin(_Time.y * _Sway.w + phase) * _Sway.z;

                float3 toCamera = GetCameraPositionWS() - centreWS;
                float3 viewDirWS = normalize(toCamera);

                // Upright (Y-locked) billboarding: the bank is a horizon wall seen from inside,
                // so puffs must stay vertical or the whole ring shears as the camera pitches.
                // _VerticalBillboard blends toward full camera-facing for the overhead layers.
                float3 right = normalize(cross(float3(0, 1, 0), viewDirWS));
                float3 upFull = cross(right, viewDirWS);
                float3 up = normalize(lerp(float3(0, 1, 0), upFull, _VerticalBillboard));

                // Oscillating tilt in the billboard plane, per puff.
                float2 corner = input.corner.xy;
                float spinAngle = sin(_Time.y * _Spin.y + phase) * _Spin.x;
                float sinA, cosA;
                sincos(spinAngle, sinA, cosA);
                corner = float2(corner.x * cosA - corner.y * sinA,
                                corner.x * sinA + corner.y * cosA);

                float3 positionWS = centreWS + right * corner.x + up * corner.y;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                // Per-puff noise window, so neighbouring puffs never share a silhouette.
                float2 puffOffset = float2(input.corner.w, input.corner.z * 0.7371);
                output.noiseUV.xy = puffOffset;

                // Main light projected into the billboard basis. The fragment steps the density
                // sample along this to get a self-shadow gradient - the cheapest stand-in for
                // marching toward the sun.
                float3 L = normalize(_MainLightPosition.xyz);
                output.noiseUV.zw = float2(dot(L, right), dot(L, up));

                output.color = input.color;

                // Dissolve the puff as the camera closes on it. _NearFade.y <= 0 disables.
                if (_NearFade.y > 0.0)
                {
                    float distance = length(toCamera);
                    output.color.a *= saturate((distance - _NearFade.x) / max(0.001, _NearFade.y - _NearFade.x));
                }

                output.viewDirWS = viewDirWS;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Density of the puff at a given point of its own 0..1 footprint.
            // The analytic radial falloff keeps the silhouette round without spending a texture
            // channel (and without bilinear bleed at the quad border).
            float SampleDensity(float2 uv, float2 puffOffset, float2 scroll)
            {
                // Quadratic falloff, deliberately not squared again: a tighter core leaves each
                // puff covering only the middle of its own footprint, which is what opens gaps
                // in the ring for the player to see the void through.
                float2 c = uv * 2.0 - 1.0;
                float shape = saturate(1.0 - dot(c, c));

                float2 baseUV = uv * _NoiseTiling + puffOffset + scroll * _WindScroll.xy;
                float2 detailUV = uv * _DetailTiling + puffOffset * 1.7 + scroll * _WindScroll.zw;

            #ifdef CLOUD_TURBULENCE
                // Domain warp: one cheap tap of the same texture, scrolled on its own axis, is
                // used to push the two real lookups around. Because the displacement itself
                // moves, the density field curls and folds in place - the boiling look of smoke -
                // for one extra fetch rather than a second noise octave.
                float2 warpUV = uv * (_NoiseTiling * 0.6) + puffOffset * 2.3
                                + scroll.x * _TurbulenceSpeed * float2(0.021, -0.013);
                float2 warp = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, warpUV).rg - 0.5;

                baseUV += warp * _Turbulence;
                detailUV += warp * _Turbulence * 1.6;
            #endif

                float n = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, baseUV).r;
                float d = SAMPLE_TEXTURE2D(_CloudNoise, sampler_CloudNoise, detailUV).g;

                // Detail erodes the base field rather than adding to it, which is what keeps the
                // billowed cauliflower edge instead of a uniform blur.
                n = saturate(n - (1.0 - d) * _DetailStrength);
                return shape * n;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 puffOffset = input.noiseUV.xy;
                float2 scroll = _Time.yy;

                float raw = SampleDensity(input.uv, puffOffset, scroll);

                // Coverage carves the cloud out of the noise field; contrast sharpens the edge.
                float density = saturate((raw - _Coverage) * _DensityScale);
                density = pow(density, _SoftEdge) * input.color.a;

                // Feather the underside so a mist card crossing a hillside has no hard cut. The
                // pipeline has no depth texture, so a soft-particle fade is not available.
                if (_BottomFade > 0.0)
                {
                    density *= smoothstep(0.0, _BottomFade, input.uv.y);
                }

                clip(density - 0.004);

                // --- Volumetric shading -------------------------------------------------------
                // One extra density tap stepped toward the sun stands in for the shadow march.
                float2 lightStep = input.noiseUV.zw * _SelfShadowStep;
                float towardsLight = SampleDensity(input.uv + lightStep, puffOffset, scroll);
                float shadowDensity = saturate((towardsLight - _Coverage) * _DensityScale);

                // Beer-Lambert: how much sunlight survives the cloud above this point.
                float transmittance = exp(-shadowDensity * _Absorption);

                half3 lit = _LightTint.rgb * _MainLightColor.rgb;

            #ifdef CLOUD_CHEAP
                half3 col = lerp(_ShadowTint.rgb, lit, transmittance);
            #else
                // Powder: thin edges scatter less light back at the viewer than the deep
                // interior, so without it billboard clouds look flat and over-bright at the rim.
                float powder = 1.0 - exp(-density * 4.0);
                float scatter = transmittance * lerp(1.0, powder, _PowderStrength);

                half3 col = lerp(_ShadowTint.rgb, lit, scatter);

                // Forward scattering lobe - the bright silver rim when looking toward the sun.
                float vdotl = saturate(dot(input.viewDirWS, _MainLightPosition.xyz));
                col += lit * pow(vdotl, _SilverPower) * _SilverLining * transmittance;
            #endif

                col *= input.color.rgb;

                float alpha = saturate(density * _Opacity);

                // Partial fog: the scene runs white linear fog ending at 220m, which would erase
                // all the shading above if applied at full strength. Blend it in so the bank
                // still belongs to the scene without going flat white.
                half3 fogged = MixFog(col, input.fogFactor);
                col = lerp(col, fogged, _FogInfluence);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
