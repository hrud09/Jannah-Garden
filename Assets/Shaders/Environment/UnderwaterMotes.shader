// Suspended particles and tiny rising bubbles around the camera under water. The whole effect is one
// static mesh of quads drawn by UnderwaterEffect: each quad's position is wrapped inside a box that
// follows the camera (the classic "weather box"), so it costs no CPU and needs no particle system.
//
// Mesh layout: POSITION = per-quad random seed in [0,1]^3 (same for all four corners),
// TEXCOORD0.xy = corner (-1..1), TEXCOORD0.z = size (m), TEXCOORD0.w = random phase.
Shader "Jannah/Environment/Underwater Motes"
{
    Properties
    {
        _MoteColor ("Colour", Color) = (0.85, 1, 0.95, 0.55)
        _BoxSize ("Box Size (m)", Vector) = (12, 7, 12, 0)
        _Rise ("Rise Speed (m/s)", Float) = 0.12
        _Sway ("Sway (m)", Float) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UnderwaterMotes"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half _UnderwaterWeight;
            float _UnderwaterSurfaceY;

            CBUFFER_START(UnityPerMaterial)
                half4 _MoteColor;
                float4 _BoxSize;
                float _Rise;
                float _Sway;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half alpha : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float t = _Time.y;
                float phase = v.uv.w * 6.2831;
                float3 box = _BoxSize.xyz;
                float3 cam = _WorldSpaceCameraPos;

                float3 drift = float3(sin(t * 0.31 + phase) * _Sway, _Rise * t * (0.5 + v.uv.w), cos(t * 0.23 + phase * 1.7) * _Sway);
                float3 p = frac(v.positionOS.xyz + (drift - cam) / box);
                float3 world = cam + (p - 0.5) * box;

                // Fade at the box walls so wrapping never pops, and right in front of the lens.
                float3 edge = (0.5 - abs(p - 0.5)) * 2.0;
                half fade = saturate(min(edge.x, min(edge.y, edge.z)) * 4.0);
                fade *= saturate((distance(world, cam) - 0.35) * 2.0);
                // Nothing above the water line.
                fade *= step(world.y, _UnderwaterSurfaceY - 0.05);

                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                world += (right * v.uv.x + up * v.uv.y) * v.uv.z;

                o.positionCS = TransformWorldToHClip(world);
                o.corner = v.uv.xy;
                o.alpha = fade * _UnderwaterWeight;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = saturate(1.0 - dot(i.corner, i.corner));
                half a = d * d * _MoteColor.a * i.alpha;
                half3 light = SampleSH(half3(0, 1, 0)) + GetMainLight().color * 0.5;
                half3 col = _MoteColor.rgb * light;
                // Additive: fade with fog rather than tinting toward it.
                a *= ComputeFogIntensity(i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
