Shader "Hidden/URP/Paper Depth Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "PaperDepthComposite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PaperBDepthRT);
            SAMPLER(sampler_PaperBDepthRT);
            TEXTURE2D(_PaperCDepthRT);
            SAMPLER(sampler_PaperCDepthRT);
            TEXTURE2D(_PaperCColorRT);
            SAMPLER(sampler_PaperCColorRT);
            float _PaperOcclusionDepthEpsilonMeters;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 background = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, uv);
                half4 cap = SAMPLE_TEXTURE2D(
                    _PaperCColorRT, sampler_PaperCColorRT, uv);
                float depthB = SAMPLE_TEXTURE2D(
                    _PaperBDepthRT, sampler_PaperBDepthRT, uv).r;
                float depthC = SAMPLE_TEXTURE2D(
                    _PaperCDepthRT, sampler_PaperCDepthRT, uv).r;
                bool capExists = depthC < 999.0;
                bool bottleExists = depthB < 999.0;
                bool capInFront = !bottleExists
                    || depthC < depthB - _PaperOcclusionDepthEpsilonMeters;
                return capExists && capInFront ? half4(cap.rgb, 1.0) : background;
            }
            ENDHLSL
        }

        Pass
        {
            Name "PaperDepthMaskQA"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMask
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PaperBDepthRT);
            SAMPLER(sampler_PaperBDepthRT);
            TEXTURE2D(_PaperCDepthRT);
            SAMPLER(sampler_PaperCDepthRT);
            TEXTURE2D(_PaperCColorRT);
            SAMPLER(sampler_PaperCColorRT);
            float _PaperOcclusionDepthEpsilonMeters;

            half4 FragMask(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half capAlpha = SAMPLE_TEXTURE2D(
                    _PaperCColorRT, sampler_PaperCColorRT, uv).a;
                float depthB = SAMPLE_TEXTURE2D(
                    _PaperBDepthRT, sampler_PaperBDepthRT, uv).r;
                float depthC = SAMPLE_TEXTURE2D(
                    _PaperCDepthRT, sampler_PaperCDepthRT, uv).r;
                bool capExists = depthC < 999.0;
                bool bottleExists = depthB < 999.0;
                bool visible = capExists && (!bottleExists
                    || depthC < depthB - _PaperOcclusionDepthEpsilonMeters);
                return visible ? half4(1, 1, 1, 1) : half4(0, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ExtractOriginalCapColor"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragExtract
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_PaperCDepthRT);
            SAMPLER(sampler_PaperCDepthRT);

            half4 FragExtract(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float depthC = SAMPLE_TEXTURE2D(
                    _PaperCDepthRT, sampler_PaperCDepthRT, uv).r;
                if (depthC >= 999.0)
                    return half4(0, 0, 0, 0);
                half4 directCameraColor = SAMPLE_TEXTURE2D_X(
                    _BlitTexture, sampler_LinearClamp, uv);
                return half4(directCameraColor.rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
