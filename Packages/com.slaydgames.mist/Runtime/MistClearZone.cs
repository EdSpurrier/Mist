using System.Collections.Generic;
using UnityEngine;

namespace SlaydGames.Mist
{
    /// <summary>
    /// A wedge or disc on the ground, and everything above it, where the mist thins away (section 4.6).
    /// The zone is centred on the transform and the wedge points along the transform's forward axis.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Rendering/Mist Clear Zone")]
    public sealed class MistClearZone : MonoBehaviour
    {
        [Tooltip("Half angle of the wedge in degrees. 180 makes a full disc.")]
        [Range(1f, 180f)] public float halfAngle = 180f;
        [Tooltip("How far the zone reaches from its centre, in metres.")]
        [Min(0.1f)] public float range = 6f;
        [Tooltip("Share of the edge that fades out.")]
        [Range(0f, 1f)] public float soft = 0.3f;
        [Tooltip("How completely the mist is cleared. 1 clears it fully.")]
        [Range(0f, 1f)] public float strength = 1f;

        static readonly List<MistClearZone> s_Active = new List<MistClearZone>();
        public static IReadOnlyList<MistClearZone> Active => s_Active;

        public bool IsDisc => halfAngle >= 180f - 1e-3f;

        /// <summary>Unit heading on the ground plane (x, z).</summary>
        public Vector2 Heading
        {
            get
            {
                var f = transform.forward;
                var h = new Vector2(f.x, f.z);
                return h.sqrMagnitude > 1e-6f ? h.normalized : Vector2.right;
            }
        }

        void OnEnable() => s_Active.Add(this);
        void OnDisable() => s_Active.Remove(this);
    }
}
