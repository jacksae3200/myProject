Shader "CpcPrototype/ScopeLens"
{
    Properties
    {
        _MainTex ("Camera Render Texture", 2D) = "black" {}
        [Normal] _BumpMap ("Circular Lens Normal", 2D) = "bump" {}
        _Distortion ("Lens Curvature", Range(-0.3, 0.3)) = 0.09
        _Zoom ("Image Zoom", Range(1, 4)) = 1.15
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.15)) = 0.025
        _Vignette ("Edge Darkening", Range(0, 1)) = 0.3
        _Tint ("Glass Tint", Color) = (0.94, 0.98, 1, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "ScopeLens"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _Distortion, _Zoom, _EdgeSoftness, _Vignette;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2.0 - 1.0;
                float radius = length(centered);
                float feather = max(_EdgeSoftness, fwidth(radius));
                half mask = 1.0 - smoothstep(1.0 - feather, 1.0, radius);
                clip(mask - 0.001);
                // A hemispherical normal bends the captured image towards the rim.
                half3 normal = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                float2 uv = 0.5 + (input.uv - 0.5) / _Zoom;
                uv += normal.xy * _Distortion * radius * radius;
                uv = saturate(uv);
                half3 image = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                image *= _Tint.rgb * (1.0 - _Vignette * pow(radius, 4.0));
                return half4(image, mask * _Tint.a);
            }
            ENDHLSL
        }
    }
}
