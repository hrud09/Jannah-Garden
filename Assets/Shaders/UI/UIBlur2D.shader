Shader "Custom/UIBlur2D"
{
    Properties
    {
        [MainTexture] _MainTex("Texture", 2D) = "white" {}
        _BlurAmount("Blur Amount", Range(0,1)) = 0.0
        _BlurStrength("Blur Strength", Range(1, 10)) = 4
        _Color("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Pass
        {
            Name "UIBlur"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _MainTex_ST;
            float _BlurAmount;
            float _BlurStrength;
            float4 _Color;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Blur strength scales with slider
                float blurSamples = lerp(0, _BlurStrength, _BlurAmount);

                if (blurSamples > 0.01)
                {
                    float2 texelSize = 1.0 / _ScreenParams.xy;
                    float4 sum = 0.0;
                    int samples = (int)blurSamples;

                    // Simple box blur
                    for (int x = -samples; x <= samples; x++)
                    {
                        for (int y = -samples; y <= samples; y++)
                        {
                            float2 offset = float2(x, y) * texelSize;
                            sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset);
                        }
                    }

                    col = sum / pow((samples * 2 + 1), 2);
                }

                return col * IN.color;
            }
            ENDHLSL
        }
    }
}
