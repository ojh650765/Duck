// The lawn surface. This is the layer that has to make the cut picture readable from the
// 90 m overhead reveal, so most of its work is edge definition and mow-stripe contrast
// rather than blade detail (the blade layer handles the close read).
//
// Mow stripes are not a sine pattern: the cut mask stores the actual direction the mower was
// travelling in each texel, and a pass travelling toward the camera catches light differently
// from one travelling away. Adjacent passes naturally alternate, exactly like a real lawn, and
// the banding shifts as the camera orbits.
Shader "Duck/GrassGround"
{
    Properties
    {
        [Header(Palette   linear values from the art bible)]
        _UncutBase   ("Uncut base",   Color) = (0.0284, 0.1470, 0.0331, 1)
        _UncutTip    ("Uncut tip",    Color) = (0.0762, 0.2963, 0.0513, 1)
        _CutBase     ("Cut base",     Color) = (0.1559, 0.3419, 0.0382, 1)
        _CutTip      ("Cut tip",      Color) = (0.3917, 0.5972, 0.0909, 1)
        _EdgeColor   ("Cut edge",     Color) = (0.0176, 0.0823, 0.0232, 1)
        _TrackColor  ("Wheel track",  Color) = (0.1046, 0.2269, 0.0307, 1)

        [Header(Detail)]
        // Procedural noise alone cannot make a surface look like grass.
        //
        // Three octaves of value noise were doing all the work here, and value noise is smooth by
        // construction — it produces patches of lighter and darker green, which is variation, not
        // texture. From the chase camera the lawn read as painted felt: no grain, nothing for the
        // eye to catch on, and no sense of what the surface is made of. This is the single biggest
        // reason the ground looked flat however the colours were tuned.
        //
        // A real detail map at two tiling rates fixes it. The project has already had one authored
        // and shipped (apron_grass_detail_512) and the lawn simply never sampled it.
        _DetailTex   ("Grass detail", 2D) = "white" {}
        _DetailScale ("Detail tiling (tiles per metre)", Float) = 0.55
        _DetailStrength ("Detail strength", Range(0,1)) = 0.55
        _DetailCutFade ("Detail fade on cut", Range(0,1)) = 0.35

        [Header(Look)]
        _MottleScale ("Mottle scale",     Float) = 0.09
        _MottleAmount("Mottle amount",    Range(0,1)) = 0.55
        _StripeAmount("Stripe contrast",  Range(0,0.5)) = 0.28
        _StripeFine  ("Stripe roller freq", Float) = 3.2
        _EdgeWidth   ("Cut edge width",   Range(0.5,6)) = 2.0
        _EdgeStrength("Cut edge strength",Range(0,1)) = 0.75
        _CutSheen    ("Fresh cut sheen",  Range(0,1)) = 0.35
        _Wrap        ("Light wrap",       Range(0,0.6)) = 0.28
        _AmbientGain ("Ambient gain", Range(0.5, 3)) = 1.05
        _AmbientFloor("Sky bounce floor", Range(0, 0.4)) = 0.035

        // _CutMask, _FieldSize, _FieldHalf and _FieldOrigin are deliberately NOT declared here.
        // Left out of the Properties block they resolve from the shader globals, which is what
        // the player's lawn wants. A rival plot gets a runtime material instance with those four
        // set on it directly, and a value set on a material always wins over the global — so one
        // shader serves the player's 64 m field and every 48 m rival lawn in the venue without
        // either path having to know about the other.
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
            #include "GrassCommon.hlsl"

            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _UncutBase, _UncutTip, _CutBase, _CutTip, _EdgeColor, _TrackColor;
                float4 _DetailTex_ST;
                float _DetailScale, _DetailStrength, _DetailCutFade;
                float _MottleScale, _MottleAmount, _StripeAmount, _StripeFine;
                float _EdgeWidth, _EdgeStrength, _CutSheen, _Wrap;
                float _AmbientGain, _AmbientFloor;
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

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 wxz = IN.positionWS.xz;
                float2 uv = WorldToMaskUV(IN.positionWS);

                float4 mask = SampleCutMaskBilinear(uv);
                float cut = mask.r;
                float track = mask.g;

                // ---- base colour with two octaves of mottling so uncut lawn is never flat ----
                float m1 = ValueNoise2D(wxz * _MottleScale);
                float m2 = ValueNoise2D(wxz * (_MottleScale * 4.7) + 31.7);
                float m3 = ValueNoise2D(wxz * (_MottleScale * 0.28) + 7.3);
                // Three scales: broad patches of richer and poorer grass, medium clumping, and
                // fine grain. One octave alone reads as noise; three read as a real field.
                float mottle = saturate(m1 * 0.46 + m2 * 0.20 + m3 * 0.34);
                mottle = lerp(0.5, mottle, _MottleAmount);

                half3 uncutCol = lerp(_UncutBase.rgb, _UncutTip.rgb, mottle);
                half3 cutCol   = lerp(_CutBase.rgb,   _CutTip.rgb,   mottle * 0.85 + 0.1);

                // ---- mow stripes ----
                // Direction is point-sampled with a dithered UV: point sampling keeps the band
                // edges crisp (real stripes have hard edges), the dither hides the 6 cm texel grid.
                float dither = (ValueNoise2D(wxz * 26.0) - 0.5) * (1.6 / MASK_RES);
                float dir01 = SampleCutMaskPoint(uv + dither).b;
                float ang = dir01 * TWO_PI;
                float2 mowDir = float2(cos(ang), sin(ang));

                float3 viewDir = GetWorldSpaceViewDir(IN.positionWS);
                // Looking straight down - which is exactly what the reveal camera does - the
                // horizontal view direction collapses to nothing and the stripes disappear at the
                // one moment the picture most needs to read as mown. Fall back to the sun's
                // horizontal direction as the reference as the view becomes vertical.
                float2 viewFlat = viewDir.xz;
                float viewFlatLen = length(viewFlat);
                float2 sunFlat = _MainLightPosition.xz;
                float sunFlatLen = length(sunFlat);
                float2 reference = sunFlatLen > 1e-3 ? sunFlat / sunFlatLen : float2(0, 1);
                float2 viewXZ = viewFlatLen > 1e-3 ? viewFlat / viewFlatLen : reference;
                float verticality = 1.0 - saturate(viewFlatLen / (length(viewDir) + 1e-4) * 3.0);
                viewXZ = normalize(lerp(viewXZ, reference, verticality) + 1e-5);
                float facing = dot(mowDir, viewXZ);

                // Fine roller texture running across each pass.
                float across = dot(wxz, float2(-mowDir.y, mowDir.x));
                float roller = sin(across * TWO_PI * _StripeFine) * 0.025;

                float stripe = 1.0 + (facing * _StripeAmount + roller) * smoothstep(0.35, 0.7, cut);
                cutCol *= stripe;

                // A faint memory of last week's mowing across the uncut lawn, so the field is
                // already a groundskeeper's lawn before the player touches it.
                float oldStripe = sin(wxz.x * 0.42) * 0.5 + 0.5;
                uncutCol *= 1.0 + (oldStripe - 0.5) * 0.055;

                half3 albedo = lerp(uncutCol, cutCol, smoothstep(0.25, 0.62, cut));

                // ---- detail grain ----
                //
                // Two tiling rates, deliberately incommensurate (0.37 is not a neat fraction of 1),
                // so the repeat of one is broken up by the other instead of beating against it.
                // A single tiling rate at this density reads as wallpaper the moment the camera
                // moves, which is worse than no texture at all.
                //
                // It multiplies rather than replaces: the palette above is doing the colour work
                // and this is only supplying grain, so it is centred on 1 and pulls the surface
                // both up and down.
                float2 dUV0 = wxz * _DetailScale;
                float2 dUV1 = wxz * (_DetailScale * 0.37) + 13.7;
                float g0 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, dUV0).r;
                float g1 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, dUV1).r;
                float grain = (g0 * 0.62 + g1 * 0.38) * 2.0 - 1.0;

                // Cut grass keeps some grain but less: a mown surface really is more uniform than
                // one that has been left, and holding the detail flat across both made the picture
                // harder to read from the reveal.
                float grainAmt = _DetailStrength * lerp(1.0, 1.0 - _DetailCutFade, smoothstep(0.35, 0.7, cut));
                albedo *= 1.0 + grain * grainAmt * 0.55;

                // ---- wheel tracks: pressed, darker, slightly glossier ----
                float trackAmt = saturate(track) * 0.85;
                albedo = lerp(albedo, _TrackColor.rgb * (0.9 + facing * 0.12), trackAmt * 0.55);

                // ---- the drawn edge: a thin dark rim wherever cut meets uncut ----
                // Width is normalised by the screen-space gradient so it stays one line thick
                // whether you are 3 m or 90 m away. This is what makes the picture read.
                float grad = max(fwidth(cut), 1e-4);
                float edge = 1.0 - saturate(abs(cut - 0.5) / (grad * _EdgeWidth));
                albedo = lerp(albedo, _EdgeColor.rgb, edge * _EdgeStrength);

                // ---- lighting: soft wrapped lambert, never black in shadow ----
                float3 N = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half ndotl = saturate(dot(N, mainLight.direction));
                half wrapped = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                half shadow = lerp(1.0, mainLight.shadowAttenuation, 0.72);

                half3 lighting = mainLight.color * wrapped * shadow;
                // Same cool floor as the blade layer, and it has to match: any difference between
                // the two shows up as a hue seam wherever the blades thin out with distance.
                half3 ambient = SampleSH(N) * _AmbientGain
                                + half3(0.62, 0.76, 1.0) * _AmbientFloor;

                // Fresh cut catches a little sheen along the direction of travel.
                float3 H = normalize(mainLight.direction + normalize(viewDir));
                half sheen = pow(saturate(dot(N, H)), 24.0) * _CutSheen * smoothstep(0.4, 0.8, cut);

                half3 color = albedo * (lighting + ambient) + sheen * mainLight.color * 0.35;
                color = MixFog(color, IN.fogCoord);
                return half4(color, 1);
            }
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
