using UnityEngine;

namespace SlaydGames.Mist.Editor
{
    /// <summary>A small cloud icon for the Mist tool, drawn in code so the package needs no texture asset.</summary>
    static class MistToolIcon
    {
        static Texture2D s_Icon;

        public static Texture2D Get()
        {
            if (s_Icon != null) return s_Icon;
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
                byte v = (byte)Mathf.RoundToInt(a * 255f);
                px[y * n + x] = new Color32(225, 235, 255, v);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_Icon = tex;
            return tex;
        }
    }
}
