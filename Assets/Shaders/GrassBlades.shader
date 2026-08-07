// The blade layer: real geometry, deformed in the vertex shader by the cut mask.
//
// Every chunk in the field draws the SAME baked blade mesh. Repetition is broken in the
// vertex shader by hashing the chunk's world position out of the object-to-world matrix and
// using it to jitter each blade's root, rotation and height. That keeps the whole field to
// two meshes in memory instead of one per chunk, and it means no per-chunk material data,
// so the SRP batcher still works.
//
// Cutting does not scale a blade down. It crushes it: the stub loses height, splays wider,
// and lies over in the direction the mower was actually travelling. That is the difference
// between "grass was cut here" and "a texture was erased here".
//
// COLOUR IS NOT FLAT. That took a report from the goose arena to fix and it is worth stating up
// front, because the obvious way to write this shader is the wrong one. A blade layer built from
// two colours and a per-blade random multiply has exactly one spatial frequency in it — the blade,
// about ten centimetres — and ten centimetres is below the size the eye will accept as a shape. A
// field of it does not read as a lawn, it reads as static laid over a flat green plane. The cure is
// LOW frequency: broad soft patches sized in tens of metres, so there is something large to see
// before there is anything small to see. Details in the colour block near the bottom of Vert.
Shader "Duck/GrassBlades"
{
    Properties
    {
        _UncutBase ("Uncut base", Color) = (0.0284, 0.1470, 0.0331, 1)
        _UncutTip  ("Uncut tip",  Color) = (0.1250, 0.3900, 0.0700, 1)
        _CutBase   ("Cut base",   Color) = (0.1559, 0.3419, 0.0382, 1)
        _CutTip    ("Cut tip",    Color) = (0.4600, 0.6600, 0.1100, 1)
        _Translucency ("Translucency", Color) = (0.55, 0.85, 0.22, 1)

        [Header(Patch variation   the field is not one colour)]
        // Deliberately the same NAME and the same DEFAULT as GrassGround's dominant mottle octave,
        // and it should stay that way: a reader comparing M_GrassBlades against M_GrassGround in the
        // inspector is entitled to assume that two properties spelled the same are the same patches.
        _MottleScale  ("Mottle scale (cells per metre)", Float) = 0.075
        _MottleAmount ("Mottle amount", Range(0, 0.6)) = 0.38
        _BroadAmount  ("Broad wash amount", Range(0, 0.4)) = 0.10
        _OldMow       ("Old mowing bands (uncut only)", Range(0, 0.2)) = 0.055
        // The old value of this was 0.28 and it was the entire visual budget of the lawn spent at a
        // frequency too high to read. Halved, on purpose. See the colour block.
        _BladeJitter  ("Per blade value jitter", Range(0, 0.4)) = 0.14

        _CutHeight   ("Cut height fraction", Range(0.02, 0.6)) = 0.16
        _TrackHeight ("Track height fraction", Range(0.02, 1)) = 0.55
        _CutLayover  ("Cut layover", Range(0, 1.5)) = 0.55
        _RootJitter  ("Root jitter (m)", Range(0, 0.5)) = 0.16
        _HeightVar   ("Height variation", Range(0, 1)) = 0.45
        _AO          ("Root darkening", Range(0, 1)) = 0.42
        _NormalBias  ("Normal toward up", Range(0, 1)) = 0.55
        // Blades are yaw-randomised per instance, so at any moment roughly half of the visible
        // blade area faces away from the sun. At the old 0.32 those faces received a fifth of the
        // key, and since the field is most of the frame that is what made the whole game read
        // dark and green: the lawn's red and blue channels were being multiplied away.
        _Wrap        ("Light wrap", Range(0, 0.6)) = 0.42
        _AmbientGain ("Ambient gain", Range(0.5, 3)) = 1.05
        _AmbientFloor("Sky bounce floor", Range(0, 0.4)) = 0.035

        _FadeStart ("Height fade start (m)", Float) = 26
        _FadeEnd   ("Height fade end (m)",   Float) = 44
        [Header(Thinning   blades disappear one by one instead of a chunk at a time)]
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
            #include "GrassCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _UncutBase, _UncutTip, _CutBase, _CutTip, _Translucency;
                float _CutHeight, _TrackHeight, _CutLayover, _RootJitter, _HeightVar;
                float _AO, _NormalBias, _Wrap, _FadeStart, _FadeEnd, _ThinStart, _ThinEnd;
                float _AmbientGain, _AmbientFloor;
                float _MottleScale, _MottleAmount, _BroadAmount, _OldMow, _BladeJitter;
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

                // ---- read the lawn ----
                float3 rootWS = TransformObjectToWorld(rootLocal);
                float4 mask = SampleCutMaskLOD(WorldToMaskUV(rootWS));
                float cut = saturate(mask.r);
                float track = saturate(mask.g);

                // ---- crush, splay and lay over ----
                float hScale = lerp(1.0, _CutHeight, cut) * lerp(1.0, _TrackHeight, track);
                offset.y *= hScale;
                offset.xz *= lerp(1.0, 1.30, cut);

                float mowAng = mask.b * TWO_PI;
                float2 mowDir = float2(cos(mowAng), sin(mowAng));
                offset.xz += mowDir * (heightFrac * cut * _CutLayover * restH);

                // ---- wind, suppressed once the blade is a stub ----
                offset.xz += WindOffset(rootWS.xz, heightFrac, bladeId) * (1.0 - cut * 0.85);

                // ---- distance fade ----
                //
                // Two effects, and the second is the important one. Every blade shortens with
                // distance, and on top of that the field THINS: as the camera pulls away, blades
                // sink one by one in order of their id, so the density falls off continuously.
                //
                // That is what makes the LOD swap invisible. The far mesh keeps only ids below the
                // cutoff, and by the distance the swap happens every blade above it has already
                // reached zero height — so the swap removes nothing that was still being drawn.
                // Without it the lawn changed density a whole chunk at a time and the eight-metre
                // grid was plainly visible sliding along with the player.
                float camDist = distance(rootWS.xz, GetCameraPositionWS().xz);

                float t = saturate((camDist - _ThinStart) / max(_ThinEnd - _ThinStart, 0.001));
                float threshold = 1.0 - t;
                float survives = 1.0 - smoothstep(threshold - 0.12, threshold + 0.12, bladeId);

                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, camDist);
                offset.y *= fade * survives;

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
                // WHY THERE IS A NOISE CALL IN THE VERTEX SHADER THAT DRAWS A MILLION BLADES.
                //
                // The report from the goose arena was "the grass colour is uniform, so it looks like
                // noise", and those two halves are one fault, not two. Every blade in the field was
                // drawn from the same two colours — base at the root, tip at the top — with nothing
                // on top of it but a per-blade random multiply. A per-blade random varies at ten
                // centimetres. Ten centimetres is below the size the eye is willing to resolve into
                // a shape at chase-camera range, so it does not read as "blades differ from one
                // another": with no larger structure behind it to belong to, it reads as speckle
                // over a flat plane. The lawn had no low frequency left in it at all, and
                // high-frequency detail with nothing underneath it is the definition of static.
                //
                // The GROUND layer has had the cure since the day it was written — three octaves of
                // mottling at roughly forty-eight, thirteen and three metres. The blades then stood
                // on top of it and hid it, which is why the fault was worst exactly where the grass
                // was thickest. So this is not an invented look: it is the blade layer carrying the
                // SAME patches as the ground it is standing in.
                //
                //     ValueNoise2D(wxz * _MottleScale)        <- GrassGround.shader, octave m1
                //     ValueNoise2D(rootWS.xz * _MottleScale)  <- here
                //
                // Same function, same argument, same default scale, biased in the same direction, so
                // a patch of poorer ground now has poorer blades growing out of it instead of the two
                // layers each drawing their own unrelated lawn.
                //
                // Sampled at the blade ROOT, not at the vertex, so all five vertices of a blade get
                // one patch value and a blade is a single flat sample of the field. A gradient
                // running up an individual blade would be a second kind of centimetre-scale detail,
                // which is the thing being removed.
                float patch = ValueNoise2D(rootWS.xz * _MottleScale);
                // Contrast-stretched, and this is not cosmetic. Hermite-interpolated value noise
                // spends most of its life near 0.5 — the interior of every cell is an average of its
                // four corners, so the raw signal has a standard deviation of about 0.2 and a patch
                // built straight off it is a wobble, not a patch. Pushed through a smoothstep it
                // actually reaches its ends and the lawn gets areas rather than a gradient. The
                // window is wider than GrassPlain's 0.38..0.72 on purpose: that one is drawing a
                // field boundary and wants a visible edge, this one is drawing where the grass grew
                // better, which has no edge at all.
                patch = smoothstep(0.30, 0.70, patch);

                // One frequency higher up, bought with two sines instead of a second noise octave.
                //
                // Value noise is about seventy operations a call and this shader already spends five
                // hashes per vertex, so a second octave is not free at this density. It does not need
                // to be noise: at forty-odd metres a plane wave is a soft wash across most of what
                // the camera can see and there is no possibility of reading it as plaid. Two of them,
                // forty-five and fifty-three metres long, crossing at roughly a right angle and at a
                // ratio that is not a neat fraction, so they never line up twice in the same place.
                // This is the term that stops a sixty-four metre lawn being one average colour with
                // clumping laid over it.
                float broad = sin(rootWS.x *  0.110 + rootWS.z * 0.085)
                            + sin(rootWS.x * -0.062 + rootWS.z * 0.101);

                // Both applied as a BIAS ON THE HEIGHT GRADIENT rather than as a tint, and that is
                // the whole reason this cannot escape the palette. The result is a clamped position
                // on the straight line between the two authored greens, so the darkest a patch can
                // possibly go is exactly _UncutBase and the lightest is exactly _UncutTip. No hue
                // exists between them that was not already being drawn on every blade; all that
                // changes is WHERE on that line a given square metre of lawn sits. Whatever the
                // scene builder authors those two colours as, the variation follows it.
                float shade = (patch - 0.5) * _MottleAmount + broad * 0.5 * _BroadAmount;
                float h = saturate(heightFrac + shade);

                half3 uncut = lerp(_UncutBase.rgb, _UncutTip.rgb, h);
                half3 cutC  = lerp(_CutBase.rgb,   _CutTip.rgb,   h);

                // A memory of last week's mowing. Deliberately the SAME expression, the same phase
                // and the same default amplitude as GrassGround.shader's `oldStripe`, because the
                // ground has been drawing these fifteen-metre bands all along and the blades have
                // been standing in front of them. Matching means the two layers band together
                // instead of the upper one washing the lower one out. Uncut only, exactly as on the
                // ground: cut grass gets its banding from the mow direction stored in the mask,
                // which is a real record of where the mower went and must not be competed with.
                float oldMow = sin(rootWS.x * 0.42) * 0.5;
                uncut *= 1.0 + oldMow * _OldMow;

                half3 col = lerp(uncut, cutC, smoothstep(0.18, 0.65, cut));
                // Root darkening reads the TRUE height fraction, not the biased one. This is contact
                // occlusion between blades — it is geometry, and geometry does not care which patch
                // of the lawn it is standing in.
                col *= lerp(1.0 - _AO, 1.0, heightFrac);

                // Per-blade value jitter, halved from the plus-or-minus fourteen percent it used to
                // be. This is the term that was reading as static, and it is reduced rather than
                // deleted: blades genuinely do differ, and this is still what stops a stand of grass
                // looking extruded from one profile. It simply no longer has to carry the entire
                // variation of the surface on its own, so it can go back to being a texture rather
                // than the subject. Note hRand also drives the blade's height, so the taller blades
                // stay the brighter ones, which is how light really falls on a stand of grass.
                col *= 1.0 + (hRand - 0.5) * _BladeJitter;

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

                // Light bleeding through the blade from behind — this is what stops stylised
                // grass reading as flat cardboard when the sun is low and behind it.
                half back = saturate(dot(-mainLight.direction, V));
                half3 trans = _Translucency.rgb * pow(back, 3.0) * IN.heightFrac * 0.55;

                half3 lighting = mainLight.color * wrapped * shadow;
                // Sky bounce, deliberately cool and deliberately small. The field under the
                // marquee and inside the tree shadows was falling to a value the eye reads as a
                // hole in the lawn; a cool floor lifts it without adding more green, which is the
                // thing there is already too much of.
                half3 ambient = SampleSH(N) * _AmbientGain
                                + half3(0.62, 0.76, 1.0) * _AmbientFloor;

                // NO SPECULAR ON THE GRASS.
                //
                // A sheen was added here and it was wrong. The wide lobe it needed — a tight one on
                // a million blades is a field of sparkles — put a pale wash across every blade
                // facing anywhere near the sun, and the field stopped being green. Round one's lawn
                // is flat green and reads better for it: at the density this shader draws, the
                // shape of the field comes from the blades themselves, and any highlight big enough
                // to see is big enough to grey the colour out.
                half3 color = IN.color * (lighting + ambient) + trans * mainLight.color * shadow;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
