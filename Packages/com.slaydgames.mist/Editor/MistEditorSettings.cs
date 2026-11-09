using System;
using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    public enum MistHandleMode { Move, Turn, Size }

    /// <summary>
    /// The look, size and lift new boxes are created with. Field names match <see cref="MistVolume"/> so the
    /// same Inspector code edits either one.
    /// </summary>
    [Serializable]
    public sealed class MistTemplate
    {
        public float width = 8f;
        public float height = 2f;
        public float depth = 8f;
        [Tooltip("Height of the base above the ground. Slightly sunk by default, so the box hugs the ground.")]
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

        /// <summary>Write the template into a volume (everything but position and yaw).</summary>
        public void ApplyTo(MistVolume v)
        {
            v.width = width; v.height = height; v.depth = depth;
            v.color = color; v.density = density; v.softness = softness; v.falloff = falloff;
            v.glowColor = glowColor; v.glowStrength = glowStrength; v.groundLight = groundLight;
            v.noise = noise; v.noiseScale = noiseScale; v.speed = speed; v.direction = direction; v.rise = rise; v.churn = churn;
            v.ClampAll();
        }

        /// <summary>"Use for new boxes": copy a volume's look, size and lift.</summary>
        public void CopyFrom(MistVolume v, float volumeLift)
        {
            width = v.width; height = v.height; depth = v.depth; lift = volumeLift;
            color = v.color; density = v.density; softness = v.softness; falloff = v.falloff;
            glowColor = v.glowColor; glowStrength = v.glowStrength; groundLight = v.groundLight;
            noise = v.noise; noiseScale = v.noiseScale; speed = v.speed; direction = v.direction; rise = v.rise; churn = v.churn;
        }

        public void ApplyPreset(MistPreset p)
        {
            height = p.height; lift = p.lift;
            color = p.color; density = p.density; softness = p.softness; falloff = p.falloff;
            glowColor = p.glowColor; glowStrength = p.glowStrength; groundLight = p.groundLight;
            noise = p.noise; noiseScale = p.noiseScale; speed = p.speed; direction = p.direction; rise = p.rise; churn = p.churn;
        }
    }

    /// <summary>
    /// Per-user editor state, remembered between sessions in UserSettings: the new-box template, the handle
    /// mode, the placing and hide-all toggles.
    /// </summary>
    [FilePath("UserSettings/MistEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MistEditorSettings : ScriptableSingleton<MistEditorSettings>
    {
        public MistTemplate template = new MistTemplate();
        public MistHandleMode handleMode = MistHandleMode.Move;
        [Tooltip("Clicking the ground in the Scene view, with the Mist tool active, places a new box.")]
        public bool placeMode = true;
        [Tooltip("Skip drawing the mist in Scene views, to see the ground under it. Outlines stay.")]
        public bool hideAllMist;
        [Tooltip("Keep the Scene view repainting so the mist rolls in edit mode.")]
        public bool animateSceneView = true;
        [Tooltip("Only update the mist when a slider is released, for slow machines.")]
        public bool updateOnReleaseOnly;
        [Tooltip("Show the drift heading and size readout next to the selected box.")]
        public bool showHandleLabels = true;

        /// <summary>Raised after <see cref="SaveNow"/> so panels can refresh.</summary>
        public static event Action Changed;

        public void SaveNow()
        {
            MistRendererFeature.HideInSceneView = hideAllMist;
            Save(true);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        void OnEnable()
        {
            MistRendererFeature.HideInSceneView = hideAllMist;
        }
    }
}
