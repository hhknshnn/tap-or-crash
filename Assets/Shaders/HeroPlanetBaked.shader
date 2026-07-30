// Palette atlas x baked vertex shading, opaque and unlit.
//
// The project runs URP's 2D Renderer, which has no 3D lighting pass: URP/Lit and
// URP/Unlit render byte-identically there, so a 3D mesh gets no form from lights.
// The Hero Planet therefore carries its lighting baked into per-corner vertex
// colours (see Tools/hero_planet_builder.py :: bake_vertex_shading) and this
// shader just multiplies the two.
//
// Opaque with ZWrite on, unlike Sprites/Default — a 3D planet with props needs
// real depth sorting, which an alpha-blended sprite shader cannot give it.
Shader "TapOrCrash/HeroPlanetBaked"
{
    Properties
    {
        _BaseMap ("Palette", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0.25, 3)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HeroPlanetForward"
            // Universal2D, not UniversalForward: the 2D Renderer's lighting pass only
            // draws Universal2D-tagged passes. A UniversalForward-only shader silently
            // renders nothing here (verified — the mesh vanished).
            Tags { "LightMode" = "Universal2D" }

            ZWrite On
            ZTest LEqual
            Cull Back
            Blend Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Exposure;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 rgb = albedo * input.color.rgb * _BaseColor.rgb * _Exposure;
                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
