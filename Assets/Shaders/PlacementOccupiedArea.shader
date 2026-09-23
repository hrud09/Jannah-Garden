// Outlines drawn over every cell block an existing item already claims, so a player aiming a new
// item can see at a glance which ground is spoken for before they ever touch the crosshair.
//
// Deliberately a separate shader from PlacementFootprint rather than a second draw of it: this one
// draws every occupied item in a single mesh/draw call, so footprint size has to travel per-vertex
// (TEXCOORD1) instead of the single material-wide _SizeMeters that shader relies on. There is no
// interior cell subdivision either — with dozens of footprints on screen at once the cell lines would
// just be noise; the border alone answers "is this ground free".
Shader "JannahGarden/PlacementOccupiedArea"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.85, 0.85, 0.9, 0)
        _BorderColor ("Border Color", Color) = (0.9, 0.92, 1, 0.75)
        _BorderWidth ("Border Width (metres)", Float) = 0.06
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-91"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlacementOccupiedArea"
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
                float2 uv : TEXCOORD0;
                // Footprint size in metres for the quad this vertex belongs to, xy = width/depth.
                // Carried per-vertex (identical across a quad's four corners) because many differently
                // sized footprints share one mesh/draw call.
                float2 sizeMeters : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 sizeMeters : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BorderColor;
                float _BorderWidth;
                float _GlobalAlpha;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.uv = IN.uv;
                OUT.sizeMeters = IN.sizeMeters;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Distance to the nearest edge in metres, not UV: a 3x1 footprint would otherwise get a
                // border three times thicker on one axis than the other.
                float2 metres = IN.uv * IN.sizeMeters;
                float2 toEdge = min(metres, IN.sizeMeters - metres);
                float edge = min(toEdge.x, toEdge.y);
                float border = 1.0 - smoothstep(_BorderWidth * 0.5, _BorderWidth, edge);

                float3 rgb = lerp(_Color.rgb, _BorderColor.rgb, border);
                float alpha = lerp(_Color.a, _BorderColor.a, border) * _GlobalAlpha;

                clip(alpha - 0.002);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
