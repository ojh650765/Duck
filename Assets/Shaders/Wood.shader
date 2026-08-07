// Sawn timber: the judges' bench, the crowd stands, the trestles, the scoreboard.
//
// Everything wooden in the game was Duck/Prop at a single unmodulated #A9773F. That is fine on a
// mower panel, which is painted metal, and wrong on wood — and it is worst exactly where it is
// looked at hardest: BenchTop and BenchFront are a 6.0 x 0.78 m slab and a 6.0 x 0.66 m panel that
// fill the bottom third of every judging close-up, so the beat the game pushes in on was sitting on
// two flat brown rectangles.
//
// This is Duck/Prop's lighting verbatim — same GetMainLight(shadowCoord), same wrap, same tinted
// shadow, same ambient gain and floor — with a procedural albedo in front of it. That is deliberate:
// wood stands next to the crowd, the judges and the tent, and a surface that lights even slightly
// differently from its neighbours reads worse than a flat one that matches. Only the albedo changes.
//
// TWO THINGS IT DOES NOT DO, both of which were tried first in principle and rejected:
//
//   * It does not reuse Duck/GrassPlain's noise. That noise is sampled in world-space XZ, which is
//     correct for ground and useless on a vertical face — the bench front would get whatever value
//     its XZ footprint has, constant up the panel, i.e. vertical streaks. Same class of bug as the
//     clamped texture sampled in world space that ImportAsTilingDetail exists to prevent.
//
//   * It does not project in world space at all. The pattern is built in OBJECT space scaled to
//     world metres, for two reasons. The windmill sails are wood and they rotate, and a world-space
//     pattern on a rotating mesh swims. And plank seams laid out from the object's own pivot land
//     symmetrically on the piece they are cut from, instead of at whatever phase the piece happens
//     to sit at in the venue.
Shader "Duck/Wood"
{
    Properties
    {
        [Header(Colour)]
        _BaseColor    ("Board", Color) = (1,1,1,1)
        // Grain, knots and seams all lerp toward this one colour so wood stays one idea rather than
        // three tinted effects. Default is the linear form of #7A4F27.
        _GrainColor   ("Grain and seams", Color) = (0.193, 0.078, 0.020, 1)
        _VertexColorAmount ("Vertex colour amount", Range(0,1)) = 1
        _OcclusionBoost ("Vertex AO contrast", Range(0, 2)) = 1

        [Header(Grain)]
        // Which way the grain runs, in the MESH's own axes. Object space and not world, so a rotated
        // or rotating piece keeps its grain running along itself. For the generated boxes this is
        // just "the axis the box was scaled long on": (1,0,0) for the bench, the stand planks and
        // the trestle tops, (0,1,0) for legs and posts.
        _GrainDir     ("Grain direction (object)", Vector) = (1,0,0,0)
        _GrainAmount  ("Grain strength", Range(0,1)) = 0.35
        // Lines per metre ACROSS the board. 22 is a ~4.5 cm pitch: coarse enough to survive a
        // gameplay-distance pixel, fine enough to read as timber in the judging close-up.
        _GrainScale   ("Grain lines per metre", Range(4, 60)) = 22
        // How much the along-the-plank frequency is held down relative to across. This is the whole
        // reason the pattern reads as wood and not as mottle: 0.06 makes every feature roughly
        // sixteen times longer than it is wide.
        _GrainStretch ("Grain elongation", Range(0.01, 0.5)) = 0.06
        // Grain that follows a straight line reads as corduroy. Two sines at incommensurate
        // wavelengths bend it; a fourth noise octave would do the same job for twenty times the
        // per-pixel cost on a surface this large on screen.
        _WarpFreq     ("Grain wander frequency", Range(0.5, 12)) = 4.0
        _WarpAmount   ("Grain wander (m)", Range(0, 0.08)) = 0.02
        // Long tonal drift down each board, taken off the same wander term for free. Real boards are
        // not one value end to end and this is the cheapest honest way to say so.
        _ToneVary     ("Lengthwise tone", Range(0, 0.25)) = 0.07

        [Header(Planks)]
        // Board width in metres. 0.26 puts three boards across the 0.78 m bench top and three across
        // the 0.66 m front.
        _PlankWidth   ("Plank width (m)", Range(0.05, 1.5)) = 0.26
        // In plank widths, from the object's centre. 0.5 lands the seams so that whole boards fit
        // between the piece's two edges instead of a seam running down its middle.
        _PlankOffset  ("Plank offset", Range(-1, 1)) = 0.5
        _SeamWidth    ("Seam width (m)", Range(0, 0.03)) = 0.008
        _SeamDepth    ("Seam darkness", Range(0, 1)) = 0.55
        // Each board is a different piece of timber, so each gets its own shade. This is the term
        // that stops a run of planks reading as one extruded surface, and it costs one hash.
        _PlankVary    ("Board-to-board shade", Range(0, 0.25)) = 0.06

        [Header(Knots)]
        _KnotScale    ("Knot features per metre", Range(0.3, 8)) = 1.8
        // Thresholded high on purpose: knots are meant to be a few marks you notice, not a texture.
        _KnotThreshold("Knot rarity", Range(0.5, 0.95)) = 0.80
        _KnotAmount   ("Knot darkness", Range(0, 1)) = 0.30

        [Header(Distance)]
        // The fine octave is a ~2 cm feature. Past about twenty metres that is sub-pixel and it
        // stops being detail and starts being shimmer — the crowd stands are 25-40 m from the reveal
        // camera and are the largest wooden mass in the venue. Fade it out and let the seams and the
        // board shades, which are metre-scale, carry the surface at range.
        _DetailFadeStart ("Fine grain fade start (m)", Range(2, 80)) = 18
        _DetailFadeRange ("Fine grain fade range (m)", Range(1, 80)) = 26

        [Header(Lighting   matched to Duck slash Prop)]
        _Smoothness   ("Smoothness", Range(0,1)) = 0.30
        _Metallic     ("Metallic", Range(0,1)) = 0
        _Wrap         ("Light wrap", Range(0,0.6)) = 0.38
        _ShadowTint   ("Shadow tint", Color) = (0.55, 0.66, 0.86, 1)
        _RimColor     ("Rim", Color) = (1, 0.95, 0.85, 1)
        _RimPower     ("Rim power", Range(0.5, 12)) = 4
        _RimStrength  ("Rim strength", Range(0, 1)) = 0.18
        // Same defaults and the same reasoning as Duck/Prop: small, so that nothing reaches pure
        // black without paying for it with the form of every object in the scene. If those are
        // retuned there, they have to be retuned here or wood will drift away from the props.
        _AmbientGain  ("Ambient gain", Range(0.5, 3)) = 1.05
        _AmbientFloor ("Bounce floor", Range(0, 0.5)) = 0.05

        // Blender writes vertex colours in sRGB and Unity does not convert them on import. Kept so
        // an authored wooden asset can take this material without its baked AO inverting.
        [Toggle(_VCOL_SRGB)] _VertexColorSRGB ("Vertex colours are sRGB", Float) = 0

        // Declared so the non-instanced path has a real uniform to read. Without this the instanced
        // property resolves to garbage and every renderer using the material silently draws nothing.
        _InstanceColor ("Instance colour", Color) = (1,1,1,1)

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // EVERY property lives in here. Anything a material writes that sits outside this block
        // becomes a global uniform under the SRP Batcher, so the last material uploaded wins and the
        // per-material value is silently dropped — which is exactly what happened to _FieldSize and
        // _FieldOrigin in GrassCommon.hlsl and is why RivalLawn.cs has to push them through a
        // MaterialPropertyBlock. Two wood materials differing only in _GrainDir would have failed
        // the same way: the posts would have taken the bench's grain direction, or the reverse,
        // depending on draw order.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _GrainColor;
            float4 _ShadowTint;
            float4 _RimColor;
            float4 _GrainDir;
            float _VertexColorAmount, _OcclusionBoost;
            float _GrainAmount, _GrainScale, _GrainStretch;
            float _WarpFreq, _WarpAmount, _ToneVary;
            float _PlankWidth, _PlankOffset, _SeamWidth, _SeamDepth, _PlankVary;
            float _KnotScale, _KnotThreshold, _KnotAmount;
            float _DetailFadeStart, _DetailFadeRange;
            float _Smoothness, _Metallic, _Wrap;
            float _RimPower, _RimStrength;
            float _AmbientGain, _AmbientFloor;
        CBUFFER_END

        #ifdef UNITY_INSTANCING_ENABLED
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            #define DUCK_INSTANCE_COLOR UNITY_ACCESS_INSTANCED_PROP(Props, _InstanceColor)
        #else
            float4 _InstanceColor;
            #define DUCK_INSTANCE_COLOR _InstanceColor
        #endif

        // The lengths of the object-to-world basis vectors, i.e. the object's world scale with its
        // rotation divided out.
        //
        // This is load-bearing. Every wooden piece in the game is Unity's unit cube with a non-uniform
        // localScale — the bench top is (6.0, 0.10, 0.78) — so raw object space is a 1x1x1 box on all
        // of them and a pattern built in it would be sixty times coarser along the bench than across
        // it, and identical on a 6 m plank and a 13 cm leg. Multiplying by this puts the pattern back
        // into metres while keeping it attached to the mesh's own axes.
        //
        // Must be read after UNITY_SETUP_INSTANCE_ID or the instanced matrix is not resolved yet.
        float3 DuckObjectScale()
        {
            // Copied to a local first, and read element by element. unity_ObjectToWorld is a macro
            // under GPU instancing, so swizzling it in place is at the mercy of how that macro
            // expands; this is the form Unity's own ShaderGraph nodes use for the same job.
            float4x4 m = GetObjectToWorldMatrix();
            return float3(length(float3(m._m00, m._m10, m._m20)),
                          length(float3(m._m01, m._m11, m._m21)),
                          length(float3(m._m02, m._m12, m._m22)));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VCOL_SRGB
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Deliberately not GrassCommon.hlsl. That header would drag the cut-mask texture, three
            // samplers and six non-CBUFFER globals into a shader that has no lawn in it, for the sake
            // of twelve lines of noise.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
                // Object space in metres, and the normal in that same space. Grain, planks and knots
                // are all built from these two so the pattern travels with the mesh.
                float3 positionGS : TEXCOORD4;
                float3 normalGS   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);

                float3 objScale = max(DuckObjectScale(), 1e-4);
                OUT.positionGS = IN.positionOS.xyz * objScale;
                // Inverse-transpose of an axis-only scale is the reciprocal, so dividing is what keeps
                // the normal perpendicular to the face after the axes have been stretched. Left
                // un-normalised; the fragment normalises once.
                OUT.normalGS = IN.normalOS / objScale;
                return OUT;
            }

            half4 Frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 instTint = DUCK_INSTANCE_COLOR;

                // Meshes with no colour stream arrive as either white or black depending on the
                // platform; treat fully black as "no vertex colours" so generated boxes and Blender
                // geometry can share one material.
                half4 vc = IN.color;
                if (dot(vc.rgb, half3(1, 1, 1)) < 1e-4) vc = half4(1, 1, 1, 1);

                #ifdef _VCOL_SRGB
                    vc.rgb = SRGBToLinear(vc.rgb);
                #endif

                half3 vcol = lerp(half3(1, 1, 1), vc.rgb, _VertexColorAmount);
                vcol = pow(max(vcol, 1e-4), _OcclusionBoost);

                half3 albedo = _BaseColor.rgb * vcol * instTint.rgb;

                // ---------------------------------------------------------------- surface frame
                //
                // A per-face 2D frame in which x runs ALONG the grain and y runs across it. Built
                // from the face normal rather than from UVs, because none of these meshes have UVs
                // worth using: Unity's cube gives every face 0..1 regardless of how far that face has
                // been stretched, so a UV-based pattern would be six times denser across the bench
                // top than along it.
                float3 Ng = normalize(IN.normalGS);
                float3 g = _GrainDir.xyz;
                // A material carried over from Duck/Prop has no _GrainDir and reads zero here, and
                // normalizing that returns NaN — which does not render as "no grain", it renders as a
                // white surface with a white shadow. Cheaper to check than to debug.
                g = dot(g, g) > 1e-6 ? normalize(g) : float3(1, 0, 0);

                // Project the grain direction into the face, then complete the frame.
                float3 t = g - Ng * dot(Ng, g);
                float tl = length(t);
                // The sawn ends of a plank have the grain running straight into them, so there is no
                // in-plane direction at all and t is zero — normalising it NaNs the whole cap. Fall
                // back to an arbitrary stable tangent and, below, relax the elongation to isotropic,
                // which is what end grain actually looks like.
                t = tl > 1e-4 ? t / tl : normalize(cross(Ng, float3(0.36, 0.60, 0.71)));
                float3 b = cross(Ng, t);

                float along = dot(IN.positionGS, t);
                float across = dot(IN.positionGS, b);

                // ---------------------------------------------------------------- planks
                float plankCoord = across / _PlankWidth + _PlankOffset;
                float plankId = floor(plankCoord);
                float withinPlank = plankCoord - plankId;

                // Wrapped before hashing. The crowd stands and the fence are combined meshes, so
                // their vertices are baked at venue coordinates and `across` there can reach 150 m,
                // giving plank ids in the high hundreds. Hash21 multiplies its input by 123.34 and
                // fp32 has no fraction left by five digits, so the per-board variation collapses into
                // flat bands. 64 boards is 17 m of bench, far more than fits in any shot.
                float boardKey = fmod(abs(plankId), 64.0);
                float phase = Hash21(float2(boardKey, 11.3)) * 43.0;
                float boardShade = lerp(1.0 - _PlankVary, 1.0 + _PlankVary,
                                        Hash21(float2(boardKey * 1.37, 5.1)));

                // Grain is measured from the board's own centre line, never from the object's origin.
                // Two reasons, and both matter: grain does not cross a joint in real timber, and this
                // keeps the high-frequency coordinate inside +-0.13 m no matter where the piece sits
                // in the venue, which is the only reason the hash above stays well-conditioned on the
                // combined meshes.
                float acrossLocal = (withinPlank - 0.5) * _PlankWidth;

                float warp = sin(along * _WarpFreq + phase) * 0.62
                           + sin(along * _WarpFreq * 2.31 + phase * 1.7) * 0.38;

                // tl is 1 on a face that contains the grain and 0 on an end cap, so it doubles as the
                // "how elongated should this face be" term for free.
                float stretch = lerp(1.0, _GrainStretch, saturate(tl));
                float2 pg = float2(along * stretch, acrossLocal + warp * _WarpAmount) * _GrainScale
                            + phase;

                float camDist = length(_WorldSpaceCameraPos - IN.positionWS);
                float detail = 1.0 - saturate((camDist - _DetailFadeStart) / max(_DetailFadeRange, 1e-3));

                // Grain lines sit where the noise crosses its own mean: |n - 0.5| is zero along a
                // curve, and because the noise is elongated that curve is a long thin streak. Two
                // octaves an irrational ratio apart, so the pattern never lines up with itself.
                float n1 = ValueNoise2D(pg);
                float n2 = ValueNoise2D(pg * 2.63 + 19.7);
                float line1 = smoothstep(0.68, 1.0, 1.0 - abs(n1 - 0.5) * 2.0);
                float line2 = smoothstep(0.74, 1.0, 1.0 - abs(n2 - 0.5) * 2.0);
                float grain = saturate(line1 * 0.80 + line2 * 0.55 * detail);

                albedo = lerp(albedo, _GrainColor.rgb, grain * _GrainAmount);

                // Knots, on their own scale and offset so they never coincide with the grain lines.
                float k = ValueNoise2D(float2(along, acrossLocal) * _KnotScale + 43.1);
                float knot = smoothstep(_KnotThreshold, _KnotThreshold + 0.10, k);
                albedo = lerp(albedo, _GrainColor.rgb * 0.75, knot * _KnotAmount);

                // Per-board shade and the lengthwise drift, multiplied over the grain rather than
                // lerped into it, so a darker board reads as darker timber and not as more grain.
                albedo *= boardShade * (1.0 + warp * _ToneVary * 0.5);

                // Seams last, so a joint stays a joint even where it crosses a knot.
                //
                // The transition is widened to the pixel footprint of `across` before the smoothstep.
                // A fixed 8 mm line is under a pixel wide by twenty metres out, and a sub-pixel dark
                // line does not fade — it crawls, which on a bank of stand planks reads as marching
                // ants. fwidth here is the whole cost of not having that.
                float halfWidth = _PlankWidth * 0.5;
                float d = abs(withinPlank - 0.5) * _PlankWidth;
                // Capped at 45% of the board. Uncapped, a piece far enough away that one pixel spans
                // half a board width gives a negative inner edge, the smoothstep returns 1 across the
                // whole surface, and the wood turns into flat seam colour — the stands would have gone
                // dark brown at exactly the distance the reveal camera watches them from.
                float edge = min(max(_SeamWidth, fwidth(across) * 1.5), halfWidth * 0.45);
                float seam = smoothstep(halfWidth - edge, halfWidth - edge * 0.3, d);
                albedo = lerp(albedo, _GrainColor.rgb * 0.6, seam * _SeamDepth);

                // ---------------------------------------------------------------- lighting
                // Byte-for-byte Duck/Prop from here down. If that shader's response is retuned this
                // block has to move with it, or the bench will stop matching the judges sitting at it.
                float3 N = normalize(IN.normalWS) * (facing >= 0 ? 1.0 : -1.0);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half shadow = mainLight.shadowAttenuation;

                half3 lit = mainLight.color * wrapped;
                half3 shadowed = mainLight.color * wrapped * _ShadowTint.rgb * 0.55;
                half3 direct = lerp(shadowed, lit, shadow);

                half3 ambient = SampleSH(N) * _AmbientGain
                                + lerp(_ShadowTint.rgb, half3(1, 1, 1), 0.45) * _AmbientFloor;

                half3 H = normalize(mainLight.direction + V);
                half gloss = exp2(lerp(4.5, 12.0, _Smoothness));
                half fres = pow(1.0 - saturate(dot(V, H)), 5.0);
                half spec = pow(saturate(dot(N, H)), gloss)
                            * ((gloss + 8.0) * 0.0125)
                            * lerp(lerp(0.30, 1.0, _Metallic), 1.0, fres)
                            * _Smoothness * saturate(ndotl * 4.0) * shadow;

                half rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                half3 color = albedo * (direct + ambient) + spec * mainLight.color
                              + rim * _RimColor.rgb * (ambient + mainLight.color * 0.4);

                color = MixFog(color, IN.fogCoord);
                return half4(color, _BaseColor.a);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct SVaryings { float4 positionCS : SV_POSITION; };

            SVaryings ShadowVert(SAttributes IN)
            {
                SVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                return OUT;
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
            #pragma target 3.5
            #pragma multi_compile_instancing

            struct DAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DVaryings { float4 positionCS : SV_POSITION; };

            DVaryings DepthVert(DAttributes IN)
            {
                DVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
