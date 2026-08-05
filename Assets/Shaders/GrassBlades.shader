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
Shader "Duck/GrassBlades"
{
    Properties
    {
        _UncutBase ("Uncut base", Color) = (0.0284, 0.1470, 0.0331, 1)
        _UncutTip  ("Uncut tip",  Color) = (0.1250, 0.3900, 0.0700, 1)
        _CutBase   ("Cut base",   Color) = (0.1559, 0.3419, 0.0382, 1)
        _CutTip    ("Cut tip",    Color) = (0.4600, 0.6600, 0.1100, 1)
        _Translucency ("Translucency", Color) = (0.55, 0.85, 0.22, 1)

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
                half3 uncut = lerp(_UncutBase.rgb, _UncutTip.rgb, heightFrac);
                half3 cutC  = lerp(_CutBase.rgb,   _CutTip.rgb,   heightFrac);
                half3 col = lerp(uncut, cutC, smoothstep(0.18, 0.65, cut));
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

                half3 color = IN.color * (lighting + ambient) + trans * mainLight.color * shadow;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
