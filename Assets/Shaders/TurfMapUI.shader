// The live territory map in the corner of the Bloom Rush HUD.
//
// It draws the ownership mask itself, decoded into the four liveries — not a redraw of the arena
// kept in step with it, which is the version that ends up lying to the player about who holds what
// the moment anything is added to the mode. One texture, one decode, and the map is by construction
// exactly the ground.
//
// A territory mode's central decision — expand, steal, defend, or contest the middle — is spatial,
// and the player is sitting eighteen inches off the floor looking down a hedge. Four percentages in
// a bar say who is winning; only a map says WHERE, and where is the whole question.
Shader "Duck/TurfMapUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Neutral   ("Unclaimed",  Color) = (0.16, 0.22, 0.14, 1)
        _Backing   ("Off arena",  Color) = (0.05, 0.06, 0.05, 0.85)
        _HedgeCol  ("Hedge",      Color) = (0.09, 0.13, 0.08, 1)
        _GridFade  ("Edge fade",  Range(0,0.4)) = 0.06

        // Required by Unity's UI batching; unused here but their absence makes a Mask misbehave.
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
            "Queue" = "Transparent" "RenderType" = "Transparent"
            "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "TurfMap"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TurfCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Neutral, _Backing, _HedgeCol;
                float _GridFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // The quad's own 0..1 UV becomes an arena position, so the map is oriented exactly
                // like the ground: north is up, and the player's spoke is where it is on the pitch.
                float2 wxz = (IN.uv - 0.5) * _TurfSize;
                float r = length(wxz);

                float hedge;
                float playable = TurfPlayable(wxz, hedge);

                float4 tap = TurfSampleRaw(TurfWorldToUV(wxz));
                int owner = TurfDecodeOwner(tap.r);

                half3 col = owner >= 0 ? _TurfLivery[owner].rgb : _Neutral.rgb;

                // Freshly taken ground flares on the map too, which is how a steal on the far side
                // of the arena announces itself to a player who cannot see it.
                col += _TurfAccent[max(owner, 0)].rgb * saturate(tap.g) * (owner >= 0 ? 0.9 : 0.0);
                col += half3(1.0, 0.85, 0.6) * saturate(tap.b) * (owner >= 0 ? 1.2 : 0.0);

                col = lerp(_HedgeCol.rgb, col, playable);

                // A ring at the plaza kerb, so the contested middle is a place on the map rather
                // than a colour in the middle of it.
                float kerb = 1.0 - saturate(abs(r - _TurfGeometry.y) / 0.9);
                col = lerp(col, half3(0.95, 0.92, 0.72), kerb * 0.55);

                // Circular crop with a soft edge, and the backing showing through outside it.
                float inside = 1.0 - smoothstep(_TurfGeometry.x, _TurfGeometry.x + 1.4, r);
                half4 outCol;
                outCol.rgb = lerp(_Backing.rgb, col, inside);
                outCol.a = lerp(_Backing.a, 1.0, inside);

                float disc = 1.0 - smoothstep(0.5 - _GridFade, 0.5, length(IN.uv - 0.5));
                outCol *= IN.color;
                outCol.a *= disc;
                return outCol;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
