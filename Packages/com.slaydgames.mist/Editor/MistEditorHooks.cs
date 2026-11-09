using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// Editor-wide hooks: keep the Scene view rolling in edit mode, release the "update on release" hold,
    /// re-seed duplicated volumes, and the menu items.
    /// </summary>
    [InitializeOnLoad]
    static class MistEditorHooks
    {
        const double kRepaintInterval = 1.0 / 30.0;
        static double s_LastRepaint;
        static bool s_WasPaused;

        static MistEditorHooks()
        {
            EditorApplication.update += Tick;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            MistRendererFeature.HideInSceneView = MistEditorSettings.instance.hideAllMist;
        }

        static void Tick()
        {
            // Release the preview hold once no slider is held any more.
            if (MistRendererFeature.PreviewPaused && GUIUtility.hotControl == 0)
                MistRendererFeature.PreviewPaused = false;
            if (s_WasPaused && !MistRendererFeature.PreviewPaused) SceneView.RepaintAll();
            s_WasPaused = MistRendererFeature.PreviewPaused;

            if (Application.isPlaying) return;
            if (MistVolume.Active.Count == 0) return;
            if (!MistEditorSettings.instance.animateSceneView) return;
            if (MistRendererFeature.HideInSceneView) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastRepaint < kRepaintInterval) return;
            s_LastRepaint = now;
            SceneView.RepaintAll();
        }

        // A duplicated volume copies its seed. Give it new billows so copies side by side do not match.
        static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) continue;
                stream.GetCreateGameObjectHierarchyEvent(i, out var data);
                if (!(EditorUtility.InstanceIDToObject(data.instanceId) is GameObject go)) continue;
                foreach (var v in go.GetComponentsInChildren<MistVolume>(true))
                {
                    if (SeedIsShared(v))
                    {
                        v.NewSeed();
                        EditorUtility.SetDirty(v);
                    }
                }
            }
        }

        static bool SeedIsShared(MistVolume v)
        {
            foreach (var other in MistVolume.Active)
                if (other != v && other != null && other.Seed == v.Seed) return true;
            return false;
        }

        [MenuItem("GameObject/Rendering/Mist Volume", false, 10)]
        static void CreateVolume(MenuCommand command)
        {
            var settings = MistEditorSettings.instance;
            var view = SceneView.lastActiveSceneView;
            Vector3 point = view != null ? view.pivot : Vector3.zero;
            point.y = MistSceneUtil.GroundHeight(point.x, point.z, 0f, out _);
            var v = MistSceneUtil.PlaceVolume(point, settings.template, "Create Mist Volume");
            if (command.context is GameObject parent) Undo.SetTransformParent(v.transform, parent.transform, "Create Mist Volume");
        }

        [MenuItem("GameObject/Rendering/Mist Clear Zone", false, 11)]
        static void CreateClearZone(MenuCommand command)
        {
            var go = new GameObject("Mist Clear Zone");
            Undo.RegisterCreatedObjectUndo(go, "Create Mist Clear Zone");
            UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);
            go.AddComponent<MistClearZone>();
            var view = SceneView.lastActiveSceneView;
            Vector3 point = view != null ? view.pivot : Vector3.zero;
            point.y = MistSceneUtil.GroundHeight(point.x, point.z, 0f, out _);
            go.transform.position = point;
            if (command.context is GameObject parent) Undo.SetTransformParent(go.transform, parent.transform, "Create Mist Clear Zone");
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/Mist/Activate Mist Tool")]
        static void ActivateTool() => MistTool.Activate();

        [MenuItem("Tools/Mist/Select All Mist Volumes")]
        static void SelectAll()
        {
            var list = new List<GameObject>();
            foreach (var v in MistVolume.Active) if (v != null) list.Add(v.gameObject);
            Selection.objects = list.ToArray();
        }
    }
}
