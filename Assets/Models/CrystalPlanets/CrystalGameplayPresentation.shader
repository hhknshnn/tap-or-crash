Shader "TapOrCrash/Crystal Gameplay Presentation"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _VisualScale ("Visual Energy Scale", Range(1, 6)) = 4.4
        _GhostOpacity ("Crystal Energy Opacity", Range(0, 1)) = 0.46
        _ShimmerStrength ("Shimmer Strength", Range(0, 2)) = 0.85
        _ShimmerColor ("Shimmer Color", Color) = (0.55, 0.95, 1, 1)
        _EnergyColor ("Energy Color", Color) = (0.68, 0.38, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "CrystalGameplayPresentation"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShimmerColor;
                float4 _EnergyColor;
                float _VisualScale;
                float _GhostOpacity;
                float _ShimmerStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS.xyz;
                positionOS.xy *= _VisualScale;
                output.positionCS = TransformObjectToHClip(positionOS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half3 BoostCrystalColor(half3 color)
            {
                half luminance = dot(color, half3(0.2126h, 0.7152h, 0.0722h));
                half3 saturated = lerp(luminance.xxx, color, 1.28h);
                return saturate(saturated * 1.12h + half3(0.025h, 0.018h, 0.055h));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;

                // Large translucent presentation layer: visual-only, so physics,
                // orbit radius, camera and SpriteRenderer bounds remain unchanged.
                half4 energySample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                energySample *= input.color;

                float phase = frac(input.uv.x * 0.72 + input.uv.y * 0.46 - _Time.y * 0.16);
                half shimmer = pow(saturate(1.0 - abs(phase - 0.5) / 0.13), 3.0);
                half pulse = 0.86h + 0.14h * sin(_Time.y * 1.45 + length(centered) * 19.0);

                half3 energyColor = BoostCrystalColor(energySample.rgb);
                energyColor = lerp(energyColor, _EnergyColor.rgb, 0.14h);
                energyColor += _ShimmerColor.rgb * shimmer * _ShimmerStrength;
                half energyAlpha = energySample.a * _GhostOpacity * pulse;

                // Original solid planet remains at its exact gameplay size.
                float2 solidUv = centered * _VisualScale + 0.5;
                half inside = step(0.0, solidUv.x) * step(solidUv.x, 1.0)
                            * step(0.0, solidUv.y) * step(solidUv.y, 1.0);
                half4 solidSample = SAMPLE_TEXTURE2D(
                    _MainTex, sampler_MainTex, saturate(solidUv)) * input.color;
                solidSample.a *= inside;
                solidSample.rgb = BoostCrystalColor(solidSample.rgb);
                solidSample.rgb += _ShimmerColor.rgb * shimmer * _ShimmerStrength * 0.32h;

                half outputAlpha = solidSample.a + energyAlpha * (1.0h - solidSample.a);
                half3 premultiplied =
                    solidSample.rgb * solidSample.a
                    + energyColor * energyAlpha * (1.0h - solidSample.a);
                half3 outputColor = premultiplied / max(outputAlpha, 0.0001h);

                return half4(outputColor, outputAlpha);
            }
            ENDHLSL
        }
    }
}
