using UnityEngine;

namespace SlaydGames.Mist
{
    /// <summary>
    /// The time the mist drifts by. Game time while playing; editor time otherwise, so the mist rolls in
    /// the Scene view too. Double precision so long sessions stay smooth (the packer wraps it per volume).
    /// </summary>
    public static class MistClock
    {
        public static double Now
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return UnityEditor.EditorApplication.timeSinceStartup;
#endif
                return Time.timeAsDouble;
            }
        }
    }
}
