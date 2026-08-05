#ifndef DUCK_GRASS_COMMON_INCLUDED
#define DUCK_GRASS_COMMON_INCLUDED

// Shared lawn helpers: cut-mask access, noise and wind. Kept in one place so the ground
// layer and the blade layer can never disagree about where the grass has been cut.

#ifndef TWO_PI
#define TWO_PI 6.28318530718
#endif
#define MASK_RES 1024.0

TEXTURE2D(_CutMask);
SAMPLER(sampler_linear_clamp);
SAMPLER(sampler_point_clamp);

float _FieldSize;
float _FieldHalf;

// Where this plot's lawn is centred, in world XZ. Zero for the player's field, which is why the
// player's material can leave it alone and keep using the globals; the rival plots across the
// venue each set their own, so one shader serves every lawn on the ground.
float2 _FieldOrigin;

// Render textures are stored bottom-up on OpenGL and top-down on D3D. Everything else in the
// game samples the mask with world-derived UVs rather than screen UVs, so Unity's usual
// compensation never happens and the cut would appear mirrored north-south. 1 = flip.
float _CutMaskFlipV;

float2 ApplyMaskFlip(float2 uv)
{
    uv.y = lerp(uv.y, 1.0 - uv.y, _CutMaskFlipV);
    return uv;
}

// Wind is global so every blade in the field agrees on the gust that is passing through.
float4 _WindParams;      // x = strength, y = speed, z = gust scale, w = gust speed
float4 _WindDirection;   // xy = normalised XZ direction

float2 WorldToMaskUV(float3 positionWS)
{
    return (positionWS.xz - _FieldOrigin) / _FieldSize + 0.5;
}

float4 SampleCutMaskBilinear(float2 uv)
{
    return SAMPLE_TEXTURE2D(_CutMask, sampler_linear_clamp, ApplyMaskFlip(uv));
}

float4 SampleCutMaskPoint(float2 uv)
{
    return SAMPLE_TEXTURE2D(_CutMask, sampler_point_clamp, ApplyMaskFlip(uv));
}

// Vertex shaders cannot rely on automatic mip selection, so blades sample level 0 explicitly.
float4 SampleCutMaskLOD(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_CutMask, sampler_linear_clamp, ApplyMaskFlip(uv), 0);
}

// ---------------------------------------------------------------- noise

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1, 0));
    float c = Hash21(i + float2(0, 1));
    float d = Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float FBM2(float2 p)
{
    return ValueNoise2D(p) * 0.65 + ValueNoise2D(p * 2.3 + 17.1) * 0.35;
}

// ---------------------------------------------------------------- wind

// Returns an XZ offset for a blade tip. `heightFrac` is 0 at the root and 1 at the tip;
// the square makes the blade bend rather than shear.
float2 WindOffset(float2 worldXZ, float heightFrac, float phase)
{
    float t = _Time.y;
    float2 dir = _WindDirection.xy;

    // A slow, large-scale gust travelling across the field so the whole lawn breathes.
    float gust = FBM2(worldXZ * _WindParams.z - dir * (t * _WindParams.w));
    gust = gust * 0.75 + 0.35;

    // Per-blade flutter.
    float wave = sin(dot(worldXZ, dir) * 0.9 - t * _WindParams.y + phase * TWO_PI);
    float flutter = sin(t * _WindParams.y * 2.7 + phase * 19.0) * 0.25;

    float bend = (wave + flutter) * gust * _WindParams.x;
    float h2 = heightFrac * heightFrac;
    return dir * bend * h2;
}

#endif
