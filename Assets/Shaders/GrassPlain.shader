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

        [Header(Look)]
        _MottleScale ("Mottle scale",  Float) = 0.06
        _MottleAmount("Mottle amount", Range(0,1)) = 0.7
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
                float _MottleScale;
                float _MottleAmount;
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

                // Same three scales as the lawn: broad patches of richer and poorer grass, medium
                // clumping, and fine grain. One octave alone reads as noise; three read as a field.
                float m1 = ValueNoise2D(wxz * _MottleScale);
                float m2 = ValueNoise2D(wxz * (_MottleScale * 4.7) + 31.7);
                float m3 = ValueNoise2D(wxz * (_MottleScale * 0.28) + 7.3);
                float mottle = saturate(m1 * 0.46 + m2 * 0.20 + m3 * 0.34);
                mottle = lerp(0.5, mottle, _MottleAmount);

                half3 albedo = lerp(_UncutBase.rgb, _UncutTip.rgb, mottle);

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
