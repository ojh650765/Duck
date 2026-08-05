// The entry card: the picture the duck has been asked to mow, and nothing else.
//
// This replaces the old corner minimap, which read the live cut mask and the target field together
// and so drew the outline, the fill, the spill and the mower's position in one frame. That is the
// answer key. With the ground guide now dissolving a third of the way into the round, leaving it on
// screen would have handed back everything the round takes away — and worse, a live coverage read
// lets the shape be brute-forced by driving until the number climbs.
//
// So this samples the target distance field ONLY. No cut mask, no mower, no progress. It says what
// you were asked for; where you are and how you are doing are the player's problem.
//
// It is drawn as ink on paper rather than as a screen for the same reason: an inset panel that
// looks like a display invites the player to keep checking it for position. A pinned sheet of paper
// reads as a reference and gets glanced at once.
Shader "Duck/ShapeCardUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}

        _Paper      ("Paper",         Color) = (0.94, 0.90, 0.79, 1)
        _PaperShade ("Paper shading", Color) = (0.84, 0.78, 0.64, 1)
        _Ink        ("Ink",           Color) = (0.22, 0.16, 0.12, 1)
        _InkFill    ("Ink wash",      Color) = (0.55, 0.47, 0.33, 1)

        // The shape is a SOLID, not a line.
        //
        // The first version drew a pale 22% wash inside a heavy outline, and at the size this sits
        // on screen that reads as a one-stroke puzzle — a route to trace rather than a region to
        // clear. It was quietly instructing the wrong verb: the round is scored on area mown, and
        // a player who traces the outline and stops has drawn the one thing that scores worst.
        //
        // A filled silhouette cannot be misread. The outline is off by default and kept only as a
        // dial, because any visible stroke starts the line-drawing reading again.
        _StrokeWidth("Stroke width",  Range(0, 0.08)) = 0
        _FillAmount ("Fill strength", Range(0, 1)) = 1
        _ShadowDrop ("Cut-out shadow", Range(0, 0.06)) = 0.016
        _Grain      ("Paper grain",   Range(0, 1)) = 0.35
        _Rounding   ("Corner rounding", Range(0, 0.5)) = 0.04

        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"
            "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_TargetSdf);
            SAMPLER(sampler_linear_clamp);
            float _ShapeRadius;
            float _SdfBand;
            float _FieldHalf;

            float4 _Paper, _PaperShade, _Ink, _InkFill, _Color;
            float _StrokeWidth, _FillAmount, _ShadowDrop, _Grain, _Rounding;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float RoundedRectMask(float2 uv, float r)
            {
                float2 p = abs(uv - 0.5) - (0.5 - r);
                float d = length(max(p, 0.0)) + min(max(p.x, p.y), 0.0) - r;
                return 1.0 - smoothstep(-0.004, 0.004, d);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = saturate(IN.uv);

                // The card crops to the picture's own frame rather than the whole 64 m field, so a
                // small shape is drawn large. The field is what the ground guide is registered to;
                // the card is about form alone, and a heart floating in a sea of margin reads as a
                // smaller, vaguer heart.
                float frame = _ShapeRadius / max(_FieldHalf, 1e-3);
                float2 sdfUV = (uv - 0.5) * frame * 1.14 + 0.5;

                float raw = SAMPLE_TEXTURE2D(_TargetSdf, sampler_linear_clamp, sdfUV).r;
                float dist = (raw * 2.0 - 1.0) * _SdfBand * _ShapeRadius;   // metres

                // Paper: a soft vertical shade plus grain, so the card does not read as flat UI.
                float grain = (Hash(floor(IN.uv * 220.0)) - 0.5) * _Grain * 0.12;
                half3 col = lerp(_Paper.rgb, _PaperShade.rgb, saturate(uv.y * 0.55 + 0.08)) + grain;

                float aa = max(fwidth(dist), 0.02);
                float inside = 1.0 - smoothstep(-aa, aa, dist);

                // A dropped shadow under the silhouette, so the shape reads as a piece of paper
                // laid ON the card rather than as a mark printed on it. Cheap depth, and it stops
                // a dark solid from looking like a hole.
                float2 sdfShadowUV = sdfUV - float2(_ShadowDrop, -_ShadowDrop);
                float rawS = SAMPLE_TEXTURE2D(_TargetSdf, sampler_linear_clamp, sdfShadowUV).r;
                float distS = (rawS * 2.0 - 1.0) * _SdfBand * _ShapeRadius;
                float shadow = 1.0 - smoothstep(-aa * 2.0, aa * 2.0, distS);
                col = lerp(col, col * 0.72, shadow * 0.55);

                // ---- the fill: mow stripes, not a flat block ----
                //
                // The card has to say "clear this area", and a solid slab says "a shape exists
                // here" without saying what to do about it. Striping the interior the way a mown
                // lawn stripes states the task in the game's own language: this is grass, and it
                // is going to be cut in passes. It also matches what the player is about to be
                // looking at on the ground, so card and lawn read as the same object.
                float2 stripeSpace = (uv - 0.5) * 26.0;
                float stripe = sin((stripeSpace.x + stripeSpace.y) * 1.35) * 0.5 + 0.5;
                half3 fillCol = lerp(_InkFill.rgb, _Ink.rgb, smoothstep(0.35, 0.65, stripe));
                col = lerp(col, fillCol, inside * _FillAmount);

                // Optional outline, off by default — any visible stroke restarts the
                // one-stroke-drawing reading this fill exists to kill.
                if (_StrokeWidth > 0.0005)
                {
                    float widthM = _StrokeWidth * _ShapeRadius * 2.0;
                    float stroke = 1.0 - smoothstep(widthM - aa, widthM + aa, abs(dist));
                    col = lerp(col, _Ink.rgb, stroke);
                }

                float a = RoundedRectMask(IN.uv, _Rounding) * IN.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
