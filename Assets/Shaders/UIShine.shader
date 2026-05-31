Shader "UI/Shine"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Stencil properties required for UI Masking
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // Shine Settings
        _ShineColor ("Shine Color", Color) = (1,1,1,1)
        _ShineWidth ("Shine Width", Range(0.01, 1.0)) = 0.2
        _ShineSmoothness ("Shine Smoothness", Range(0.0, 1.0)) = 0.5
        _ShineGlow ("Shine Glow Intensity", Range(0.0, 5.0)) = 1.5
        _ShineAngle ("Shine Angle (Degrees)", Range(-180.0, 180.0)) = 45.0
        [KeywordEnum(Single, Double)] _ShineStyle ("Shine Style", Float) = 0
        [KeywordEnum(Additive, Replace)] _ShineBlendMode ("Shine Blend Mode", Float) = 0
        _ShineShadow ("Contrast Shadow Intensity", Range(0.0, 1.0)) = 0.2
        
        // Automatic Animation Settings
        _ShineSpeed ("Shine Speed", Range(0.05, 5.0)) = 1.0
        _ShineInterval ("Shine Delay/Interval (Sec)", Range(0.0, 10.0)) = 2.0
        
        // Manual Animation Settings
        [Toggle(_MANUALCONTROL_ON)] _ManualControl ("Manual Control", Float) = 0
        _ShineLocation ("Shine Location (0-1)", Range(0.0, 1.0)) = 0.0
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

            #pragma shader_feature_local _MANUALCONTROL_ON
            #pragma shader_feature_local _SHINESTYLE_SINGLE _SHINESTYLE_DOUBLE
            #pragma shader_feature_local _SHINEBLENDMODE_ADDITIVE _SHINEBLENDMODE_REPLACE

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
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            // Shine uniforms
            fixed4 _ShineColor;
            float _ShineWidth;
            float _ShineSmoothness;
            float _ShineGlow;
            float _ShineAngle;
            float _ShineSpeed;
            float _ShineInterval;
            float _ShineLocation;
            float _ShineStyle;
            float _ShineBlendMode;
            float _ShineShadow;

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
                half4 texCol = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 finalCol = texCol * IN.color;

                // Shine calculations
                float rad = _ShineAngle * 0.0174533; // Deg to Rad
                float c = cos(rad);
                float s = sin(rad);

                // Find projection bounds of UV space corners [0, 1]
                float p00 = 0.0;
                float p10 = c;
                float p01 = s;
                float p11 = c + s;

                float minProj = min(p00, min(p10, min(p01, p11)));
                float maxProj = max(p00, max(p10, max(p01, p11)));

                // Project current pixel UV along the shine angle direction
                float project = IN.texcoord.x * c + IN.texcoord.y * s;

                float shinePos = minProj - _ShineWidth;

                #if _MANUALCONTROL_ON
                    // Manual interpolation between min and max bounds
                    shinePos = lerp(minProj - _ShineWidth, maxProj + _ShineWidth, _ShineLocation);
                #else
                    // Automatic periodic time sweep
                    float cycleTime = 1.0 + _ShineInterval;
                    float t = fmod(_Time.y * _ShineSpeed, cycleTime);
                    if (t < 1.0)
                    {
                        shinePos = lerp(minProj - _ShineWidth, maxProj + _ShineWidth, t);
                    }
                #endif

                // Calculate signed distance to the shine line center
                float d = project - shinePos;

                float shine = 0.0;
                float shadowDist = 0.0;
                float edge1 = _ShineWidth;
                float edge0 = _ShineWidth * (1.0 - _ShineSmoothness);

                #if _SHINESTYLE_DOUBLE
                    // Sunglasses double stripe flash (thick main line + thin side line)
                    float mainWidth = _ShineWidth * 0.6;
                    float thinWidth = _ShineWidth * 0.25;
                    float thinOffset = _ShineWidth * 0.75;
                    
                    float d1 = abs(d);
                    float d2 = abs(d - thinOffset);
                    
                    float edge1_main = mainWidth;
                    float edge0_main = mainWidth * (1.0 - _ShineSmoothness);
                    float edge1_thin = thinWidth;
                    float edge0_thin = thinWidth * (1.0 - _ShineSmoothness);
                    
                    float shine1 = smoothstep(edge1_main, edge0_main, d1);
                    float shine2 = smoothstep(edge1_thin, edge0_thin, d2) * 0.6;
                    
                    shine = max(shine1, shine2) * _ShineGlow;
                    
                    // Determine distance to the nearest stripe center for shadow
                    shadowDist = (d < thinOffset * 0.5) ? d1 : d2;
                    edge0 = (d < thinOffset * 0.5) ? edge0_main : edge0_thin;
                #else
                    // Standard single stripe
                    float d1 = abs(d);
                    shine = smoothstep(edge1, edge0, d1) * _ShineGlow;
                    shadowDist = d1;
                #endif

                // Calculate shadow for contrast on white surfaces
                float shadow = 0.0;
                float shadowWidth = _ShineWidth * 1.5;
                if (shadowDist > edge0 && shadowDist < shadowWidth)
                {
                    float t_shadow = (shadowDist - edge0) / max(shadowWidth - edge0, 0.0001);
                    shadow = (1.0 - t_shadow) * _ShineShadow;
                }

                // Apply contrast shadow first
                finalCol.rgb *= (1.0 - shadow * finalCol.a);

                // Overlay the shine color on top of the image color
                #if _SHINEBLENDMODE_REPLACE
                    finalCol.rgb = lerp(finalCol.rgb, _ShineColor.rgb * _ShineGlow, shine * finalCol.a);
                #else
                    finalCol.rgb += _ShineColor.rgb * shine * finalCol.a;
                #endif

                // UI Rect Mask Clipping
                finalCol.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);

                #ifdef UNITY_UI_ALPHACLIP
                    clip (finalCol.a - 0.001);
                #endif

                return finalCol;
            }
        ENDCG
        }
    }
}
