using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// Outlines in the Scene view: pale blue for normal boxes, faint for hidden ones, yellow and thicker for
    /// the selected one. Gizmos draw after the frame, so they always sit over the mist. Occluded parts are
    /// drawn fainter rather than dropped.
    /// </summary>
    static class MistGizmos
    {
        static readonly Color s_Normal = new Color(0.55f, 0.8f, 1f, 0.9f);
        static readonly Color s_Hidden = new Color(0.55f, 0.8f, 1f, 0.25f);
        static readonly Color s_Selected = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color s_ClearZone = new Color(1f, 0.6f, 0.3f, 0.9f);
        static readonly Vector3[] s_Corners = new Vector3[8];
        static readonly Vector3[] s_Loop = new Vector3[5];
        static readonly Vector3[] s_Pair = new Vector3[2];

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Active | GizmoType.InSelectionHierarchy)]
        static void DrawVolume(MistVolume v, GizmoType type)
        {
            if (v == null || !v.isActiveAndEnabled) return;
            bool selected = Selection.Contains(v.gameObject);
            Color color = selected ? s_Selected : (v.hidden ? s_Hidden : s_Normal);
            float width = selected ? 4f : 2f;

            MistSceneUtil.Corners(v, s_Corners);

            var prevZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            DrawEdges(color, width);
            Handles.zTest = CompareFunction.Greater;
            DrawEdges(new Color(color.r, color.g, color.b, color.a * 0.25f), width);
            Handles.zTest = prevZTest;
        }

        static void DrawEdges(Color color, float width)
        {
            Handles.color = color;
            for (int i = 0; i < 4; i++) s_Loop[i] = s_Corners[i];
            s_Loop[4] = s_Corners[0];
            Handles.DrawAAPolyLine(width, s_Loop);
            for (int i = 0; i < 4; i++) s_Loop[i] = s_Corners[4 + i];
            s_Loop[4] = s_Corners[4];
            Handles.DrawAAPolyLine(width, s_Loop);
            for (int i = 0; i < 4; i++)
            {
                s_Pair[0] = s_Corners[i];
                s_Pair[1] = s_Corners[4 + i];
                Handles.DrawAAPolyLine(width, s_Pair);
            }
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected | GizmoType.Active)]
        static void DrawClearZone(MistClearZone z, GizmoType type)
        {
            if (z == null || !z.isActiveAndEnabled) return;
            bool selected = Selection.Contains(z.gameObject);
            Handles.color = selected ? s_Selected : s_ClearZone;
            Vector3 c = z.transform.position;
            Vector2 h = z.Heading;
            Vector3 heading = new Vector3(h.x, 0f, h.y);
            if (z.IsDisc)
            {
                Handles.DrawWireDisc(c, Vector3.up, z.range, selected ? 3f : 1.5f);
            }
            else
            {
                Vector3 from = Quaternion.Euler(0f, -z.halfAngle, 0f) * heading;
                Handles.DrawWireArc(c, Vector3.up, from, z.halfAngle * 2f, z.range, selected ? 3f : 1.5f);
                Handles.DrawLine(c, c + from * z.range);
                Handles.DrawLine(c, c + Quaternion.Euler(0f, z.halfAngle, 0f) * heading * z.range);
            }
            Handles.DrawLine(c, c + heading * z.range * 0.5f, 2f);
        }
    }
}
