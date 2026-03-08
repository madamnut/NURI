Shader "A_Game/Terrain Material Id"
{
    Properties
    {
        [NoScaleOffset] _BaseMapArray("Base Map Array", 2DArray) = "" {}
        [NoScaleOffset] _NormalMapArray("Normal Map Array", 2DArray) = "" {}
        [NoScaleOffset] _RoughnessMapArray("Roughness Map Array", 2DArray) = "" {}
        [NoScaleOffset] _AOMapArray("AO Map Array", 2DArray) = "" {}
        [NoScaleOffset] _HeightMapArray("Height Map Array", 2DArray) = "" {}

        _MaterialCount("Material Count", Float) = 1
        _Tiling("Tiling", Float) = 0.2
        _BlendSharpness("Blend Sharpness", Float) = 5
        _NormalStrength("Normal Strength", Float) = 1
        _RoughnessStrength("Roughness Strength", Float) = 1
        _AOStrength("AO Strength", Float) = 0.2
        _HeightStrength("Height Strength", Float) = 0

        [HideInInspector] _SpecColor("Specular", Color) = (0.2, 0.2, 0.2, 1)
        [HideInInspector] _Surface("__surface", Float) = 0.0
        [HideInInspector] _Blend("__blend", Float) = 0.0
        [HideInInspector] _Cull("__cull", Float) = 2.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector][NoScaleOffset] unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TerrainPassVertex
            #pragma fragment TerrainPassFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #define MAX_TERRAIN_MATERIALS 255

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _SpecColor;
            half4 _MaterialBaseColors[MAX_TERRAIN_MATERIALS];
            half _MaterialCount;
            half _Tiling;
            half _BlendSharpness;
            half _NormalStrength;
            half _RoughnessStrength;
            half _AOStrength;
            half _HeightStrength;
            half _SrcBlend;
            half _DstBlend;
            half _SrcBlendAlpha;
            half _DstBlendAlpha;
            half _ZWrite;
            half _Cull;
            CBUFFER_END

            TEXTURE2D_ARRAY(_BaseMapArray);
            SAMPLER(sampler_BaseMapArray);
            TEXTURE2D_ARRAY(_NormalMapArray);
            SAMPLER(sampler_NormalMapArray);
            TEXTURE2D_ARRAY(_RoughnessMapArray);
            SAMPLER(sampler_RoughnessMapArray);
            TEXTURE2D_ARRAY(_AOMapArray);
            SAMPLER(sampler_AOMapArray);
            TEXTURE2D_ARRAY(_HeightMapArray);
            SAMPLER(sampler_HeightMapArray);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 materialInfo : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 materialInfo : TEXCOORD2;
                half3 vertexLighting : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            int GetMaterialSliceIndex(float materialId)
            {
                int roundedMaterialId = (int)floor(materialId + 0.5);
                int clampedMaterialId = clamp(roundedMaterialId, 1, max((int)_MaterialCount, 1));
                return clampedMaterialId - 1;
            }

            void SampleTerrainLayer(
                float3 positionWS,
                float3 geometryNormalWS,
                int sliceIndex,
                out half3 albedo,
                out half3 worldNormal,
                out half smoothness,
                out half occlusion)
            {
                float3 weights = abs(normalize(geometryNormalWS));
                weights = pow(weights, max(_BlendSharpness, 0.0001h));
                weights /= max(weights.x + weights.y + weights.z, 0.0001);

                float2 uvX = positionWS.zy * _Tiling;
                float2 uvY = positionWS.xz * _Tiling;
                float2 uvZ = positionWS.xy * _Tiling;
                float slice = (float)sliceIndex;

                half4 albedoX = SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_BaseMapArray, uvX, slice);
                half4 albedoY = SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_BaseMapArray, uvY, slice);
                half4 albedoZ = SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_BaseMapArray, uvZ, slice);
                half4 baseColor = _MaterialBaseColors[sliceIndex];
                albedo = (albedoX.rgb * weights.x + albedoY.rgb * weights.y + albedoZ.rgb * weights.z) * baseColor.rgb;

                worldNormal = geometryNormalWS;

                half roughnessX = SAMPLE_TEXTURE2D_ARRAY(_RoughnessMapArray, sampler_RoughnessMapArray, uvX, slice).r;
                half roughnessY = SAMPLE_TEXTURE2D_ARRAY(_RoughnessMapArray, sampler_RoughnessMapArray, uvY, slice).r;
                half roughnessZ = SAMPLE_TEXTURE2D_ARRAY(_RoughnessMapArray, sampler_RoughnessMapArray, uvZ, slice).r;
                half roughness = roughnessX * weights.x + roughnessY * weights.y + roughnessZ * weights.z;
                roughness = lerp(0.8h, roughness, saturate(_RoughnessStrength));
                smoothness = saturate((1.0h - roughness) * 0.1h);

                half aoX = SAMPLE_TEXTURE2D_ARRAY(_AOMapArray, sampler_AOMapArray, uvX, slice).r;
                half aoY = SAMPLE_TEXTURE2D_ARRAY(_AOMapArray, sampler_AOMapArray, uvY, slice).r;
                half aoZ = SAMPLE_TEXTURE2D_ARRAY(_AOMapArray, sampler_AOMapArray, uvZ, slice).r;
                half ambientOcclusion = aoX * weights.x + aoY * weights.y + aoZ * weights.z;
                occlusion = lerp(1.0h, ambientOcclusion, saturate(_AOStrength));
            }

            Varyings TerrainPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.materialInfo = input.materialInfo;
                output.vertexLighting = VertexLighting(positionInputs.positionWS, output.normalWS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 TerrainPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 geometryNormalWS = normalize(input.normalWS);
                int sliceIndex = GetMaterialSliceIndex(input.materialInfo.x);

                half3 albedo;
                half3 worldNormal;
                half smoothness;
                half occlusion;

                SampleTerrainLayer(
                    input.positionWS,
                    geometryNormalWS,
                    sliceIndex,
                    albedo,
                    worldNormal,
                    smoothness,
                    occlusion);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.specular = half3(0.0h, 0.0h, 0.0h);
                surfaceData.metallic = 0.0h;
                surfaceData.smoothness = smoothness;
                surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
                surfaceData.emission = half3(0.0h, 0.0h, 0.0h);
                surfaceData.occlusion = occlusion;
                surfaceData.alpha = 1.0h;
                surfaceData.clearCoatMask = 0.0h;
                surfaceData.clearCoatSmoothness = 0.0h;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(worldNormal);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }
}
