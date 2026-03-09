Shader "A_Game/Fluid Stylized"
{
    Properties
    {
        _ShallowColor("Shallow Color", Color) = (0.10, 0.54, 0.66, 1)
        _DeepColor("Deep Color", Color) = (0.09, 0.29, 0.54, 1)
        _ReflectionTint("Reflection Tint", Color) = (1, 1, 1, 1)

        _SurfaceAlpha("Surface Alpha", Range(0, 1)) = 0.32
        _Smoothness("Smoothness", Range(0, 1)) = 0.55
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.82
        _FresnelPower("Fresnel Power", Range(0.1, 8)) = 4
        _GlintStrength("Glint Strength", Range(0, 1)) = 0.28
        _GlintScale("Glint Scale", Float) = 18
        _GlintSharpness("Glint Sharpness", Range(8, 128)) = 42

        _FalloffStrength("Falloff Strength", Range(0, 1)) = 0.28
        _FalloffDepth("Falloff Depth", Range(0, 1)) = 0.55
        _RefractionStrength("Refraction Strength", Range(0, 1)) = 0.2
        _UnderwaterRefractionStrength("Underwater Refraction Strength", Range(0, 1)) = 0.28

        _PrimaryWaveDirection("Primary Wave Direction", Vector) = (0.9, 0.25, 0, 0)
        _PrimaryWaveFrequency("Primary Wave Frequency", Float) = 0.22
        _PrimaryWaveSpeed("Primary Wave Speed", Float) = 0.28
        _PrimaryWaveAmplitude("Primary Wave Amplitude", Range(0, 1)) = 0.22
        _PrimaryNoiseScale("Primary Noise Scale", Float) = 0.24
        _PrimaryNoiseSpeed("Primary Noise Speed", Float) = 0.06
        _PrimaryPhaseNoiseStrength("Primary Phase Noise Strength", Range(0, 1)) = 0.18
        _PrimaryAmplitudeNoiseStrength("Primary Amplitude Noise Strength", Range(0, 1)) = 0.18

        _SecondaryWaveDirection("Secondary Wave Direction", Vector) = (-0.35, 0.94, 0, 0)
        _SecondaryWaveFrequency("Secondary Wave Frequency", Float) = 0.37
        _SecondaryWaveSpeed("Secondary Wave Speed", Float) = 0.19
        _SecondaryWaveAmplitude("Secondary Wave Amplitude", Range(0, 1)) = 0.12
        _SecondaryNoiseScale("Secondary Noise Scale", Float) = 0.36
        _SecondaryNoiseSpeed("Secondary Noise Speed", Float) = 0.08
        _SecondaryPhaseNoiseStrength("Secondary Phase Noise Strength", Range(0, 1)) = 0.22
        _SecondaryAmplitudeNoiseStrength("Secondary Amplitude Noise Strength", Range(0, 1)) = 0.22

        _DetailNormalStrength("Detail Normal Strength", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor;
            half4 _DeepColor;
            half4 _ReflectionTint;
            float4 _PrimaryWaveDirection;
            float4 _SecondaryWaveDirection;
            half _SurfaceAlpha;
            half _Smoothness;
            half _ReflectionStrength;
            half _FresnelPower;
            half _GlintStrength;
            half _GlintScale;
            half _GlintSharpness;
            half _FalloffStrength;
            half _FalloffDepth;
            half _RefractionStrength;
            half _UnderwaterRefractionStrength;
            half _PrimaryWaveFrequency;
            half _PrimaryWaveSpeed;
            half _PrimaryWaveAmplitude;
            half _PrimaryNoiseScale;
            half _PrimaryNoiseSpeed;
            half _PrimaryPhaseNoiseStrength;
            half _PrimaryAmplitudeNoiseStrength;
            half _SecondaryWaveFrequency;
            half _SecondaryWaveSpeed;
            half _SecondaryWaveAmplitude;
            half _SecondaryNoiseScale;
            half _SecondaryNoiseSpeed;
            half _SecondaryPhaseNoiseStrength;
            half _SecondaryAmplitudeNoiseStrength;
            half _DetailNormalStrength;
            CBUFFER_END

            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);

            float4x4 _PlanarReflectionMatrix;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float viewDepth : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            float2 NormalizeSafe2(float2 v, float2 fallback)
            {
                float lenSq = dot(v, v);
                return lenSq > 0.0001f ? v * rsqrt(lenSq) : fallback;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34f, 345.45f));
                p += dot(p, p + 34.345f);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0f - 2.0f * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0f, 0.0f));
                float c = Hash21(i + float2(0.0f, 1.0f));
                float d = Hash21(i + float2(1.0f, 1.0f));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FractalNoise(float2 p)
            {
                float n0 = ValueNoise(p);
                float n1 = ValueNoise(p * 2.13f + float2(11.3f, 7.1f));
                float n2 = ValueNoise(p * 4.31f + float2(-5.2f, 19.7f));
                return n0 * 0.57f + n1 * 0.28f + n2 * 0.15f;
            }

            void EvaluateWaveField(float3 positionWS, out float heightValue, out float2 gradient)
            {
                float2 dirA = NormalizeSafe2(_PrimaryWaveDirection.xy, float2(1.0f, 0.0f));
                float2 dirB = NormalizeSafe2(_SecondaryWaveDirection.xy, float2(0.0f, 1.0f));
                float time = _Time.y;

                float2 perpA = float2(-dirA.y, dirA.x);
                float2 perpB = float2(-dirB.y, dirB.x);

                float2 waveNoiseUvA =
                    float2(dot(positionWS.xz, dirA), dot(positionWS.xz, perpA)) * max(_PrimaryNoiseScale, 0.0001f) +
                    float2(time * (_PrimaryNoiseSpeed * 0.61f + _PrimaryWaveSpeed * 0.17f), -time * (_PrimaryNoiseSpeed * 0.47f + _PrimaryWaveSpeed * 0.11f));
                float2 waveNoiseUvB =
                    float2(dot(positionWS.xz, dirB), dot(positionWS.xz, perpB)) * max(_SecondaryNoiseScale, 0.0001f) +
                    float2(-time * (_SecondaryNoiseSpeed * 0.53f + _SecondaryWaveSpeed * 0.13f), time * (_SecondaryNoiseSpeed * 0.76f + _SecondaryWaveSpeed * 0.19f));

                float phaseNoiseA = (FractalNoise(waveNoiseUvA + float2(7.3f, -3.9f)) - 0.5f) * (_PrimaryPhaseNoiseStrength * 3.1f);
                float phaseNoiseB = (FractalNoise(waveNoiseUvB + float2(-21.4f, 15.8f)) - 0.5f) * (_SecondaryPhaseNoiseStrength * 3.4f);
                float amplitudeNoiseA = lerp(
                    1.0f - _PrimaryAmplitudeNoiseStrength,
                    1.0f + _PrimaryAmplitudeNoiseStrength,
                    FractalNoise(waveNoiseUvA * 1.37f + float2(18.7f, 9.1f)));
                float amplitudeNoiseB = lerp(
                    1.0f - _SecondaryAmplitudeNoiseStrength,
                    1.0f + _SecondaryAmplitudeNoiseStrength,
                    FractalNoise(waveNoiseUvB * 1.91f + float2(-14.2f, 13.6f)));

                float phaseA = dot(positionWS.xz, dirA * _PrimaryWaveFrequency) + time * _PrimaryWaveSpeed + phaseNoiseA;
                float phaseB = dot(positionWS.xz, dirB * _SecondaryWaveFrequency) + time * _SecondaryWaveSpeed + phaseNoiseB;

                float waveA = sin(phaseA) * _PrimaryWaveAmplitude * amplitudeNoiseA;
                float waveB = sin(phaseB) * _SecondaryWaveAmplitude * amplitudeNoiseB;

                heightValue = waveA + waveB;

                gradient =
                    dirA * (cos(phaseA) * _PrimaryWaveFrequency * _PrimaryWaveAmplitude * amplitudeNoiseA) +
                    dirB * (cos(phaseB) * _SecondaryWaveFrequency * _SecondaryWaveAmplitude * amplitudeNoiseB);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.viewDepth = -TransformWorldToView(positionInputs.positionWS).z;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 geometryNormalWS = normalize(input.normalWS);
                float upwardSurface = saturate(geometryNormalWS.y);

                float waveHeight;
                float2 waveGradient;
                EvaluateWaveField(input.positionWS, waveHeight, waveGradient);
                float3 waveNormalWS = normalize(float3(-waveGradient.x, 1.0f, -waveGradient.y));
                float3 surfaceNormalWS = normalize(lerp(geometryNormalWS, waveNormalWS, saturate(0.35f + upwardSurface * 0.65f)));

                float2 screenUv = input.screenPos.xy / max(input.screenPos.w, 0.0001f);
                float2 refractionOffset =
                    (surfaceNormalWS.xz * 0.6f + waveGradient * 0.18f) *
                    (_RefractionStrength * lerp(0.5f, 1.35f, upwardSurface));
                float2 refractUv = screenUv + refractionOffset;
                refractUv = saturate(refractUv);

                half3 deepColor = _DeepColor.rgb;
                half3 shallowColor = _ShallowColor.rgb;

                float rawSceneDepth = SampleSceneDepth(refractUv);
                float sceneDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterDepth = max(sceneDepth - input.viewDepth, 0.0f);
                float falloff = 1.0f - saturate((1.0f - _FalloffDepth) * waterDepth);
                half3 baseColor = lerp(deepColor, shallowColor, falloff * _FalloffStrength);
                float2 underwaterDistortUv = screenUv + refractionOffset * lerp(1.15f, 2.1f, saturate(waterDepth * 0.08f));
                underwaterDistortUv = saturate(underwaterDistortUv);
                half3 underwaterDistortedScene = SampleSceneColor(underwaterDistortUv) * shallowColor;

                float4 reflectionClip = mul(_PlanarReflectionMatrix, float4(input.positionWS, 1.0f));
                float2 reflectionUv = reflectionClip.xy / max(reflectionClip.w, 0.0001f);
                reflectionUv = reflectionUv * 0.5f + 0.5f;
                reflectionUv.y = 1.0f - reflectionUv.y;
                float2 reflectionDistortion =
                    (surfaceNormalWS.xz * 0.16f + waveGradient * 0.015f) *
                    (_RefractionStrength * 0.08f);
                reflectionUv += reflectionDistortion;
                reflectionUv = clamp(reflectionUv, 0.001f, 0.999f);

                half3 reflectionColor = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, reflectionUv).rgb * _ReflectionTint.rgb;

                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                float frontFacing = dot(viewDirWS, geometryNormalWS);
                float underwaterSurfaceView = saturate(upwardSurface * step(frontFacing, 0.0f));
                float2 underwaterUv = screenUv +
                    (surfaceNormalWS.xz * 0.45f + waveGradient * 0.24f) *
                    (_UnderwaterRefractionStrength * 1.2f);
                underwaterUv = saturate(underwaterUv);
                half3 underwaterScene = SampleSceneColor(underwaterUv) * shallowColor;

                float fresnel = pow(1.0f - saturate(dot(viewDirWS, surfaceNormalWS)), _FresnelPower);
                float reflectionWeight = saturate((0.08f + 0.92f * fresnel) * _ReflectionStrength * lerp(0.2f, 1.0f, upwardSurface));
                float underwaterViewWeight = saturate((1.0f - fresnel) * falloff * 1.2f);

                Light mainLight = GetMainLight();
                float3 lightDirWS = SafeNormalize(mainLight.direction);
                float3 halfVector = SafeNormalize(lightDirWS + viewDirWS);
                float glintHighlight = pow(saturate(dot(surfaceNormalWS, halfVector)), _GlintSharpness);
                float2 glintUv = input.positionWS.xz * _GlintScale + lightDirWS.xz * (_Time.y * 0.45f);
                float glintNoiseA = ValueNoise(glintUv + float2(17.3f, -9.1f));
                float glintNoiseB = ValueNoise(glintUv * 1.91f + float2(-13.7f, 21.4f));
                float glintMask = saturate((glintNoiseA * 0.65f + glintNoiseB * 0.35f - 0.62f) * 3.2f);
                float glint = glintHighlight * glintMask * saturate(mainLight.shadowAttenuation);
                half3 specularColor = mainLight.color * glint * _GlintStrength * lerp(0.4h, 1.0h, upwardSurface);

                half3 aboveSurfaceBase = lerp(baseColor, underwaterDistortedScene, underwaterViewWeight);
                half3 aboveSurfaceColor = lerp(aboveSurfaceBase, reflectionColor, reflectionWeight) + specularColor;
                half3 underwaterColor = lerp(baseColor, underwaterScene, 0.92h);
                half3 finalColor = lerp(aboveSurfaceColor, underwaterColor, underwaterSurfaceView);
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, _SurfaceAlpha);
            }
            ENDHLSL
        }
    }
}
