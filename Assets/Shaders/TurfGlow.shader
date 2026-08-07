// The additive layer Bloom Rush throws in the air: claim rings, steal bursts, petals, the horn wave.
//
// Unlit, additive, no depth write, no shadows, one draw per piece with the colour and fade carried
// on a MaterialPropertyBlock so nothing allocates a material at runtime. Deliberately spartan: on
// a WebGL frame that is already paying for a 1024² mask copy and four mowers, the effects layer
// has to be the cheapest thing on screen, and every one of these is a handful of triangles with a
// two-instruction fragment shader.
//
// Soft-edged through vertex colour alpha rather than a texture, so there is no sampler bound at all
// and nothing to load, decode or keep resident.
Shader "Duck/TurfGlow"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _Fade  ("Fade",   Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            // Depth tested but not written, so a ring lying on the turf is occluded by a mower in
            // front of it and does not stack with itself where two rings overlap.
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Additive: the alpha does the fading by scaling the colour, so there is no blend
                // state that can leave a dark rectangle behind when the piece is retired.
                half a = IN.color.a * _Fade;
                return half4(_Color.rgb * IN.color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
