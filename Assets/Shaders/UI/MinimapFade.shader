Shader "UI/MinimapFade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Minimap Render Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Stencil properties required for UI Masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // --- Out-of-world fade -------------------------------------------------
        // Everything the minimap camera sees beyond the playable world dissolves into
        // _OutsideColor instead of showing the camera's flat clear colour.
        _OutsideColor ("Outside World Colour", Color) = (0.09, 0.11, 0.13, 1)
        _WorldFade ("Outside Fade Distance (world units)", Range(0, 60)) = 10

        // --- Panel edge fade ---------------------------------------------------
        // Melts the square render texture into the frame so it has no hard border.
        _CornerRadius ("Edge Corner Roundness", Range(0, 1)) = 0.35
        _EdgeFade ("Edge Fade Width", Range(0.001, 1)) = 0.18
        _VignetteColor ("Vignette Colour", Color) = (0, 0, 0, 1)
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.35

        // Set from MinimapFade.cs — maps UV space onto world XZ, rotation included.
        _MapOrigin ("Map Centre (world XZ)", Vector) = (0,0,0,0)
        _MapRight ("Map U Axis (world XZ)", Vector) = (1,0,0,0)
        _MapUp ("Map V Axis (world XZ)", Vector) = (0,1,0,0)
        _WorldBounds ("World Bounds (minX, minZ, maxX, maxZ)", Vector) = (-100,-100,100,100)
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed4 _OutsideColor;
            float _WorldFade;

            float _CornerRadius;
            float _EdgeFade;
            fixed4 _VignetteColor;
            float _VignetteStrength;

            float4 _MapOrigin;
            float4 _MapRight;
            float4 _MapUp;
            float4 _WorldBounds;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = IN.vertex;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 col = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // --- Dissolve everything beyond the playable world ------------------
                // Rebuild the world XZ position this texel was rendered from. The minimap
                // camera turns with the player, so the axes come in as vectors rather
                // than a plain axis-aligned rect.
                float2 uvCentred = IN.texcoord - 0.5;
                float2 worldXZ = _MapOrigin.xy + uvCentred.x * _MapRight.xy + uvCentred.y * _MapUp.xy;

                float2 insideMin = worldXZ - _WorldBounds.xy;
                float2 insideMax = _WorldBounds.zw - worldXZ;
                float insideDistance = min(min(insideMin.x, insideMin.y), min(insideMax.x, insideMax.y));
                float worldMask = smoothstep(0.0, max(_WorldFade, 0.0001), insideDistance);

                col.rgb = lerp(_OutsideColor.rgb, col.rgb, worldMask);
                col.a = lerp(_OutsideColor.a, col.a, worldMask);

                // --- Melt the panel edge into the frame ----------------------------
                // Rounded-box signed distance in [-1,1] UV space: negative inside.
                float2 p = uvCentred * 2.0;
                float radius = clamp(_CornerRadius, 0.0, 1.0);
                float2 q = abs(p) - (1.0 - radius);
                float edgeDistance = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                float edgeMask = smoothstep(0.0, -_EdgeFade, edgeDistance);
                col.rgb = lerp(_VignetteColor.rgb, col.rgb, lerp(1.0, edgeMask, _VignetteStrength));
                col.a *= edgeMask;

                col.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);

                #ifdef UNITY_UI_ALPHACLIP
                    clip (col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
}
