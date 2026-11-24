Shader "Hidden/CPC/City Selective Pixel"
{
    Properties
    {
        _PixelHeight ("Pixel Height", Float) = 200
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "City Pixel Pass"
            Cull Off
            ZWrite Off
            ZTest Always
            Stencil
            {
                Ref 1
                ReadMask 1
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelHeight;
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float height = max(1.0, _PixelHeight);
                float2 grid = float2(height * _ScreenParams.x / _ScreenParams.y, height);
                float2 uv = (floor(input.texcoord * grid) + 0.5) / grid;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Transparent Exclusion Mask"
            ColorMask 0
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 MaskVertex(float3 position : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(position);
            }

            half4 MaskFragment() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}

