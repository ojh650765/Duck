// The groundskeeper's chalk outline of today's picture, and — during the reveal — the
// ghosted comparison overlay that shows how close the duck actually got.
//
// Drawn from the target signed-distance texture rather than a bitmap, so the line stays a
// crisp constant width from a 3 m chase camera and from a 90 m overhead shot alike.
Shader "Duck/ChalkGuide"
{
    Properties
    {
        _ChalkColor  ("Chalk",        Color) = (0.90, 0.87, 0.76, 1)
        _GhostFill   ("Ghost fill",   Color) = (1.0, 0.95, 0.55, 1)
        _MissColor   ("Missed area",  Color) = (0.85, 0.25, 0.22, 1)
        _SpillColor  ("Spilled area", Color) = (0.95, 0.55, 0.15, 1)

        _LineWidth   ("Line width (m)",  Range(0.05, 1.2)) = 0.30
        _LineAlpha   ("Line alpha",      Range(0, 1)) = 0.62
        _Patchiness  ("Chalk patchiness",Range(0, 1)) = 0.45
        _ScuffFade   ("Fade under cut",  Range(0, 1)) = 0.75

        _GhostAmount ("Ghost fill amount", Range(0, 1)) = 0
        _AnalysisAmount ("Analysis overlay", Range(0, 1)) = 0
        _SweepPhase  ("Reveal sweep phase", Range(-1.2, 1.2)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "Chalk"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "GrassCommon.hlsl"

            TEXTURE2D(_TargetSdf);
            float _ShapeRadius;
            float _SdfBand;

            CBUFFER_START(UnityPerMaterial)
                float4 _ChalkColor, _GhostFill, _MissColor, _SpillColor;
                float _LineWidth, _LineAlpha, _Patchiness, _ScuffFade;
                float _GhostAmount, _AnalysisAmount, _SweepPhase;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float  fogCoord   : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;
                float2 uv = WorldToMaskUV(IN.positionWS);

                // Stored value is the distance remapped over a narrow band; undo that to metres.
                float raw = SAMPLE_TEXTURE2D(_TargetSdf, sampler_linear_clamp, uv).r;
                float dist = (raw * 2.0 - 1.0) * _SdfBand * _ShapeRadius;

                float cut = SampleCutMaskBilinear(uv).r;

                half4 result = half4(0, 0, 0, 0);

                // ---- the chalk line ----
                float w = _LineWidth * 0.5;
                float aa = max(fwidth(dist), 0.01);
                float stroke = 1.0 - smoothstep(w - aa, w + aa, abs(dist));

                // Chalk is laid by hand: break it up so it never reads as a vector stroke.
                float patch = FBM2(wxz * 1.7) * 0.6 + FBM2(wxz * 6.3) * 0.4;
                stroke *= lerp(1.0, smoothstep(0.28, 0.62, patch), _Patchiness);

                // The mower scuffs the chalk away as it passes over it.
                stroke *= lerp(1.0, 1.0 - _ScuffFade, saturate(cut));

                result.rgb = _ChalkColor.rgb;
                result.a = stroke * _LineAlpha;

                // ---- reveal: fill the target region, wiping in from the far side ----
                if (_GhostAmount > 0.001)
                {
                    float sweep = smoothstep(_SweepPhase - 0.14, _SweepPhase + 0.02, uv.y);
                    float insideMask = 1.0 - smoothstep(-0.05, 0.05, dist);
                    float fill = insideMask * (1.0 - sweep) * _GhostAmount;

                    // A bright leading edge on the wipe so the reveal has a moment of motion.
                    float crest = exp(-pow((uv.y - _SweepPhase) * 26.0, 2.0)) * insideMask * _GhostAmount;

                    half3 fillCol = _GhostFill.rgb;
                    result.rgb = lerp(result.rgb, fillCol, saturate(fill + crest * 1.4));
                    result.a = saturate(result.a + fill * 0.16 + crest * 0.55);
                }

                // ---- verdict: paint what was missed and what spilled ----
                if (_AnalysisAmount > 0.001)
                {
                    float insideMask = 1.0 - smoothstep(-0.02, 0.02, dist);
                    float isCut = smoothstep(0.35, 0.6, cut);

                    float missed = insideMask * (1.0 - isCut);
                    float spilled = (1.0 - insideMask) * isCut;

                    // Hatching so the two overlays stay distinguishable for colour-blind players.
                    float hatch = saturate(sin((wxz.x + wxz.y) * 3.4) * 0.5 + 0.62);
                    float hatch2 = saturate(sin((wxz.x - wxz.y) * 3.4) * 0.5 + 0.62);

                    half3 overlay = result.rgb;
                    float overlayA = result.a;

                    overlay = lerp(overlay, _MissColor.rgb, saturate(missed * hatch));
                    overlayA = saturate(overlayA + missed * hatch * 0.55 * _AnalysisAmount);
                    overlay = lerp(overlay, _SpillColor.rgb, saturate(spilled * hatch2));
                    overlayA = saturate(overlayA + spilled * hatch2 * 0.5 * _AnalysisAmount);

                    result.rgb = overlay;
                    result.a = overlayA;
                }

                clip(result.a - 0.004);
                result.rgb = MixFog(result.rgb, IN.fogCoord);
                return result;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
