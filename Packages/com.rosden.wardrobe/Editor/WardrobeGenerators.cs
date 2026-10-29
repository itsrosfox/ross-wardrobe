using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Generates the two maps a garment needs from its mesh: an ID map (one flat colour per UV island) and a
    /// 16-bit gradient map (position along an axis). Authors can paint their own instead.
    /// </summary>
    public static class WardrobeGenerators {
        public enum GradientDirection {
            [InspectorName("Top → Bottom (−Y)")] TopToBottom,
            [InspectorName("Bottom → Top (+Y)")] BottomToTop,
            [InspectorName("+X → −X")] PositiveToNegativeX,
            [InspectorName("−X → +X")] NegativeToPositiveX,
            [InspectorName("Front → Back (+Z → −Z)")] FrontToBack,
            [InspectorName("Back → Front (−Z → +Z)")] BackToFront,
        }

        /// <summary>A UV island of the garment mesh, as written to the ID map.</summary>
        public class Island {
            public int index;
            public Color32 color;
            public float uvArea;
            public Vector3 center; // model space, area-weighted
            public Bounds bounds;  // model space
            internal float weight;
            internal bool hasBounds;
        }

        private struct MeshData {
            public Vector3[] vertices;
            public Vector2[] uv;
            public int[] triangles;
        }

        // ---------------------------------------------------------------- ID map

        /// <summary>
        /// Writes "&lt;Garment&gt; ID.png" next to the garment with one colour per UV island (biggest island first)
        /// and assigns it. With <paramref name="createRegions"/>, also makes one region per island.
        /// </summary>
        public static List<Island> GenerateIdMap(WardrobeGarment g, int size, bool createRegions) {
            if (g.mesh == null) throw new InvalidOperationException("Assign the garment's mesh first.");
            var m = ReadMesh(g.mesh);
            var triIsland = FindIslands(m, out int count);

            var islands = Enumerable.Range(0, count).Select(_ => new Island()).ToArray();
            var tris = m.triangles;
            for (int t = 0; t < triIsland.Length; t++) {
                var isl = islands[triIsland[t]];
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                Vector2 ua = m.uv[a], ub = m.uv[b], uc = m.uv[c];
                Vector3 pa = m.vertices[a], pb = m.vertices[b], pc = m.vertices[c];
                isl.uvArea += Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (ub.y - ua.y) * (uc.x - ua.x)) * 0.5f;
                float area = Vector3.Cross(pb - pa, pc - pa).magnitude * 0.5f;
                isl.center += (pa + pb + pc) / 3f * area;
                isl.weight += area;
                if (!isl.hasBounds) { isl.bounds = new Bounds(pa, Vector3.zero); isl.hasBounds = true; }
                isl.bounds.Encapsulate(pa);
                isl.bounds.Encapsulate(pb);
                isl.bounds.Encapsulate(pc);
            }

            var order = Enumerable.Range(0, count).OrderByDescending(k => islands[k].uvArea).ToArray();
            var remap = new int[count];
            var sorted = new List<Island>();
            for (int n = 0; n < count; n++) {
                var isl = islands[order[n]];
                remap[order[n]] = n;
                isl.index = n;
                isl.color = IslandColor(n);
                if (isl.weight > 0) isl.center /= isl.weight;
                sorted.Add(isl);
            }

            var ids = new int[size * size];
            var filled = new bool[size * size];
            for (int t = 0; t < triIsland.Length; t++) {
                int id = remap[triIsland[t]] + 1;
                Raster(m.uv[tris[t * 3]] * size, m.uv[tris[t * 3 + 1]] * size, m.uv[tris[t * 3 + 2]] * size, size,
                    (i, w0, w1, w2) => { ids[i] = id; filled[i] = true; });
            }
            Dilate(ids, filled, size, PaddingFor(size));

            var px = new Color32[size * size];
            var background = new Color32(0, 0, 0, 255);
            for (int i = 0; i < px.Length; i++) px[i] = ids[i] > 0 ? sorted[ids[i] - 1].color : background;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            tex.SetPixels32(px);
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string path = $"{WardrobeAssets.FolderOf(g)}/{WardrobeAssets.FileName(WardrobeAssets.BaseName(g) + " ID")}.png";
            var idMap = WardrobeAssets.WriteTexture(path, png, ti => {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Point;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = Mathf.Max(32, size);
            });

            Undo.RecordObject(g, "Generate ID Map");
            g.idMap = idMap;
            if (createRegions) {
                g.regions.Clear();
                foreach (var isl in sorted)
                    g.regions.Add(new WardrobeRegion { name = $"Part {isl.index + 1}", ids = new List<Color32> { isl.color } });
            }
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssetIfDirty(g);
            return sorted;
        }

        private static Color32 IslandColor(int n) {
            float hue = Mathf.Repeat(0.08f + n * 0.618034f, 1f);
            float saturation = n % 2 == 0 ? 0.75f : 0.5f;
            float value = n % 3 == 0 ? 0.95f : 0.78f;
            Color32 c = Color.HSVToRGB(hue, saturation, value);
            c.a = 255;
            return c;
        }

        // Triangles connected through vertices with the same position and UV belong to the same island.
        private static int[] FindIslands(MeshData m, out int count) {
            var bounds = GeometryUtility.CalculateBounds(m.vertices, Matrix4x4.identity);
            float scale = 1e6f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 1e-12f);
            var weld = new Dictionary<(int, int, int, int, int), int>();
            var welded = new int[m.vertices.Length];
            for (int i = 0; i < welded.Length; i++) {
                Vector3 p = m.vertices[i] - bounds.min;
                Vector2 t = m.uv[i];
                var key = (Q(p.x, scale), Q(p.y, scale), Q(p.z, scale), Q(t.x, 1e6f), Q(t.y, 1e6f));
                if (!weld.TryGetValue(key, out welded[i])) {
                    welded[i] = weld.Count;
                    weld.Add(key, welded[i]);
                }
            }

            var parent = Enumerable.Range(0, weld.Count).ToArray();
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            var tris = m.triangles;
            for (int t = 0; t < tris.Length; t += 3) {
                int a = Find(welded[tris[t]]);
                parent[Find(welded[tris[t + 1]])] = a;
                parent[Find(welded[tris[t + 2]])] = a;
            }

            var islandOfRoot = new Dictionary<int, int>();
            var triIsland = new int[tris.Length / 3];
            for (int t = 0; t < triIsland.Length; t++) {
                int root = Find(welded[tris[t * 3]]);
                if (!islandOfRoot.TryGetValue(root, out int k)) {
                    k = islandOfRoot.Count;
                    islandOfRoot.Add(root, k);
                }
                triIsland[t] = k;
            }
            count = islandOfRoot.Count;
            return triIsland;
        }

        private static int Q(float v, float scale) => Mathf.RoundToInt(v * scale);

        // ---------------------------------------------------------------- gradient map

        /// <summary>
        /// Writes "&lt;Garment&gt; Gradient.png" next to the garment: a 16-bit grayscale map running 0→1 along
        /// <paramref name="direction"/> in model space, imported as R16, and assigns it.
        /// </summary>
        public static void GenerateGradientMap(WardrobeGarment g, int size, GradientDirection direction) {
            if (g.mesh == null) throw new InvalidOperationException("Assign the garment's mesh first.");
            var m = ReadMesh(g.mesh);
            Vector3 axis = Axis(direction);

            var along = new float[m.vertices.Length];
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < along.Length; i++) {
                along[i] = Vector3.Dot(m.vertices[i], axis);
                min = Mathf.Min(min, along[i]);
                max = Mathf.Max(max, along[i]);
            }
            float range = Mathf.Max(max - min, 1e-6f);

            var values = new float[size * size];
            var filled = new bool[size * size];
            var tris = m.triangles;
            for (int t = 0; t < tris.Length; t += 3) {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                float ta = (along[a] - min) / range, tb = (along[b] - min) / range, tc = (along[c] - min) / range;
                Raster(m.uv[a] * size, m.uv[b] * size, m.uv[c] * size, size,
                    (i, w0, w1, w2) => { values[i] = w0 * ta + w1 * tb + w2 * tc; filled[i] = true; });
            }
            Dilate(values, filled, size, PaddingFor(size));

            var data = new ushort[size * size];
            for (int i = 0; i < data.Length; i++) data[i] = (ushort)Mathf.RoundToInt(Mathf.Clamp01(values[i]) * 65535f);

            string path = $"{WardrobeAssets.FolderOf(g)}/{WardrobeAssets.FileName(WardrobeAssets.BaseName(g) + " Gradient")}.png";
            var gradientMap = WardrobeAssets.WriteTexture(path, Png16.EncodeGray(data, size, size), ti => {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = Mathf.Max(32, size);
                foreach (string platform in new[] { "Standalone", "Android", "iPhone" })
                    ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings {
                        name = platform, overridden = true, maxTextureSize = Mathf.Max(32, size),
                        format = TextureImporterFormat.R16, textureCompression = TextureImporterCompression.Uncompressed,
                    });
            });

            Undo.RecordObject(g, "Generate Gradient Map");
            g.gradientMap = gradientMap;
            EditorUtility.SetDirty(g);
            AssetDatabase.SaveAssetIfDirty(g);
        }

        private static Vector3 Axis(GradientDirection d) {
            switch (d) {
                case GradientDirection.TopToBottom: return Vector3.down;
                case GradientDirection.BottomToTop: return Vector3.up;
                case GradientDirection.PositiveToNegativeX: return Vector3.left;
                case GradientDirection.NegativeToPositiveX: return Vector3.right;
                case GradientDirection.FrontToBack: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        // ---------------------------------------------------------------- shared

        private static int PaddingFor(int size) => Mathf.Max(4, size / 128);

        // Vertices come back in the model's space (as it appears when dropped in a scene), not the mesh's own:
        // FBX imports often keep the mesh Z-up and put the axis conversion on the node.
        private static MeshData ReadMesh(Mesh mesh) {
            var data = ReadRawMesh(mesh);
            var matrix = ModelMatrix(mesh);
            for (int i = 0; i < data.vertices.Length; i++) data.vertices[i] = matrix.MultiplyPoint3x4(data.vertices[i]);
            return data;
        }

        private static Matrix4x4 ModelMatrix(Mesh mesh) {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(mesh));
            if (model == null) return Matrix4x4.identity;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh == mesh) return smr.transform.localToWorldMatrix;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == mesh) return filter.transform.localToWorldMatrix;
            return Matrix4x4.identity;
        }

        private static MeshData ReadRawMesh(Mesh mesh) {
            if (mesh.isReadable) return Copy(mesh);
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                throw new InvalidOperationException($"Mesh '{mesh.name}' isn't readable. Enable Read/Write on it and try again.");
            // Turn Read/Write on just long enough to read the mesh, then restore the import settings.
            importer.isReadable = true;
            importer.SaveAndReimport();
            try {
                var readable = mesh.isReadable ? mesh
                    : AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(x => x.name == mesh.name) ?? mesh;
                return Copy(readable);
            } finally {
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
        }

        private static MeshData Copy(Mesh mesh) {
            var data = new MeshData { vertices = mesh.vertices, uv = mesh.uv, triangles = mesh.triangles };
            if (data.uv == null || data.uv.Length != data.vertices.Length)
                throw new InvalidOperationException($"Mesh '{mesh.name}' has no UVs.");
            return data;
        }

        // Calls plot(pixelIndex, w0, w1, w2) for every pixel centre inside the triangle (coordinates in pixels).
        private static void Raster(Vector2 a, Vector2 b, Vector2 c, int size, Action<int, float, float, float> plot) {
            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-9f) return;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
            const float eps = -1e-4f;
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++) {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = Edge(b, c, p) / area, w1 = Edge(c, a, p) / area, w2 = Edge(a, b, p) / area;
                if (w0 >= eps && w1 >= eps && w2 >= eps) plot(y * size + x, w0, w1, w2);
            }
        }

        private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        // Grows filled pixels outwards so mip maps and filtering don't pull in the background at UV seams.
        private static void Dilate<T>(T[] data, bool[] filled, int size, int radius) {
            var distance = new byte[data.Length];
            var queue = new Queue<int>();
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) {
                int i = y * size + x;
                if (filled[i] && ((x > 0 && !filled[i - 1]) || (x < size - 1 && !filled[i + 1]) ||
                                  (y > 0 && !filled[i - size]) || (y < size - 1 && !filled[i + size])))
                    queue.Enqueue(i);
            }
            void Visit(int from, int to) {
                if (filled[to]) return;
                filled[to] = true;
                data[to] = data[from];
                distance[to] = (byte)(distance[from] + 1);
                queue.Enqueue(to);
            }
            while (queue.Count > 0) {
                int i = queue.Dequeue();
                if (distance[i] >= radius) continue;
                int x = i % size, y = i / size;
                if (x > 0) Visit(i, i - 1);
                if (x < size - 1) Visit(i, i + 1);
                if (y > 0) Visit(i, i - size);
                if (y < size - 1) Visit(i, i + size);
            }
        }

        /// <summary>Minimal 16-bit grayscale PNG writer (Unity's encoders only do 8 bits per channel here).</summary>
        private static class Png16 {
            private static readonly uint[] CrcTable = MakeCrcTable();

            /// <param name="data">Rows bottom-up, like Unity textures.</param>
            public static byte[] EncodeGray(ushort[] data, int width, int height) {
                int stride = width * 2;
                var raw = new byte[height * (stride + 1)];
                var previous = new byte[stride];
                var current = new byte[stride];
                for (int row = 0; row < height; row++) {
                    int y = height - 1 - row; // PNG rows run top-down
                    for (int x = 0; x < width; x++) {
                        ushort v = data[y * width + x];
                        current[x * 2] = (byte)(v >> 8);
                        current[x * 2 + 1] = (byte)v;
                    }
                    int o = row * (stride + 1);
                    raw[o] = 2; // "Up" filter: difference to the row above, which compresses smooth gradients well
                    for (int k = 0; k < stride; k++) raw[o + 1 + k] = (byte)(current[k] - previous[k]);
                    (previous, current) = (current, previous);
                }

                byte[] deflated;
                using (var ms = new MemoryStream()) {
                    using (var deflate = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) deflate.Write(raw, 0, raw.Length);
                    deflated = ms.ToArray();
                }
                var zlib = new byte[deflated.Length + 6];
                zlib[0] = 0x78;
                zlib[1] = 0x9C;
                Buffer.BlockCopy(deflated, 0, zlib, 2, deflated.Length);
                WriteBigEndian(zlib, zlib.Length - 4, Adler32(raw));

                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 16; // bit depth; colour type, compression, filter and interlace stay 0

                using (var png = new MemoryStream()) {
                    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                    WriteChunk(png, "IHDR", header);
                    WriteChunk(png, "IDAT", zlib);
                    WriteChunk(png, "IEND", new byte[0]);
                    return png.ToArray();
                }
            }

            private static void WriteChunk(Stream s, string type, byte[] data) {
                var length = new byte[4];
                WriteBigEndian(length, 0, (uint)data.Length);
                var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
                s.Write(length, 0, 4);
                s.Write(typeBytes, 0, 4);
                s.Write(data, 0, data.Length);
                uint crc = 0xFFFFFFFFu;
                foreach (byte b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
                foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
                var crcBytes = new byte[4];
                WriteBigEndian(crcBytes, 0, crc ^ 0xFFFFFFFFu);
                s.Write(crcBytes, 0, 4);
            }

            private static uint[] MakeCrcTable() {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++) {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                return table;
            }

            private static uint Adler32(byte[] data) {
                uint a = 1, b = 0;
                foreach (byte d in data) {
                    a = (a + d) % 65521;
                    b = (b + a) % 65521;
                }
                return (b << 16) | a;
            }

            private static void WriteBigEndian(byte[] buffer, int offset, uint value) {
                buffer[offset] = (byte)(value >> 24);
                buffer[offset + 1] = (byte)(value >> 16);
                buffer[offset + 2] = (byte)(value >> 8);
                buffer[offset + 3] = (byte)value;
            }
        }
    }
}
