// The ground of garden areas the player has not unlocked yet, tinted while they are placing an item.
//
// Tinted rather than flat-washed, and latticed with the same grid the placement overlay draws, because
// a solid wash over textured terrain reads as a different kind of grass — exactly the wrong message —
// where a lattice reads as surveyed, not-yet-yours ground. Sharing the lattice with
// PlacementGrid.shader is the point: the cells line up across the boundary, so locked ground looks
// like the same garden waiting to be opened rather than a separate, stripey material.
//
// Truly desaturating the terrain underneath would need the frame grabbed and sampled back, which
// is a screen-space pass this ships too thin a phone budget for. A tinted overlay with the terrain
// reading through it costs one transparent draw and says the same thing.
//
// The lines are generated from world XZ and fed the same origin and cell size the C# lattice uses, so
// they sit on the real cell boundaries at any camera distance and do not stretch across the long quads
// the run-length mesh builder emits. Their thickness is measured in pixels, not metres, so the lattice
// stays legible underfoot and dissolves to an even tone in the distance instead of shimmering.
//
// The whole overlay fades out toward the placement radius so the mesh's own edge is never the thing
// the player sees.
//
// It draws both halves of the answer: the grey over ground the player has not unlocked, and — through
// a second material carrying green colours, a rim and a pulse — the ground they have. One shader for
// both because they are the same picture asked in opposite directions, and because two shaders would
// be two places to fix the day the lattice or the falloff changes. The rim and pulse default to zero,
// so the grey material behaves exactly as it always has.
Shader "JannahGarden/PlacementLockedGround"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.32, 0.32, 0.35, 0.72)
        _LineColor ("Grid Line Color", Color) = (0.10, 0.10, 0.12, 0.78)
        _MajorLineColor ("Major Grid Line Color", Color) = (0.06, 0.06, 0.08, 0.92)
        _CellSize ("Cell Size (metres)", Float) = 0.25
        _GridOrigin ("Grid Origin (world, xz used)", Vector) = (0, 0, 0, 0)
        _MajorEvery ("Major Line Every N Cells", Float) = 4
        _LineWidth ("Line Width (pixels)", Float) = 1.1
        _MajorLineWidth ("Major Line Width (pixels)", Float) = 1.8
        _Center ("Fade Center (world)", Vector) = (0, 0, 0, 0)
        _Radius ("Fade Radius (metres)", Float) = 12
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1

        // Zero by default, which is exactly the overlay as it shipped: the grey wants to sit still and
        // be read, so the glow below belongs to the green "this ground is yours" material alone.
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 3)) = 0
        _RimWidth ("Rim Width (fraction of a cell)", Range(0.05, 1)) = 0.55
        _Pulse ("Pulse Amount", Range(0, 1)) = 0
        _PulseSpeed ("Pulse Speed (cycles/sec)", Float) = 0.6
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
                // uv.x carries the rim weight the mesh builder wrote per lattice corner — see
                // GardenAreaMesh.RimWeight. Nothing samples a texture here.
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float rim : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _LineColor;
                float4 _MajorLineColor;
                float4 _GridOrigin;
                float4 _Center;
                float _CellSize;
                float _MajorEvery;
                float _LineWidth;
                float _MajorLineWidth;
                float _Radius;
                float _GlobalAlpha;
                float4 _RimColor;
                float _RimStrength;
                float _RimWidth;
                float _Pulse;
                float _PulseSpeed;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.rim = IN.uv.x;
                return OUT;
            }

            // Screen-space-constant line thickness, the same measure PlacementGrid uses: dividing the
            // distance-to-nearest-gridline by fwidth converts it into pixels, so a line stays the width
            // it was authored at whether the cell covers half the screen or two pixels of it. Without
            // this, a quarter-metre grid seen from the default camera height turns into a sheet of
            // aliasing.
            float LineMask(float2 coord, float widthPixels)
            {
                float2 d = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-5);
                return 1.0 - saturate(min(d.x, d.y) / max(widthPixels, 1e-5));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 world = IN.positionWS.xz;
                float2 coord = (world - _GridOrigin.xz) / max(_CellSize, 1e-4);

                float minor = LineMask(coord, _LineWidth);
                float major = LineMask(coord / max(_MajorEvery, 1.0), _MajorLineWidth);

                // Wash first, then minor lines over it, then major lines over those: each layer is a
                // plain over-blend so a line's own alpha reads as "how much darker than the ground
                // beneath me", which is how the style asset's colours are meant to be judged.
                float3 rgb = _Color.rgb;
                float alpha = _Color.a;

                float minorA = minor * _LineColor.a;
                rgb = lerp(rgb, _LineColor.rgb, minorA);
                alpha = lerp(alpha, 1.0, minorA);

                float majorA = major * _MajorLineColor.a;
                rgb = lerp(rgb, _MajorLineColor.rgb, majorA);
                alpha = lerp(alpha, 1.0, majorA);

                // The edge of the shape, brightened. The rim weight interpolates from 1 at a boundary
                // corner to 0 at the corner behind it, so the band is one cell wide however large the
                // area is — a border drawn on the zone's own outline rather than a line the player has
                // to infer from where the wash stops.
                float rim = saturate((IN.rim - (1.0 - _RimWidth)) / max(_RimWidth, 1e-4)) * _RimStrength;
                rgb = lerp(rgb, _RimColor.rgb, saturate(rim * _RimColor.a));
                alpha = lerp(alpha, 1.0, saturate(rim * _RimColor.a));

                // Same radial falloff the grid sheet uses, so the two overlays end together.
                float2 toCenter = world - _Center.xz;
                alpha *= 1.0 - smoothstep(_Radius * 0.72, _Radius, length(toCenter));

                // Breathing, so ground that has just been offered to the player reads as live rather
                // than as another painted texture. Amplitude is a fraction of the alpha the overlay
                // already has, so the pulse never brightens past what the style asked for.
                alpha *= 1.0 - _Pulse * 0.5 * (1.0 - cos(_Time.y * _PulseSpeed * 6.2831853));

                alpha *= _GlobalAlpha;

                clip(alpha - 0.002);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
