Shader "Hidden/URP/Paper Linear Eye Depth"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "LinearEyeDepth"
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float eyeDepthMeters : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.eyeDepthMeters = -TransformWorldToView(positionWS).z;
                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                return max(input.eyeDepthMeters, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
