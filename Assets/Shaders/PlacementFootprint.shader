// The block of cells the item being placed will actually claim, tinted green when it can go there and
// red when it can't.
//
// Feedback lives here rather than on the model itself on purpose: the ghost is deliberately shown
// desaturated (PlaceableItem.UpdateSaturation(0f)) to read as "not real yet", and tinting it red would
// fight that. Colouring the ground under it also answers the more useful question — not "is this
// allowed" but "exactly how much space is this going to take".
Shader "JannahGarden/PlacementFootprint"
{
    Properties
    {
        _Color ("Fill Color", Color) = (0.35, 1, 0.45, 0.22)
        _BorderColor ("Border Color", Color) = (0.5, 1, 0.6, 0.85)
        _CellColor ("Interior Cell Line Color", Color) = (1, 1, 1, 0.18)
        _SizeMeters ("Footprint Size (metres, xy used)", Vector) = (1, 1, 0, 0)
        _BorderWidth ("Border Width (metres)", Float) = 0.06
        _CellSize ("Cell Size", Float) = 0.25
        _GridOrigin ("Grid Origin (world, xz used)", Vector) = (0, 0, 0, 0)
        _LineWidth ("Cell Line Width (pixels)", Float) = 1.0
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-90"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlacementFootprint"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _BorderColor;
                float4 _CellColor;
                float4 _SizeMeters;
                float4 _GridOrigin;
                float _BorderWidth;
                float _CellSize;
                float _LineWidth;
                float _GlobalAlpha;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Distance to the nearest edge in metres, not UV: a 3x1 footprint would otherwise get a
                // border three times thicker on one axis than the other.
                float2 metres = IN.uv * _SizeMeters.xy;
                float2 toEdge = min(metres, _SizeMeters.xy - metres);
                float edge = min(toEdge.x, toEdge.y);
                float border = 1.0 - smoothstep(_BorderWidth * 0.5, _BorderWidth, edge);

                // Interior cell divisions, so the player can count the cells being claimed.
                float2 coord = (IN.positionWS.xz - _GridOrigin.xz) / max(_CellSize, 1e-4);
                float2 d = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-5);
                float cellLine = 1.0 - saturate(min(d.x, d.y) / max(_LineWidth, 1e-5));

                float3 rgb = _Color.rgb;
                float alpha = _Color.a;

                rgb = lerp(rgb, _CellColor.rgb, cellLine * _CellColor.a);
                alpha = max(alpha, cellLine * _CellColor.a);

                rgb = lerp(rgb, _BorderColor.rgb, border);
                alpha = lerp(alpha, _BorderColor.a, border);

                alpha *= _GlobalAlpha;
                clip(alpha - 0.002);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
