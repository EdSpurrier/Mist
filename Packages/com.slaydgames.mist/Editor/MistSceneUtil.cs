using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>Scene view helpers: where the ground is, which box a ray hits, and lift.</summary>
    public static class MistSceneUtil
    {
        /// <summary>
        /// The ground under a Scene view mouse position: whatever the mouse ray hits (meshes with or without
        /// colliders), else the y = fallbackY plane.
        /// </summary>
        public static bool GroundHit(Vector2 guiPosition, float fallbackY, out Vector3 point)
        {
            if (HandleUtility.PlaceObject(guiPosition, out point, out _)) return true;
            var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            if (Physics.Raycast(ray, out var hit, 5000f, ~0, QueryTriggerInteraction.Ignore)) { point = hit.point; return true; }
            return RayPlane(ray, fallbackY, out point);
        }

        public static bool RayPlane(Ray ray, float y, out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>
        /// Ground height at (x, z): the highest collider hit casting down from well above. Without colliders
        /// the fallback is returned and found is false.
        /// </summary>
        public static float GroundHeight(float x, float z, float fallback, out bool found)
        {
            var origin = new Vector3(x, Mathf.Max(fallback, 0f) + 500f, z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                found = true;
                return hit.point.y;
            }
            found = false;
            return fallback;
        }

        /// <summary>A volume's lift: how far its base sits above the ground under it. 0 ground is assumed without colliders.</summary>
        public static float LiftOf(MistVolume v)
        {
            var p = v.transform.position;
            return p.y - GroundHeight(p.x, p.z, 0f, out _);
        }

        /// <summary>Move a volume so its base sits at lift above the ground under (x, z).</summary>
        public static void SetLift(MistVolume v, float lift)
        {
            var p = v.transform.position;
            float ground = GroundHeight(p.x, p.z, 0f, out _);
            Undo.RecordObject(v.transform, "Lift Mist Volume");
            v.transform.position = new Vector3(p.x, ground + lift, p.z);
        }

        /// <summary>Pick the volume whose turned box the ray enters first.</summary>
        public static MistVolume PickVolume(Ray ray, out float enterDistance)
        {
            MistVolume best = null;
            enterDistance = float.MaxValue;
            foreach (var v in MistVolume.Active)
            {
                if (v == null || !v.isActiveAndEnabled) continue;
                if (RayBox(v, ray, out float t0, out float t1) && t1 > t0 && t0 < enterDistance)
                {
                    enterDistance = t0;
                    best = v;
                }
            }
            return best;
        }

        /// <summary>Slab test against a volume's turned box. t0 is clamped to 0.</summary>
        public static bool RayBox(MistVolume v, Ray ray, out float t0, out float t1)
        {
            float yaw = v.YawRadians;
            float c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
            Vector3 d = ray.origin - v.Center;
            Vector3 lo = new Vector3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            Vector3 ld = new Vector3(c * ray.direction.x - s * ray.direction.z, ray.direction.y, s * ray.direction.x + c * ray.direction.z);
            Vector3 h = new Vector3(v.width * 0.5f, v.height * 0.5f, v.depth * 0.5f);

            t0 = 0f; t1 = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float dir = Mathf.Abs(ld[i]) < 1e-6f ? 1e-6f : ld[i];
                float inv = 1f / dir;
                float ta = (-h[i] - lo[i]) * inv, tb = (h[i] - lo[i]) * inv;
                t0 = Mathf.Max(t0, Mathf.Min(ta, tb));
                t1 = Mathf.Min(t1, Mathf.Max(ta, tb));
            }
            return t1 > t0;
        }

        /// <summary>The 8 corners of a volume's box, bottom four first.</summary>
        public static void Corners(MistVolume v, Vector3[] into)
        {
            var rot = Quaternion.Euler(0f, v.YawDegrees, 0f);
            Vector3 right = rot * Vector3.right * (v.width * 0.5f);
            Vector3 fwd = rot * Vector3.forward * (v.depth * 0.5f);
            Vector3 up = Vector3.up * v.height;
            Vector3 b = v.transform.position;
            into[0] = b - right - fwd; into[1] = b + right - fwd; into[2] = b + right + fwd; into[3] = b - right + fwd;
            for (int i = 0; i < 4; i++) into[4 + i] = into[i] + up;
        }

        /// <summary>Create a volume from the template with its base at point + lift, selected, with an Undo step.</summary>
        public static MistVolume PlaceVolume(Vector3 groundPoint, MistTemplate template, string undoName = "Place Mist Volume")
        {
            var go = new GameObject("Mist Volume");
            Undo.RegisterCreatedObjectUndo(go, undoName);
            UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);
            GameObjectUtility.EnsureUniqueNameForSibling(go);
            var v = go.AddComponent<MistVolume>();
            template.ApplyTo(v);
            v.NewSeed();
            go.transform.position = groundPoint + Vector3.up * template.lift;
            Selection.activeGameObject = go;
            return v;
        }
    }
}
