// The workhorse material for every solid object in the game: the mower, the duck, the judges,
// the crowd, the fence, the barn.
//
// Three things URP/Lit does not give us and this does:
//   * vertex colours as the albedo source, which is how the Blender assets carry their paint
//     and their baked ambient occlusion without a single texture;
//   * per-instance tint through GPU instancing, so ninety spectators are a couple of draws;
//   * the same soft wrapped lighting the grass uses, so characters sit in the lawn instead of
//     looking pasted onto it.
Shader "Duck/Prop"
{
    Properties
    {
        _BaseColor    ("Tint", Color) = (1,1,1,1)
        _VertexColorAmount ("Vertex colour amount", Range(0,1)) = 1
        _Smoothness   ("Smoothness", Range(0,1)) = 0.22
        _Metallic     ("Metallic", Range(0,1)) = 0
        _Wrap         ("Light wrap", Range(0,0.6)) = 0.30
        _ShadowTint   ("Shadow tint", Color) = (0.55, 0.66, 0.86, 1)
        _RimColor     ("Rim", Color) = (1, 0.95, 0.85, 1)
        _RimPower     ("Rim power", Range(0.5, 12)) = 4
        _RimStrength  ("Rim strength", Range(0, 1)) = 0.18
        _OcclusionBoost ("Vertex AO contrast", Range(0, 2)) = 1
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
        _Cutoff       ("Cutoff", Range(0,1)) = 0.5

        // Blender writes vertex colours in sRGB and Unity does not convert them on import,
        // so authored assets need this on and generated white-vertex geometry does not care.
        [Toggle(_VCOL_SRGB)] _VertexColorSRGB ("Vertex colours are sRGB", Float) = 0

        // Declared so the non-instanced path has a real uniform to read. Without this the
        // instanced property resolves to garbage and every renderer using the material
        // silently draws nothing.
        _InstanceColor ("Instance colour", Color) = (1,1,1,1)

        // Authored meshes can arrive with individual faces wound the wrong way, which back-face
        // culling turns into holes. Rendering the characters double-sided and flipping the
        // shading normal per face makes the model read correctly either way.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadowTint;
            float4 _RimColor;
            float _VertexColorAmount, _Smoothness, _Metallic, _Wrap;
            float _RimPower, _RimStrength, _OcclusionBoost, _Cutoff;
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
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _VCOL_SRGB
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
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
                return OUT;
            }

            half4 Frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 instTint = DUCK_INSTANCE_COLOR;

                // Meshes with no colour stream can arrive as either white or black depending on
                // the platform; treat fully black as "no vertex colours" so generated geometry
                // and Blender geometry can share one material.
                half4 vc = IN.color;
                if (dot(vc.rgb, half3(1, 1, 1)) < 1e-4) vc = half4(1, 1, 1, 1);

                #ifdef _VCOL_SRGB
                    vc.rgb = SRGBToLinear(vc.rgb);
                #endif

                // Blender bakes ambient occlusion into the vertex colour, so pushing its
                // contrast here is effectively an AO strength control.
                half3 vcol = lerp(half3(1, 1, 1), vc.rgb, _VertexColorAmount);
                vcol = pow(max(vcol, 1e-4), _OcclusionBoost);

                half3 albedo = _BaseColor.rgb * vcol * instTint.rgb;
                half alpha = _BaseColor.a * IN.color.a * instTint.a;

                #ifdef _ALPHATEST_ON
                    clip(alpha - _Cutoff);
                #endif

                // Flip the shading normal on back faces so a mis-wound triangle still lights
                // like the surface it belongs to instead of going black.
                float3 N = normalize(IN.normalWS) * (facing >= 0 ? 1.0 : -1.0);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half shadow = mainLight.shadowAttenuation;

                // Shadows are tinted toward sky blue rather than driven to black — the single
                // biggest thing keeping this out of "default Unity" territory.
                half3 lit = mainLight.color * wrapped;
                half3 shadowed = mainLight.color * wrapped * _ShadowTint.rgb * 0.55;
                half3 direct = lerp(shadowed, lit, shadow);

                half3 ambient = SampleSH(N);

                half3 H = normalize(mainLight.direction + V);
                half spec = pow(saturate(dot(N, H)), lerp(8.0, 96.0, _Smoothness))
                            * _Smoothness * lerp(0.25, 1.6, _Metallic) * shadow;

                half rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimStrength;

                half3 color = albedo * (direct + ambient) + spec * mainLight.color
                              + rim * _RimColor.rgb * (ambient + mainLight.color * 0.4);

                color = MixFog(color, IN.fogCoord);
                return half4(color, alpha);
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
