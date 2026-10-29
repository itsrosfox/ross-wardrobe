using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Turns a garment + look into a recoloured texture: on the GPU for the live preview, and baked to a
    /// PNG + material copy for the avatar.
    /// </summary>
    public static class WardrobeBaker {
        public const int MaxRegions = 31;
        private const int Slots = MaxRegions + 1;
        private const int AtlasWidth = 256;
        private const int LevelSize = 512;
        private const int MaxPreviewSize = 2048;
        private const string ShaderName = "Hidden/RosDen/WardrobeBake";

        /// <summary>One flat colour of an ID map.</summary>
        public class Part {
            public Color32 color;
            public int pixels;
        }

        private class IdData {
            public Hash128 hash;
            public int width, height;
            public byte[] part; // per pixel: 0 = background, otherwise index into parts + 1
            public List<Part> parts;
        }

        private class Prepared {
            public string key;
            public Texture2D regionTex;
            public float[] baseLevel;
        }

        private static readonly Dictionary<string, IdData> IdCache = new Dictionary<string, IdData>();
        private static readonly Dictionary<int, Prepared> PrepCache = new Dictionary<int, Prepared>();
        private static Material _material;
        private static Texture2D _atlas;

        // ---------------------------------------------------------------- checks

        public static string Problem(WardrobeGarment g) {
            if (g == null) return "Assign a Wardrobe Garment.";
            if (g.baseColor == null) return $"'{g.name}' has no base colour texture.";
            if (g.idMap == null) return $"'{g.name}' has no ID map yet. Generate or assign one on the garment asset.";
            if (g.regions.Count == 0) return $"'{g.name}' has no regions yet. Add them on the garment asset.";
            if (Shader.Find(ShaderName) == null) return "The wardrobe bake shader is missing. Reimport the Ros' Wardrobe package.";
            return null;
        }

        public static string Problem(WardrobeRecolor rc) {
            string problem = Problem(rc.garment);
            if (problem != null) return problem;
            if (rc.target == null) return "Assign the garment's renderer.";
            var materials = rc.target.sharedMaterials;
            if (rc.materialSlot < 0 || rc.materialSlot >= materials.Length) return "The material slot doesn't exist on that renderer.";
            var material = SourceMaterial(rc);
            if (material == null) return "There's no material to copy. Assign one to the garment asset or the renderer.";
            if (!material.HasProperty(rc.garment.textureProperty))
                return $"Material '{material.name}' has no texture property '{rc.garment.textureProperty}'.";
            return null;
        }

        public static bool IsOriginal(WardrobeLook look) =>
            look == null || look.styles.All(s => s.mode == RegionMode.Keep);

        public static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// <summary>Where baked textures/materials go: next to the garment, in "Recolors".</summary>
        public static string OutputFolder(WardrobeGarment g) {
            string path = AssetDatabase.GetAssetPath(g);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                return "Assets/Wardrobe Recolors/" + WardrobeAssets.FileName(WardrobeAssets.BaseName(g));
            string dir = Path.GetDirectoryName(path).Replace('\\', '/');
            if (dir.EndsWith("_Assets")) dir = Path.GetDirectoryName(dir).Replace('\\', '/');
            return dir + "/Recolors";
        }

        // ---------------------------------------------------------------- ID map

        /// <summary>The flat colours ("parts") of the garment's ID map, biggest first.</summary>
        public static List<Part> GetParts(WardrobeGarment g) =>
            g.idMap == null ? new List<Part>() : ReadIdData(g.idMap).parts;

        private static IdData ReadIdData(Texture2D idMap) {
            string path = AssetDatabase.GetAssetPath(idMap);
            var hash = AssetDatabase.GetAssetDependencyHash(path);
            if (IdCache.TryGetValue(path, out var cached) && cached.hash == hash) return cached;

            var px = SourcePixels(idMap, out int w, out int h);
            int n = px.Length;

            // Every distinct colour, ignoring black/transparent background.
            var index = new Dictionary<int, int>();
            var colors = new List<Color32>();
            var counts = new List<int>();
            var provisional = new int[n];
            int lastKey = int.MinValue, last = -1;
            for (int i = 0; i < n; i++) {
                var c = px[i];
                if (c.a < 128 || (c.r | c.g | c.b) == 0) { provisional[i] = -1; continue; }
                int key = (c.r << 16) | (c.g << 8) | c.b;
                if (key != lastKey) {
                    lastKey = key;
                    if (!index.TryGetValue(key, out last)) {
                        last = colors.Count;
                        index.Add(key, last);
                        colors.Add(new Color32(c.r, c.g, c.b, 255));
                        counts.Add(0);
                    }
                }
                provisional[i] = last;
                counts[last]++;
            }

            // Real parts have solid interiors; anti-aliased edge colours don't. Edges join the nearest part.
            var interior = new int[colors.Count];
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++) {
                int i = y * w + x, k = provisional[i];
                if (k >= 0 && provisional[i - 1] == k && provisional[i + 1] == k && provisional[i - w] == k && provisional[i + w] == k)
                    interior[k]++;
            }
            var majors = Enumerable.Range(0, colors.Count).Where(k => interior[k] >= 4)
                .OrderByDescending(k => counts[k]).Take(254).ToList();
            var map = new byte[colors.Count];
            for (int m = 0; m < majors.Count; m++) map[majors[m]] = (byte)(m + 1);
            for (int k = 0; k < colors.Count; k++) {
                if (map[k] != 0 || majors.Count == 0) continue;
                int best = 0, bestDistance = int.MaxValue;
                for (int m = 0; m < majors.Count; m++) {
                    var a = colors[k];
                    var b = colors[majors[m]];
                    int d = (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b);
                    if (d < bestDistance) { bestDistance = d; best = m; }
                }
                map[k] = (byte)(best + 1);
            }

            var data = new IdData {
                hash = hash, width = w, height = h, part = new byte[n],
                parts = majors.Select(k => new Part { color = colors[k], pixels = counts[k] }).ToList(),
            };
            for (int i = 0; i < n; i++) data.part[i] = provisional[i] < 0 ? (byte)0 : map[provisional[i]];
            IdCache[path] = data;
            return data;
        }

        private static Prepared Prepare(WardrobeGarment g) {
            var id = ReadIdData(g.idMap);
            string key = id.hash + "|" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(g.baseColor)) + "|" + RegionsKey(g);
            int instance = g.GetInstanceID();
            if (PrepCache.TryGetValue(instance, out var p) && p.key == key && p.regionTex != null) return p;
            if (p != null && p.regionTex != null) Object.DestroyImmediate(p.regionTex);

            var partToRegion = new byte[id.parts.Count + 1];
            for (int r = 0; r < g.regions.Count && r < MaxRegions; r++)
                foreach (var c in g.regions[r].ids) {
                    int part = id.parts.FindIndex(x => Same(x.color, c));
                    if (part >= 0) partToRegion[part + 1] = (byte)(r + 1);
                }
            var regionOf = new byte[id.part.Length];
            for (int i = 0; i < regionOf.Length; i++) regionOf[i] = partToRegion[id.part[i]];

            var regionTex = new Texture2D(id.width, id.height, TextureFormat.R8, false, true) {
                name = "Wardrobe Regions", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            regionTex.SetPixelData(regionOf, 0);
            regionTex.Apply(false, false);

            p = new Prepared { key = key, regionTex = regionTex, baseLevel = BaseLevels(g.baseColor, regionOf, id.width, id.height) };
            PrepCache[instance] = p;
            return p;
        }

        private static string RegionsKey(WardrobeGarment g) {
            var sb = new StringBuilder();
            foreach (var r in g.regions) {
                sb.Append('/');
                foreach (var c in r.ids) sb.Append(ColorUtility.ToHtmlStringRGB(c));
            }
            return sb.ToString();
        }

        // Average brightness of the base texture inside each region, as the bake shader sees it.
        private static float[] BaseLevels(Texture2D baseColor, byte[] regionOf, int w, int h) {
            var rt = RenderTexture.GetTemporary(LevelSize, LevelSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Color[] px;
            try {
                Blit(baseColor, rt, null);
                var tex = ReadBack(rt, TextureFormat.RGBAFloat, true);
                px = tex.GetPixels();
                Object.DestroyImmediate(tex);
            } finally {
                RenderTexture.ReleaseTemporary(rt);
            }

            var sum = new double[Slots];
            var count = new int[Slots];
            for (int y = 0; y < LevelSize; y++) {
                int iy = Mathf.Min(h - 1, (int)((y + 0.5f) * h / LevelSize));
                for (int x = 0; x < LevelSize; x++) {
                    int ix = Mathf.Min(w - 1, (int)((x + 0.5f) * w / LevelSize));
                    int s = regionOf[iy * w + ix];
                    if (s == 0) continue;
                    var c = px[y * LevelSize + x];
                    sum[s] += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    count[s]++;
                }
            }
            var level = new float[Slots];
            for (int s = 0; s < Slots; s++) level[s] = count[s] > 0 ? Mathf.Max((float)(sum[s] / count[s]), 1e-3f) : 1f;
            return level;
        }

        // ---------------------------------------------------------------- rendering

        private static Material BakeMaterial {
            get {
                if (_material == null) {
                    var shader = Shader.Find(ShaderName);
                    if (shader == null) throw new InvalidOperationException("The wardrobe bake shader is missing. Reimport the Ros' Wardrobe package.");
                    _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return _material;
            }
        }

        private static Texture2D Atlas {
            get {
                if (_atlas == null)
                    _atlas = new Texture2D(AtlasWidth, Slots, TextureFormat.RGBA32, false, false) {
                        name = "Wardrobe Gradients", hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                    };
                return _atlas;
            }
        }

        /// <summary>Renders the look into <paramref name="rt"/>, from the garment's base texture or <paramref name="source"/>.</summary>
        public static void Render(WardrobeGarment g, WardrobeLook look, RenderTexture rt, Texture source = null) {
            var p = Prepare(g);
            var material = BakeMaterial;
            var atlas = Atlas;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            var colors = new Vector4[Slots];
            var modes = new float[Slots];
            var row = new Color32[AtlasWidth];
            for (int r = 0; r < g.regions.Count && r < MaxRegions; r++) {
                var style = look?.Get(g.regions[r].name);
                if (style == null || style.mode == RegionMode.Keep) continue;
                int s = r + 1;
                modes[s] = (float)style.mode;
                colors[s] = linear ? style.color.linear : style.color;
                if (style.mode == RegionMode.Gradient && style.gradient != null) {
                    for (int x = 0; x < AtlasWidth; x++) row[x] = style.gradient.Evaluate(x / (AtlasWidth - 1f));
                    atlas.SetPixels32(0, s, AtlasWidth, 1, row);
                }
            }
            atlas.Apply(false);

            material.SetTexture("_RegionTex", p.regionTex);
            material.SetTexture("_GradTex", g.gradientMap != null ? (Texture)g.gradientMap : Texture2D.blackTexture);
            material.SetTexture("_GradientAtlas", atlas);
            material.SetVectorArray("_RegionColor", colors);
            material.SetFloatArray("_RegionMode", modes);
            material.SetFloatArray("_BaseLevel", p.baseLevel);
            material.SetFloat("_Detail", g.detail);
            Blit(source != null ? source : g.baseColor, rt, material);
        }

        /// <summary>Renders the look at preview size into a reusable render texture.</summary>
        public static RenderTexture RenderPreview(WardrobeGarment g, WardrobeLook look, RenderTexture rt) {
            float scale = Mathf.Min(1f, (float)MaxPreviewSize / Mathf.Max(g.baseColor.width, g.baseColor.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(g.baseColor.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(g.baseColor.height * scale));
            if (rt == null || rt.width != w || rt.height != h) {
                if (rt != null) Object.DestroyImmediate(rt);
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                    name = "Wardrobe Preview", hideFlags = HideFlags.HideAndDontSave,
                    useMipMap = true, autoGenerateMips = true,
                };
            }
            Render(g, look, rt);
            return rt;
        }

        /// <summary>Shows a texture on a renderer without touching its material (nothing is saved).</summary>
        public static void SetPreview(Renderer renderer, int slot, string property, Texture texture) {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, slot);
            block.SetTexture(property, texture);
            renderer.SetPropertyBlock(block, slot);
        }

        public static void ClearPreview(Renderer renderer, int slot) {
            if (renderer == null) return;
            renderer.SetPropertyBlock(null, slot);
        }

        /// <summary>Full-resolution recolour as a readable texture (from the base's source file when possible).</summary>
        public static Texture2D BakeTexture(WardrobeGarment g, WardrobeLook look) {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(g.baseColor)) as TextureImporter;
            bool srgb = importer == null || importer.sRGBTexture;
            bool alpha = importer != null && importer.DoesSourceTextureHaveAlpha();
            // The imported texture is usually compressed and size-capped; bake from the source file instead
            // so the result is only compressed once.
            var loaded = LoadSource(g.baseColor, !srgb);
            Texture source = loaded != null ? (Texture)loaded : g.baseColor;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            try {
                Render(g, look, rt, source);
                return ReadBack(rt, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, !srgb);
            } finally {
                RenderTexture.ReleaseTemporary(rt);
                if (loaded != null) Object.DestroyImmediate(loaded);
            }
        }

        // ---------------------------------------------------------------- apply

        /// <summary>
        /// Bakes the component's look to "&lt;Garment&gt; - &lt;Look&gt;.png/.mat" in the output folder and puts
        /// the material on the renderer. The Original look puts the garment's own material back.
        /// </summary>
        public static void Apply(WardrobeRecolor rc) {
            string problem = Problem(rc);
            if (problem != null) throw new InvalidOperationException(problem);
            var g = rc.garment;
            var original = SourceMaterial(rc);

            Material result = original;
            Texture2D texture = null;
            if (!IsOriginal(rc.look)) {
                string folder = OutputFolder(g);
                WardrobeAssets.EnsureFolder(folder);
                string look = string.IsNullOrWhiteSpace(rc.lookName) ? "Custom" : rc.lookName.Trim();
                string name = WardrobeAssets.FileName($"{WardrobeAssets.BaseName(g)} - {look}");

                var baked = BakeTexture(g, rc.look);
                byte[] png;
                try { png = baked.EncodeToPNG(); }
                finally { Object.DestroyImmediate(baked); }
                texture = WardrobeAssets.WriteTexture($"{folder}/{name}.png", png, WardrobeAssets.CopySettingsFrom(g.baseColor));

                string materialPath = $"{folder}/{name}.mat";
                result = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (result == null) {
                    result = new Material(original);
                    AssetDatabase.CreateAsset(result, materialPath);
                } else if (result != original) {
                    EditorUtility.CopySerialized(original, result);
                }
                result.name = name;
                result.SetTexture(g.textureProperty, texture);
                EditorUtility.SetDirty(result);
                AssetDatabase.SaveAssetIfDirty(result);
            }

            Undo.RecordObjects(new Object[] { rc, rc.target }, "Apply Recolour");
            var materials = rc.target.sharedMaterials;
            materials[rc.materialSlot] = result;
            rc.target.sharedMaterials = materials;
            rc.bakedTexture = texture;
            rc.bakedMaterial = texture != null ? result : null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc.target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc);
            EditorUtility.SetDirty(rc.target);
            EditorUtility.SetDirty(rc);
        }

        // The material recolours are copied from: the garment's, else whatever is on the slot right now.
        private static Material SourceMaterial(WardrobeRecolor rc) {
            if (rc.garment != null && rc.garment.material != null) return rc.garment.material;
            var materials = rc.target != null ? rc.target.sharedMaterials : null;
            return materials != null && rc.materialSlot >= 0 && rc.materialSlot < materials.Length ? materials[rc.materialSlot] : null;
        }

        // ---------------------------------------------------------------- texture IO

        private static Texture2D LoadSource(Texture2D texture, bool linear) {
            string path = AssetDatabase.GetAssetPath(texture);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return null;
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear) { hideFlags = HideFlags.HideAndDontSave };
            if (loaded.LoadImage(File.ReadAllBytes(path))) return loaded;
            Object.DestroyImmediate(loaded);
            return null;
        }

        private static Color32[] SourcePixels(Texture2D texture, out int width, out int height) {
            var loaded = LoadSource(texture, true);
            if (loaded != null) {
                width = loaded.width;
                height = loaded.height;
                var px = loaded.GetPixels32();
                Object.DestroyImmediate(loaded);
                return px;
            }
            width = texture.width;
            height = texture.height;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try {
                Blit(texture, rt, null);
                var tex = ReadBack(rt, TextureFormat.RGBA32, true);
                var px = tex.GetPixels32();
                Object.DestroyImmediate(tex);
                return px;
            } finally {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Graphics.Blit leaves its target active, which breaks IMGUI if it happens mid-inspector.
        private static void Blit(Texture source, RenderTexture target, Material material) {
            var previous = RenderTexture.active;
            if (material != null) Graphics.Blit(source, target, material);
            else Graphics.Blit(source, target);
            RenderTexture.active = previous;
        }

        private static Texture2D ReadBack(RenderTexture rt, TextureFormat format, bool linear) {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, format, false, linear);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = previous;
            return tex;
        }
    }
}
