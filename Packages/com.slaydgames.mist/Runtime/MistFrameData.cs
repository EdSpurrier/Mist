using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlaydGames.Mist
{
    /// <summary>
    /// The per-frame CPU work of section 3: pick the volumes a camera can see, sort them by distance, and
    /// pack them, the scene lights that reach them and the clear zones into float4 arrays for the shader.
    /// One instance is reused by the render pass to avoid allocations.
    /// </summary>
    public sealed class MistFrameData
    {
        public const int MaxVolumes = 64;        // nearest volumes marched
        public const int MaxShadeVolumes = 16;   // nearest volumes that shade the ground
        public const int MaxLights = 16;         // scene lights offered to the shader (it picks 4 per pixel)
        public const int MaxClearZones = 8;
        public const int FloatsPerVolume = 6;    // A..F in section 3
        public const int FloatsPerLight = 3;
        public const int FloatsPerClear = 2;

        public readonly Vector4[] volumes = new Vector4[MaxVolumes * FloatsPerVolume];
        public readonly Vector4[] lights = new Vector4[MaxLights * FloatsPerLight];
        public readonly Vector4[] clears = new Vector4[MaxClearZones * FloatsPerClear];
        public int volumeCount, lightCount, clearCount;

        /// <summary>The volumes that were kept this frame, nearest first. Useful for debugging and tests.</summary>
        public readonly List<MistVolume> kept = new List<MistVolume>(MaxVolumes);

        readonly Plane[] m_Planes = new Plane[6];
        readonly List<Entry> m_Entries = new List<Entry>(64);
        readonly List<LightEntry> m_LightEntries = new List<LightEntry>(32);
        Bounds m_KeptBounds;

        struct Entry { public float distance; public MistVolume volume; }
        struct LightEntry { public float distance; public int index; }

        static readonly Comparison<Entry> s_ByDistance = (a, b) => a.distance.CompareTo(b.distance);
        static readonly Comparison<LightEntry> s_LightsByDistance = (a, b) => a.distance.CompareTo(b.distance);

        /// <summary>Pick, sort and pack the volumes for this camera. Returns false when there is nothing to draw.</summary>
        public bool CollectVolumes(Camera camera, double time)
        {
            volumeCount = 0;
            kept.Clear();
            m_Entries.Clear();

            GeometryUtility.CalculateFrustumPlanes(camera, m_Planes);
            Vector3 eye = camera.transform.position;

            var active = MistVolume.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var v = active[i];
                if (v == null || !v.isActiveAndEnabled || v.hidden || v.density <= 0f || !v.HasSize) continue;
                if (!GeometryUtility.TestPlanesAABB(m_Planes, v.WorldBounds)) continue;
                m_Entries.Add(new Entry { distance = (v.Center - eye).sqrMagnitude, volume = v });
            }
            if (m_Entries.Count == 0) return false;

            m_Entries.Sort(s_ByDistance);
            volumeCount = Mathf.Min(MaxVolumes, m_Entries.Count);

            for (int i = 0; i < volumeCount; i++)
            {
                var v = m_Entries[i].volume;
                kept.Add(v);
                PackVolume(v, time, volumes, i * FloatsPerVolume);
                var b = v.WorldBounds;
                if (i == 0) m_KeptBounds = b; else m_KeptBounds.Encapsulate(b);
            }
            return true;
        }

        /// <summary>Pack one volume into six float4s (section 3, step 3). Colours go out linear.</summary>
        public static void PackVolume(MistVolume v, double time, Vector4[] dst, int at)
        {
            float yaw = v.YawRadians;
            float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);

            // Hard clamps at pack time too, so values set from code without OnValidate still hold.
            float width = Mathf.Clamp(v.width, MistRanges.MinWidth, MistRanges.MaxWidth);
            float depth = Mathf.Clamp(v.depth, MistRanges.MinWidth, MistRanges.MaxWidth);
            float height = Mathf.Clamp(v.height, MistRanges.MinHeight, MistRanges.MaxHeight);
            float density = Mathf.Clamp(v.density, MistRanges.MinDensity, MistRanges.MaxDensity);
            float noiseScale = Mathf.Clamp(v.noiseScale, MistRanges.MinNoiseScale, MistRanges.MaxNoiseScale);
            float glowStrength = Mathf.Clamp(v.glowStrength, MistRanges.MinGlow, MistRanges.MaxGlow);

            Vector3 center = v.transform.position + Vector3.up * (height * 0.5f);
            Color color = v.color.linear;
            Color glow = v.glowColor.linear * glowStrength;

            // Noise offsets in texture units, wrapped to 0..1 so precision holds in long sessions (step 2).
            double scale = Mathf.Max(0.05f, noiseScale) * MistNoiseTexture.BillowsPerRepeat;
            double heading = v.direction * Mathf.Deg2Rad;
            float driftX = Wrap(-Math.Sin(heading) * v.speed * time / scale);
            float driftY = Wrap(-v.rise * time / scale);
            float driftZ = Wrap(-Math.Cos(heading) * v.speed * time / scale);
            float churnPhase = Wrap(-v.churn * time / scale);

            // The seed is sent as a small float so it keeps its precision as a texture offset.
            float seed = v.Seed & 0x3FFF;

            dst[at + 0] = new Vector4(center.x, center.y, center.z, cos);                        // A
            dst[at + 1] = new Vector4(width * 0.5f, height * 0.5f, depth * 0.5f, sin);            // B
            dst[at + 2] = new Vector4(color.r, color.g, color.b, density);                        // C
            dst[at + 3] = new Vector4(glow.r, glow.g, glow.b, Mathf.Clamp01(v.softness));         // D
            dst[at + 4] = new Vector4(driftX, driftY, driftZ, (float)(1.0 / scale));              // E
            dst[at + 5] = new Vector4(churnPhase, Mathf.Clamp01(v.noise), Mathf.Clamp01(v.falloff), seed); // F
        }

        static float Wrap(double x) => (float)(x - Math.Floor(x));

        /// <summary>
        /// Pick the point and spot lights that reach the kept volumes, nearest to the camera first, and pack
        /// them as (position, range), (linear colour x intensity, cos outer or -2 for point), (spot direction, cos inner).
        /// The volumes' own glow lights are skipped: the glow term already stands for them.
        /// </summary>
        public void CollectLights(Camera camera, NativeArray<VisibleLight> visibleLights, int mainLightIndex)
        {
            lightCount = 0;
            m_LightEntries.Clear();
            if (volumeCount == 0 || !visibleLights.IsCreated) return;

            Vector3 eye = camera.transform.position;
            for (int i = 0; i < visibleLights.Length; i++)
            {
                if (i == mainLightIndex) continue;
                var vl = visibleLights[i];
                if (vl.lightType != LightType.Point && vl.lightType != LightType.Spot) continue;
                if (vl.light != null && MistVolume.IsMistLight(vl.light)) continue;
                Vector3 pos = vl.localToWorldMatrix.GetColumn(3);
                if (m_KeptBounds.SqrDistance(pos) > vl.range * vl.range) continue;
                m_LightEntries.Add(new LightEntry { distance = (pos - eye).sqrMagnitude, index = i });
            }
            if (m_LightEntries.Count == 0) return;

            m_LightEntries.Sort(s_LightsByDistance);
            lightCount = Mathf.Min(MaxLights, m_LightEntries.Count);
            for (int k = 0; k < lightCount; k++)
            {
                var vl = visibleLights[m_LightEntries[k].index];
                Vector3 pos = vl.localToWorldMatrix.GetColumn(3);
                Vector3 dir = ((Vector3)vl.localToWorldMatrix.GetColumn(2)).normalized;
                Color c = vl.finalColor;   // linear, intensity included
                float cosOuter = -2f, cosInner = -2f;
                if (vl.lightType == LightType.Spot)
                {
                    cosOuter = Mathf.Cos(vl.spotAngle * 0.5f * Mathf.Deg2Rad);
                    float inner = vl.light != null ? vl.light.innerSpotAngle : vl.spotAngle * 0.8f;
                    cosInner = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad);
                    if (cosInner <= cosOuter) cosInner = cosOuter + 1e-3f;
                }
                int at = k * FloatsPerLight;
                lights[at + 0] = new Vector4(pos.x, pos.y, pos.z, vl.range);
                lights[at + 1] = new Vector4(c.r, c.g, c.b, cosOuter);
                lights[at + 2] = new Vector4(dir.x, dir.y, dir.z, cosInner);
            }
        }

        /// <summary>Pack the clear zones as (x, z, heading x, heading z), (cos half angle or -2 for a disc, range, soft, strength).</summary>
        public void CollectClearZones()
        {
            clearCount = 0;
            var active = MistClearZone.Active;
            for (int i = 0; i < active.Count && clearCount < MaxClearZones; i++)
            {
                var z = active[i];
                if (z == null || !z.isActiveAndEnabled || z.strength <= 0f) continue;
                Vector3 p = z.transform.position;
                Vector2 h = z.Heading;
                float cosHalf = z.IsDisc ? -2f : Mathf.Cos(z.halfAngle * Mathf.Deg2Rad);
                int at = clearCount * FloatsPerClear;
                clears[at + 0] = new Vector4(p.x, p.z, h.x, h.y);
                clears[at + 1] = new Vector4(cosHalf, z.range, Mathf.Clamp01(z.soft), Mathf.Clamp01(z.strength));
                clearCount++;
            }
        }
    }
}
