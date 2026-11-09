using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// A named look. It stores height and lift with the look but not width, depth or turn, so it fits any box.
    /// Colours are sRGB, as picked.
    /// </summary>
    [Serializable]
    public sealed class MistPreset
    {
        public string name = "Preset";
        public float height = 2f;
        public float lift = -0.3f;
        [ColorUsage(false)] public Color color = new Color(0.85f, 0.9f, 1f);
        public float density = 0.6f;
        public float softness = 0.45f;
        public float falloff = 0.6f;
        [ColorUsage(false)] public Color glowColor = new Color(0.4f, 0.8f, 1f);
        public float glowStrength = 0f;
        public float groundLight = 0f;
        public float noise = 0.65f;
        public float noiseScale = 2.5f;
        public float speed = 0.5f;
        public float direction = 60f;
        public float rise = 0.05f;
        public float churn = 0.3f;

        public MistPreset Clone() => (MistPreset)MemberwiseClone();

        public static MistPreset FromVolume(MistVolume v, float lift, string name)
        {
            return new MistPreset
            {
                name = name, height = v.height, lift = lift,
                color = v.color, density = v.density, softness = v.softness, falloff = v.falloff,
                glowColor = v.glowColor, glowStrength = v.glowStrength, groundLight = v.groundLight,
                noise = v.noise, noiseScale = v.noiseScale, speed = v.speed, direction = v.direction, rise = v.rise, churn = v.churn,
            };
        }

        public static MistPreset FromTemplate(MistTemplate t, string name)
        {
            return new MistPreset
            {
                name = name, height = t.height, lift = t.lift,
                color = t.color, density = t.density, softness = t.softness, falloff = t.falloff,
                glowColor = t.glowColor, glowStrength = t.glowStrength, groundLight = t.groundLight,
                noise = t.noise, noiseScale = t.noiseScale, speed = t.speed, direction = t.direction, rise = t.rise, churn = t.churn,
            };
        }

        /// <summary>Apply the look to a volume. The caller records the Undo and moves the base for the lift.</summary>
        public void ApplyTo(MistVolume v)
        {
            v.height = height;
            v.color = color; v.density = density; v.softness = softness; v.falloff = falloff;
            v.glowColor = glowColor; v.glowStrength = glowStrength; v.groundLight = groundLight;
            v.noise = noise; v.noiseScale = noiseScale; v.speed = speed; v.direction = direction; v.rise = rise; v.churn = churn;
            v.ClampAll();
        }
    }

    /// <summary>
    /// The project's presets, kept in ProjectSettings so a team shares them. Reset restores the starters.
    /// </summary>
    [FilePath("ProjectSettings/MistPresets.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MistPresetLibrary : ScriptableSingleton<MistPresetLibrary>
    {
        public List<MistPreset> presets = new List<MistPreset>();
        [SerializeField] bool m_Initialised;

        public static event Action Changed;

        void OnEnable()
        {
            if (!m_Initialised)
            {
                presets = Starters();
                m_Initialised = true;
            }
        }

        public void SaveNow()
        {
            Save(true);
            Changed?.Invoke();
        }

        public string UniqueName(string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) wanted = "Preset";
            string name = wanted;
            int n = 2;
            while (presets.Exists(p => p.name == name)) name = wanted + " " + n++;
            return name;
        }

        public void Add(MistPreset p) { p.name = UniqueName(p.name); presets.Add(p); SaveNow(); }

        public void Overwrite(int index, MistPreset from)
        {
            if (index < 0 || index >= presets.Count) return;
            string name = presets[index].name;
            var copy = from.Clone();
            copy.name = name;
            presets[index] = copy;
            SaveNow();
        }

        public void Rename(int index, string name)
        {
            if (index < 0 || index >= presets.Count || string.IsNullOrWhiteSpace(name)) return;
            if (presets[index].name == name) return;
            presets[index].name = UniqueName(name);
            SaveNow();
        }

        public void Delete(int index)
        {
            if (index < 0 || index >= presets.Count) return;
            presets.RemoveAt(index);
            SaveNow();
        }

        public void ResetToStarters()
        {
            presets = Starters();
            SaveNow();
        }

        /// <summary>The starter presets of section 5. Drift direction is 60 degrees unless stated.</summary>
        public static List<MistPreset> Starters()
        {
            return new List<MistPreset>
            {
                Make("Morning mist", 1.5f, -0.3f, new Color(0.92f, 0.95f, 1f), 0.45f, 0.6f, 0.7f, null, 0f, 0f, 0.6f, 3f, 0.3f, 60f, 0.02f, 0.2f),
                Make("Fog bank", 4f, -0.3f, new Color(0.86f, 0.88f, 0.9f), 2.2f, 0.65f, 0.4f, null, 0f, 0f, 0.5f, 5f, 0.25f, 60f, 0f, 0.15f),
                Make("Swamp fog", 1.2f, -0.4f, new Color(0.62f, 0.74f, 0.55f), 1.2f, 0.5f, 0.75f, null, 0f, 0f, 0.75f, 2f, 0.2f, 60f, 0.03f, 0.5f),
                Make("Toxic glow", 1.8f, -0.3f, new Color(0.45f, 0.95f, 0.35f), 0.9f, 0.5f, 0.6f, new Color(0.35f, 1f, 0.25f), 3f, 4f, 0.8f, 2f, 0.6f, 60f, 0.15f, 0.8f),
                Make("Violet haze", 2.5f, -0.2f, new Color(0.75f, 0.58f, 1f), 0.7f, 0.6f, 0.55f, new Color(0.7f, 0.42f, 1f), 2f, 3f, 0.65f, 3f, 0.35f, 60f, 0.1f, 0.4f),
                Make("Smoke drift", 3f, 0f, new Color(0.36f, 0.34f, 0.33f), 1.4f, 0.55f, 0.35f, null, 0f, 0f, 0.85f, 2.5f, 1.2f, 45f, 0.4f, 0.6f),
                Make("Lava steam", 3f, -0.2f, new Color(0.92f, 0.78f, 0.68f), 0.8f, 0.55f, 0.5f, new Color(1f, 0.45f, 0.15f), 2.5f, 5f, 0.8f, 2f, 0.4f, 60f, 0.6f, 0.7f),
            };
        }

        static MistPreset Make(string name, float height, float lift, Color color, float density, float soft, float falloff,
            Color? glow, float glowStrength, float light, float noise, float noiseScale, float speed, float direction, float rise, float churn)
        {
            return new MistPreset
            {
                name = name, height = height, lift = lift, color = color, density = density, softness = soft, falloff = falloff,
                glowColor = glow ?? new Color(0.4f, 0.8f, 1f), glowStrength = glowStrength, groundLight = light,
                noise = noise, noiseScale = noiseScale, speed = speed, direction = direction, rise = rise, churn = churn,
            };
        }
    }
}
