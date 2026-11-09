using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlaydGames.Mist.Editor
{
    /// <summary>Adds the Mist renderer feature to the project's URP renderers, the way the renderer Inspector does.</summary>
    public static class MistSetup
    {
        [MenuItem("Tools/Mist/Add Mist To URP Renderers")]
        public static void AddFeatureToRenderers()
        {
            int added = 0;
            foreach (var data in RendererDatas())
            {
                if (HasFeature(data)) continue;
                AddFeature(data);
                added++;
            }
            if (added > 0) AssetDatabase.SaveAssets();
            Debug.Log(added > 0 ? $"Mist: added the Mist renderer feature to {added} URP renderer(s)." : "Mist: every URP renderer already has the Mist renderer feature.");
        }

        /// <summary>Every Mist renderer feature on the project's URP renderers.</summary>
        public static List<MistRendererFeature> FindFeatures()
        {
            var found = new List<MistRendererFeature>();
            foreach (var data in RendererDatas())
                foreach (var f in data.rendererFeatures)
                    if (f is MistRendererFeature m) found.Add(m);
            return found;
        }

        static bool HasFeature(ScriptableRendererData data)
        {
            foreach (var f in data.rendererFeatures) if (f is MistRendererFeature) return true;
            return false;
        }

        static IEnumerable<ScriptableRendererData> RendererDatas()
        {
            var seenAssets = new HashSet<UniversalRenderPipelineAsset>();
            var seen = new HashSet<ScriptableRendererData>();
            var assets = new List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
            for (int i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));

            foreach (var a in assets)
            {
                if (!(a is UniversalRenderPipelineAsset urp) || !seenAssets.Add(urp)) continue;
                var so = new SerializedObject(urp);
                var list = so.FindProperty("m_RendererDataList");
                if (list == null) continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue is ScriptableRendererData data && seen.Add(data))
                        yield return data;
                }
            }
        }

        static void AddFeature(ScriptableRendererData data)
        {
            var feature = ScriptableObject.CreateInstance<MistRendererFeature>();
            feature.name = "Mist";
            Undo.RegisterCreatedObjectUndo(feature, "Add Mist Renderer Feature");
            if (EditorUtility.IsPersistent(data)) AssetDatabase.AddObjectToAsset(feature, data);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            if (map != null)
            {
                map.arraySize++;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(data);
        }
    }
}
