// Procedural placement lattice drawn on the ground while the player is positioning an item.
//
// The lines are generated from world XZ rather than sampled from a texture, so the grid stays crisp at
// any camera distance, costs no memory, and — crucially — lines up with the real GardenGrid cells no
// matter where the terrain sits, because it is fed the same origin and cell size the C# lattice uses.
//
// Drawn by PlacementGridView on a terrain-following mesh: depth-testing but not depth-writing, with a
// small polygon offset so it hugs the ground instead of z-fighting with it.
Shader "JannahGarden/PlacementGrid"
{
    Properties
    {
        _MinorColor ("Minor Line Color", Color) = (1, 1, 1, 0.22)
        _MajorColor ("Major Line Color", Color) = (1, 1, 1, 0.45)
        _CellSize ("Cell Size", Float) = 0.25
        _GridOrigin ("Grid Origin (world, xz used)", Vector) = (0, 0, 0, 0)
        _MajorEvery ("Major Line Every N Cells", Float) = 4
        _LineWidth ("Line Width (pixels)", Float) = 1.1
        _Radius ("Fade Radius", Float) = 15
        _FadeStart ("Fade Start (fraction of radius)", Range(0, 1)) = 0.55
        _Center ("Fade Center (world, xz used)", Vector) = (0, 0, 0, 0)
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlacementGrid"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
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
                float4 _MinorColor;
                float4 _MajorColor;
                float4 _GridOrigin;
                float4 _Center;
                float _CellSize;
                float _MajorEvery;
                float _LineWidth;
                float _Radius;
                float _FadeStart;
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

            // Screen-space-constant line thickness: dividing the distance-to-nearest-gridline by
            // fwidth converts it into pixels, so lines stay one pixel wide whether the cell covers
            // half the screen or two pixels of it. Without this, a 0.25 m grid viewed from the
            // default camera height turns into a solid sheet of aliasing.
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
                float major = LineMask(coord / max(_MajorEvery, 1.0), _LineWidth);

                float3 rgb = lerp(_MinorColor.rgb, _MajorColor.rgb, major);
                float alpha = max(minor * _MinorColor.a, major * _MajorColor.a);

                float radial = 1.0 - smoothstep(_FadeStart * _Radius, _Radius, distance(world, _Center.xz));
                alpha *= radial * _GlobalAlpha;

                clip(alpha - 0.002);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
