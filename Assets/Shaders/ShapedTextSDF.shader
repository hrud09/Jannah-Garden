// Minimal SDF alpha-cutout shader for ShapedTextGraphic. Deliberately NOT a reuse of TMP's own
// "TextMeshPro/Mobile/Distance Field" shader: that shader expects a richer per-vertex format
// (UV0 as a float4 carrying a bold/style flag in .w, screen-derivative-based edge scaling, optional
// outline/underlay passes, GUI clip-texture sampling) tuned for TMP_Text's own mesh generator.
// ShapedTextGraphic produces a much simpler vertex stream (plain atlas UV + vertex color), so it gets
// its own shader rather than fighting TMP's undocumented vertex-format assumptions.
Shader "UI/ShapedTextSDF"
{
    Properties
    {
        _MainTex ("Atlas (SDF, Alpha8)", 2D) = "white" {}
        _Smoothing ("Edge Smoothing", Range(0.001, 0.2)) = 0.04
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4 mask : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ClipRect;
            float _Smoothing;

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.mask = float4(v.vertex.xy * 2 - clamp(_ClipRect.xy + _ClipRect.zw, -2, 2), 0.25, 0.25);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = tex2D(_MainTex, i.uv).a;
                float alpha = smoothstep(0.5 - _Smoothing, 0.5 + _Smoothing, d);

                fixed4 color = i.color;
                color.a *= alpha;
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }
}
