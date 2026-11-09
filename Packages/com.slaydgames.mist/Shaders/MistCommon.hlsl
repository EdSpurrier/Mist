#ifndef SLAYD_MIST_COMMON_INCLUDED
#define SLAYD_MIST_COMMON_INCLUDED

// Shared by every mist pass: the packed data, the fullscreen triangle, depth helpers, the box tests
// (section 4.1), density (4.2), clear zones (4.6), scene lights and the span bookkeeping of the march (4.3).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

#define MIST_MAX_HITS 8            // boxes one ray can hold
#define MIST_MAX_PIXEL_LIGHTS 4    // scene lights evaluated per pixel

// ------------------------------------------------------------------ data from MistRenderPass

StructuredBuffer<float4> _MistVolumes;   // six float4 per volume, A..F of section 3
StructuredBuffer<float4> _MistLights;    // three per light: (pos, range) (linear colour, cos outer or -2 for point) (spot dir, cos inner)
StructuredBuffer<float4> _MistClears;    // two per clear zone: (x, z, heading x, heading z) (cos half angle or -2 for a disc, range, soft, strength)
int _MistVolumeCount;
int _MistShadeCount;                     // nearest volumes that shade the ground (16)
int _MistLightCount;
int _MistClearCount;

TEXTURE3D(_MistNoise);        SAMPLER(sampler_MistNoise);   // 48^3, repeat, bilinear
TEXTURE2D_FLOAT(_MistDepth);  // the camera depth texture
TEXTURE2D(_MistSource);       // the previous stage's output
TEXTURE2D(_MistSource2);      // temporal pass: the history

float4x4 _MistInvViewProj;
float4x4 _MistPrevViewProj;
float4   _MistParams0;    // x: max steps, y: self shadow on, z: scene lights on, w: frame jitter
float4   _MistParams1;    // x: ground shade strength, y: camera moved this frame, z: far distance when there is no depth, w: history valid
float4   _MistSunDir;     // xyz: toward the sun, w: 1 when there is a sun
float4   _MistSunColor;   // rgb: linear colour x intensity x renderer "sun"
float4   _MistAmbient;    // rgb: linear ambient x renderer "ambient"
float4   _MistTargetSize; // xy: mist target size in pixels, zw: 1 / size

// Volume fields, named as in the brief.
float4 VolA(int i) { return _MistVolumes[i * 6 + 0]; }   // centre xyz, cos(yaw)
float4 VolB(int i) { return _MistVolumes[i * 6 + 1]; }   // half size xyz, sin(yaw)
float4 VolC(int i) { return _MistVolumes[i * 6 + 2]; }   // colour rgb, density
float4 VolD(int i) { return _MistVolumes[i * 6 + 3]; }   // glow rgb (x strength), softness
float4 VolE(int i) { return _MistVolumes[i * 6 + 4]; }   // drift xyz, 1 / (noiseScale x 5)
float4 VolF(int i) { return _MistVolumes[i * 6 + 5]; }   // churn phase, noise, falloff, seed

// ------------------------------------------------------------------ fullscreen triangle

struct Attributes { uint vertexID : SV_VertexID; };
struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

Varyings MistVert(Attributes IN)
{
    Varyings OUT;
    OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
    OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
    return OUT;
}

// ------------------------------------------------------------------ depth (reversed Z and orthographic cameras minded)

float MistRawDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_MistDepth, sampler_PointClamp, uv, 0).r;
}

bool MistIsSky(float raw)
{
#if UNITY_REVERSED_Z
    return raw <= 1e-7;
#else
    return raw >= 1.0 - 1e-7;
#endif
}

// Positive distance along the view axis.
float MistViewZ(float raw)
{
    if (unity_OrthoParams.w > 0.5)
    {
#if UNITY_REVERSED_Z
        raw = 1.0 - raw;
#endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw, _ZBufferParams);
}

float3 MistWorldPos(float2 uv, float raw)
{
    return ComputeWorldSpacePosition(uv, raw, _MistInvViewProj);
}

// ------------------------------------------------------------------ 4.1 box tests

float3 VolLocal(int i, float3 p)   // world -> box frame
{
    float c = VolA(i).w, s = VolB(i).w;
    float3 d = p - VolA(i).xyz;
    return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
}

float3 VolDir(int i, float3 v)
{
    float c = VolA(i).w, s = VolB(i).w;
    return float3(c * v.x - s * v.z, v.y, s * v.x + c * v.z);
}

// Slab test. Returns (enter, exit); exit <= enter means a miss. Enter is clamped to 0.
float2 VolHit(int i, float3 o, float3 dir)
{
    float3 h = VolB(i).xyz;
    float3 lo = VolLocal(i, o);
    float3 ld = VolDir(i, dir);
    float3 safe = abs(ld) < 1e-6 ? 1e-6 : ld;
    float3 inv = 1.0 / safe;
    float3 ta = (-h - lo) * inv;
    float3 tb = ( h - lo) * inv;
    float3 tmin = min(ta, tb), tmax = max(ta, tb);
    return float2(max(max(tmin.x, tmin.y), max(tmin.z, 0.0)), min(min(tmax.x, tmax.y), tmax.z));
}

// ------------------------------------------------------------------ 4.6 clear zones

float MistClearKeep(float3 p)
{
    float keep = 1.0;
    [loop] for (int c = 0; c < _MistClearCount; c++)
    {
        float4 a = _MistClears[c * 2], b = _MistClears[c * 2 + 1];
        float2 d = p.xz - a.xy;
        float r = length(d);
        if (r >= b.y) continue;
        float side = 1.0;
        if (b.x > -1.5)   // a wedge, not a disc
        {
            float cs = r > 1e-3 ? dot(d / r, a.zw) : 1.0;
            side = smoothstep(b.x, b.x + (1.0 - b.x) * b.z, cs);
        }
        float end = 1.0 - smoothstep(b.y * (1.0 - b.z), b.y, r);
        keep *= 1.0 - b.w * side * end;
    }
    return keep;
}

// ------------------------------------------------------------------ 4.2 density

// Soft box shape with the top thinned: 0 outside, up to 1 inside.
float VolShape(int i, float3 p)
{
    float3 l = VolLocal(i, p), h = VolB(i).xyz, d = h - abs(l);
    if (min(d.x, min(d.y, d.z)) <= 0.0) return 0.0;
    float3 f = smoothstep(0.0, max(1e-3, h * VolD(i).w), d);
    float top = smoothstep(0.0, 1.0, (l.y + h.y) / max(2.0 * h.y, 1e-3));
    return f.x * f.y * f.z * (1.0 - VolF(i).z * top);
}

float MistNoise(float3 q)
{
    return SAMPLE_TEXTURE3D_LOD(_MistNoise, sampler_MistNoise, q, 0).r;
}

float VolDensity(int i, float3 p)
{
    float s = VolShape(i, p);
    if (s <= 0.0) return 0.0;
    float k = VolF(i).y;
    if (k <= 0.0) return VolC(i).w * s;
    float4 e = VolE(i);
    float3 q = p * e.w + e.xyz + VolF(i).w * 0.137;
    float n1 = MistNoise(q);
    float3 q2 = q * 2.0 + float3(0.37, VolF(i).x, 0.61) + (n1 - 0.5) * 0.35;   // the fine octave churns up, bent by the coarse
    float n = smoothstep(0.15, 0.85, n1 * 0.65 + MistNoise(q2) * 0.35);
    // The noise eats into the middle too, not just the edges, or the box reads as solid with fuzzy sides.
    return VolC(i).w * saturate((s * (1.0 + k * 0.3) - (1.0 - n) * k * 1.2) / max(1.0 - k * 0.6, 0.1));
}

// ------------------------------------------------------------------ 4.3 the ray's boxes and spans

static int   s_HitCount;
static float s_HitT0[MIST_MAX_HITS];
static float s_HitT1[MIST_MAX_HITS];
static int   s_HitId[MIST_MAX_HITS];
static uint  s_SpanMask;              // which hit slots the current span marches together
static int   s_LightCount;
static int   s_LightId[MIST_MAX_PIXEL_LIGHTS];

// Slab-test every box, clip to the scene, keep up to 8 hits sorted by where the ray enters them.
void MistGatherHits(float3 o, float3 dir, float sceneT)
{
    s_HitCount = 0;
    [loop] for (int i = 0; i < _MistVolumeCount; i++)
    {
        float2 r = VolHit(i, o, dir);
        r.y = min(r.y, sceneT);
        if (r.y <= r.x) continue;

        if (s_HitCount == MIST_MAX_HITS && r.x >= s_HitT0[MIST_MAX_HITS - 1]) continue;   // farther than everything kept

        // Insertion sort with fixed trip counts (a data-dependent while loop confuses the shader compiler).
        int pos = 0;
        [unroll] for (int j = 0; j < MIST_MAX_HITS; j++)
            if (j < s_HitCount && s_HitT0[j] <= r.x) pos = j + 1;
        [unroll] for (int k = MIST_MAX_HITS - 1; k > 0; k--)
        {
            if (k > pos)
            {
                s_HitT0[k] = s_HitT0[k - 1];
                s_HitT1[k] = s_HitT1[k - 1];
                s_HitId[k] = s_HitId[k - 1];
            }
        }
        s_HitT0[pos] = r.x;
        s_HitT1[pos] = r.y;
        s_HitId[pos] = i;
        s_HitCount = min(s_HitCount + 1, MIST_MAX_HITS);
    }
}

// Density of every box in the current span at p. Colour and glow are density-weighted averages.
float MistSpanDensity(float3 p, out float3 col, out float3 glow)
{
    float d = 0.0;
    col = 0.0;
    glow = 0.0;
    [loop] for (int k = 0; k < s_HitCount; k++)
    {
        if ((s_SpanMask & (1u << k)) == 0u) continue;
        int i = s_HitId[k];
        float di = VolDensity(i, p);
        if (di <= 0.0) continue;
        d += di;
        col += di * VolC(i).rgb;
        glow += di * VolD(i).rgb;
    }
    if (d > 0.0)
    {
        float inv = 1.0 / d;
        col *= inv;
        glow *= inv;
    }
    return d * MistClearKeep(p);
}

float MistSpanDensityOnly(float3 p)
{
    float3 c, g;
    return MistSpanDensity(p, c, g);
}

// Up to 4 point or spot lights whose range reaches the marched stretch of this ray.
void MistPickLights(float3 o, float3 dir, float tStart, float tEnd)
{
    s_LightCount = 0;
    if (_MistParams0.z < 0.5) return;
    [loop] for (int l = 0; l < _MistLightCount; l++)
    {
        if (s_LightCount >= MIST_MAX_PIXEL_LIGHTS) break;
        float4 P = _MistLights[l * 3];
        float t = clamp(dot(P.xyz - o, dir), tStart, tEnd);
        float3 toLight = P.xyz - (o + dir * t);
        if (dot(toLight, toLight) < P.w * P.w) s_LightId[s_LightCount++] = l;
    }
}

// Scene light reaching p. The mist scatters every way, so there is no phase term.
// Falloff: win = 1 - (d^2/r^2)^2, light = colour x win^2 / (d^2 + 1), spots x smoothstep(cosOuter, cosInner, angle).
float3 MistSceneLights(float3 p)
{
    float3 sum = 0.0;
    [loop] for (int k = 0; k < s_LightCount; k++)
    {
        int l = s_LightId[k];
        float4 P = _MistLights[l * 3], Q = _MistLights[l * 3 + 1], R = _MistLights[l * 3 + 2];
        float3 toLight = P.xyz - p;
        float d2 = dot(toLight, toLight);
        float r2 = P.w * P.w;
        if (d2 >= r2) continue;
        float ratio = d2 / r2;
        float win = 1.0 - ratio * ratio;
        float a = win * win / (d2 + 1.0);
        if (Q.w > -1.5)   // spot
        {
            float cd = dot(-toLight * rsqrt(max(d2, 1e-6)), R.xyz);
            a *= smoothstep(Q.w, R.w, cd);
        }
        sum += Q.rgb * a;
    }
    return sum;
}

#endif // SLAYD_MIST_COMMON_INCLUDED
