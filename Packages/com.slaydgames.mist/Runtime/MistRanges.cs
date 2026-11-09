using UnityEngine;

namespace SlaydGames.Mist
{
    /// <summary>
    /// Allowed ranges for every mist value (section 1 of the brief) and the narrower slider ranges the
    /// editor shows (section 5). Values are clamped to the hard ranges whenever they are loaded or set.
    /// </summary>
    public static class MistRanges
    {
        // Hard ranges: data is clamped to these.
        public const float MinWidth = 0.5f, MaxWidth = 128f;
        public const float MinHeight = 0.25f, MaxHeight = 32f;
        public const float MinDensity = 0f, MaxDensity = 8f;
        public const float MinGlow = 0f, MaxGlow = 20f;
        public const float MinLight = 0f, MaxLight = 40f;
        public const float MinNoiseScale = 0.2f, MaxNoiseScale = 40f;
        public const float MinSpeed = 0f, MaxSpeed = 20f;
        public const float MinRise = -5f, MaxRise = 5f;
        public const float MinChurn = 0f, MaxChurn = 10f;

        // Slider ranges shown in the Inspector and the Mist window.
        public const float UiMaxWidth = 64f;
        public const float UiMaxHeight = 16f;
        public const float UiMinLift = -3f, UiMaxLift = 12f;
        public const float UiMaxDensity = 4f;
        public const float UiMaxGlow = 10f;
        public const float UiMaxLight = 20f;
        public const float UiMinNoiseScale = 0.3f, UiMaxNoiseScale = 12f;
        public const float UiMaxSpeed = 5f;
        public const float UiMinRise = -1f, UiMaxRise = 2f;
        public const float UiMaxChurn = 3f;

        public static float Wrap360(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
