using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The pictures in the pattern pickers: a repeating pattern as a swatch of cloth in its starting colours (about 3 cm,
    /// at least 1.5 repeats), a full or finished one as the garment wearing it (a thumbnail). Swatches are kept in
    /// Library/Ros' Wardrobe/Cache, by the pattern file and its textures, so they're only made once.
    /// </summary>
    internal static class WardrobePatternPreviews {
        private const int Size = 64;
        private const int Samples = 4;
        private const int Version = 1;   // bump when swatches are drawn differently   // per pixel and axis, so fine weaves average out like cloth seen from afar
        private static readonly Dictionary<string, Texture2D> Swatches = new Dictionary<string, Texture2D>();
        private static int _revision = -1;

        [InitializeOnLoadMethod]
        private static void Hook() => AssemblyReloadEvents.beforeAssemblyReload += Clear;

        /// <summary>The picture for the pattern on the garment; null while a thumbnail is being made (then
        /// <paramref name="repaint"/> is called).</summary>
        public static Texture Get(WardrobePattern pattern, WardrobeGarment g, string materialType, Action repaint) {
            if (pattern == null) return null;
            if (pattern.type != WardrobePattern.Kind.Simple) return WardrobeThumbnails.Get(g, Wearing(g, pattern), materialType, repaint);
            if (_revision != WardrobePatterns.Revision) {
                Clear();
                _revision = WardrobePatterns.Revision;
            }
            if (Swatches.TryGetValue(pattern.id, out var swatch) && swatch != null) return swatch;
            string file = System.IO.Path.Combine(WardrobeThumbnails.CacheFolder, "Swatches",
                Hash128.Compute($"{Version}/{pattern.id}/{WardrobeThumbnails.Dependency(pattern.jsonPath)}/{WardrobeThumbnails.Dependency(Weights(pattern))}") + ".png");
            swatch = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            if (!System.IO.File.Exists(file) || !swatch.LoadImage(System.IO.File.ReadAllBytes(file))) {
                Object.DestroyImmediate(swatch);
                swatch = Swatch(pattern);
                try {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                    System.IO.File.WriteAllBytes(file, swatch.EncodeToPNG());
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't cache a swatch: {e.Message}");
                }
            }
            Swatches[pattern.id] = swatch;
            return swatch;
        }

        // The swatch is drawn from the tiling weights, so a new weights texture makes a new swatch.
        private static string Weights(WardrobePattern pattern) =>
            pattern.tiled.weights != null ? AssetDatabase.GetAssetPath(pattern.tiled.weights) : null;

        // Every part in the pattern, in its starting colours.
        private static WardrobeLook Wearing(WardrobeGarment g, WardrobePattern pattern) {
            var look = new WardrobeLook();
            foreach (var region in g.regions)
                look.styles.Add(new RegionStyle { region = region.name, pattern = pattern.id, colors = pattern.slots.Select(s => s.Default()).ToList() });
            return look;
        }

        private static Texture2D Swatch(WardrobePattern pattern) {
            var tile = pattern.tileSize;
            float span = Mathf.Max(0.03f, 1.5f * Mathf.Max(tile.x, tile.y));
            var weights = pattern.tiled.weights != null ? Read(pattern.tiled.weights, span / tile.x, span / tile.y) : null;
            var slots = pattern.slots.Select(s => s.Default()).ToList();
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++) {
                float t = (x + 0.5f) / Size;   // gradients run across the swatch
                var c = Color.black;
                if (weights == null) c = slots[0].Evaluate(t).linear;
                else {
                    var w = weights[y * Size + x];
                    float sum = Mathf.Max(w.r + w.g + w.b + w.a, 1e-4f);
                    for (int k = 0; k < slots.Count && k < 4; k++) c += slots[k].Evaluate(t).linear * (w[k] / sum);
                }
                c.a = 1;
                pixels[y * Size + x] = c.gamma;
            }
            var swatch = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            swatch.SetPixels(pixels);
            swatch.Apply();
            return swatch;
        }

        // The texture's values over the swatch (repeated so many times across and down), averaged per pixel.
        private static Color[] Read(Texture texture, float across, float down) {
            int n = Size * Samples;
            var rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            var read = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
            try {
                Graphics.Blit(texture, rt, new Vector2(across, down), Vector2.zero);
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, n, n), 0, 0);
                read.Apply();
                var src = read.GetPixels();
                var o = new Color[Size * Size];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) o[(y / Samples) * Size + x / Samples] += src[y * n + x] / (Samples * Samples);
                return o;
            } finally {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(read);
            }
        }

        private static void Clear() {
            foreach (var s in Swatches.Values) if (s != null) Object.DestroyImmediate(s);
            Swatches.Clear();
        }
    }
}
