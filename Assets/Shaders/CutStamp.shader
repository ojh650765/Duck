// Stamps the mower's blade swath and wheel tracks into the cut mask render texture.
// Drawn by CutMask.cs through a CommandBuffer with an orthographic projection that
// maps world XZ directly onto the mask, so a mesh vertex is literally (worldX, worldZ, 0).
//
// Mask channels:  R = cut amount, G = wheel track, B = mow direction (angle / 2pi), A = cut
//
// One quad per swath segment. The quad is oversized by the swath radius at both ends and
// the real shape is resolved in the fragment shader as the distance to the segment, which
// gives round caps and a feathered edge for free — no gaps however fast the mower moves.
Shader "Duck/CutStamp"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 seg        : TEXCOORD0; // xy = segment start (world XZ), zw = segment end
            float4 param      : TEXCOORD1; // x = radius, y = direction 0..1, z = strength, w = feather
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 worldXZ    : TEXCOORD0;
            float4 seg        : TEXCOORD1;
            float4 param      : TEXCOORD2;
        };

        Varyings Vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
            OUT.worldXZ = IN.positionOS.xy;
            OUT.seg = IN.seg;
            OUT.param = IN.param;
            return OUT;
        }

        // Coverage of this pixel by the capsule defined by the segment and radius.
        float Coverage(Varyings IN)
        {
            float2 a = IN.seg.xy;
            float2 b = IN.seg.zw;
            float2 pa = IN.worldXZ - a;
            float2 ba = b - a;
            float denom = max(dot(ba, ba), 1e-6);
            float h = saturate(dot(pa, ba) / denom);
            float d = length(pa - ba * h);

            float radius = IN.param.x;
            float feather = max(IN.param.w, 1e-4);
            return (1.0 - smoothstep(radius - feather, radius, d)) * IN.param.z;
        }
        ENDHLSL

        // ---- Pass 0: the cutting deck. Writes cut amount, direction and alpha. ----
        Pass
        {
            Name "Cut"
            ColorMask RBA
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCut

            float4 FragCut(Varyings IN) : SV_Target
            {
                float cov = Coverage(IN);
                clip(cov - 0.002);
                return float4(1.0, 0.0, IN.param.y, cov);
            }
            ENDHLSL
        }

        // ---- Pass 1: wheel tracks. Writes only the track channel so it cannot erase a cut. ----
        Pass
        {
            Name "Track"
            ColorMask G
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTrack

            float4 FragTrack(Varyings IN) : SV_Target
            {
                float cov = Coverage(IN);
                clip(cov - 0.002);
                return float4(0.0, 1.0, 0.0, cov);
            }
            ENDHLSL
        }

        // ---- Pass 2: generic paint, used to pre-stamp decorative mowing (last year's picture). ----
        Pass
        {
            Name "Paint"
            ColorMask RBA
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPaint

            float4 FragPaint(Varyings IN) : SV_Target
            {
                float cov = Coverage(IN);
                clip(cov - 0.002);
                return float4(1.0, 0.0, IN.param.y, cov);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
