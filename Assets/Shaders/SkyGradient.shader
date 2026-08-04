// Painterly skybox: a three-stop vertical gradient, warm scatter around the sun, a soft sun
// disc and a band of stylised high cloud. Cheap enough to be free in WebGL and it sets the
// entire mood of the game, so it is worth not using the default procedural sky.
Shader "Duck/SkyGradient"
{
    Properties
    {
        _Zenith    ("Zenith",   Color) = (0.0865, 0.3231, 0.6584, 1)
        _Mid       ("Mid sky",  Color) = (0.3564, 0.6376, 0.8469, 1)
        _Horizon   ("Horizon",  Color) = (0.6376, 0.8308, 0.9046, 1)
        _GroundCol ("Below horizon", Color) = (0.3200, 0.4200, 0.3000, 1)

        _MidPoint  ("Gradient mid point", Range(0.02, 0.9)) = 0.28
        _HorizonSharp ("Horizon sharpness", Range(0.5, 8)) = 2.4

        _SunColor  ("Sun tint", Color) = (1.0, 0.9532, 0.8148, 1)
        _SunSize   ("Sun size", Range(0.001, 0.2)) = 0.030
        _SunSoft   ("Sun softness", Range(0.001, 0.4)) = 0.055
        _SunGlow   ("Sun glow spread", Range(1, 64)) = 12
        _SunGlowStrength ("Sun glow strength", Range(0, 2)) = 0.55

        _CloudColor ("Cloud", Color) = (1, 0.98, 0.94, 1)
        _CloudAmount ("Cloud amount", Range(0, 1)) = 0.35
        _CloudScale ("Cloud scale", Range(0.2, 6)) = 1.7
        _CloudHeight ("Cloud band height", Range(0.02, 0.7)) = 0.22
        _CloudDrift ("Cloud drift speed", Range(0, 0.05)) = 0.006
        _Exposure  ("Exposure", Range(0.2, 3)) = 1.0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Zenith, _Mid, _Horizon, _GroundCol, _SunColor, _CloudColor;
            float _MidPoint, _HorizonSharp, _SunSize, _SunSoft, _SunGlow, _SunGlowStrength;
            float _CloudAmount, _CloudScale, _CloudHeight, _CloudDrift, _Exposure;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            float Hash21S(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise2S(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21S(i), b = Hash21S(i + float2(1, 0));
                float c = Hash21S(i + float2(0, 1)), d = Hash21S(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * Noise2S(p); p *= 2.13; a *= 0.5; }
                return v;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float h = d.y;

                // Three-stop gradient with a deliberately sharp horizon so the hills read against it.
                float up = saturate(h);
                // pow() with a negative base is undefined and was drawing a hard arc across the
                // sky exactly at the gradient mid point. Clamp the base before raising it.
                float lower = pow(saturate(1.0 - up / max(_MidPoint, 1e-3)), _HorizonSharp);
                float3 sky = lerp(_Mid.rgb, _Horizon.rgb, lower);
                float upper = saturate((up - _MidPoint) / max(1.0 - _MidPoint, 1e-3));
                sky = lerp(sky, _Zenith.rgb, pow(saturate(upper), 0.85));

                // _MainLightPosition.xyz points from the world toward the sun.
                float3 toSun = normalize(_MainLightPosition.xyz);
                float cosA = dot(d, toSun);

                // Warm forward scatter: the sky gets creamier the closer you look to the sun.
                float glow = pow(saturate(cosA * 0.5 + 0.5), _SunGlow);
                sky += _SunColor.rgb * glow * _SunGlowStrength * saturate(1.0 - up * 0.4);

                // Cloud band, drifting slowly, thickest a little above the horizon.
                float2 cuv = d.xz / max(abs(h) + 0.12, 0.12);
                cuv = cuv * _CloudScale + _Time.y * _CloudDrift * float2(1.0, 0.35);
                float c = Fbm(cuv);
                float band = exp(-pow((h - _CloudHeight) / 0.30, 2.0));
                float clouds = saturate((c - 0.52) * 3.4) * band * _CloudAmount * saturate(h * 8.0);
                sky = lerp(sky, _CloudColor.rgb, clouds);

                // Sun disc, drawn last so cloud passes in front of the glow but not the disc.
                float ang = acos(clamp(cosA, -1.0, 1.0));
                float disc = 1.0 - smoothstep(_SunSize, _SunSize + _SunSoft, ang);
                sky = lerp(sky, _SunColor.rgb * 1.9, disc);

                // Below the horizon we are looking at the ground plane's haze, not black.
                sky = lerp(_GroundCol.rgb, sky, saturate(h * 14.0 + 0.5));

                return half4(sky * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
