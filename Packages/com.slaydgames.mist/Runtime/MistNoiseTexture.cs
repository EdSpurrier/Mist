using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace SlaydGames.Mist
{
    /// <summary>
    /// The 48^3 tiling noise the billows are cut from (section 2): inverted Worley noise, so the billows are
    /// round, blended with a little value noise. Built once on the CPU and kept for the session.
    /// One repeat of the texture holds five billows.
    /// </summary>
    public static class MistNoiseTexture
    {
        public const int Size = 48;
        public const int BillowsPerRepeat = 5;

        static Texture3D s_Texture;

        public static Texture3D Get()
        {
            if (s_Texture == null) s_Texture = Build();
            return s_Texture;
        }

        public static void Release()
        {
            if (s_Texture == null) return;
            if (Application.isPlaying) Object.Destroy(s_Texture);
            else Object.DestroyImmediate(s_Texture);
            s_Texture = null;
        }

        static Texture3D Build()
        {
            const int n = Size;
            var data = new byte[n * n * n];
            const int cells = 6;                      // Worley feature points per side
            var points = new Vector3[cells * cells * cells];
            for (int z = 0; z < cells; z++)
            for (int y = 0; y < cells; y++)
            for (int x = 0; x < cells; x++)
                points[(z * cells + y) * cells + x] = new Vector3(x + Hash3(x, y, z, 1), y + Hash3(x, y, z, 2), z + Hash3(x, y, z, 3));

            float cellSize = n / (float)cells;
            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Position in cell units.
                float px = x / cellSize, py = y / cellSize, pz = z / cellSize;
                int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py), cz = Mathf.FloorToInt(pz);

                // Nearest feature point over the 27 neighbouring cells, each wrapped so the texture tiles:
                // the feature point of the wrapped cell is moved to the unwrapped neighbour's place.
                float nearest = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy, nz = cz + dz;
                    int wx = Wrap(nx, cells), wy = Wrap(ny, cells), wz = Wrap(nz, cells);
                    Vector3 fp = points[(wz * cells + wy) * cells + wx];
                    fp.x += nx - wx; fp.y += ny - wy; fp.z += nz - wz;
                    float ddx = fp.x - px, ddy = fp.y - py, ddz = fp.z - pz;
                    float d2 = ddx * ddx + ddy * ddy + ddz * ddz;
                    if (d2 < nearest) nearest = d2;
                }

                float worley = 1f - Mathf.Min(1f, Mathf.Sqrt(nearest) * 1.1f);
                float value = worley * 0.65f
                            + ValueNoise(x, y, z, n, 8, 21) * 0.23f
                            + ValueNoise(x, y, z, n, 16, 22) * 0.12f;
                data[(z * n + y) * n + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
            }

            var tex = new Texture3D(n, n, n, GraphicsFormat.R8_UNorm, TextureCreationFlags.None)
            {
                name = "Mist Noise 48^3",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixelData(data, 0);
            tex.Apply(false, true);
            return tex;
        }

        static int Wrap(int v, int m) => ((v % m) + m) % m;

        static float Hash3(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1442695041;
                h = (int)((uint)(h ^ (int)((uint)h >> 13)) * 1274126177u);
                return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296f;
            }
        }

        // Tileable value noise: a lattice of f cells per side that wraps.
        static float ValueNoise(int x, int y, int z, int size, int f, int seed)
        {
            float sx = x / (float)size * f, sy = y / (float)size * f, sz = z / (float)size * f;
            int xi = Mathf.FloorToInt(sx), yi = Mathf.FloorToInt(sy), zi = Mathf.FloorToInt(sz);
            float ux = Smooth(sx - xi), uy = Smooth(sy - yi), uz = Smooth(sz - zi);

            float H(int i, int j, int k) => Hash3(Wrap(xi + i, f), Wrap(yi + j, f), Wrap(zi + k, f), seed);

            return Mathf.Lerp(
                Mathf.Lerp(Mathf.Lerp(H(0, 0, 0), H(1, 0, 0), ux), Mathf.Lerp(H(0, 1, 0), H(1, 1, 0), ux), uy),
                Mathf.Lerp(Mathf.Lerp(H(0, 0, 1), H(1, 0, 1), ux), Mathf.Lerp(H(0, 1, 1), H(1, 1, 1), ux), uy), uz);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
