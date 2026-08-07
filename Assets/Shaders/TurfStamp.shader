// Writes ownership into the Bloom Rush turf mask.
//
// Drawn by TurfMask.cs through a CommandBuffer with an orthographic projection that maps world XZ
// straight onto the mask, so a mesh vertex is literally (worldX, worldZ, 0) — the same trick
// CutStamp uses for the lawn, and for the same reason: one quad per swath segment, oversized at
// both ends, with the real capsule resolved per fragment. No gaps however fast a mower moves.
//
// Mask channels:
//   R = owner. 0 is neutral; competitor i is stored as (i+1)/8.
//   G = claim freshness, 1 at the moment of claiming, decayed towards 0 by the Decay pass.
//   B = steal heat, 1 when the claim took the ground off somebody, decayed faster than G.
//   A = a per-claim hash, frozen at claim time. Varies the flowers so two neighbouring passes do
//       not grow the identical bed.
//
// OWNERSHIP IS WRITTEN HARD AND THE EFFECTS ARE WRITTEN SOFT, in two passes, and that split is the
// whole reason the mode is scoreable. A single feathered pass lerps R between two owner codes, and
// a code halfway between HORACE and MARGOT decodes to whoever happens to sit between them in the
// enum — an entire feathered border's worth of ground silently awarded to a third party. So pass 0
// clips at half coverage and writes R opaque, which is exactly the rule TurfMask's CPU grid applies
// to the same swath; pass 1 feathers the look into G, B and A where a smooth edge costs nothing.

Shader "Duck/TurfStamp"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // For TurfPlayableHard. The stamp has to obey the SAME definition of what is playable as the
        // scoring grid, and the only way to guarantee that is to call the same function.
        #include "TurfCommon.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float4 seg        : TEXCOORD0; // xy = segment start (world XZ), zw = segment end
            float4 param      : TEXCOORD1; // x = radius, y = owner code, z = steal heat, w = feather
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

        // How much of this texel the capsule covers, 0..1.
        float Coverage(Varyings IN)
        {
            float2 a = IN.seg.xy;
            float2 b = IN.seg.zw;
            float2 pa = IN.worldXZ - a;
            float2 ba = b - a;
            float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
            float d = length(pa - ba * h);

            float radius = IN.param.x;
            float feather = max(IN.param.w, 1e-4);
            return saturate((radius - d) / feather + 0.5);
        }

        float Hash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        // Refuse to write anywhere the score would refuse to count.
        //
        // This is the stamp obeying the same rule the CPU grid already obeyed, and it was missing.
        // TurfMask.Claim skips a cell whose byte says Blocked, so the NUMBERS were always right —
        // but nothing stopped the render texture being painted under a hedge or out past the
        // touchline, so the PICTURE showed territory in places that could never be scored. Ground a
        // player can see themselves claiming and then does not get credit for is the worst kind of
        // lie a territory mode can tell.
        //
        // It matters most on the strip between the touchline and the barrier: a mower can drive
        // there, so it was painting a coloured ring around an arena that ends further in.
        //
        // THE TEST IS TurfPlayableHard AND NOT THE TurfPlayable THE GROUND SHADER USES, and the two
        // shaders asking the same question through two different functions is the design and not a
        // leftover. TurfPlayable is feathered on purpose — the arena's colour and its glow ought to
        // die away over the last metre instead of stopping on a stencil line, and that softness is
        // the ground layer's to keep. But a feather is a picture, and this is a RULE: the stamp is
        // the only thing writing the render texture the picture is read from, and what it refuses to
        // write has to be the exact set TurfArena.IsPlayable refuses, to the metre.
        //
        // Clipping the feathered ramp at half strength was NOT that set. The ramps are 1.2 m wide,
        // so half strength lands 0.6 m inside the touchline and 0.6 m outside the core wall, while
        // the CPU grid counts and claims all the way to both. Two rings — about 157 m2 at the rim,
        // 31 m2 around the core — changed hands, paid out, and never got painted. Reading a score
        // for ground that is still visibly neutral is the worst lie a territory mode can tell, and
        // it is worse than the ring this function was written to stop, because that one at least
        // erred towards showing the player LESS than they were given.
        //
        // Not fixed by lowering the 0.5, which is the tempting one-liner: at the hedge the ramp is
        // exactly 0.5 where the clearance is exactly zero, so the old threshold was already right
        // there, and anything lower walks the paint into the hedges instead. See TurfPlayableHard.
        void ClipToArena(float2 worldXZ)
        {
            clip(TurfPlayableHard(worldXZ) ? 1.0 : -1.0);
        }
        ENDHLSL

        // ---- pass 0: ownership. Hard edged, R only. -------------------------------------------
        Pass
        {
            Name "Claim"
            Blend Off
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings IN) : SV_Target
            {
                ClipToArena(IN.worldXZ);
                // Half coverage or nothing. A texel belongs to exactly one competitor at all times,
                // which is what makes the percentages add up and the borders stay crisp.
                clip(Coverage(IN) - 0.5);
                return float4(IN.param.y, 0, 0, 0);
            }
            ENDHLSL
        }

        // ---- pass 1: the look. Feathered, GBA only. -------------------------------------------
        Pass
        {
            Name "Bloom"
            // Max rather than alpha blending. Two swaths crossing in the same frame — which happens
            // every time two mowers meet — would otherwise average their freshness down and put a
            // dim seam through the middle of the most dramatic moment in the mode.
            BlendOp Max
            Blend One One
            ColorMask GBA

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Frag(Varyings IN) : SV_Target
            {
                ClipToArena(IN.worldXZ);
                float cov = Coverage(IN);
                clip(cov - 0.02);

                // The hash is frozen per claim and must not be dimmed by coverage, or the feathered
                // rim of every swath grows the same flower. Quantised to roughly a bloom's footprint
                // so a cluster shares a seed instead of shimmering per texel.
                float seed = Hash(floor(IN.worldXZ * 3.0));

                return float4(0, cov, cov * IN.param.z, seed);
            }
            ENDHLSL
        }

        // ---- pass 2: decay. Full-screen, run once per frame before the stamps. -----------------
        Pass
        {
            Name "Decay"
            Blend Off

            HLSLPROGRAM
            #pragma vertex DecayVert
            #pragma fragment DecayFrag

            TEXTURE2D(_TurfPrev);
            SAMPLER(sampler_point_clamp);
            float2 _TurfDecay;   // x = freshness multiplier this frame, y = steal heat multiplier

            struct DecayAttributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct DecayVaryings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            DecayVaryings DecayVert(DecayAttributes IN)
            {
                DecayVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 DecayFrag(DecayVaryings IN) : SV_Target
            {
                // Point sampled and 1:1, so R and A come through bit-exact. Anything that resampled
                // ownership here would smear the whole map by a texel per frame, and after two
                // minutes of that there would be no borders left at all.
                float4 c = SAMPLE_TEXTURE2D(_TurfPrev, sampler_point_clamp, IN.uv);
                c.g *= _TurfDecay.x;
                c.b *= _TurfDecay.y;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
