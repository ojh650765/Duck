// Grass that is only ever looked at, never mown.
//
// The meadow around the championship ground, and the aprons inside it, want to read as the same
// living turf as the lawns — but they have no cut mask, no blades, no wheel tracks and no mow
// stripes, and paying for that machinery on the single largest mesh in the scene would be silly.
// Until now they used the flat prop material instead, which is why every overhead shot of the
// venue sat on a sheet of dead green: from the reveal and the tour cameras the meadow is most of
// the frame, and a solid colour there flattens the whole picture.
//
// So this is the lawn's look with the lawn's function removed: the same three-octave mottling and
// the same palette, one texture fetch lighter than the ground shader and with no dependency on the
// cut mask at all. It is deliberately not a variant of Duck/GrassGround — a branch on the hot path
// of the biggest mesh in the scene costs more than a second shader does.
Shader "Duck/GrassPlain"
{
    Properties
    {
        [Header(Palette   linear values from the art bible)]
        _UncutBase   ("Base",  Color) = (0.0284, 0.1470, 0.0331, 1)
        _UncutTip    ("Tip",   Color) = (0.0762, 0.2963, 0.0513, 1)

        _PatchDark   ("Patch shade",   Color) = (0.0180, 0.0980, 0.0230, 1)
        _DryTint     ("Dry grass",     Color) = (0.1720, 0.2280, 0.0620, 1)

        [Header(Look)]
        _MottleScale ("Mottle scale",  Float) = 0.06
        _MottleAmount("Mottle amount", Range(0,1)) = 0.7
        _PatchScale  ("Patch scale",   Float) = 0.011
        _PatchAmount ("Patch amount",  Range(0,1)) = 0.55
        _DryAmount   ("Dry patches",   Range(0,1)) = 0.35
        _GrainScale  ("Grain scale",   Float) = 0.75
        _GrainAmount ("Grain amount",  Range(0,0.5)) = 0.14
        _Wrap        ("Light wrap",    Range(0,0.9)) = 0.35
        [Tooltip(Faint mown banding so the meadow is not perfectly uniform)]
        _OldStripe   ("Old mowing",    Range(0,0.2)) = 0.05
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
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GrassCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _UncutBase;
                float4 _UncutTip;
                float4 _PatchDark;
                float4 _DryTint;
                float _MottleScale;
                float _MottleAmount;
                float _PatchScale;
                float _PatchAmount;
                float _DryAmount;
                float _GrainScale;
                float _GrainAmount;
                float _Wrap;
                float _OldStripe;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

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
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;

                // Four scales, because smooth noise on its own is just a colour wobble — it made
                // the meadow read as a tinted plane rather than as ground. What sells it is
                // structure at very different sizes:
                //
                //   patches  ~90 m   fields of richer and poorer grass, with a soft but real edge
                //   dry      ~60 m   straw-coloured ground where it has been baked
                //   mottle   ~18 m   clumping within a field
                //   grain     ~1 m   the speckle you only notice up close, which is what stops the
                //                    surface looking like paper at the player's own eye level
                float m1 = ValueNoise2D(wxz * _MottleScale);
                float m2 = ValueNoise2D(wxz * (_MottleScale * 4.7) + 31.7);
                float m3 = ValueNoise2D(wxz * (_MottleScale * 0.28) + 7.3);
                float mottle = saturate(m1 * 0.46 + m2 * 0.20 + m3 * 0.34);
                mottle = lerp(0.5, mottle, _MottleAmount);

                half3 albedo = lerp(_UncutBase.rgb, _UncutTip.rgb, mottle);

                // Broad patches. Pushed through smoothstep so they have an edge you can see rather
                // than fading into one another — a field boundary, not a gradient.
                float patch = ValueNoise2D(wxz * _PatchScale + 13.1);
                patch = smoothstep(0.38, 0.72, patch);
                albedo = lerp(albedo, _PatchDark.rgb, (1.0 - patch) * _PatchAmount);

                // Dry ground, on its own scale and offset so it never lines up with the patches.
                float dry = ValueNoise2D(wxz * (_PatchScale * 1.6) + 57.4);
                dry = smoothstep(0.62, 0.86, dry);
                albedo = lerp(albedo, _DryTint.rgb, dry * _DryAmount);

                // Fine grain. Two octaves an irrational ratio apart so it never tiles visibly.
                float g = ValueNoise2D(wxz * _GrainScale) * 0.6
                        + ValueNoise2D(wxz * (_GrainScale * 2.37) + 91.3) * 0.4;
                albedo *= 1.0 + (g - 0.5) * 2.0 * _GrainAmount;

                // A memory of old mowing, at a much broader pitch than the playfield's, so the
                // meadow reads as managed land without competing with the picture being cut.
                float old = sin(wxz.x * 0.031 + wxz.y * 0.017) * 0.5 + 0.5;
                albedo *= 1.0 + (old - 0.5) * _OldStripe;

                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half3 direct = mainLight.color * wrapped * lerp(0.55, 1.0, mainLight.shadowAttenuation);
                half3 ambient = SampleSH(N);

                half3 color = albedo * (direct + ambient);
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
