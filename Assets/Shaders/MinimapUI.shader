// The corner minimap. Reads the same two globals the world does — the live cut mask and the
// target signed-distance field — so it can never disagree with the lawn the player is looking at.
//
// It is the player's only way to judge coverage while nose-down in the grass, so it is drawn for
// legibility rather than prettiness: flat fills, a hard target outline, spill called out in
// orange, and the duck as an unmissable arrow.
Shader "Duck/MinimapUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Uncut     ("Uncut",  Color) = (0.10, 0.24, 0.11, 1)
        _Cut       ("Cut",    Color) = (0.55, 0.78, 0.28, 1)
        _Spill     ("Spill",  Color) = (0.92, 0.52, 0.16, 1)
        _Outline   ("Target outline", Color) = (1, 0.97, 0.86, 1)
        _InsideTint("Target fill", Color) = (0.16, 0.34, 0.16, 1)
        _MowerColor("Mower", Color) = (0.90, 0.24, 0.20, 1)

        _MowerUV   ("Mower UV + heading", Vector) = (0.5, 0.5, 0, 0)
        _OutlineWidth ("Outline width", Range(0.001, 0.05)) = 0.008
        _MowerSize ("Mower marker size", Range(0.005, 0.08)) = 0.026
        _Rounding  ("Corner rounding", Range(0, 0.5)) = 0.06

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

            TEXTURE2D(_CutMask);
            TEXTURE2D(_TargetSdf);
            SAMPLER(sampler_linear_clamp);
            float _ShapeRadius;
            float _SdfBand;
            float _FieldHalf;
            float _CutMaskFlipV;

            float4 _Uncut, _Cut, _Spill, _Outline, _InsideTint, _MowerColor, _Color;
            float4 _MowerUV;
            float _OutlineWidth, _MowerSize, _Rounding;

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

            float RoundedRectMask(float2 uv, float r)
            {
                float2 p = abs(uv - 0.5) - (0.5 - r);
                float d = length(max(p, 0.0)) + min(max(p.x, p.y), 0.0) - r;
                return 1.0 - smoothstep(-0.004, 0.004, d);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = saturate(IN.uv);

                float2 cutUV = float2(uv.x, lerp(uv.y, 1.0 - uv.y, _CutMaskFlipV));
                float cut = SAMPLE_TEXTURE2D(_CutMask, sampler_linear_clamp, cutUV).r;
                float raw = SAMPLE_TEXTURE2D(_TargetSdf, sampler_linear_clamp, uv).r;
                float dist = (raw * 2.0 - 1.0) * _SdfBand * _ShapeRadius;   // metres

                float inside = 1.0 - smoothstep(-0.15, 0.15, dist);
                float isCut = smoothstep(0.35, 0.6, cut);

                // Base: dark outside the target, slightly lifted inside it, so the player can
                // see the shape they are filling even before they have cut anything.
                half3 col = lerp(_Uncut.rgb, _InsideTint.rgb, inside);

                // Cut grass reads bright inside the shape and orange outside it.
                half3 cutCol = lerp(_Spill.rgb, _Cut.rgb, inside);
                col = lerp(col, cutCol, isCut);

                // Hard target outline on top of everything.
                float widthM = _OutlineWidth * _FieldHalf * 2.0;
                float outline = 1.0 - smoothstep(widthM * 0.5, widthM, abs(dist));
                col = lerp(col, _Outline.rgb, outline * 0.9);

                // Mower marker: a triangle pointing along the heading.
                float2 d = uv - _MowerUV.xy;
                float2 fwd = float2(_MowerUV.z, _MowerUV.w);
                float2 side = float2(-fwd.y, fwd.x);
                float along = dot(d, fwd) / _MowerSize;
                float across = dot(d, side) / _MowerSize;
                float tri = step(along, 1.0) * step(-0.7, along) *
                            step(abs(across), 0.55 * (1.0 - along) + 0.06);
                col = lerp(col, _MowerColor.rgb, saturate(tri));
                // A pale halo so the marker survives on top of bright cut grass.
                float halo = 1.0 - smoothstep(_MowerSize * 1.1, _MowerSize * 1.5, length(d));
                col = lerp(col, half3(1, 1, 1), saturate(halo - tri) * 0.35);

                float a = RoundedRectMask(IN.uv, _Rounding) * IN.color.a;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
