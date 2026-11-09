using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// The three groups of section 5 (Box, Look, Rolling) drawn over a SerializedObject. The same code edits
    /// selected volumes (prefix "") or the new-box template (prefix "template."). Lift and Turn of a volume
    /// are derived from its transform, so they go through callbacks rather than properties.
    /// </summary>
    public static class MistGroupsGUI
    {
        static readonly GUIContent s_Width = new GUIContent("Width", "Full size along the box's own X, metres.");
        static readonly GUIContent s_Depth = new GUIContent("Depth", "Full size along the box's own Z, metres.");
        static readonly GUIContent s_Height = new GUIContent("Height", "Full height, metres.");
        static readonly GUIContent s_Lift = new GUIContent("Lift", "Height of the base above the ground. Negative sinks it so it hugs the ground.");
        static readonly GUIContent s_Turn = new GUIContent("Turn", "Yaw in degrees.");
        static readonly GUIContent s_Colour = new GUIContent("Colour", "Tint of the light the mist scatters.");
        static readonly GUIContent s_Thickness = new GUIContent("Thickness", "Extinction per metre at the densest point.");
        static readonly GUIContent s_Soft = new GUIContent("Soft sides", "How far in from each side the mist fades.");
        static readonly GUIContent s_Thin = new GUIContent("Thin at top", "How much thinner the mist is toward the top.");
        static readonly GUIContent s_GlowColour = new GUIContent("Glow colour", "Colour the mist gives off itself.");
        static readonly GUIContent s_Glow = new GUIContent("Glow", "How strongly the mist glows. Lights nothing around it.");
        static readonly GUIContent s_GroundLight = new GUIContent("Ground light", "Intensity of a real point light inside the box, so glowing mist lights the ground. 0 is none.");
        static readonly GUIContent s_Billows = new GUIContent("Billows", "How much noise breaks the mist into billows.");
        static readonly GUIContent s_BillowSize = new GUIContent("Billow size", "Size of the billows, metres.");
        static readonly GUIContent s_Drift = new GUIContent("Drift", "Drift speed, metres per second.");
        static readonly GUIContent s_DriftDir = new GUIContent("Drift direction", "Heading in degrees: 0 toward +Z, 90 toward +X.");
        static readonly GUIContent s_Rise = new GUIContent("Rise", "Upward drift, metres per second.");
        static readonly GUIContent s_Churn = new GUIContent("Churn", "How fast the billows roll over in place.");

        static bool s_BoxOpen = true, s_LookOpen = true, s_RollingOpen = true;

        /// <summary>
        /// Draw the groups. volumes is null when editing the template. Returns true when something changed.
        /// </summary>
        public static bool Draw(SerializedObject so, string prefix, MistVolume[] volumes)
        {
            bool isTemplate = volumes == null || volumes.Length == 0;
            so.Update();
            EditorGUI.BeginChangeCheck();

            s_BoxOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_BoxOpen, "Box");
            if (s_BoxOpen)
            {
                EditorGUI.indentLevel++;
                Slider(so, prefix + "width", s_Width, MistRanges.MinWidth, MistRanges.UiMaxWidth);
                Slider(so, prefix + "depth", s_Depth, MistRanges.MinWidth, MistRanges.UiMaxWidth);
                Slider(so, prefix + "height", s_Height, MistRanges.MinHeight, MistRanges.UiMaxHeight);
                if (isTemplate)
                {
                    Slider(so, prefix + "lift", s_Lift, MistRanges.UiMinLift, MistRanges.UiMaxLift);
                }
                else
                {
                    LiftField(volumes);
                    TurnField(volumes);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            s_LookOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_LookOpen, "Look");
            if (s_LookOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(so.FindProperty(prefix + "color"), s_Colour);
                Slider(so, prefix + "density", s_Thickness, MistRanges.MinDensity, MistRanges.UiMaxDensity);
                Percent(so, prefix + "softness", s_Soft);
                Percent(so, prefix + "falloff", s_Thin);
                EditorGUILayout.PropertyField(so.FindProperty(prefix + "glowColor"), s_GlowColour);
                Slider(so, prefix + "glowStrength", s_Glow, MistRanges.MinGlow, MistRanges.UiMaxGlow);
                Slider(so, prefix + "groundLight", s_GroundLight, MistRanges.MinLight, MistRanges.UiMaxLight);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            s_RollingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_RollingOpen, "Rolling");
            if (s_RollingOpen)
            {
                EditorGUI.indentLevel++;
                var noise = so.FindProperty(prefix + "noise");
                Slider(so, prefix + "noise", s_Billows, 0f, 1f);
                if (!noise.hasMultipleDifferentValues && noise.floatValue <= 0f)
                    EditorGUILayout.LabelField(" ", "smooth", EditorStyles.miniLabel);
                Slider(so, prefix + "noiseScale", s_BillowSize, MistRanges.UiMinNoiseScale, MistRanges.UiMaxNoiseScale);
                Slider(so, prefix + "speed", s_Drift, MistRanges.MinSpeed, MistRanges.UiMaxSpeed);
                Slider(so, prefix + "direction", s_DriftDir, 0f, 359f);
                Slider(so, prefix + "rise", s_Rise, MistRanges.UiMinRise, MistRanges.UiMaxRise);
                Slider(so, prefix + "churn", s_Churn, MistRanges.MinChurn, MistRanges.UiMaxChurn);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            bool changed = EditorGUI.EndChangeCheck();
            so.ApplyModifiedProperties();

            // "Update only on release": while a slider is held, the renderer keeps the last packed data.
            var settings = MistEditorSettings.instance;
            MistRendererFeature.PreviewPaused = settings.updateOnReleaseOnly && GUIUtility.hotControl != 0;
            return changed;
        }

        static void Slider(SerializedObject so, string path, GUIContent label, float min, float max)
        {
            var prop = so.FindProperty(path);
            if (prop == null) { EditorGUILayout.HelpBox("Missing property " + path, MessageType.Error); return; }
            EditorGUILayout.Slider(prop, min, max, label);
        }

        static void Percent(SerializedObject so, string path, GUIContent label)
        {
            var prop = so.FindProperty(path);
            if (prop == null) return;
            EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float pct = EditorGUILayout.Slider(label, Mathf.Round(prop.floatValue * 100f), 0f, 100f);
            if (EditorGUI.EndChangeCheck()) prop.floatValue = Mathf.Clamp01(pct / 100f);
            EditorGUI.showMixedValue = false;
        }

        static void LiftField(MistVolume[] volumes)
        {
            float lift = MistSceneUtil.LiftOf(volumes[0]);
            bool mixed = false;
            for (int i = 1; i < volumes.Length && !mixed; i++) mixed = Mathf.Abs(MistSceneUtil.LiftOf(volumes[i]) - lift) > 1e-3f;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            float newLift = EditorGUILayout.Slider(s_Lift, lift, MistRanges.UiMinLift, MistRanges.UiMaxLift);
            if (EditorGUI.EndChangeCheck())
                foreach (var v in volumes) MistSceneUtil.SetLift(v, newLift);
            EditorGUI.showMixedValue = false;
        }

        static void TurnField(MistVolume[] volumes)
        {
            float yaw = volumes[0].YawDegrees;
            bool mixed = false;
            for (int i = 1; i < volumes.Length && !mixed; i++) mixed = Mathf.Abs(Mathf.DeltaAngle(volumes[i].YawDegrees, yaw)) > 1e-3f;
            EditorGUI.showMixedValue = mixed;
            EditorGUI.BeginChangeCheck();
            float newYaw = EditorGUILayout.Slider(s_Turn, Mathf.Round(yaw * 10f) / 10f, 0f, 359f);
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var v in volumes)
                {
                    Undo.RecordObject(v.transform, "Turn Mist Volume");
                    v.SetYaw(newYaw);
                }
            }
            EditorGUI.showMixedValue = false;
        }

        // ------------------------------------------------------------------ per-box buttons and presets

        /// <summary>Hide/Show, Use for new boxes, and a preset picker for the selected volumes.</summary>
        public static void DrawVolumeActions(MistVolume[] volumes)
        {
            if (volumes == null || volumes.Length == 0) return;
            bool anyVisible = false;
            foreach (var v in volumes) anyVisible |= !v.hidden;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(anyVisible ? "Hide" : "Show"))
                {
                    Undo.RecordObjects(volumes, anyVisible ? "Hide Mist Volume" : "Show Mist Volume");
                    foreach (var v in volumes) { v.hidden = anyVisible; v.SyncLight(); EditorUtility.SetDirty(v); }
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button(new GUIContent("Use for new boxes", "Copy this box's look, size and lift into the new-box template.")))
                {
                    var settings = MistEditorSettings.instance;
                    settings.template.CopyFrom(volumes[0], MistSceneUtil.LiftOf(volumes[0]));
                    settings.SaveNow();
                }
            }

            DrawPresetPicker(volumes);
        }

        static int s_PickerIndex;

        static void DrawPresetPicker(MistVolume[] volumes)
        {
            var lib = MistPresetLibrary.instance;
            var names = new string[lib.presets.Count];
            for (int i = 0; i < names.Length; i++) names[i] = lib.presets[i].name;
            s_PickerIndex = Mathf.Clamp(s_PickerIndex, 0, Mathf.Max(0, names.Length - 1));

            using (new EditorGUILayout.HorizontalScope())
            {
                s_PickerIndex = EditorGUILayout.Popup(new GUIContent("Preset"), s_PickerIndex, names);
                using (new EditorGUI.DisabledScope(names.Length == 0))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(60f)))
                        ApplyPreset(lib.presets[s_PickerIndex], volumes);
                }
                if (GUILayout.Button(new GUIContent("Save as new", "Save this box's look as a new preset."), GUILayout.Width(90f)))
                {
                    lib.Add(MistPreset.FromVolume(volumes[0], MistSceneUtil.LiftOf(volumes[0]), volumes[0].name));
                    s_PickerIndex = lib.presets.Count - 1;
                }
            }
        }

        /// <summary>Apply a preset to volumes: the look, the height, and the base moved for the lift. One Undo step.</summary>
        public static void ApplyPreset(MistPreset preset, MistVolume[] volumes)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            foreach (var v in volumes)
            {
                Undo.RecordObject(v, "Apply Mist Preset");
                preset.ApplyTo(v);
                MistSceneUtil.SetLift(v, preset.lift);
                v.SyncLight();
                EditorUtility.SetDirty(v);
            }
            Undo.CollapseUndoOperations(group);
            Undo.SetCurrentGroupName("Apply Mist Preset " + preset.name);
            SceneView.RepaintAll();
        }
    }
}
