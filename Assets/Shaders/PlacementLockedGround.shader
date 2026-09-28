// The ground of garden areas the player has not unlocked yet, tinted while they are placing an item.
//
// Grey, and hatched rather than flat-tinted. Grey because ground that is not yours yet should read
// as drained of the colour the rest of the garden has. Hatched because a solid wash over textured
// terrain reads as a different kind of grass, which is exactly the wrong message, where diagonal
// stripes read as cordoned-off ground in any culture and survive being drawn over sand, grass and
// dirt alike.
//
// Truly desaturating the terrain underneath would need the frame grabbed and sampled back, which
// is a screen-space pass this ships too thin a phone budget for. A grey overlay with the terrain
// reading through it costs one transparent draw and says the same thing.
//
// The wash is a mid grey and the stripes are darker than it, not lighter: this garden is bright and
// sits under heavy atmospheric fog, and a light stripe over lit grass disappears at any distance.
// The stripes are also wide enough to survive being seen from across the garden rather than only
// from underfoot.
//
// The stripes are generated from world XZ, not UV, so they stay the same width everywhere and do not
// stretch across the long quads the run-length mesh builder emits. The whole overlay fades out toward
// the placement radius so the mesh's own edge is never the thing the player sees.
Shader "JannahGarden/PlacementLockedGround"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.32, 0.32, 0.35, 0.72)
        _HatchColor ("Hatch Color", Color) = (0.10, 0.10, 0.12, 0.78)
        _HatchSpacing ("Hatch Spacing (metres)", Float) = 1.3
        _HatchWidth ("Hatch Width (metres)", Float) = 0.5
        _Center ("Fade Center (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Fade Radius (metres)", Float) = 12
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            // Behind the grid sheet and the footprint highlight: this is context the player reads
            // around what they are doing, never something that should draw over the item itself.
            "Queue" = "Transparent-92"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlacementLockedGround"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _HatchColor;
                float _HatchSpacing;
                float _HatchWidth;
                float4 _Center;
                float _Radius;
                float _GlobalAlpha;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 45-degree stripes: x + z is constant along one diagonal, so the sum alone is the
                // stripe coordinate. No rotation matrix, no seams where quads meet.
                float spacing = max(_HatchSpacing, 0.01);
                float diagonal = (IN.positionWS.x + IN.positionWS.z) * 0.70710678;
                float phase = frac(diagonal / spacing) * spacing;

                // Antialiased in metres-per-pixel so the stripes stay crisp underfoot and dissolve to
                // an even tone in the distance instead of shimmering.
                float softness = max(fwidth(diagonal), 0.0001);
                float stripe = 1.0 - smoothstep(_HatchWidth - softness, _HatchWidth + softness, phase);

                float3 rgb = lerp(_Color.rgb, _HatchColor.rgb, stripe);
                float alpha = lerp(_Color.a, _HatchColor.a, stripe);

                // Same radial falloff the grid sheet uses, so the two overlays end together.
                float2 toCenter = IN.positionWS.xz - _Center.xz;
                float distance = length(toCenter);
                alpha *= 1.0 - smoothstep(_Radius * 0.72, _Radius, distance);
                alpha *= _GlobalAlpha;

                clip(alpha - 0.002);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
