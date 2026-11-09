using UnityEditor;
using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>The Mist tool's toolbar icon: the package's PNG, or a cloud drawn in code if the asset is missing.</summary>
    static class MistToolIcon
    {
        public const string AssetPath = "Packages/com.slaydgames.mist/Editor/Icons/MistTool.png";

        static Texture2D s_Icon;

        public static Texture2D Get()
        {
            if (s_Icon != null) return s_Icon;
            s_Icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (s_Icon == null) s_Icon = Draw();
            return s_Icon;
        }

        static Texture2D Draw()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, name = "Mist Tool Icon" };
            var px = new Color32[n * n];
            // Three overlapping discs on a flat base read as a low bank of mist.
            Vector3[] discs = { new Vector3(10f, 13f, 6.5f), new Vector3(17f, 16f, 8f), new Vector3(23f, 13f, 6f) };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float a = 0f;
                if (y >= 7)
                {
                    foreach (var d in discs)
                    {
                        float dist = Mathf.Sqrt((x + 0.5f - d.x) * (x + 0.5f - d.x) + (y + 0.5f - d.y) * (y + 0.5f - d.y));
                        a = Mathf.Max(a, Mathf.Clamp01(d.z + 0.5f - dist));
                    }
                    if (y < 10) a = Mathf.Max(a, x >= 4 && x <= 28 ? Mathf.Clamp01(10f - y) : 0f);
                }
                px[y * n + x] = new Color32(225, 235, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
