// Bloom Rush's blade layer: the mowing round's grass, run backwards.
//
// The lawn's blade shader takes standing grass and CRUSHES it where the cut mask says a mower has
// been. This one takes short, ordinary turf and GROWS it where the ownership mask says somebody has
// claimed it — taller, in that gardener's colour, flowering at the tip, springing up over the
// second after the roller passes. Same baked chunk meshes, same per-chunk hashing, same wind, same
// thinning-with-distance so the LOD swap stays invisible. Only the mask it reads and the direction
// of the effect are different.
//
// That is the whole answer to the problem this mode had. Ownership drawn only into the FLOOR is a
// coloured texture however carefully it is shaded: it has no height, it does not move in the wind,
// and at mower height its texel grid is visible as pixels. Ownership drawn as PLANTING is grass —
// it stands up, it catches the light on one side, it bends, and the boundary between two
// gardeners' territory is a boundary between two kinds of growth rather than a line between two
// colours. It also costs nothing new: the geometry was already there for the lawn.
Shader "Duck/TurfBlades"
{
    Properties
    {
        [Header(Unclaimed turf   short and plain)]
        _WildBase ("Wild base", Color) = (0.0284, 0.1470, 0.0331, 1)
        _WildTip  ("Wild tip",  Color) = (0.1250, 0.3900, 0.0700, 1)

        [Header(Claimed planting)]
        // How much taller a claimed blade stands than an unclaimed one, and how much of its tip
        // takes the gardener's flower colour.
        //
        // Written as comments and not as [Tooltip] attributes. ShaderLab has its own small set of
        // property attributes and Tooltip is not one of them — it is a C# attribute, and a quoted
        // string full of punctuation inside a Properties block stops the parser dead. The shader
        // then does not fail to COMPILE, it fails to EXIST: Shader.Find returns null, no error is
        // logged anywhere, and the only symptom is an arena with no grass in it.
        _GrowHeight ("Claimed height", Range(1, 3)) = 1.75
        _BloomTip ("Flowering tip", Range(0, 1)) = 0.30
        _Translucency ("Translucency", Color) = (0.55, 0.85, 0.22, 1)

        [Header(Shape)]
        _RootJitter  ("Root jitter (m)", Range(0, 0.5)) = 0.16
        _HeightVar   ("Height variation", Range(0, 1)) = 0.45
        _AO          ("Root darkening", Range(0, 1)) = 0.42
        _NormalBias  ("Normal toward up", Range(0, 1)) = 0.55
        _Wrap        ("Light wrap", Range(0, 0.6)) = 0.42
        _AmbientGain ("Ambient gain", Range(0.5, 3)) = 1.05
        _AmbientFloor("Sky bounce floor", Range(0, 0.4)) = 0.035

        [Header(Gate lanterns)]
        _GateColor     ("Lantern colour", Color) = (1.0, 0.74, 0.42, 1)
        _GateRange     ("Lantern reach (m)", Float) = 11
        _GateIntensity ("Lantern strength", Range(0,3)) = 1.25

        [Header(Distance)]
        _FadeStart ("Height fade start (m)", Float) = 26
        _FadeEnd   ("Height fade end (m)",   Float) = 44
        _ThinStart ("Thin start (m)", Float) = 15
        _ThinEnd   ("Thin end (m)",   Float) = 42
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Both, deliberately. GrassCommon supplies the hashing and the WIND, and the wind has to
            // be the same wind: a Bloom Rush blade and a lawn blade gusting out of step would be two
            // different games' grass, and the arena already borrows the lawn's chunk meshes.
            #include "GrassCommon.hlsl"
            #include "TurfCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WildBase, _WildTip, _Translucency;
                float _GrowHeight, _BloomTip;
                float _RootJitter, _HeightVar, _AO, _NormalBias, _Wrap;
                float _FadeStart, _FadeEnd, _ThinStart, _ThinEnd;
                float4 _GateColor;
                float _GateRange, _GateIntensity;
                float _AmbientGain, _AmbientFloor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;  // x = height fraction 0..1, y = side -1..1
                float4 data       : TEXCOORD1;  // xy = root (chunk local), z = blade id, w = rest height
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half3  color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                half   heightFrac : TEXCOORD4;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 rootLocal = float3(IN.data.x, 0, IN.data.y);
                float  bladeId   = IN.data.z;
                float  restH     = max(IN.data.w, 1e-3);
                float  heightFrac = IN.uv.x;

                float3 offset = IN.positionOS.xyz - rootLocal;

                // ---- per-chunk seed straight out of the transform: no per-instance data needed ----
                float2 chunkWS = float2(unity_ObjectToWorld._m03, unity_ObjectToWorld._m23);
                float seed = Hash21(chunkWS * 0.1373 + 4.71);

                // ---- break repetition: jitter, rotate and resize every blade ----
                float2 j = float2(Hash21(float2(bladeId * 37.1, seed)),
                                  Hash21(float2(seed * 3.3, bladeId * 61.7))) - 0.5;
                rootLocal.xz += j * _RootJitter;

                float a = Hash21(float2(bladeId * 11.9, seed + 3.17)) * TWO_PI;
                float sa = sin(a), ca = cos(a);
                offset.xz = float2(offset.x * ca - offset.z * sa, offset.x * sa + offset.z * ca);

                float hRand = Hash21(float2(seed + 7.77, bladeId * 23.3));
                float hVar = 1.0 + (hRand - 0.5) * 2.0 * _HeightVar;
                offset.y *= hVar;
                restH *= hVar;

                // ---- read who owns the ground this blade is standing in ----
                float3 rootWS = TransformObjectToWorld(rootLocal);
                float4 mask = TurfSampleLOD(TurfWorldToUV(rootWS.xz));
                int owner = TurfDecodeOwner(mask.r);
                float fresh = saturate(mask.g);

                // ---- grow ----
                //
                // Claimed ground stands up. The growth is scaled by how recently it was claimed, so
                // a blade is at its unclaimed height the instant the roller passes and reaches full
                // height about a second later: the trail behind a mower is visibly RISING rather
                // than switching on, which is the single thing that makes this read as planting.
                float claimed = owner >= 0 ? 1.0 : 0.0;
                float grown = claimed * (1.0 - fresh * 0.85);
                offset.y *= lerp(1.0, _GrowHeight, grown);

                // A claimed blade also stands straighter and narrower - clipped, tended, deliberate
                // - where wild turf splays.
                offset.xz *= lerp(1.0, 0.86, grown);

                // ---- where grass grows, which is NOT the same as where it can be claimed ----
                //
                // Two different questions, and conflating them left the arena bald.
                //
                // Under a HEDGE, nothing grows: that is a wall, and grass coming through it is
                // wrong. But past the TOUCHLINE the ground is still lawn — it is simply out of
                // play. Culling blades on the same test that refuses paint left the three and a
                // half metre run-off between the touchline and the barrier as bare dirt, so the
                // arena visibly stopped a stride short of its own fence.
                //
                // So: blades everywhere on the ground mesh except under a hedge, fading out only
                // where the mesh itself ends. The paint still stops at the touchline — see
                // TurfStamp.ClipToArena — which is the honest place for it to stop.
                float hedgeDepth;
                TurfPlayable(rootWS.xz, hedgeDepth);
                float grass = 1.0 - saturate(hedgeDepth * 3.0);
                float edgeR = length(rootWS.xz);
                grass *= 1.0 - smoothstep(_TurfGeometry.x + 4.5, _TurfGeometry.x + 6.5, edgeR);

                // ---- wind. New growth is springier; settled turf leans with the gust. ----
                offset.xz += WindOffset(rootWS.xz, heightFrac, bladeId) * (1.0 + grown * 0.35);

                // ---- distance fade and thinning ----
                //
                // Straight from the lawn's blade layer and for the same reason: blades sink one by
                // one in order of id as the camera pulls away, so the LOD swap removes only blades
                // that already had zero height. Without it the field changes density a whole chunk
                // at a time and the grid slides visibly along with the player.
                float camDist = distance(rootWS.xz, GetCameraPositionWS().xz);

                float t = saturate((camDist - _ThinStart) / max(_ThinEnd - _ThinStart, 0.001));
                float threshold = 1.0 - t;
                float survives = 1.0 - smoothstep(threshold - 0.12, threshold + 0.12, bladeId);

                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, camDist);
                offset.y *= fade * survives * grass;

                float3 posOS = rootLocal + offset;
                float3 posWS = TransformObjectToWorld(posOS);

                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);

                float3 nOS = float3(IN.normalOS.x * ca - IN.normalOS.z * sa,
                                    IN.normalOS.y,
                                    IN.normalOS.x * sa + IN.normalOS.z * ca);
                float3 nWS = TransformObjectToWorldNormal(nOS);
                OUT.normalWS = normalize(lerp(nWS, float3(0, 1, 0), _NormalBias));

                // ---- colour, resolved per vertex: blades are small, this is plenty ----
                //
                // Unclaimed turf is the wild green. Claimed turf is the gardener's livery at the
                // root running into their flower colour at the tip, so a claimed patch reads as
                // planting in bloom from above and as coloured grass from the seat of a mower.
                half3 wild = lerp(_WildBase.rgb, _WildTip.rgb, heightFrac);

                int safeOwner = max(owner, 0);
                half3 livery = _TurfLivery[safeOwner].rgb;
                half3 accent = _TurfAccent[safeOwner].rgb;
                // Livery at the root, livery-with-a-hint-of-flower at the tip. The first pass ran
                // most of the way to the accent colour and every territory came out pastel — DUCK's
                // deep red read as salmon and BRAMBLE's purple as pale blue, which is four washes
                // of nearly the same value and the one thing a territory mode cannot afford.
                //
                // A blade is GRASS in somebody's colour with a flower on the end of it, not a
                // flower on a stick.
                half3 tip = lerp(livery, accent, 0.5);
                half3 planted = lerp(livery * 0.72, tip, heightFrac * _BloomTip + heightFrac * 0.35);

                half3 col = lerp(wild, planted, claimed);
                // Freshly turned ground shows through at the roots of new planting for a moment.
                col = lerp(col, col * 1.25, fresh * fresh * claimed);

                col *= lerp(1.0 - _AO, 1.0, heightFrac);
                col *= 0.86 + 0.28 * hRand;

                OUT.color = col;
                OUT.heightFrac = heightFrac;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                float3 N = normalize(IN.normalWS) * (facing > 0 ? 1 : -1);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, 0.7);

                // Light bleeding through the blade from behind — what stops stylised grass reading
                // as flat cardboard when the sun is low and behind it.
                half back = saturate(dot(-mainLight.direction, V));
                half3 trans = _Translucency.rgb * pow(back, 3.0) * IN.heightFrac * 0.55;

                half3 lighting = mainLight.color * wrapped * shadow;

                // The same computed gateway pools the floor uses. Grass standing in lamp light has
                // to be lit by it too, or the blades read as a dark fringe over a lit floor — which
                // is worse than neither being lit.
                lighting += _GateColor.rgb * TurfGateGlow(IN.positionWS.xz, _GateRange) * _GateIntensity;

                half3 ambient = SampleSH(N) * _AmbientGain
                                + half3(0.62, 0.76, 1.0) * _AmbientFloor;

                half3 color = IN.color * (lighting + ambient) + trans * mainLight.color * shadow;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
