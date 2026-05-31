Shader "UI/Custom/ClothWobble"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Stencil)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Header(Use UI Alpha Clip)]
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        [Header(Main Swing)]
        [Toggle] _EnableMainSwing ("Enable Main Swing", Float) = 1
        _SpeedMain ("Swing Speed", Range(0, 30)) = 2
        _FreqMain ("Swing Frequency", Range(0, 20)) = 1
        _AmpMain ("Swing Strength", Range(0, 0.5)) = 0.02
        
        [Header(Detail Ripples)]
        [Toggle] _EnableDetailRipples ("Enable Detail Ripples", Float) = 1
        _SpeedDetail ("Ripple Speed", Range(0, 50)) = 10
        _FreqDetail ("Ripple Frequency", Range(0, 50)) = 15
        _AmpDetail ("Ripple Strength", Range(0, 0.1)) = 0.005

        [Header(Material Properties)]
        _Rigidity ("Stiffness (Bend Curve)", Range(0.1, 8)) = 1.5
        [Toggle] _AnchorTop ("Use Anchor (Fixed Side)", Float) = 1
        _AnchorPivot ("Anchor Pivot Y (0=Bottom, 1=Top)", Range(0, 1)) = 1.0

        [Header(Vertical Control)]
        _TopFade ("Top Wobble Strength", Range(0, 1)) = 1.0
        _BottomFade ("Bottom Wobble Strength", Range(0, 1)) = 1.0

        [Header(Edge Fraying)]
        [Toggle] _EnableEdgeFraying ("Enable Edge Fraying", Float) = 1
        _EdgeFrayAmount ("Fray Size (0 to Disable)", Range(0, 0.2)) = 0.05
        _EdgeFrayScale ("Fray Thread Density", Range(10, 300)) = 150
        _EdgeFraySharpness ("Fray Sharpness", Range(1, 20)) = 10
        _EdgeFrayNoiseIntensity ("Fray Noise Intensity", Range(0, 1)) = 0.5
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
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            
            // Toggles
            float _EnableMainSwing;
            float _EnableDetailRipples;
            float _EnableEdgeFraying;

            // Main Swing Params
            float _SpeedMain;
            float _FreqMain;
            float _AmpMain;

            // Detail Ripple Params
            float _SpeedDetail;
            float _FreqDetail;
            float _AmpDetail;

            // Structure Params
            float _Rigidity;
            float _AnchorTop;
            float _AnchorPivot;

            // Y-Axis Controls
            float _TopFade;
            float _BottomFade;

            // Edge Fray Controls
            float _EdgeFrayAmount;
            float _EdgeFrayScale;
            float _EdgeFraySharpness;
            float _EdgeFrayNoiseIntensity;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = IN.vertex;

                // --- Two-Layer Wave Simulation --- //
                
                // 1. Main Swing: Slower, larger waves (simulates base movement/wind)
                float swing = 0;
                if (_EnableMainSwing > 0.5)
                {
                    float timeMain = _Time.y * _SpeedMain;
                    swing = sin(timeMain + IN.texcoord.y * _FreqMain) * _AmpMain;
                }

                // 2. Detail Ripple: Faster, tighter waves (simulates loose cloth fluttering)
                float ripple = 0;
                if (_EnableDetailRipples > 0.5)
                {
                    float timeDetail = _Time.y * _SpeedDetail;
                    ripple = cos(timeDetail + IN.texcoord.y * _FreqDetail) * _AmpDetail; // Cos used for offset phase
                }

                float totalWave = swing + ripple;

                // --- Structural Logic --- //
                
                // Rigidity: Controls how "bendable" the object is from the anchor point.
                float influence = 1.0;
                if (_AnchorTop > 0.5)
                {
                    // Calc distance from anchor
                    float maxDist = max(_AnchorPivot, 1.0 - _AnchorPivot);
                    float distFromAnchor = abs(IN.texcoord.y - _AnchorPivot) / max(maxDist, 0.0001);
                    
                    // Pow(x, _Rigidity):
                    // _Rigidity = 1: Linear bend (Rubber)
                    // _Rigidity > 1: Stiff top, bends only at very bottom (Heavy cloth/Leather)
                    // _Rigidity < 1: Bends immediately (Silk/Very light)
                    influence = pow(abs(distFromAnchor), _Rigidity);
                }

                // Explicit Y-Axis Wobble Controls (0 = no wobble, 1 = full wobble at that end)
                float yFadeMultiplier = lerp(_BottomFade, _TopFade, IN.texcoord.y);
                influence *= yFadeMultiplier;

                // Apply Offset (Scaled for UI pixel space)
                IN.vertex.x += totalWave * influence * 1000; 

                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // --- Edge Fray / Cloth Roughness Logic --- //
                if (_EnableEdgeFraying > 0.5 && _EdgeFrayAmount > 0.001)
                {
                    float distX = min(IN.texcoord.x, 1.0 - IN.texcoord.x);
                    float distY = min(IN.texcoord.y, 1.0 - IN.texcoord.y);
                    float distEdge = min(distX, distY);

                    // Horizontal threads (appear on left/right edges)
                    float u = IN.texcoord.y * _EdgeFrayScale;
                    float threadID_X = floor(u);
                    float t_X = frac(u);
                    float randomX = frac(sin(threadID_X * 12.9898) * 43758.5453) * 2.0 - 1.0;
                    float threadX = sin(t_X * 3.14159265) * randomX;

                    // Vertical threads (appear on top/bottom edges)
                    float v = IN.texcoord.x * _EdgeFrayScale;
                    float threadID_Y = floor(v);
                    float t_Y = frac(v);
                    float randomY = frac(sin(threadID_Y * 78.233) * 43758.5453) * 2.0 - 1.0;
                    float threadY = sin(t_Y * 3.14159265) * randomY;

                    // Apply the correct threads based on the closest edge
                    float weaveNoise = (distX < distY) ? threadX : threadY;

                    float edgeFactor = distEdge / _EdgeFrayAmount;
                    float jaggedEdge = edgeFactor + weaveNoise * _EdgeFrayNoiseIntensity;

                    float edgeAlpha = saturate((jaggedEdge - 0.1) * _EdgeFraySharpness);
                    color.a *= edgeAlpha;
                }
                // ----------------------------------------- //

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
