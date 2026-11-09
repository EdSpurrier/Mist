using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>Inspector for <see cref="MistVolume"/>: the three groups, per-box actions and presets.</summary>
    [CustomEditor(typeof(MistVolume))]
    [CanEditMultipleObjects]
    public sealed class MistVolumeEditor : UnityEditor.Editor
    {
        MistVolume[] m_Volumes;

        void OnEnable()
        {
            m_Volumes = new MistVolume[targets.Length];
            for (int i = 0; i < targets.Length; i++) m_Volumes[i] = (MistVolume)targets[i];
        }

        public override void OnInspectorGUI()
        {
            var first = m_Volumes[0];
            if (first == null) return;

            if (first.transform.lossyScale != Vector3.one || Mathf.Abs(first.transform.eulerAngles.x) > 0.01f || Mathf.Abs(first.transform.eulerAngles.z) > 0.01f)
                EditorGUILayout.HelpBox("Mist boxes only turn about the vertical axis and ignore scale. Size them with Width, Height and Depth.", MessageType.Info);

            if (MistGroupsGUI.Draw(serializedObject, "", m_Volumes))
            {
                foreach (var v in m_Volumes) v.SyncLight();
            }

            EditorGUILayout.Space();
            MistGroupsGUI.DrawVolumeActions(m_Volumes);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Mist Tool", "Activate the Scene view tool for placing, dragging and shaping mist boxes.")))
                    MistTool.Activate();
                if (GUILayout.Button(new GUIContent("Mist Window", "Open the Mist window: template, presets and Scene view options.")))
                    MistWindow.Open();
            }

            if (MistSetup.FindFeatures().Count == 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("No URP renderer has the Mist renderer feature, so nothing will be drawn.", MessageType.Warning);
                if (GUILayout.Button("Add Mist To URP Renderers")) MistSetup.AddFeatureToRenderers();
            }
        }
    }
}
