using System.Collections.Generic;
using UnityEngine;

namespace SlaydGames.Mist
{
    /// <summary>
    /// One box of volumetric mist (section 1 of the brief). The box's base (bottom centre) is the
    /// transform position and its yaw is the transform's Y rotation. Tilt and scale are ignored: a mist
    /// box only ever turns about the vertical axis, and its size lives in <see cref="width"/>,
    /// <see cref="height"/> and <see cref="depth"/> so it can be clamped.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Mist Volume")]
    [HelpURL("https://github.com/EdSpurrier/Mist")]
    public sealed class MistVolume : MonoBehaviour
    {
        public const string LightObjectName = "Mist Light";

        [Header("Box")]
        [Tooltip("Full size along the box's own X axis, in metres.")]
        public float width = 8f;
        [Tooltip("Full height, in metres. The base sits at the transform position.")]
        public float height = 2f;
        [Tooltip("Full size along the box's own Z axis, in metres.")]
        public float depth = 8f;

        [Header("Look")]
        [Tooltip("Tint of the light the mist scatters.")]
        [ColorUsage(false)] public Color color = new Color(0.85f, 0.9f, 1f);
        [Tooltip("Thickness: extinction per metre where the mist is densest.")]
        public float density = 0.6f;
        [Tooltip("Soft sides: how far in from each side the mist fades, as a share of the half size. 0 is hard walls.")]
        public float softness = 0.45f;
        [Tooltip("Thin at top: 0 is even, 1 leaves nothing at the top.")]
        public float falloff = 0.6f;
        [Tooltip("Colour of the light the mist gives off itself. It follows the billows and lights nothing around it.")]
        [ColorUsage(false)] public Color glowColor = new Color(0.4f, 0.8f, 1f);
        [Tooltip("How strongly the mist glows.")]
        public float glowStrength = 0f;
        [Tooltip("Ground light: intensity of a real point light kept inside the box so glowing mist lights the ground. 0 is none.")]
        public float groundLight = 0f;

        [Header("Rolling")]
        [Tooltip("Billows: how much noise breaks the mist up. 0 is smooth.")]
        public float noise = 0.65f;
        [Tooltip("Billow size, in metres.")]
        public float noiseScale = 2.5f;
        [Tooltip("Drift speed, metres per second.")]
        public float speed = 0.5f;
        [Tooltip("Drift heading in degrees: 0 is toward +Z, 90 toward +X.")]
        public float direction = 60f;
        [Tooltip("Upward drift, metres per second.")]
        public float rise = 0.05f;
        [Tooltip("How fast the billows roll over in place.")]
        public float churn = 0.3f;

        [Tooltip("Kept but not drawn.")]
        public bool hidden;

        // Which billows this box shows. It must be stable across reloads and visibility changes, so it is
        // chosen once when the volume is created and serialized (never an instance id or draw order).
        [SerializeField, HideInInspector] int seed;

        public int Seed => seed;

        // ---------------------------------------------------------------- registry

        static readonly List<MistVolume> s_Active = new List<MistVolume>();
        static readonly HashSet<Light> s_OwnLights = new HashSet<Light>();

        /// <summary>Every enabled mist volume in loaded scenes, in no particular order.</summary>
        public static IReadOnlyList<MistVolume> Active => s_Active;

        /// <summary>True for the point lights the volumes keep for their glow, so the mist does not scatter its own light.</summary>
        public static bool IsMistLight(Light l) => l != null && s_OwnLights.Contains(l);

        // ---------------------------------------------------------------- derived values

        public Vector3 BasePosition => transform.position;
        public Vector3 Center => transform.position + Vector3.up * (height * 0.5f);
        public float YawDegrees => transform.eulerAngles.y;
        public float YawRadians => transform.eulerAngles.y * Mathf.Deg2Rad;
        public bool HasSize => width > 0f && height > 0f && depth > 0f;

        /// <summary>World-space axis-aligned bounds of the turned box (section 3, step 1).</summary>
        public Bounds WorldBounds
        {
            get
            {
                float yaw = YawRadians;
                float c = Mathf.Abs(Mathf.Cos(yaw)), s = Mathf.Abs(Mathf.Sin(yaw));
                float hw = width * 0.5f, hd = depth * 0.5f;
                float ex = c * hw + s * hd;
                float ez = s * hw + c * hd;
                return new Bounds(Center, new Vector3(ex * 2f, height, ez * 2f));
            }
        }

        /// <summary>Set the yaw, keeping the box upright.</summary>
        public void SetYaw(float degrees)
        {
            transform.rotation = Quaternion.Euler(0f, MistRanges.Wrap360(degrees), 0f);
        }

        /// <summary>Pick new billows. Called when a volume is created or duplicated.</summary>
        public void NewSeed()
        {
            seed = Random.Range(1, int.MaxValue);
        }

        /// <summary>Clamp every value to its allowed range (section 1).</summary>
        public void ClampAll()
        {
            width = Mathf.Clamp(width, MistRanges.MinWidth, MistRanges.MaxWidth);
            depth = Mathf.Clamp(depth, MistRanges.MinWidth, MistRanges.MaxWidth);
            height = Mathf.Clamp(height, MistRanges.MinHeight, MistRanges.MaxHeight);
            density = Mathf.Clamp(density, MistRanges.MinDensity, MistRanges.MaxDensity);
            softness = Mathf.Clamp01(softness);
            falloff = Mathf.Clamp01(falloff);
            glowStrength = Mathf.Clamp(glowStrength, MistRanges.MinGlow, MistRanges.MaxGlow);
            groundLight = Mathf.Clamp(groundLight, MistRanges.MinLight, MistRanges.MaxLight);
            noise = Mathf.Clamp01(noise);
            noiseScale = Mathf.Clamp(noiseScale, MistRanges.MinNoiseScale, MistRanges.MaxNoiseScale);
            speed = Mathf.Clamp(speed, MistRanges.MinSpeed, MistRanges.MaxSpeed);
            direction = MistRanges.Wrap360(direction);
            rise = Mathf.Clamp(rise, MistRanges.MinRise, MistRanges.MaxRise);
            churn = Mathf.Clamp(churn, MistRanges.MinChurn, MistRanges.MaxChurn);
            color.a = 1f;
            glowColor.a = 1f;
        }

        // ---------------------------------------------------------------- lifecycle

        void Reset()
        {
            NewSeed();
        }

        void OnEnable()
        {
            if (seed == 0) NewSeed();
            ClampAll();
            s_Active.Add(this);
            SyncLight();
        }

        void OnDisable()
        {
            s_Active.Remove(this);
            DestroyLight();
        }

        void OnValidate()
        {
            if (seed == 0) NewSeed();
            ClampAll();
            if (isActiveAndEnabled) SyncLight();
        }

        void LateUpdate()
        {
            // In the editor, OnValidate covers Inspector edits. While playing, values may change from code.
            if (Application.isPlaying) SyncLight();
        }

        // ---------------------------------------------------------------- glow light

        Light m_Light;

        /// <summary>The point light this box keeps when <see cref="groundLight"/> is above zero, or null.</summary>
        public Light GlowLight => m_Light;

        /// <summary>
        /// Keep a real point light inside the box when light > 0 (section 5, "Glow that lights things").
        /// It sits at base + min(height/2, 1.5), takes the glow colour, intensity = light and
        /// range = max(width, depth) * 0.6 + 2. The light object is hidden and never saved; it is rebuilt on load.
        /// </summary>
        public void SyncLight()
        {
            if (groundLight <= 0f)
            {
                DestroyLight();
                return;
            }

            if (m_Light == null)
            {
                var existing = transform.Find(LightObjectName);
                if (existing != null) m_Light = existing.GetComponent<Light>();
                if (m_Light == null)
                {
                    var go = new GameObject(LightObjectName) { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(transform, false);
                    m_Light = go.AddComponent<Light>();
                    m_Light.type = LightType.Point;
                    m_Light.shadows = LightShadows.None;
                }
                s_OwnLights.Add(m_Light);
            }

            m_Light.transform.localPosition = new Vector3(0f, Mathf.Min(height * 0.5f, 1.5f), 0f);
            m_Light.transform.localRotation = Quaternion.identity;
            m_Light.color = glowColor;
            m_Light.intensity = groundLight;
            m_Light.range = Mathf.Max(width, depth) * 0.6f + 2f;
            m_Light.enabled = !hidden;
        }

        void DestroyLight()
        {
            if (m_Light == null)
            {
                var existing = transform.Find(LightObjectName);
                if (existing != null) m_Light = existing.GetComponent<Light>();
            }
            if (m_Light == null) return;

            s_OwnLights.Remove(m_Light);
            var go = m_Light.gameObject;
            m_Light = null;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
