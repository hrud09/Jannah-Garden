Shader "UI/Blur"
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

        // Blur mode & type
        [KeywordEnum(Texture, Background)] _BlurMode ("Blur Mode", Float) = 0
        [KeywordEnum(Box, Gaussian, Radial, Directional)] _BlurType ("Blur Type", Float) = 0
        
        // Blur parameter controls
        _BlurAmount ("Blur Amount / Radius", Range(0, 50)) = 10
        _BlurSamples ("Blur Samples (Quality)", Range(4, 32)) = 16
        _BlurSigma ("Gaussian Sigma (Falloff)", Range(0.1, 2.0)) = 0.5
        
        // Custom offset settings
        _BlurDir ("Directional Vector", Vector) = (1, 1, 0, 0)
        _BlurCenter ("Radial Center (UV)", Vector) = (0.5, 0.5, 0, 0)
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

            #pragma shader_feature _BLURMODE_TEXTURE _BLURMODE_BACKGROUND
            #pragma shader_feature _BLURTYPE_BOX _BLURTYPE_GAUSSIAN _BLURTYPE_RADIAL _BLURTYPE_DIRECTIONAL

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
                float4 screenPos : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;

            // Global URP variables for screen buffer
            sampler2D _CameraOpaqueTexture;
            float4 _CameraOpaqueTexture_TexelSize;

            float _BlurAmount;
            float _BlurSamples;
            float _BlurSigma;
            float4 _BlurDir;
            float4 _BlurCenter;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.worldPosition = IN.vertex;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                
                OUT.color = IN.color * _Color;
                return OUT;
            }

            // Conditional macro to sample either scene background or main texture
            #if _BLURMODE_BACKGROUND
                #define SAMPLE_TEX(uv_coord) tex2D(_CameraOpaqueTexture, uv_coord)
            #else
                #define SAMPLE_TEX(uv_coord) (tex2D(_MainTex, uv_coord) + _TextureSampleAdd)
            #endif

            fixed4 frag(v2f IN) : SV_Target
            {
                int samples = (int)_BlurSamples;
                samples = clamp(samples, 4, 32);

                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 0.0001);

                // Set sample coordinates and texel sizes depending on the active blur mode
                float2 uv;
                float2 texelSize;

                #if _BLURMODE_BACKGROUND
                    uv = screenUV;
                    texelSize = _CameraOpaqueTexture_TexelSize.xy;
                    // Fallback to screen resolution if URP has not populated texel size
                    if (texelSize.x <= 0 || texelSize.y <= 0)
                    {
                        texelSize = (_ScreenParams.zw - 1.0);
                    }
                #else
                    uv = IN.texcoord;
                    texelSize = _MainTex_TexelSize.xy;
                #endif

                float4 blurredColor = float4(0, 0, 0, 0);

                #if _BLURTYPE_BOX
                    // 1D Cross Box Blur (fast and lightweight)
                    float4 colorSum = SAMPLE_TEX(uv);
                    float weightSum = 1.0;
                    float2 step = (_BlurAmount / max(samples, 1)) * texelSize;

                    for (int i = 1; i <= 32; i++)
                    {
                        if (i > samples) break;
                        float weight = 1.0;
                        colorSum += SAMPLE_TEX(uv + float2(i * step.x, 0)) * weight;
                        colorSum += SAMPLE_TEX(uv - float2(i * step.x, 0)) * weight;
                        colorSum += SAMPLE_TEX(uv + float2(0, i * step.y)) * weight;
                        colorSum += SAMPLE_TEX(uv - float2(0, i * step.y)) * weight;
                        weightSum += weight * 4.0;
                    }
                    blurredColor = colorSum / weightSum;

                #elif _BLURTYPE_GAUSSIAN
                    // 1D Cross Gaussian Blur (premium aesthetics)
                    float4 colorSum = SAMPLE_TEX(uv);
                    float weightSum = 1.0;
                    float2 step = (_BlurAmount / max(samples, 1)) * texelSize;
                    float sigma = max(_BlurSigma, 0.01);

                    for (int i = 1; i <= 32; i++)
                    {
                        if (i > samples) break;
                        float x = (float)i / samples;
                        float weight = exp(-(x * x) / (2.0 * sigma * sigma));
                        
                        colorSum += SAMPLE_TEX(uv + float2(i * step.x, 0)) * weight;
                        colorSum += SAMPLE_TEX(uv - float2(i * step.x, 0)) * weight;
                        colorSum += SAMPLE_TEX(uv + float2(0, i * step.y)) * weight;
                        colorSum += SAMPLE_TEX(uv - float2(0, i * step.y)) * weight;
                        weightSum += weight * 4.0;
                    }
                    blurredColor = colorSum / weightSum;

                #elif _BLURTYPE_RADIAL
                    // Radial / Zoom Blur (radiates outward from a center point)
                    float2 dir = uv - _BlurCenter.xy;
                    float2 step = dir * (_BlurAmount * 0.01) / max(samples, 1);
                    float4 colorSum = SAMPLE_TEX(uv);
                    float weightSum = 1.0;

                    for (int i = 1; i <= 32; i++)
                    {
                        if (i > samples) break;
                        float weight = 1.0;
                        colorSum += SAMPLE_TEX(uv - i * step) * weight;
                        weightSum += weight;
                    }
                    blurredColor = colorSum / weightSum;

                #elif _BLURTYPE_DIRECTIONAL
                    // Directional / Motion Blur (lines up with a directional angle)
                    float2 dir = normalize(_BlurDir.xy);
                    if (length(_BlurDir.xy) < 0.001) {
                        dir = float2(1, 0); // Avoid division by zero
                    }
                    float2 step = dir * (_BlurAmount * texelSize) / max(samples, 1);
                    float4 colorSum = SAMPLE_TEX(uv);
                    float weightSum = 1.0;

                    for (int i = 1; i <= 32; i++)
                    {
                        if (i > samples) break;
                        float weight = 1.0;
                        colorSum += SAMPLE_TEX(uv + i * step) * weight;
                        colorSum += SAMPLE_TEX(uv - i * step) * weight;
                        weightSum += weight * 2.0;
                    }
                    blurredColor = colorSum / weightSum;
                #endif

                half4 finalCol = blurredColor;
                
                #if _BLURMODE_BACKGROUND
                    // Tint the blurred background color with UI vertex colors
                    finalCol.rgb *= IN.color.rgb;
                    // Mask by UI Image's sprite alpha channel to support curved corners/borders
                    half spriteAlpha = tex2D(_MainTex, IN.texcoord).a;
                    finalCol.a = spriteAlpha * IN.color.a;
                #else
                    // Standard texture color blending
                    finalCol *= IN.color;
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
