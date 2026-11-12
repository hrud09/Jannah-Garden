// JannahGarden: mobile version of JannahGarden/SNB_Nature_URP, used by everything in "Assets/Jungle Optimized".
// Same look and the same vertex-colour wind (B = trunk sway, R = leaf flutter), with these differences:
//  - Fewer shader variants (no SSAO / additional-light shadows / screen-space shadows): smaller builds, less shader memory.
//  - Normal mapping is optional (_NORMALMAP), so materials without a normal map skip the texture fetch + TBN maths.
//  - _COMBINED_MESH: for the baked undergrowth chunks. Each source plant's own object-space height is stored in UV2.x
//    at bake time, so the height gradient keeps working after hundreds of plants are merged into one mesh.
Shader "JannahGarden/SNB_Nature_URP_Mobile"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 1
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        _Smoothness("Smoothness", Range(0, 1)) = 0.1
        _Brightness("Brightness", Range(0, 5)) = 1
        _ColorVariation("Additive Colour", Color) = (0,0,0,0)
        _HeightBrightness("Height Brightness", Range(0, 8)) = 0
        _HeightStartGradient("Height Gradient Start", Float) = 1
        _HeightGradient("Height Gradient Length", Float) = 1.5
        _Translucency("Leaf Translucency", Range(0, 2)) = 0
        _WindTrunkAmplitude("Wind Trunk Amplitude", Range(0, 1)) = 0
        _WindTrunkSpeed("Wind Trunk Speed", Range(0, 1)) = 0
        _WindFoliageAmplitude("Wind Foliage Amplitude", Range(0, 1)) = 0
        _WindFoliageSpeed("Wind Foliage Speed", Range(0, 1)) = 0
        [Toggle(_COMBINED_MESH)] _CombinedMesh("Combined Mesh (height in UV2)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Cutoff;
            half _Smoothness;
            half _Brightness;
            half4 _ColorVariation;
            half _HeightBrightness;
            float _HeightStartGradient;
            float _HeightGradient;
            half _Translucency;
            float _WindTrunkAmplitude;
            float _WindTrunkSpeed;
            float _WindFoliageAmplitude;
            float _WindFoliageSpeed;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        float3 SNB_Mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
        float2 SNB_Mod289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
        float3 SNB_Permute(float3 x) { return SNB_Mod289(((x * 34.0) + 1.0) * x); }
        float SNB_SimplexNoise(float2 v)
        {
            const float4 C = float4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
            float2 i = floor(v + dot(v, C.yy));
            float2 x0 = v - i + dot(i, C.xx);
            float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
            float4 x12 = x0.xyxy + C.xxzz;
            x12.xy -= i1;
            i = SNB_Mod289(i);
            float3 p = SNB_Permute(SNB_Permute(i.y + float3(0.0, i1.y, 1.0)) + i.x + float3(0.0, i1.x, 1.0));
            float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
            m = m * m; m = m * m;
            float3 x = 2.0 * frac(p * C.www) - 1.0;
            float3 h = abs(x) - 0.5;
            float3 ox = floor(x + 0.5);
            float3 a0 = x - ox;
            m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
            float3 g;
            g.x = a0.x * x0.x + h.x * x0.y;
            g.yz = a0.yz * x12.xz + h.yz * x12.yw;
            return 130.0 * dot(m, g);
        }

        // Same wind as the SNB shaders (object-space offset).
        float3 SNB_ApplyWind(float3 positionOS, float3 normalOS, float4 color)
        {
            float t = _Time.y * (2.0 * _WindTrunkSpeed);
            float3 offset = float3(sin(t) * _WindTrunkAmplitude * color.b, 0.0, color.b * (_WindTrunkAmplitude * 0.5) * cos(t));
            float3 positionWS = TransformObjectToWorld(positionOS);
            float n = SNB_SimplexNoise(positionWS.xy + (_Time.y * _WindFoliageSpeed) * float2(2.0, 2.0));
            offset += n * _WindFoliageAmplitude * normalOS * color.r;
            return positionOS + offset;
        }

        void SNB_AlphaClip(float2 uv)
        {
            #if defined(_ALPHATEST_ON)
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
                clip(a - _Cutoff);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _COMBINED_MESH
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                #if defined(_NORMALMAP)
                float4 tangentWS : TEXCOORD3;
                #endif
                float objectHeight : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 posOS = SNB_ApplyWind(input.positionOS.xyz, input.normalOS, input.color);
                VertexPositionInputs p = GetVertexPositionInputs(posOS);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                #if defined(_NORMALMAP)
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                #endif
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                #if defined(_COMBINED_MESH)
                o.objectHeight = input.uv1.x;
                #else
                o.objectHeight = input.positionOS.y;
                #endif
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                #if defined(_ALPHATEST_ON)
                    clip(tex.a - _Cutoff);
                #endif

                half3 albedo = saturate(_ColorVariation.rgb) + _Brightness * tex.rgb * _BaseColor.rgb;
                albedo += _HeightBrightness * saturate((input.objectHeight - _HeightStartGradient) / max(_HeightGradient, 0.001)) * albedo;

                #if defined(_NORMALMAP)
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                    float sgn = input.tangentWS.w;
                    float3 bitangent = sgn * cross(input.normalWS, input.tangentWS.xyz);
                    float3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
                #else
                    half3 normalTS = half3(0, 0, 1);
                    float3 normalWS = input.normalWS;
                #endif
                normalWS = NormalizeNormalPerPixel(normalWS * (facing > 0 ? 1.0 : -1.0));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.normalTS = normalTS;
                surface.occlusion = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                // Cheap leaf translucency: light shining through leaves toward the camera.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half3 transDir = mainLight.direction + normalWS * 0.2;
                half trans = pow(saturate(dot(inputData.viewDirectionWS, -transDir)), 4.0) * _Translucency;
                color.rgb += albedo * mainLight.color * trans * mainLight.shadowAttenuation;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings ShadowVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                float3 posOS = SNB_ApplyWind(input.positionOS.xyz, input.normalOS, input.color);
                float3 positionWS = TransformObjectToWorld(posOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                o.positionCS = ApplyShadowClamping(positionCS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { SNB_AlphaClip(input.uv); return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings DepthVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(SNB_ApplyWind(input.positionOS.xyz, input.normalOS, input.color));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }
            half DepthFrag(Varyings input) : SV_Target { SNB_AlphaClip(input.uv); return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings DNVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionCS = TransformObjectToHClip(SNB_ApplyWind(input.positionOS.xyz, input.normalOS, input.color));
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }
            half4 DNFrag(Varyings input, half facing : VFACE) : SV_Target
            {
                SNB_AlphaClip(input.uv);
                float3 n = NormalizeNormalPerPixel(input.normalWS * (facing > 0 ? 1.0 : -1.0));
                return half4(n, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
