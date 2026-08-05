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

        // The round's memory beat. The outline does not dim uniformly — that reads as a UI
        // element being faded out. It is eaten away in patches, as though the grass were
        // growing back over it, so the last thing the player sees is a broken, unreliable
        // line rather than a clean one at low opacity.
        _Dissolve    ("Guide dissolve",  Range(0, 1)) = 0
        _DissolveEdge("Dissolve edge glow", Range(0, 1)) = 0.55

        // Position anchors. These never fade: they state where the picture sits and how big
        // it is, so what the player has to remember is the SHAPE, not the registration.
        // Without them a perfectly remembered outline drawn two metres off scores as a miss,
        // which reads as the game cheating rather than as the player forgetting.
        _AnchorColor ("Anchor mark", Color) = (0.97, 0.93, 0.80, 1)
        _AnchorAmount("Anchor amount", Range(0, 1)) = 1
        _AnchorArm   ("Anchor arm (m)", Range(0.5, 6)) = 2.6
        _AnchorWidth ("Anchor width (m)", Range(0.05, 0.8)) = 0.26

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
                float4 _ChalkColor, _GhostFill, _MissColor, _SpillColor, _AnchorColor;
                float _LineWidth, _LineAlpha, _Patchiness, _ScuffFade;
                float _Dissolve, _DissolveEdge;
                float _AnchorAmount, _AnchorArm, _AnchorWidth;
                float _GhostAmount, _AnalysisAmount, _SweepPhase;
            CBUFFER_END

            /// Antialiased filled box, in the same units as the position handed in. Used for the
            /// anchor marks, which are the one part of this shader that has to stay pin sharp from
            /// a 3 m chase camera and a 90 m overhead alike.
            float AABox(float2 p, float2 halfSize)
            {
                float2 d = abs(p) - halfSize;
                float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                float aa = max(fwidth(dist), 0.008);
                return 1.0 - smoothstep(-aa, aa, dist);
            }

            /// One corner bracket: two arms running back toward the centre of the field.
            float CornerBracket(float2 wxz, float2 corner, float2 inward, float arm, float w)
            {
                float2 alongX = wxz - float2(corner.x + inward.x * arm * 0.5, corner.y);
                float2 alongZ = wxz - float2(corner.x, corner.y + inward.y * arm * 0.5);
                return max(AABox(alongX, float2(arm * 0.5, w)),
                           AABox(alongZ, float2(w, arm * 0.5)));
            }

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

                // ---- the memory beat: the guide is eaten away, not dimmed ----
                //
                // Two noise scales, so the line breaks into blotches with ragged interiors. A
                // uniform alpha ramp reads as a HUD element being switched off; this reads as
                // chalk being lost into the grass, which is the whole point of the beat — the
                // player should feel the picture being taken away from them rather than see a
                // fade. The threshold sweeps past the full noise range so _Dissolve = 1 is
                // genuinely nothing left, with no stubborn survivors.
                float disNoise = FBM2(wxz * 0.85) * 0.65 + FBM2(wxz * 3.10) * 0.35;
                float cutoff = _Dissolve * 1.28 - 0.14;
                float keep = smoothstep(cutoff, cutoff + 0.13, disNoise);
                stroke *= keep;

                // A hot rim on whatever is being eaten this instant, so the loss registers as an
                // event. Gated on _Dissolve being genuinely in flight — at rest it must contribute
                // nothing, or the outline glows for the whole first third of the round.
                float dissolving = step(0.002, _Dissolve) * (1.0 - step(0.998, _Dissolve));
                float rim = exp(-pow((disNoise - cutoff) * 8.5, 2.0)) * dissolving;

                result.rgb = lerp(_ChalkColor.rgb, _GhostFill.rgb, saturate(rim * _DissolveEdge));
                result.a = saturate(stroke * _LineAlpha + rim * _DissolveEdge * 0.30 * _LineAlpha);

                // ---- permanent registration anchors ----
                //
                // Four corner brackets on the picture's own frame, plus a centre cross. These do
                // NOT dissolve. They tell the player where the picture sits and how big it is;
                // what has to be remembered is its shape.
                //
                // Without them, a perfectly recalled outline drawn two metres off scores as a
                // total miss, because scoring is coverage against a fixed grid. That reads as the
                // game cheating rather than as the player forgetting, and it would have put every
                // contestant on a D.
                if (_AnchorAmount > 0.001)
                {
                    float R = _ShapeRadius;
                    float w = _AnchorWidth * 0.5;
                    float arm = _AnchorArm;

                    // Plot-local, NOT world.
                    //
                    // These were laid out around the world origin, which is the middle of the
                    // player's lawn — so the anchors existed on exactly one plot in the venue and
                    // the three rivals had none. Since the marks are what tell a contestant where
                    // the picture sits and how big it is, that meant the rivals were being asked
                    // to do the harder job, on a lawn the player then flies over and compares
                    // themselves against. Referencing _FieldOrigin puts a set on every plot; for
                    // the player it is (0,0) and nothing about their field changes.
                    float2 lxz = wxz - _FieldOrigin;

                    float mark = 0.0;
                    mark = max(mark, CornerBracket(lxz, float2(-R, -R), float2( 1,  1), arm, w));
                    mark = max(mark, CornerBracket(lxz, float2( R, -R), float2(-1,  1), arm, w));
                    mark = max(mark, CornerBracket(lxz, float2(-R,  R), float2( 1, -1), arm, w));
                    mark = max(mark, CornerBracket(lxz, float2( R,  R), float2(-1, -1), arm, w));

                    // Centre cross: the registration point the corners triangulate onto.
                    mark = max(mark, AABox(lxz, float2(arm * 0.55, w)));
                    mark = max(mark, AABox(lxz, float2(w, arm * 0.55)));

                    // Anchors are chalk too, so the mower marks them — but only lightly. Scuffing
                    // them out at the same rate as the outline would destroy the one reference the
                    // player is supposed to keep, and the centre cross sits exactly where the round
                    // starts, so it would be the first thing erased.
                    mark *= lerp(1.0, 0.55, saturate(cut));

                    float markA = mark * _AnchorAmount;
                    result.rgb = lerp(result.rgb, _AnchorColor.rgb, markA);
                    result.a = saturate(result.a + markA * 0.85);
                }

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
