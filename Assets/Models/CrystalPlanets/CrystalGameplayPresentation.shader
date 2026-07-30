Shader "TapOrCrash/Crystal Gameplay Presentation"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _VisualScale ("Visual Scale", Range(1, 2)) = 1
        _GhostOpacity ("Legacy Energy Opacity", Range(0, 1)) = 0
        _ShimmerStrength ("Shimmer Strength", Range(0, 2)) = 0.72
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
                half3 saturated = lerp(luminance.xxx, color, 1.38h);
                return saturate(saturated * 1.24h + half3(0.035h, 0.025h, 0.065h));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv - 0.5;
                half4 sample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                sample *= input.color;

                float phase = frac(input.uv.x * 0.72 + input.uv.y * 0.46 - _Time.y * 0.16);
                half shimmer = pow(saturate(1.0 - abs(phase - 0.5) / 0.13), 3.0);
                half pulse = 0.86h + 0.14h * sin(_Time.y * 1.45 + length(centered) * 19.0);

                half3 color = BoostCrystalColor(sample.rgb);
                color += _ShimmerColor.rgb * shimmer * _ShimmerStrength * 0.34h;
                color = lerp(color, color * _EnergyColor.rgb, (pulse - 0.86h) * 0.16h);

                // One opaque presentation surface at the real gameplay boundary.
                // The orbit/capture radius is therefore derived from exactly what
                // the player sees instead of a larger translucent ghost.
                return half4(saturate(color), sample.a);
            }
            ENDHLSL
        }
    }
}
