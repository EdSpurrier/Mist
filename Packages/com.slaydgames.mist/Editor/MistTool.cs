using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>
    /// The Scene view tool of section 5. With it active: clicking a box selects it (nearest entry wins);
    /// clicking the ground places a new box from the template when Place is on, or deselects otherwise;
    /// dragging a box moves it across the ground keeping its lift; and the selected box shows Move, Turn
    /// or Size handles. Undo covers everything, one drag being one step.
    /// </summary>
    [EditorTool("Mist Volumes")]
    [Icon(MistToolIcon.AssetPath)]
    public sealed class MistTool : EditorTool
    {
        static readonly int s_Hint = "SlaydGames.MistTool".GetHashCode();
        static readonly Color s_HandleColor = new Color(1f, 0.85f, 0.2f);

        GUIContent m_Icon;
        public override GUIContent toolbarIcon =>
            m_Icon ??= new GUIContent(MistToolIcon.Get(), "Mist Volumes: place, select, drag and shape mist boxes.");

        // Ground drag.
        bool m_Dragging;
        bool m_DragMoved;
        MistVolume m_DragVolume;
        Vector2 m_GrabOffset;       // base xz minus the grabbed ground point xz
        float m_DragLift;           // base y minus the grabbed ground point y
        float m_FallbackGroundY;
        int m_UndoGroup;

        // Lift captured at the start of a Move handle drag on x or z.
        bool m_HandleLiftValid;
        float m_HandleLift;

        public static bool IsActive => ToolManager.activeToolType == typeof(MistTool);
        public static void Activate() => ToolManager.SetActiveTool<MistTool>();

        public override void OnActivated() => SceneView.RepaintAll();
        public override void OnWillBeDeactivated() => SceneView.RepaintAll();

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView sceneView)) return;
            var settings = MistEditorSettings.instance;
            var e = Event.current;

            int id = GUIUtility.GetControlID(s_Hint, FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(id);
                if (GUIUtility.hotControl == 0) m_HandleLiftValid = false;
            }

            var selected = SelectedVolume();
            if (selected != null)
            {
                switch (settings.handleMode)
                {
                    case MistHandleMode.Move: DrawMoveHandles(selected); break;
                    case MistHandleMode.Turn: DrawTurnHandle(selected); break;
                    case MistHandleMode.Size: DrawSizeHandles(selected, sceneView); break;
                }
                if (settings.showHandleLabels) DrawLabel(selected);
            }

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || e.alt || HandleUtility.nearestControl != id) break;
                    OnMouseDown(e, settings, selected, id);
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id || !m_Dragging) break;
                    OnMouseDrag(e);
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (m_Dragging)
                    {
                        Undo.CollapseUndoOperations(m_UndoGroup);
                        if (m_DragMoved) Undo.SetCurrentGroupName("Move Mist Volume");
                    }
                    m_Dragging = false;
                    m_DragVolume = null;
                    e.Use();
                    break;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && selected != null && GUIUtility.hotControl == 0)
                    {
                        Selection.activeGameObject = null;
                        e.Use();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ clicks and drags

        void OnMouseDown(Event e, MistEditorSettings settings, MistVolume selected, int id)
        {
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var hit = MistSceneUtil.PickVolume(ray, out _);
            if (hit != null)
            {
                if (Selection.activeGameObject != hit.gameObject) Selection.activeGameObject = hit.gameObject;
                BeginDrag(hit, e.mousePosition);
                GUIUtility.hotControl = id;
                e.Use();
                return;
            }

            if (settings.placeMode)
            {
                float fallbackY = selected != null ? selected.transform.position.y - MistSceneUtil.LiftOf(selected) : 0f;
                if (MistSceneUtil.GroundHit(e.mousePosition, fallbackY, out var point))
                {
                    Undo.IncrementCurrentGroup();
                    var created = MistSceneUtil.PlaceVolume(point, settings.template);
                    BeginDrag(created, e.mousePosition, keepGroup: true);
                    GUIUtility.hotControl = id;
                    e.Use();
                }
                return;
            }

            if (selected != null)
            {
                Selection.activeGameObject = null;
                e.Use();
            }
        }

        void BeginDrag(MistVolume v, Vector2 mouse, bool keepGroup = false)
        {
            m_Dragging = true;
            m_DragMoved = false;
            m_DragVolume = v;
            Vector3 basePos = v.transform.position;
            if (MistSceneUtil.GroundHit(mouse, basePos.y, out var p))
            {
                m_GrabOffset = new Vector2(basePos.x - p.x, basePos.z - p.z);
                m_DragLift = basePos.y - p.y;
                m_FallbackGroundY = p.y;
            }
            else
            {
                m_GrabOffset = Vector2.zero;
                m_DragLift = 0f;
                m_FallbackGroundY = basePos.y;
            }
            if (!keepGroup) Undo.IncrementCurrentGroup();
            m_UndoGroup = Undo.GetCurrentGroup();
        }

        void OnMouseDrag(Event e)
        {
            if (m_DragVolume == null) return;
            if (!MistSceneUtil.GroundHit(e.mousePosition, m_FallbackGroundY, out var p)) return;
            var t = m_DragVolume.transform;
            Undo.RecordObject(t, "Move Mist Volume");
            t.position = new Vector3(p.x + m_GrabOffset.x, p.y + m_DragLift, p.z + m_GrabOffset.y);
            m_DragMoved = true;
            e.Use();
        }

        // ------------------------------------------------------------------ handles

        void DrawMoveHandles(MistVolume v)
        {
            var t = v.transform;
            Vector3 pos = t.position;
            float size = HandleUtility.GetHandleSize(pos);
            var snap = EditorSnapSettings.move;

            Handles.color = Handles.xAxisColor;
            EditorGUI.BeginChangeCheck();
            Vector3 nx = Handles.Slider(pos, Vector3.right, size, Handles.ArrowHandleCap, snap.x);
            if (EditorGUI.EndChangeCheck()) MoveOnGround(v, new Vector3(nx.x, pos.y, pos.z));

            Handles.color = Handles.zAxisColor;
            EditorGUI.BeginChangeCheck();
            Vector3 nz = Handles.Slider(pos, Vector3.forward, size, Handles.ArrowHandleCap, snap.z);
            if (EditorGUI.EndChangeCheck()) MoveOnGround(v, new Vector3(pos.x, pos.y, nz.z));

            Handles.color = Handles.yAxisColor;
            EditorGUI.BeginChangeCheck();
            Vector3 ny = Handles.Slider(pos, Vector3.up, size, Handles.ArrowHandleCap, snap.y);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Move Mist Volume");
                t.position = new Vector3(pos.x, ny.y, pos.z);
            }
        }

        // Moving on x or z keeps the height above the ground recorded when the drag started.
        void MoveOnGround(MistVolume v, Vector3 target)
        {
            var t = v.transform;
            Vector3 pos = t.position;
            if (!m_HandleLiftValid)
            {
                m_HandleLift = pos.y - MistSceneUtil.GroundHeight(pos.x, pos.z, pos.y, out _);
                m_HandleLiftValid = true;
            }
            float ground = MistSceneUtil.GroundHeight(target.x, target.z, pos.y - m_HandleLift, out _);
            Undo.RecordObject(t, "Move Mist Volume");
            t.position = new Vector3(target.x, ground + m_HandleLift, target.z);
        }

        void DrawTurnHandle(MistVolume v)
        {
            var t = v.transform;
            Vector3 center = v.Center;
            float radius = Mathf.Max(v.width, v.depth) * 0.5f + HandleUtility.GetHandleSize(center) * 0.3f;
            Handles.color = s_HandleColor;
            EditorGUI.BeginChangeCheck();
            Quaternion rot = Handles.Disc(Quaternion.Euler(0f, v.YawDegrees, 0f), center, Vector3.up, radius, false, EditorSnapSettings.rotate);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(t, "Turn Mist Volume");
                v.SetYaw(rot.eulerAngles.y);
            }
            // A tick toward the box's own +Z, so the turn can be read.
            Handles.DrawLine(center, center + Quaternion.Euler(0f, v.YawDegrees, 0f) * Vector3.forward * radius, 2f);
        }

        void DrawSizeHandles(MistVolume v, SceneView sceneView)
        {
            var rot = Quaternion.Euler(0f, v.YawDegrees, 0f);
            Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
            Vector3 basePos = v.transform.position;
            Vector3 mid = v.Center;
            float hw = v.width * 0.5f, hd = v.depth * 0.5f;
            float cap = HandleUtility.GetHandleSize(mid) * 0.12f;
            Handles.color = s_HandleColor;

            // One handle on each side (symmetric about the centre) and one on the top.
            SideHandle(v, mid + right * hw, right, cap, p => v.width = Clamp(2f * Vector3.Dot(p - mid, right), MistRanges.MinWidth, MistRanges.MaxWidth));
            SideHandle(v, mid - right * hw, -right, cap, p => v.width = Clamp(2f * Vector3.Dot(mid - p, right), MistRanges.MinWidth, MistRanges.MaxWidth));
            SideHandle(v, mid + fwd * hd, fwd, cap, p => v.depth = Clamp(2f * Vector3.Dot(p - mid, fwd), MistRanges.MinWidth, MistRanges.MaxWidth));
            SideHandle(v, mid - fwd * hd, -fwd, cap, p => v.depth = Clamp(2f * Vector3.Dot(mid - p, fwd), MistRanges.MinWidth, MistRanges.MaxWidth));
            SideHandle(v, basePos + Vector3.up * v.height, Vector3.up, cap, p => v.height = Clamp(Vector3.Dot(p - basePos, Vector3.up), MistRanges.MinHeight, MistRanges.MaxHeight));

            // Centre handle: scales all three, growing as you drag right or up on screen.
            var cam = sceneView.camera.transform;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider2D(mid, cam.forward, cam.right, cam.up, cap * 1.3f, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 delta = moved - mid;
                float amount = Vector3.Dot(delta, cam.right) + Vector3.Dot(delta, cam.up);
                float factor = Mathf.Max(0.01f, 1f + amount / (HandleUtility.GetHandleSize(mid) * 2f));
                Undo.RecordObject(v, "Resize Mist Volume");
                v.width = Clamp(v.width * factor, MistRanges.MinWidth, MistRanges.MaxWidth);
                v.depth = Clamp(v.depth * factor, MistRanges.MinWidth, MistRanges.MaxWidth);
                v.height = Clamp(v.height * factor, MistRanges.MinHeight, MistRanges.MaxHeight);
                v.SyncLight();
            }
        }

        static void SideHandle(MistVolume v, Vector3 point, Vector3 dir, float cap, System.Action<Vector3> apply)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 p = Handles.Slider(point, dir, cap, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(v, "Resize Mist Volume");
                apply(p);
                v.SyncLight();
            }
        }

        static float Clamp(float x, float min, float max) => Mathf.Clamp(x, min, max);

        void DrawLabel(MistVolume v)
        {
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white }, alignment = TextAnchor.UpperCenter };
            Vector3 top = v.transform.position + Vector3.up * (v.height + HandleUtility.GetHandleSize(v.Center) * 0.2f);
            Handles.Label(top, $"{v.name}\n{v.width:0.#} x {v.height:0.#} x {v.depth:0.#}  turn {v.YawDegrees:0}°", style);
        }

        static MistVolume SelectedVolume()
        {
            var go = Selection.activeGameObject;
            return go != null ? go.GetComponent<MistVolume>() : null;
        }
    }
}
