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

        // Environment fill. Deliberately shader defaults and NOT set per material: every .mat in
        // the project was written before these existed, so they resolve from here and one edit
        // moves the whole game's shade response. That matters because the fix has to reach
        // M_Spectators and M_Judges too, and those are built by a different pass.
        // 1.05 and 0.05, down from 1.35 and 0.16.
        //
        // The higher pair was set to stop north-facing surfaces going black, and it worked, but it
        // overshot into the opposite failure: a judge sitting under a solid red awning was exactly
        // as bright as one standing in open sun, so the set had no shading left at all.
        //
        // The FLOOR is the term that does the damage, and it is worth understanding why rather than
        // just turning it down. It is added unconditionally — it is not multiplied by the light and
        // it is not attenuated by the shadow — so every unit of floor raises shadowed and lit
        // surfaces by the same amount, which compresses exactly the difference a shadow is made of.
        // At 0.16 against a lit value near 1.0 it was flattening roughly a sixth of the available
        // range, on top of an amplified SH term doing the same thing more gently. That is why the
        // main light's shadows appeared to be missing entirely even once they were being cast.
        //
        // Small is the point: enough bounce that nothing reaches pure black, not enough to pay for
        // it with the form of every object in the game.
        _AmbientGain  ("Ambient gain", Range(0.5, 3)) = 1.05
        _AmbientFloor ("Bounce floor", Range(0, 0.5)) = 0.05

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

                // A face turned away from the sun still bottoms out around 0.4 of the key even at
                // the high wrap the props use, and SampleSH alone could not carry the rest: the
                // scene uses a gradient ambient probe, and ambientIntensity is only applied to
                // skybox ambient, so the probe is the raw gradient colour with no multiplier.
                // The result was that the judges' bench read as unlit rather than shaded and the
                // crowd in the stands sank into the hedge behind it.
                //
                // The floor is _ShadowTint pulled most of the way to white. Using the tint keeps
                // the fill the same colour family as the shadows so shade stays one idea, and
                // desaturating it means the fill lifts value without dyeing every prop blue.
                half3 ambient = SampleSH(N) * _AmbientGain
                                + lerp(_ShadowTint.rgb, half3(1, 1, 1), 0.45) * _AmbientFloor;

                // Tight, normalised, fresnel-weighted, and gated by the key light.
                //
                // The old highlight was a wide blob: exponent 27 at the default smoothness, applied
                // at full strength wherever N.H was positive, which is most of the object. That is a
                // uniform sheen rather than a highlight — it flattens form instead of describing it,
                // and it is exactly the plastic look. Three corrections: an exponential gloss range
                // so the lobe is small, a (gloss+8)/8 normalisation so shrinking it does not just
                // make everything dimmer, and the ndotl gate so the unlit side of a prop can no
                // longer carry a specular the sun could not have put there.
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
