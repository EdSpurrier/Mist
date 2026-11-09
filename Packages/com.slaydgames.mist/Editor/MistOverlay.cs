using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace SlaydGames.Mist.Editor
{
    /// <summary>Scene view overlay: the Mist tool, Place, handle mode, hide all mist and animate toggles.</summary>
    [Overlay(typeof(SceneView), "Mist", true)]
    sealed class MistOverlay : Overlay
    {
        static readonly GUIContent[] s_Modes =
        {
            new GUIContent("Move", "Arrows: x and z keep the height above the ground, y moves straight up and down."),
            new GUIContent("Turn", "A ring about up, changing yaw only."),
            new GUIContent("Size", "A handle on each side and the top, plus a centre handle that scales all three."),
        };

        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(OnGUI) { style = { minWidth = 190 } };
            return container;
        }

        static void OnGUI()
        {
            var settings = MistEditorSettings.instance;
            bool changed = false;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool toolOn = MistTool.IsActive;
                bool newToolOn = GUILayout.Toggle(toolOn, new GUIContent("Mist Tool", "Place, select, drag and shape mist boxes in the Scene view."), EditorStyles.miniButtonLeft);
                if (newToolOn != toolOn)
                {
                    if (newToolOn) MistTool.Activate();
                    else UnityEditor.EditorTools.ToolManager.RestorePreviousPersistentTool();
                }
                using (new EditorGUI.DisabledScope(!MistTool.IsActive))
                {
                    bool place = GUILayout.Toggle(settings.placeMode, new GUIContent("Place", "Clicking the ground places a new box from the template."), EditorStyles.miniButtonRight);
                    if (place != settings.placeMode) { settings.placeMode = place; changed = true; }
                }
            }

            EditorGUI.BeginChangeCheck();
            var mode = (MistHandleMode)GUILayout.Toolbar((int)settings.handleMode, s_Modes, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) { settings.handleMode = mode; changed = true; }

            using (new EditorGUILayout.HorizontalScope())
            {
                bool hide = GUILayout.Toggle(settings.hideAllMist, new GUIContent("Hide all mist", "Skip drawing the mist in Scene views. Outlines stay."), EditorStyles.miniButtonLeft);
                if (hide != settings.hideAllMist) { settings.hideAllMist = hide; changed = true; }
                bool animate = GUILayout.Toggle(settings.animateSceneView, new GUIContent("Animate", "Keep the Scene view repainting so the mist rolls."), EditorStyles.miniButtonRight);
                if (animate != settings.animateSceneView) { settings.animateSceneView = animate; changed = true; }
            }

            if (GUILayout.Button(new GUIContent("Mist Window", "Template, presets and options."), EditorStyles.miniButton))
                MistWindow.Open();

            if (changed) settings.SaveNow();
        }
    }
}
