// UI shader with a diagonal band of light that sweeps across the sprite now and then (UIIdle's
// Shimmer). Same structure as UI/Default, so masks, RectMask2D clipping and alpha clip keep working.
//
// The band is measured across the sprite's own UV rect (_UVRect, set by UIIdle from the sprite), so
// it sweeps the whole graphic even when the sprite is packed in an atlas. It only ever adds light,
// weighted by the sprite's alpha, so transparent corners never glow.
//
// This shader ships through Resources/UIShimmer.mat (a material in Resources is always built with
// its shader), never through Shader.Find, which returns null for stripped shaders on device.
Shader "UI/Jannah Shimmer"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ShimmerColor ("Shimmer Colour (A = strength)", Color) = (1,0.97,0.85,0.55)
        _ShimmerWidth ("Band Width", Range(0.02, 0.6)) = 0.16
        _ShimmerSlant ("Band Slant", Range(0, 2)) = 0.6
        _ShimmerPeriod ("Seconds Between Sweeps", Float) = 3.2
        _ShimmerSweep ("Sweep Seconds", Float) = 0.7
        _ShimmerOffset ("Time Offset", Float) = 0
        _ShimmerIntensity ("Intensity (0 = off)", Range(0, 1)) = 1
        _UVRect ("Sprite UV Rect (xy = min, zw = size)", Vector) = (0,0,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            fixed4 _ShimmerColor;
            float _ShimmerWidth;
            float _ShimmerSlant;
            float _ShimmerPeriod;
            float _ShimmerSweep;
            float _ShimmerOffset;
            float _ShimmerIntensity;
            float4 _UVRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                // Where the band is: 0..1 while sweeping, past 1 while resting between sweeps.
                float period = max(_ShimmerPeriod, 0.01);
                float t = fmod(_Time.y + _ShimmerOffset, period) / max(_ShimmerSweep, 0.01);
                float2 uv = (IN.texcoord - _UVRect.xy) / max(_UVRect.zw, 0.0001);
                float s = (uv.x + uv.y * _ShimmerSlant) / (1.0 + _ShimmerSlant);
                float pos = lerp(-_ShimmerWidth, 1.0 + _ShimmerWidth, saturate(t));
                float band = saturate(1.0 - abs(s - pos) / _ShimmerWidth);
                band = band * band * (3.0 - 2.0 * band) * step(t, 1.0);

                color.rgb *= color.a; // premultiply (Blend One OneMinusSrcAlpha)
                color.rgb += _ShimmerColor.rgb * (band * _ShimmerColor.a * _ShimmerIntensity * color.a);
                return color;
            }
        ENDCG
        }
    }
}
