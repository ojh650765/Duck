// The Bloom Rush floor: the soil layer under the planting.
//
// It works with Duck/TurfBlades rather than instead of it. The blades are the grass and carry the
// height, the wind and most of the colour; this carries what a blade layer cannot — the ground
// between the blades, everything past the distance the blades thin out at, and the whole arena as
// seen from the ninety-metre overhead reveal, where the planting has faded to nothing and the
// percentages still have to be readable.
//
// Three rules it is built on, each of which was learned by getting it wrong:
//
//   FEEDBACK IS PLANTING FIRST, LIGHT SECOND. The claim, the steal, the border and the crown are
//   all done in ALBEDO — as a change in what is growing there — and that ordering is not
//   negotiable. This shader carried no emissive at all while the match was in daylight, and it was
//   right not to: a glowing floor under a midday sun reads as a light rig switched on beneath the
//   turf rather than as a garden.
//
//   The arena is played at NIGHT now, and at night the same term means something else. New growth
//   and freshly turned earth catching what little light there is gives the player a trail they can
//   follow across a dark arena, and lets them see an opponent laying one down from the far side of
//   it. So there IS emissive here — but only on ground that has just changed hands, brightest on
//   the newest and settling to a low ember, so a mower leaves a cooling wake rather than a
//   territory that glows flat.
//
//   OWNERSHIP IS POINT SAMPLED AND DITHERED. Filtering the mask is not allowed: a tap between two
//   owner codes decodes to a competitor who is not on the pitch. But raw point sampling makes every
//   boundary a staircase of nine-centimetre squares, which at mower height is visibly pixels. So
//   the tap POSITION is jittered by a noise about a texel wide, and the staircase becomes a ragged
//   stippled edge — which is what a boundary between two kinds of planting actually looks like.
//
//   ONE TEST FOR WHAT IS PLAYABLE. The hedge footprint and the touchline are carved out of the
//   floor by the same TurfArena geometry the scoring grid and the collision ring use, so there is
//   no strip anywhere in the arena that looks paintable and is not.
//
// Borders are resolved by four point taps mapped to colour and then blended. Blending the IDS would
// invent owners; blending the COLOURS gives an antialiased edge that is always one of the four real
// liveries.
Shader "Duck/TurfGround"
{
    Properties
    {
        [Header(Neutral lawn)]
        _NeutralBase  ("Neutral base",  Color) = (0.0412, 0.1373, 0.0451, 1)
        _NeutralTip   ("Neutral tip",   Color) = (0.0902, 0.2314, 0.0745, 1)
        _SoilColor    ("Under hedge",   Color) = (0.0510, 0.0392, 0.0275, 1)

        [Header(Claimed turf)]
        _TurfDarken   ("Claimed shade",     Range(0,1)) = 0.34
        _PatternDepth ("Turf pattern depth",Range(0,1)) = 0.42
        _BladeGrain   ("Blade grain",       Range(0,1)) = 0.55

        [Header(Blooms)]
        _BloomDensity ("Flowers per metre", Float) = 1.35
        _BloomSize    ("Flower size",       Range(0.05,0.5)) = 0.26
        _BloomLift    ("Flower relief",     Range(0,1)) = 0.7

        [Header(Borders)]
        _BorderWidth  ("Border width (texels)", Range(0.5,6)) = 2.2
        _BorderEdge   ("Border strength",   Range(0,3)) = 1.15

        [Header(Night glow)]
        _PathGlow   ("Fresh path glow", Range(0,4)) = 1.6
        _ClaimEmber ("Settled ember",   Range(0,1)) = 0.09
        _StealGlow  ("Steal glow",      Range(0,4)) = 2.2

        [Header(Gate lanterns)]
        _GateColor     ("Lantern colour", Color) = (1.0, 0.74, 0.42, 1)
        _GateRange     ("Lantern reach (m)", Float) = 11
        _GateIntensity ("Lantern strength", Range(0,3)) = 1.25

        [Header(Light)]
        _Wrap         ("Light wrap",   Range(0,0.6)) = 0.28
        _AmbientGain  ("Ambient gain", Range(0.5,3)) = 1.05
        _AmbientFloor ("Sky bounce",   Range(0,0.4)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TurfCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _NeutralBase, _NeutralTip, _SoilColor;
                float _TurfDarken, _PatternDepth, _BladeGrain;
                float _BloomDensity, _BloomSize, _BloomLift;
                float _BorderWidth, _BorderEdge;
                float _PathGlow, _ClaimEmber, _StealGlow;
                float4 _GateColor;
                float _GateRange, _GateIntensity;
                float _Wrap, _AmbientGain, _AmbientFloor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // ---- one competitor's turf: colour, pattern and how it grows -----------------------

            // Each competitor mows a different pattern at their own bearing. Colour alone is not
            // enough — four saturated hues on grass in shadow at forty metres converge, and the one
            // question this mode asks every second is "whose is that".
            float TurfPattern(int owner, float2 wxz)
            {
                float phase = _TurfLivery[owner].a;          // their spoke bearing, radians
                float2 dir = float2(sin(phase), cos(phase));
                float along  = dot(wxz, dir);
                float across = dot(wxz, float2(-dir.y, dir.x));

                if (owner == 0)                              // broad roller stripes
                    return sin(across * 1.05) * 0.5 + 0.5;
                if (owner == 1)                              // tight ribbing
                    return frac(across * 0.62) < 0.5 ? 0.86 : 0.18;
                if (owner == 2)                              // chequerboard
                    return (frac(across * 0.42) < 0.5) == (frac(along * 0.42) < 0.5) ? 0.84 : 0.2;
                // concentric rings out from the middle of the arena
                return sin(length(wxz) * 1.15) * 0.5 + 0.5;
            }

            // Flowers, as geometry the shader fakes rather than as dots it stamps.
            //
            // A cell grid hashed twice: once for whether this cell has a bloom at all and where in
            // the cell it sits, once for its size. Returns coverage in x and a fake height in y,
            // which the lighting below turns into a lit dome with a contact shadow. That relief is
            // what stops the claimed ground reading as a decal.
            float2 Blooms(float2 wxz, float seed, float grow)
            {
                float2 cell = wxz * _BloomDensity;
                float2 id = floor(cell);
                float2 f = frac(cell) - 0.5;

                float h = TurfHash21(id + seed * 37.0);
                if (h < 0.42) return 0.0;                    // most cells stay plain turf

                float2 jitter = float2(TurfHash21(id + 11.3), TurfHash21(id + 27.9)) - 0.5;
                float d = length(f - jitter * 0.62);

                float radius = _BloomSize * (0.55 + h * 0.75) * grow;
                float cov = 1.0 - smoothstep(radius * 0.72, radius, d);
                float dome = sqrt(saturate(1.0 - (d / max(radius, 1e-3))));
                return float2(cov, dome * cov);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;

                float hedgeDepth;
                float playable = TurfPlayable(wxz, hedgeDepth);

                // ---- resolve ownership: four point taps, blended in colour space ---------------
                //
                // The tap position is DITHERED by a high-frequency noise about a texel wide. Point
                // sampling is not optional here - a filtered tap between two owner codes decodes to
                // a competitor who is not there - but its cost is that every boundary in the arena
                // is a staircase of 9 cm squares, and at mower height that reads as pixels rather
                // than as grass. Jittering where each pixel asks turns the staircase into a ragged
                // stippled edge, which is what a real boundary between two kinds of planting looks
                // like. One noise call, no filtering, and it is the same trick the lawn's mow
                // stripes use to hide the same grid - see GrassGround.
                float2 uv = TurfWorldToUV(wxz);
                float2 texel = 1.0 / (_TurfSize / _TurfTexel);

                float2 jitter = (float2(TurfNoise(wxz * 21.0), TurfNoise(wxz * 21.0 + 8.3)) - 0.5)
                              * texel * 1.8;
                uv += jitter;

                float4 here = TurfSampleRaw(uv);
                int ownHere = TurfDecodeOwner(here.r);

                // The three neighbours needed for an antialiased edge, sampled half a texel out so
                // the four taps straddle the seam rather than all landing in the same cell.
                float4 nx = TurfSampleRaw(uv + float2(texel.x, 0));
                float4 nz = TurfSampleRaw(uv + float2(0, texel.y));
                float4 nd = TurfSampleRaw(uv + texel);
                int ownX = TurfDecodeOwner(nx.r);
                int ownZ = TurfDecodeOwner(nz.r);
                int ownD = TurfDecodeOwner(nd.r);

                // How contested this texel's neighbourhood is. Drives the animated border below and
                // nothing else — the ownership the score reads is `ownHere` and only `ownHere`.
                float disagree = (ownX != ownHere ? 1.0 : 0.0)
                               + (ownZ != ownHere ? 1.0 : 0.0)
                               + (ownD != ownHere ? 1.0 : 0.0);

                // ---- neutral lawn -------------------------------------------------------------
                // Five octaves, and the top two are the ones that matter most.
                //
                // The first pass stopped at half-metre features, which is fine from the reveal and
                // reads as nothing at all from a mower — at two metres from the camera the whole
                // arena floor was one flat colour with a slow gradient across it. A surface needs
                // grain at roughly the size of the thing it is made of, and this one is made of
                // grass, so the last two octaves run at blade and clump scale.
                float m1 = TurfNoise(wxz * 0.11);
                float m2 = TurfNoise(wxz * 0.53 + 31.7);
                float m3 = TurfNoise(wxz * 0.031 + 7.3);
                float mottle = saturate(m1 * 0.44 + m2 * 0.22 + m3 * 0.34);

                float grain = (TurfNoise(wxz * 3.1 + 5.1) - 0.5) * 0.13
                            + (TurfNoise(wxz * 11.0 + 19.4) - 0.5) * 0.075;

                half3 albedo = lerp(_NeutralBase.rgb, _NeutralTip.rgb, mottle);

                // A groundskeeper's rings, so the arena is already a kept lawn before anybody has
                // touched it, and so the radial read of the wheel — hub, hedge, loop — is there in
                // the very first frame. Broad bands, because this is also the only thing telling a
                // driver on the loop how far out they are.
                //
                // Not subtle. The first pass ran these at four percent and they were simply not on
                // screen, which left the neutral arena as a flat green disc and made the first
                // claim look like the ground had been switched on rather than mown.
                float r0 = length(wxz);
                float ring = sin(r0 * 0.42) * 0.155                 // the broad bands
                           + sin(r0 * 2.10) * 0.045;                // the roller inside each one
                albedo *= 1.0 + ring + grain;

                float relief = 0.0;
                float3 emissive = 0.0;

                if (ownHere >= 0)
                {
                    float3 livery = _TurfLivery[ownHere].rgb;
                    float3 accent = _TurfAccent[ownHere].rgb;

                    float grow = saturate(here.g * 2.2);          // 1 the instant it is claimed
                    float steal = saturate(here.b);

                    // 1 · the turf changes species.
                    float pat = TurfPattern(ownHere, wxz);
                    float grain = TurfNoise(wxz * 7.3) * 2.0 - 1.0;
                    half3 turf = livery * lerp(1.0 - _TurfDarken, 1.0, pat);
                    turf *= 1.0 + grain * _BladeGrain * 0.22;

                    // Grown in over the first moments rather than switched on, so a swath reads as
                    // something being planted behind the machine.
                    float settled = saturate(1.0 - here.g * 1.35);
                    albedo = lerp(albedo, turf, saturate(0.35 + settled * 0.65));

                    // 2 · flowers.
                    //
                    // Sparse, and only ever HALF replacing the turf underneath. Blooms at ninety
                    // percent over a cell grid this dense covered most of a claimed patch, and the
                    // ground stopped being a gardener's colour and became a field of pale dots —
                    // which is the one thing the whole layer is there to avoid. Flowers grow OUT of
                    // somebody's turf; they are not a second surface laid over it.
                    float2 bloom = Blooms(wxz, here.a, saturate(settled * 1.25));
                    albedo = lerp(albedo, accent, bloom.x * 0.55);
                    relief = bloom.y * _BloomLift;

                    // Contact shade at the foot of each bloom, which is most of what sells them as
                    // standing up out of the turf rather than lying on it.
                    albedo *= 1.0 - saturate(bloom.x - bloom.y) * 0.35;

                    // 3 · the claim wave, as new growth rather than as a light.
                    //
                    // There is no emissive anywhere in this shader and that is deliberate. A glowing
                    // floor is the tell of a territory mode that is painting rather than planting,
                    // and from a low camera it reads as a light rig under the turf. Fresh planting
                    // is PALER and a little yellower than settled turf, which is what new growth
                    // actually looks like, so the wave reads as the grass being young and it ages
                    // into the surrounding colour over the second after the claim.
                    float wave = saturate(here.g);
                    albedo = lerp(albedo, lerp(albedo, accent, 0.5) * 1.25, wave * wave);

                    // The path glows, because the arena is played at NIGHT.
                    //
                    // This shader carried no emissive at all for most of its life, and that was the
                    // right call while the match was in daylight: a glowing floor under a midday sun
                    // reads as a light rig switched on beneath the turf, not as planting. At night
                    // the same term reads as what it is — freshly turned ground and new growth
                    // catching what little light there is, a trail you can follow across a dark
                    // arena and see an opponent laying down from the far side of it.
                    //
                    // Brightest on the newest ground and settling to a low ember, so a mower leaves
                    // a bright wake that cools behind it rather than the whole territory glowing
                    // flat.
                    // The trail lights up BEHIND the machine, not underneath it.
                    //
                    // The obvious form is wave squared, brightest the instant ground changes hands
                    // — and the ground that has just changed hands is the ground directly under the
                    // roller. So the brightest patch in the arena was always exactly where the
                    // player's own mower was standing, tracking it perfectly, and from a chase
                    // camera that is not a trail at all: it is a spotlight bolted to the character.
                    //
                    // wave * (1 - wave) is zero at the moment of the claim, peaks a beat later, and
                    // fades. The ground under the machine stays dark, the planting behind it comes
                    // up, and the arena reads as something taking hold in the wake rather than as a
                    // lamp being carried around. Scaled by four so the peak still reaches one.
                    float bloomIn = wave * (1.0 - wave) * 4.0;
                    emissive += accent * (bloomIn * _PathGlow + _ClaimEmber);

                    // 4 · the steal: turned earth in the seam, not a flash. Taking ground off
                    // somebody leaves soil showing through for a moment before the new planting
                    // closes over it.
                    float burn = steal * steal;
                    albedo = lerp(albedo, _SoilColor.rgb * 1.6, saturate(burn) * 0.5);
                    // Ground taken off somebody burns hotter than ground merely claimed.
                    emissive += lerp(accent, half3(1.0, 0.86, 0.55), 0.55) * burn * _StealGlow;
                }

                // ---- the border ---------------------------------------------------------------
                //
                // Antialiased by mixing the neighbours' colours in, and animated by a band running
                // along the seam so a contested front line reads as alive even when nobody is
                // driving on it.
                if (disagree > 0.0)
                {
                    float3 neigh = 0.0;
                    float weight = 0.0;
                    int ids[3] = { ownX, ownZ, ownD };
                    [unroll]
                    for (int k = 0; k < 3; k++)
                    {
                        if (ids[k] == ownHere) continue;
                        neigh += ids[k] >= 0 ? _TurfLivery[ids[k]].rgb * 0.75 : _NeutralBase.rgb;
                        weight += 1.0;
                    }
                    neigh /= max(weight, 1.0);

                    float edge = saturate(disagree / 3.0);
                    albedo = lerp(albedo, neigh, edge * 0.34);

                    // A line of longer, paler, unmown grass along the seam, so a front line reads
                    // from the reveal height as a LINE rather than only as a colour change.
                    // Lightened, not lit, for the same reason as everything else here.
                    albedo = lerp(albedo, albedo * 1.4 + 0.03, edge * _BorderEdge * 0.32);
                }

                // ---- the crown ------------------------------------------------------------------
                //
                // Whoever holds the hub gets a wider roller, and a rule that silently makes somebody
                // half again as fast has to be visible ON THE GROUND it applies to — not only in a
                // line of HUD text the player is not reading while driving. The kerb line lights in
                // the holder's colour and breathes, and it flares as the crown changes hands.
                int crownHolder = (int)_TurfPulse.y;
                if (crownHolder >= 0)
                {
                    float kerb = 1.0 - saturate(abs(r0 - _TurfGeometry.y) / 1.1);
                    albedo = lerp(albedo, _TurfAccent[crownHolder].rgb, kerb * 0.5);
                }

                // ---- what is not playable ------------------------------------------------------
                //
                // Two different things, and the first version treated them as one. Ground under a
                // hedge is bare earth; ground beyond the touchline is still lawn — it is simply not
                // in play. Painting both as soil put a black moat between the arena and its own
                // barrier, which from a chase camera read as the level running out.
                //
                // Both are decided by the SAME test the score uses, so there is no strip anywhere
                // that looks paintable and is not.
                albedo = lerp(albedo, _SoilColor.rgb * (0.7 + mottle * 0.5), saturate(hedgeDepth * 2.2));

                // Beyond the touchline the ground is still lawn, only slightly cooler so the line
                // itself reads. It used to drop to two thirds, which against a night sky looked
                // like the arena ended and something dead began.
                float outside = 1.0 - saturate((_TurfGeometry.x - r0) / 1.4);
                albedo = lerp(albedo, albedo * 0.86, outside);

                // ---- lighting ------------------------------------------------------------------
                float3 N = normalize(IN.normalWS);
                // Tilt the normal on the blooms so they catch the sun on one side. Cheap bump, and
                // it is the only thing making the flowers read as round.
                float2 slope = float2(ddx(relief), ddy(relief)) * 26.0;
                N = normalize(N + float3(slope.x, 0, slope.y));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, 0.72);

                half3 lighting = mainLight.color * wrapped * shadow;

                // The gateways, lit. See TurfCommon.TurfGateGlow for why this is computed rather
                // than placed. Added as light and not as emission, so the grass it falls on is
                // shaded by it — a pool that only brightened albedo would read as a painted circle.
                lighting += _GateColor.rgb * TurfGateGlow(wxz, _GateRange) * _GateIntensity;

                // ---- the lanterns -------------------------------------------------------------
                //
                // Bloom Rush is the only night level in the game, and until this loop existed the
                // eight lit gateways through the wall were pure geometry: bright emissive lamps that
                // cast not one photon onto the turf underneath them. From the loop that reads as
                // stickers on a dark field, and it wastes the single strongest navigational cue the
                // arena has — the way in is the lit place.
                //
                // Wrapped exactly like the key light rather than plain lambert, so a pool of lamp
                // light falls off the same way the moonlight does and the two do not look like two
                // different renderers sharing a floor.
                #ifdef _ADDITIONAL_LIGHTS
                {
                    uint count = GetAdditionalLightsCount();
                    for (uint li = 0u; li < count; ++li)
                    {
                        Light add = GetAdditionalLight(li, IN.positionWS);
                        half addNdotl = saturate(dot(N, add.direction));
                        half addWrap = saturate((addNdotl + _Wrap) / (1.0 + _Wrap));
                        lighting += add.color * addWrap
                                  * add.distanceAttenuation * add.shadowAttenuation;
                    }
                }
                #endif

                half3 ambient = SampleSH(N) * _AmbientGain
                              + half3(0.62, 0.76, 1.0) * _AmbientFloor;

                emissive *= playable;
                half3 color = albedo * (lighting + ambient) + emissive;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct SAttrib { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 ShadowVert(SAttrib IN) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
