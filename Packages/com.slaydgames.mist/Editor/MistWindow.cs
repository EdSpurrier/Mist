using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// The Mist panel (section 5): edits the selected boxes, or the new-box template when nothing is
    /// selected, and manages presets. Also holds the Scene view options.
    /// </summary>
    public sealed class MistWindow : EditorWindow
    {
        static readonly GUIContent[] s_Modes = { new GUIContent("Move"), new GUIContent("Turn"), new GUIContent("Size") };

        Vector2 m_Scroll;
        int m_PresetIndex;
        string m_PresetName = "";
        bool m_Renaming;
        bool m_PresetsOpen = true;
        bool m_OptionsOpen;

        [MenuItem("Window/Rendering/Mist Volumes")]
        [MenuItem("Tools/Mist/Mist Window")]
        public static void Open()
        {
            var w = GetWindow<MistWindow>("Mist");
            w.minSize = new Vector2(300f, 400f);
            w.Show();
        }

        void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            Undo.undoRedoPerformed += Repaint;
            MistEditorSettings.Changed += Repaint;
            MistPresetLibrary.Changed += Repaint;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
            MistEditorSettings.Changed -= Repaint;
            MistPresetLibrary.Changed -= Repaint;
        }

        void OnGUI()
        {
            var settings = MistEditorSettings.instance;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            DrawToolbar(settings);
            EditorGUILayout.Space();

            var volumes = SelectedVolumes();
            if (volumes.Length > 0)
            {
                EditorGUILayout.LabelField(volumes.Length == 1 ? "Editing: " + volumes[0].name : $"Editing: {volumes.Length} boxes", EditorStyles.boldLabel);
                var so = new SerializedObject(volumes);
                if (MistGroupsGUI.Draw(so, "", volumes))
                    foreach (var v in volumes) v.SyncLight();
                EditorGUILayout.Space();
                MistGroupsGUI.DrawVolumeActions(volumes);
            }
            else
            {
                EditorGUILayout.LabelField("Editing: new-box template", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Nothing selected. These values are used for the next box placed with the Mist tool.", MessageType.None);
                var so = new SerializedObject(settings);
                if (MistGroupsGUI.Draw(so, "template.", null)) settings.SaveNow();
                if (GUILayout.Button(new GUIContent("Reset template to defaults")))
                {
                    settings.template = new MistTemplate();
                    settings.SaveNow();
                }
            }

            EditorGUILayout.Space();
            DrawPresets(settings, volumes);

            EditorGUILayout.Space();
            DrawOptions(settings);

            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar(MistEditorSettings settings)
        {
            bool changed = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool toolOn = MistTool.IsActive;
                bool newToolOn = GUILayout.Toggle(toolOn, new GUIContent("Mist Tool", "Activate the Scene view tool."), EditorStyles.toolbarButton);
                if (newToolOn != toolOn)
                {
                    if (newToolOn) MistTool.Activate();
                    else UnityEditor.EditorTools.ToolManager.RestorePreviousPersistentTool();
                }
                using (new EditorGUI.DisabledScope(!MistTool.IsActive))
                {
                    bool place = GUILayout.Toggle(settings.placeMode, new GUIContent("Place", "Clicking the ground places a new box."), EditorStyles.toolbarButton);
                    if (place != settings.placeMode) { settings.placeMode = place; changed = true; }
                }
                GUILayout.Space(8f);
                EditorGUI.BeginChangeCheck();
                var mode = (MistHandleMode)GUILayout.Toolbar((int)settings.handleMode, s_Modes, EditorStyles.toolbarButton);
                if (EditorGUI.EndChangeCheck()) { settings.handleMode = mode; changed = true; }
                GUILayout.FlexibleSpace();
                bool hide = GUILayout.Toggle(settings.hideAllMist, new GUIContent("Hide all mist", "Skip drawing the mist in Scene views."), EditorStyles.toolbarButton);
                if (hide != settings.hideAllMist) { settings.hideAllMist = hide; changed = true; }
            }
            if (changed) settings.SaveNow();
        }

        void DrawPresets(MistEditorSettings settings, MistVolume[] volumes)
        {
            var lib = MistPresetLibrary.instance;
            m_PresetsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(m_PresetsOpen, "Presets");
            if (m_PresetsOpen)
            {
                EditorGUI.indentLevel++;
                var names = new string[lib.presets.Count];
                for (int i = 0; i < names.Length; i++) names[i] = lib.presets[i].name;
                m_PresetIndex = Mathf.Clamp(m_PresetIndex, 0, Mathf.Max(0, names.Length - 1));
                bool any = names.Length > 0;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    m_PresetIndex = EditorGUILayout.Popup(m_PresetIndex, names);
                    if (EditorGUI.EndChangeCheck()) m_Renaming = false;
                    using (new EditorGUI.DisabledScope(!any))
                    {
                        if (GUILayout.Button(new GUIContent("Apply", "Apply the preset to the selected boxes, or to the template."), GUILayout.Width(60f)))
                        {
                            var preset = lib.presets[m_PresetIndex];
                            if (volumes.Length > 0) MistGroupsGUI.ApplyPreset(preset, volumes);
                            else { settings.template.ApplyPreset(preset); settings.SaveNow(); }
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    m_PresetName = EditorGUILayout.TextField(m_Renaming ? "New name" : "Name", m_PresetName);
                    if (m_Renaming)
                    {
                        if (GUILayout.Button("OK", GUILayout.Width(40f))) { lib.Rename(m_PresetIndex, m_PresetName); m_Renaming = false; }
                        if (GUILayout.Button("Cancel", GUILayout.Width(60f))) m_Renaming = false;
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Save as new", "Save the current look (selected box or template) as a new preset.")))
                    {
                        lib.Add(CurrentLook(settings, volumes, string.IsNullOrWhiteSpace(m_PresetName) ? "New preset" : m_PresetName));
                        m_PresetIndex = lib.presets.Count - 1;
                        m_PresetName = "";
                    }
                    using (new EditorGUI.DisabledScope(!any))
                    {
                        if (GUILayout.Button(new GUIContent("Update", "Overwrite the chosen preset with the current look.")))
                            lib.Overwrite(m_PresetIndex, CurrentLook(settings, volumes, names[m_PresetIndex]));
                        if (GUILayout.Button("Rename")) { m_Renaming = true; m_PresetName = names[m_PresetIndex]; }
                        if (GUILayout.Button("Delete") && EditorUtility.DisplayDialog("Delete preset", $"Delete the preset \"{names[m_PresetIndex]}\"?", "Delete", "Cancel"))
                            lib.Delete(m_PresetIndex);
                    }
                    if (GUILayout.Button(new GUIContent("Reset", "Restore the starter presets, dropping your own.")) &&
                        EditorUtility.DisplayDialog("Reset presets", "Replace all presets with the starter set?", "Reset", "Cancel"))
                        lib.ResetToStarters();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static MistPreset CurrentLook(MistEditorSettings settings, MistVolume[] volumes, string name)
        {
            return volumes.Length > 0
                ? MistPreset.FromVolume(volumes[0], MistSceneUtil.LiftOf(volumes[0]), name)
                : MistPreset.FromTemplate(settings.template, name);
        }

        void DrawOptions(MistEditorSettings settings)
        {
            m_OptionsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(m_OptionsOpen, "Scene view options");
            if (m_OptionsOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                settings.animateSceneView = EditorGUILayout.Toggle(new GUIContent("Animate in edit mode", "Keep the Scene view repainting so the mist rolls."), settings.animateSceneView);
                settings.updateOnReleaseOnly = EditorGUILayout.Toggle(new GUIContent("Update only on release", "Hold the mist while a slider is dragged, for slow machines."), settings.updateOnReleaseOnly);
                settings.showHandleLabels = EditorGUILayout.Toggle(new GUIContent("Handle labels", "Show the size readout next to the selected box."), settings.showHandleLabels);
                if (EditorGUI.EndChangeCheck()) settings.SaveNow();

                var features = MistSetup.FindFeatures();
                if (features.Count == 0)
                {
                    EditorGUILayout.HelpBox("No URP renderer has the Mist renderer feature, so nothing will be drawn.", MessageType.Warning);
                    if (GUILayout.Button("Add Mist To URP Renderers")) MistSetup.AddFeatureToRenderers();
                }
                else if (GUILayout.Button(new GUIContent("Renderer settings", "Select the URP renderer that holds the Mist settings.")))
                {
                    Selection.activeObject = features[0];
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static MistVolume[] SelectedVolumes()
        {
            var list = new List<MistVolume>();
            foreach (var go in Selection.gameObjects)
            {
                var v = go.GetComponent<MistVolume>();
                if (v != null) list.Add(v);
            }
            return list.ToArray();
        }
    }
}
