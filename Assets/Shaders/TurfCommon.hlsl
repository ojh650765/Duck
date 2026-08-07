#ifndef DUCK_TURF_COMMON_INCLUDED
#define DUCK_TURF_COMMON_INCLUDED

// Shared Bloom Rush turf helpers: mask access, owner decoding, arena geometry.
//
// Kept in one place so the ground layer, the bloom layer and the overhead reveal can never
// disagree about who owns a piece of ground or where the hedges are. TurfArena.cs holds the same
// geometry in C#; the two are kept in step by TurfMask pushing its constants as shader globals
// every time it wakes, rather than by both hard-coding the numbers.

#ifndef TWO_PI
#define TWO_PI 6.28318530718
#endif

TEXTURE2D(_TurfMask);
SAMPLER(sampler_turf_point_clamp);

// Written by TurfMask.Awake.
float  _TurfSize;          // metres covered by the mask, edge to edge
float  _TurfTexel;         // metres per mask texel
float  _TurfFlipV;         // 1 when this platform stores render textures top-down
float4 _TurfLivery[4];     // rgb = each competitor's colour, a = their turf pattern phase
float4 _TurfAccent[4];     // rgb = the colour their flowers bloom in
float4 _TurfGeometry;      // x = arena radius, y = plaza radius, z = hedge inner, w = hedge outer
float4 _TurfGaps;          // x = spoke half-width, y = choke half-width, z = ramp half-width, w = core radius
float4 _TurfPulse;         // x = match urgency 0..1, y = crown holder (-1 none), z = crown pulse

float2 TurfWorldToUV(float2 worldXZ)
{
    float2 uv = worldXZ / _TurfSize + 0.5;
    uv.y = lerp(uv.y, 1.0 - uv.y, _TurfFlipV);
    return uv;
}

float4 TurfSampleRaw(float2 uv)
{
    return SAMPLE_TEXTURE2D(_TurfMask, sampler_turf_point_clamp, uv);
}

// Vertex shaders cannot rely on automatic mip selection, so the blade layer asks for level 0
// explicitly. Point sampled like everything else that reads ownership.
float4 TurfSampleLOD(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_TurfMask, sampler_turf_point_clamp, uv, 0);
}

// Owner id from the mask's red channel: -1 for neutral ground, 0..3 for a competitor.
//
// Point sampled and rounded, never filtered. A bilinear tap between two owner codes decodes to
// whoever happens to sit between them, which would hand a border's worth of ground to a third
// party and put a stripe of the wrong colour down every seam in the arena.
int TurfDecodeOwner(float encoded)
{
    // Clamped, and it is not paranoia. Every read of the result indexes a four-element constant
    // array, and the mask is not always the mask: before TurfMask wakes, and in the editor with the
    // scene merely open, the sampler is bound to whatever the platform hands back for an unset
    // texture — which is white on some drivers and decodes to owner seven. That reads four vectors
    // past the end of the livery array and renders as garbage that differs per machine.
    return clamp((int)round(encoded * 8.0) - 1, -1, 3);
}

float TurfHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float TurfNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = TurfHash21(i);
    float b = TurfHash21(i + float2(1, 0));
    float c = TurfHash21(i + float2(0, 1));
    float d = TurfHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}


// The eight gateways, as light, computed rather than placed.
//
// Bloom Rush is the only night level in the game and the lit gates are its strongest navigational
// cue — the way in is the bright place. Getting that light onto the turf by the obvious route does
// not work here, and it is worth writing down why so nobody spends another hour on it:
//
//   * URP is configured PerVertex for additional lights (m_AdditionalLightsRenderingMode: 1), so a
//     fragment-side GetAdditionalLight loop is compiled out entirely and lights nothing.
//   * The per-object limit is TWO. The arena floor is ONE mesh, so even switching to PerPixel would
//     let two of the eight lamps light the entire ground and the other six would do nothing.
//   * Both of those are project-wide graphics settings shared with two finished stages, and turning
//     them up to fix one level's lighting is a regression risk and a WebGL cost everywhere else.
//
// So the pools are derived from the same TurfArena geometry every other shape in this shader comes
// from. Eight distance tests, no lights, no per-object limits, no variants, identical on every
// platform, and it cannot drift out of step with where the gates actually are.
float TurfGateGlow(float2 wxz, float range)
{
    float mid = (_TurfGeometry.z + _TurfGeometry.w) * 0.5;
    float best = 0.0;

    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float quarter = i * (TWO_PI * 0.25);
        float2 spoke = float2(sin(quarter + TWO_PI * 0.125), cos(quarter + TWO_PI * 0.125)) * mid;
        float2 choke = float2(sin(quarter), cos(quarter)) * mid;
        best = max(best, 1.0 - saturate(length(wxz - spoke) / range));
        best = max(best, 1.0 - saturate(length(wxz - choke) / range));
    }
    // Squared, so the pool has a bright core and a long soft skirt rather than a linear cone with
    // a visible rim — a lantern falls off faster than distance.
    return best * best;
}

// ---------------------------------------------------------------- arena geometry
//
// The same wheel TurfArena.IsPlayable describes, evaluated per fragment. The shader carves the
// hedge footprint out of the ground itself rather than trusting a prop to cover it, so what the
// player sees as unpaintable and what the scoring counts as unpaintable are the same shape.

float TurfArcDistance(float angA, float angB, float r)
{
    float d = abs(angA - angB);
    d = min(d, TWO_PI - d);
    return d * r;
}

// Signed clearance into the nearest gap, in metres. Positive is open ground.
float TurfGapOpening(float2 wxz, float r)
{
    if (r < 1e-3) return _TurfGaps.x;
    float ang = atan2(wxz.x, wxz.y);

    float best = -1e6;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float quarter = i * (TWO_PI * 0.25);
        best = max(best, _TurfGaps.x - TurfArcDistance(ang, quarter + TWO_PI * 0.125, r));   // spoke
        best = max(best, _TurfGaps.y - TurfArcDistance(ang, quarter, r));                    // choke
    }
    return best;
}

// 1 where ground can be painted, 0 under a hedge, with a metre of softness so the transition is a
// planted edge rather than a stencil.
//
// This is the LOOK. Every ramp in it is there to make an edge die away over a metre instead of
// ending on a stencil line, which is exactly what the ground layer, the blades and the map want and
// exactly what the STAMP must not have — see TurfPlayableHard below for the rule.
float TurfPlayable(float2 wxz, out float hedgeDepth)
{
    float r = length(wxz);
    hedgeDepth = 0.0;

    float outer = saturate((_TurfGeometry.x - r) / 1.2);
    // The sealed core at dead centre. Same test as TurfArena.IsPlayable, so the ground stops
    // being paintable exactly where the wall stops the machines — a metre of disagreement here
    // and the mask paints a ring nobody can reach.
    outer *= saturate((r - _TurfGaps.w) / 1.2);
    if (r < _TurfGeometry.z || r > _TurfGeometry.w) return outer;

    float open = TurfGapOpening(wxz, r);
    hedgeDepth = saturate(-open / 3.0);
    return outer * saturate(open / 1.1 + 0.5);
}

// The same question, answered HARD. No feather anywhere in it, and that is the entire point.
//
// A soft edge is the right answer to "how should the arena stop looking like arena" and the wrong
// answer to "where is the arena", and TurfStamp was asking the second question with the first
// function. Clipping the feathered ramp at half strength is quietly a DIFFERENT ARENA to the one
// TurfArena.IsPlayable describes: saturate((ArenaRadius - r) / 1.2) does not reach 0.5 until 0.6 m
// inside the touchline, and the core ramp not until 0.6 m outside the wall. TurfMask.BuildGrid
// marks both of those rings playable, TurfMask.Claim changes their owner and pays the player for
// them, and the stamp then discarded every fragment in them — so they counted toward the score and
// rendered as untouched neutral turf forever. Roughly 157 m2 around the rim and 31 m2 around the
// core: ground a player drove, was scored for, and watched stay grey.
//
// It cannot be fixed by lowering the clip threshold, which is the tempting one-character version.
// Inside the hedge band the ramp returns saturate(open / 1.1 + 0.5), which is exactly 0.5 when the
// clearance is zero — half strength is ALREADY sitting on the hedge face and is already correct
// there. Any threshold below it buys back the missing rim by letting paint run 1.1 * (0.5 - t)
// metres INTO the hedges — the same disagreement with the same score, pointing the other way, and
// against the one edge in this arena the player can see with their own eyes. The hedge face has to
// stay exactly at "clearance greater than zero", so the rim has to be fixed somewhere else. Here.
//
// Structured to read against IsPlayable line for line, squared radii and all, because the day those
// two stop matching is the day the picture stops matching the percentages. Every radius comes from
// the uniforms TurfPalette pushes out of the TurfArena constants, so moving the touchline moves
// this with it and no shader is edited.
bool TurfPlayableHard(float2 wxz)
{
    float rSq = dot(wxz, wxz);
    if (rSq > _TurfGeometry.x * _TurfGeometry.x) return false;   // past the touchline
    if (rSq < _TurfGaps.w * _TurfGaps.w) return false;           // inside the sealed core
    float r = sqrt(rSq);
    if (r < _TurfGeometry.z || r > _TurfGeometry.w) return true; // hub and court, or the outer loop
    return TurfGapOpening(wxz, r) > 0.0;                         // in the wall: only through a gap
}

#endif
