Shader "CpcPrototype/Combat Effect"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        [HDR] _Tint("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend("Destination Blend", Float) = 10
        _Radial("Radial Mask", Float) = 0
        _MaskMode("Texture Mask Mode", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha [_DestinationBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float _Radial;
                float _MaskMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float falloff = 1 - smoothstep(.15, .5, length(input.uv - .5));
                if (_MaskMode > 2.5)
                    texel.rgb = 1;
                else if (_MaskMode > 1.5)
                    texel.a *= 1 - smoothstep(.38, .70, texel.r);
                else if (_MaskMode > .5)
                    texel = float4(1, 1, 1, texel.a * texel.r);
                return float4(texel.rgb * _Tint.rgb, texel.a * _Tint.a * lerp(1, falloff, _Radial));
            }
            ENDHLSL
        }
    }
}


