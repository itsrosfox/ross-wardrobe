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
    /// Turns a garment + look into textures: every region in its pattern and colours, with the patterns' relief and
    /// surface, on the GPU for the live preview, and baked to PNGs + a material copy for the avatar.
    /// </summary>
    public static class WardrobeBaker {
        public const int MaxRegions = 31;
        private const int Slots = MaxRegions + 1;
        private const int AtlasWidth = 256, AtlasRows = Slots * 4;
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
            public Texture2D partTex;
            public float[] partScale; // metres per UV unit, by part index + 1
            public Vector4[] baseColor; // the garment's own average colour (as the shader sees it) per region
        }

        private static readonly Dictionary<string, IdData> IdCache = new Dictionary<string, IdData>();
        private static readonly Dictionary<int, Prepared> PrepCache = new Dictionary<int, Prepared>();
        private static Material _material;
        private static Texture2D _atlas;

        // ---------------------------------------------------------------- checks

        public static string Problem(WardrobeGarment g) {
            if (g == null) return "Assign a Wardrobe Garment.";
            if (g.baseColor == null) return $"'{g.name}' has no base colour texture.";
            if (g.idMap == null) return $"'{g.name}' has no ID map yet. Generate or assign one in its .rgarm.";
            if (g.regions.Count == 0) return $"'{g.name}' has no regions yet. Add them in its .rgarm.";
            if (Shader.Find(ShaderName) == null) return "The wardrobe bake shader is missing. Reimport the Ros' Wardrobe package.";
            return null;
        }

        public static string Problem(WardrobeRecolor rc) {
            string problem = Problem(rc.garment);
            if (problem != null) return problem;
            if (rc.target == null) return "Assign the garment's renderer.";
            var materials = rc.target.sharedMaterials;
            if (rc.materialSlot < 0 || rc.materialSlot >= materials.Length) return "The material slot doesn't exist on that renderer.";
            var type = MaterialType(rc);
            if (type == null)
                return $"No material type (.rmat) fits {rc.garment.displayName}. Ros' Wardrobe ships a PBR one; reimport the package if it's missing.";
            return type.Problem;
        }

        /// <summary>One pattern a look uses on the garment, and the regions (indices) in it.</summary>
        public class Layer {
            public WardrobePattern pattern;   // null when the look names a pattern that isn't in the project
            public WardrobePattern.Maps maps = new WardrobePattern.Maps();
            public bool uv;                   // maps laid out on the garment's UVs (else they tile)
            public List<int> regions = new List<int>();
        }

        /// <summary>A region's style in the look; regions it doesn't set are the default pattern in the garment's own colour.</summary>
        public static RegionStyle StyleOf(WardrobeGarment g, WardrobeLook look, int region) =>
            look?.Get(g.regions[region].name) ?? new RegionStyle {
                region = g.regions[region].name, pattern = "",
                colors = new List<SlotColor> { new SlotColor { color = OwnColor(g, region) } },
            };

        /// <summary>The garment's own average colour in a region (what an untouched region starts as).</summary>
        public static Color OwnColor(WardrobeGarment g, int region) {
            if (Problem(g) != null || region < 0 || region >= MaxRegions) return Color.white;
            Color c = Prepare(g).baseColor[region + 1];
            c.a = 1;
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c;
        }

        /// <summary>Colour slot <paramref name="i"/> of a style: the look's, else its pattern's starting one.</summary>
        public static SlotColor SlotOf(RegionStyle style, WardrobePattern pattern, int i) =>
            style.Slot(i) ?? pattern?.SlotAt(i)?.Default() ?? new SlotColor { color = i == 0 ? Color.white : Color.black };

        /// <summary>The patterns a look uses on the garment, each with the regions in it.</summary>
        public static List<Layer> LayersOf(WardrobeGarment g, WardrobeLook look) {
            var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < g.regions.Count && r < MaxRegions; r++) {
                string id = StyleOf(g, look, r).pattern ?? "";
                if (!layers.TryGetValue(id, out var layer)) {
                    var pattern = WardrobePatterns.Get(id);
                    layer = new Layer { pattern = pattern };
                    if (pattern != null) (layer.maps, layer.uv) = pattern.MapsFor(g);
                    layers[id] = layer;
                }
                layer.regions.Add(r);
            }
            return layers.Values.ToList();
        }

        internal static bool HasRelief(WardrobeGarment g, WardrobeLook look) => LayersOf(g, look).Any(l => l.maps.normal != null || l.maps.height != null);

        internal static bool HasSurface(WardrobeGarment g, WardrobeLook look) =>
            LayersOf(g, look).Any(l => l.maps.surface != null || l.maps.occlusion != null);

        public static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// <summary>Where baked textures/materials (and saved presets) go: see <see cref="WardrobeMaterialTypes.OutputFolder"/>.</summary>
        public static string OutputFolder(WardrobeGarment g) => WardrobeMaterialTypes.OutputFolder(g);

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
            if (p != null && p.partTex != null) Object.DestroyImmediate(p.partTex);

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

            // Part per texel and each part's real-world UV size, so fabrics tile at the same size on every part.
            var partTex = new Texture2D(id.width, id.height, TextureFormat.R8, false, true) {
                name = "Wardrobe Parts", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            partTex.SetPixelData(id.part, 0);
            partTex.Apply(false, false);
            var partScale = new float[256];
            float fallback = g.metresPerUv > 0 ? g.metresPerUv : 1f;
            for (int i = 0; i < partScale.Length; i++) partScale[i] = fallback;
            for (int i = 0; i < id.parts.Count && i < 255; i++) {
                var known = g.partScales.Find(s => Same(s.id, id.parts[i].color));
                if (known != null && known.metresPerUv > 0) partScale[i + 1] = known.metresPerUv;
            }

            p = new Prepared {
                key = key, regionTex = regionTex, partTex = partTex, partScale = partScale,
            };
            p.baseColor = BaseColors(g.baseColor, regionOf, id.width, id.height);
            PrepCache[instance] = p;
            return p;
        }

        private static string RegionsKey(WardrobeGarment g) {
            var sb = new StringBuilder().Append(g.metresPerUv).Append(':').Append(g.partScales.Count);
            foreach (var r in g.regions) {
                sb.Append('/');
                foreach (var c in r.ids) sb.Append(ColorUtility.ToHtmlStringRGB(c));
            }
            return sb.ToString();
        }

        // Average colour of the base texture inside each region, as the bake shader sees it.
        private static Vector4[] BaseColors(Texture2D baseColor, byte[] regionOf, int w, int h) {
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

            var sumColor = new Vector4[Slots];
            var count = new int[Slots];
            for (int y = 0; y < LevelSize; y++) {
                int iy = Mathf.Min(h - 1, (int)((y + 0.5f) * h / LevelSize));
                for (int x = 0; x < LevelSize; x++) {
                    int ix = Mathf.Min(w - 1, (int)((x + 0.5f) * w / LevelSize));
                    int s = regionOf[iy * w + ix];
                    if (s == 0) continue;
                    var c = px[y * LevelSize + x];
                    sumColor[s] += (Vector4)c;
                    count[s]++;
                }
            }
            var color = new Vector4[Slots];
            for (int s = 0; s < Slots; s++) color[s] = count[s] > 0 ? sumColor[s] / count[s] : Vector4.one;
            return color;
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
                    _atlas = new Texture2D(AtlasWidth, AtlasRows, TextureFormat.RGBA32, false, false) {
                        name = "Wardrobe Colour Slots", hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                    };
                return _atlas;
            }
        }

        /// <summary>Renders the look into <paramref name="rt"/>, over the garment's base texture or <paramref name="source"/> (for its alpha and untouched texels).</summary>
        public static void Render(WardrobeGarment g, WardrobeLook look, RenderTexture rt, Texture source = null) {
            var p = Prepare(g);
            var material = BakeMaterial;
            FillSlots(g, look);
            material.SetTexture("_RegionTex", p.regionTex);
            material.SetTexture("_GradTex", g.gradientMap != null ? (Texture)g.gradientMap : Texture2D.blackTexture);
            material.SetTexture("_SlotAtlas", Atlas);
            material.SetTexture("_PartTex", p.partTex);
            material.SetFloatArray("_PartScale", p.partScale);
            Blit(source != null ? source : g.baseColor, rt, null);
            foreach (var layer in LayersOf(g, look)) {
                Bind(material, g, look, layer);
                FabricPass(rt, material, 0);
            }
        }

        // Every region's 4 colour slots, one atlas row each: a flat colour, or a gradient along the gradient map.
        private static void FillSlots(WardrobeGarment g, WardrobeLook look) {
            var atlas = Atlas;
            var row = new Color32[AtlasWidth];
            for (int r = 0; r < g.regions.Count && r < MaxRegions; r++) {
                var style = StyleOf(g, look, r);
                var pattern = WardrobePatterns.Get(style.pattern);
                for (int i = 0; i < 4; i++) {
                    var slot = SlotOf(style, pattern, i);
                    bool gradient = slot.useGradient && slot.gradient != null && g.gradientMap != null;
                    for (int x = 0; x < AtlasWidth; x++) row[x] = gradient ? slot.gradient.Evaluate(x / (AtlasWidth - 1f)) : slot.color;
                    atlas.SetPixels32(0, (r + 1) * 4 + i, AtlasWidth, 1, row);
                }
            }
            atlas.Apply(false);
        }

        /// <summary>
        /// Renders the look's normal map into <paramref name="rt"/> (linear): the garment's own normal map with the
        /// relief (normal and/or height map) of every pattern the look uses blended in, or put in its place for patterns
        /// that replace it. False when no pattern it uses has relief.
        /// </summary>
        public static bool RenderNormal(WardrobeGarment g, WardrobeLook look, RenderTexture rt, Texture garmentNormal) {
            var layers = LayersOf(g, look).Where(l => l.maps.normal != null || l.maps.height != null).ToList();
            if (layers.Count == 0) return false;
            var scratch = RenderTexture.GetTemporary(4, 4);
            try {
                Render(g, look, scratch); // binds the region/part data on the material
            } finally {
                RenderTexture.ReleaseTemporary(scratch);
            }
            var material = BakeMaterial;
            Blit(garmentNormal != null ? garmentNormal : Texture2D.normalTexture, rt, material, 1);
            foreach (var layer in layers) {
                Bind(material, g, look, layer);
                FabricPass(rt, material, 2);
            }
            return true;
        }

        // Binds one pattern and marks the regions in it, with their tiling.
        private static void Bind(Material material, WardrobeGarment g, WardrobeLook look, Layer layer) {
            var on = new float[Slots];
            var tiling = new Vector4[Slots];
            var tileSize = layer.pattern != null ? layer.pattern.tileSize : new Vector2(0.01f, 0.01f);
            foreach (int r in layer.regions) {
                var style = StyleOf(g, look, r);
                on[r + 1] = 1;
                tiling[r + 1] = Tiling(tileSize, style.scale, style.rotate);
            }
            var m = layer.maps;
            material.SetFloatArray("_LayerOn", on);
            material.SetVectorArray("_Tiling", tiling);
            material.SetFloat("_PatternOn", m.weights != null ? 1 : 0);
            material.SetTexture("_PatternTex", m.weights != null ? (Texture)m.weights : Texture2D.whiteTexture);
            material.SetFloat("_PatternUV", layer.uv ? 1 : 0);
            material.SetTexture("_PatternNormal", m.normal != null ? (Texture)m.normal : Texture2D.normalTexture);
            material.SetFloat("_NormalStrength", layer.pattern != null ? layer.pattern.normalStrength : 0);
            material.SetFloat("_HasHeight", m.height != null ? 1 : 0);
            material.SetTexture("_PatternHeight", m.height != null ? (Texture)m.height : Texture2D.blackTexture);
            material.SetFloat("_HeightStrength", layer.pattern != null ? layer.pattern.heightStrength : 0);
            material.SetFloat("_ReplaceNormal", layer.pattern != null && layer.pattern.replaceNormal ? 1 : 0);
            material.SetFloat("_HasSurface", m.surface != null ? 1 : 0);
            material.SetTexture("_PatternSurface", m.surface != null ? (Texture)m.surface : Texture2D.whiteTexture);
            material.SetTexture("_PatternOcclusion", m.occlusion != null ? (Texture)m.occlusion : Texture2D.whiteTexture);
            material.SetFloat("_Sheen", layer.pattern != null ? layer.pattern.sheen : 0);
            material.SetFloat("_PatternGradOn", m.gradient != null ? 1 : 0);
            material.SetTexture("_PatternGrad", m.gradient != null ? (Texture)m.gradient : Texture2D.blackTexture);
            material.SetFloat("_AlbedoOn", m.albedo != null ? 1 : 0);
            material.SetTexture("_AlbedoTex", m.albedo != null ? (Texture)m.albedo : Texture2D.blackTexture);
            material.SetTexture("_MaskTex", m.mask != null ? (Texture)m.mask : Texture2D.whiteTexture);
        }

        // x, y: tiles per metre (scale applied); z: 1 = turned 90°.
        private static Vector4 Tiling(Vector2 tileSize, float scale, bool rotate) {
            scale = Mathf.Max(scale, 0.01f);
            return new Vector4(1f / Mathf.Max(tileSize.x * scale, 1e-4f), 1f / Mathf.Max(tileSize.y * scale, 1e-4f), rotate ? 1 : 0, 0);
        }

        /// <summary>
        /// Renders the look's surface map into <paramref name="rt"/> (linear; R metalness, G roughness, B occlusion, A sheen):
        /// the garment's own with each pattern's metalness, roughness, sheen and occlusion laid over its regions. False
        /// when no pattern it uses has a surface or occlusion map.
        /// </summary>
        public static bool RenderSurface(WardrobeGarment g, WardrobeLook look, RenderTexture rt) {
            var layers = LayersOf(g, look).Where(l => l.maps.surface != null || l.maps.occlusion != null).ToList();
            if (layers.Count == 0) return false;
            var scratch = RenderTexture.GetTemporary(4, 4);
            try {
                Render(g, look, scratch); // binds the region/part data on the material
            } finally {
                RenderTexture.ReleaseTemporary(scratch);
            }
            FillGarmentSurface(g, rt);
            var material = BakeMaterial;
            foreach (var layer in layers) {
                Bind(material, g, look, layer);
                FabricPass(rt, material, 3);
            }
            return true;
        }

        /// <summary>The garment's own surface map at its base colour's size (R metalness, G roughness, B occlusion, A sheen); the caller destroys it.</summary>
        public static RenderTexture GarmentSurface(WardrobeGarment g) {
            var size = SourceSize(g.baseColor);
            var rt = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                name = "Wardrobe Garment Surface", hideFlags = HideFlags.HideAndDontSave,
            };
            FillGarmentSurface(g, rt);
            return rt;
        }

        internal static void FillGarmentSurface(WardrobeGarment g, RenderTexture rt) {
            var maps = new[] { g.metalness, g.roughness, g.occlusion, g.sheen };
            var fallbacks = new[] { 0f, 0.8f, 1f, 0.2f };
            var material = BakeMaterial;
            var channel = Vector4.zero;
            var invert = Vector4.zero;
            var value = Vector4.zero;
            for (int c = 0; c < 4; c++) {
                var m = maps[c];
                bool texture = m != null && m.texture != null && m.channel != TextureChannel.None;
                material.SetTexture("_Pack" + c, texture ? (Texture)m.texture : Texture2D.whiteTexture);
                channel[c] = texture ? (int)m.channel : -1;
                invert[c] = texture && m.invert ? 1 : 0;
                value[c] = m != null ? m.value : fallbacks[c];
            }
            material.SetVector("_PackChannel", channel);
            material.SetVector("_PackInvert", invert);
            material.SetVector("_PackValue", value);
            Blit(Texture2D.whiteTexture, rt, material, 4);
        }

        /// <summary>
        /// Packs a material type's texture into <paramref name="rt"/>: each channel from the albedo (the look's, or the
        /// garment's), the garment's opacity, or the surface map (R metalness, G roughness, B occlusion, A sheen).
        /// </summary>
        public static void Pack(WardrobeGarment g, WardrobeMaterialType.Output output, Texture albedo, Texture surface, RenderTexture rt) {
            var material = BakeMaterial;
            var channel = Vector4.zero;
            var invert = Vector4.zero;
            var value = Vector4.zero;
            for (int c = 0; c < 4; c++) {
                var source = output.albedo ? (c < 3 ? WardrobeMaterialType.Source.AlbedoR + c : WardrobeMaterialType.Source.Opacity) : output.channels[c];
                Texture texture = null;
                int pick = -1;
                bool flip = false;
                switch (source) {
                    case WardrobeMaterialType.Source.AlbedoR:
                    case WardrobeMaterialType.Source.AlbedoG:
                    case WardrobeMaterialType.Source.AlbedoB:
                        texture = albedo;
                        pick = source - WardrobeMaterialType.Source.AlbedoR;
                        break;
                    case WardrobeMaterialType.Source.Opacity:
                        if (WardrobeMaterialTypes.OpacityIsAlbedoAlpha(g)) {
                            texture = albedo;
                            pick = 3;
                        } else {
                            texture = g.opacity.texture;
                            pick = (int)g.opacity.channel;
                            flip = g.opacity.invert;
                        }
                        break;
                    case WardrobeMaterialType.Source.Metalness: texture = surface; pick = 0; break;
                    case WardrobeMaterialType.Source.Roughness: texture = surface; pick = 1; break;
                    case WardrobeMaterialType.Source.Smoothness: texture = surface; pick = 1; flip = true; break;
                    case WardrobeMaterialType.Source.Occlusion: texture = surface; pick = 2; break;
                    case WardrobeMaterialType.Source.Sheen: texture = surface; pick = 3; break;
                }
                if (texture == null) pick = -1;
                material.SetTexture("_Pack" + c, texture != null ? texture : Texture2D.whiteTexture);
                channel[c] = pick;
                invert[c] = flip ? 1 : 0;
                value[c] = output.albedo ? 1 : output.values[c];
            }
            material.SetVector("_PackChannel", channel);
            material.SetVector("_PackInvert", invert);
            material.SetVector("_PackValue", value);
            Blit(Texture2D.whiteTexture, rt, material, 4);
        }

        /// <summary>The look's surface map at preview size, or null when no pattern it uses has surface or occlusion maps.</summary>
        public static RenderTexture RenderPreviewSurface(WardrobeGarment g, WardrobeLook look, RenderTexture rt) {
            if (!HasSurface(g, look)) {
                if (rt != null) Object.DestroyImmediate(rt);
                return null;
            }
            rt = PreviewTarget(g, rt, "Wardrobe Preview Surface", RenderTextureReadWrite.Linear);
            RenderSurface(g, look, rt);
            return rt;
        }

        /// <summary>A material type's texture for the live preview, packed from the preview albedo and surface.</summary>
        public static RenderTexture RenderPreviewOutput(WardrobeGarment g, WardrobeMaterialType.Output output, Texture albedo, Texture surface, RenderTexture rt) {
            rt = PreviewTarget(g, rt, "Wardrobe Preview " + output.property, RenderTextureReadWrite.Linear);
            Pack(g, output, albedo, surface, rt);
            return rt;
        }

        private static RenderTexture PreviewTarget(WardrobeGarment g, RenderTexture rt, string name, RenderTextureReadWrite readWrite) {
            float scale = Mathf.Min(1f, (float)MaxPreviewSize / Mathf.Max(g.baseColor.width, g.baseColor.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(g.baseColor.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(g.baseColor.height * scale));
            if (rt != null && rt.width == w && rt.height == h) return rt;
            if (rt != null) Object.DestroyImmediate(rt);
            return new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, readWrite) {
                name = name, hideFlags = HideFlags.HideAndDontSave, useMipMap = true, autoGenerateMips = true,
            };
        }

        // Runs a pass that reads the result so far and writes the next one.
        private static void FabricPass(RenderTexture rt, Material material, int pass) {
            var temp = RenderTexture.GetTemporary(rt.descriptor);
            try {
                Blit(rt, temp, material, pass);
                Blit(temp, rt, null);
            } finally {
                RenderTexture.ReleaseTemporary(temp);
            }
        }

        /// <summary>Renders the look at preview size into a reusable render texture.</summary>
        public static RenderTexture RenderPreview(WardrobeGarment g, WardrobeLook look, RenderTexture rt) {
            rt = PreviewTarget(g, rt, "Wardrobe Preview", RenderTextureReadWrite.sRGB);
            Render(g, look, rt);
            return rt;
        }

        /// <summary>The look's normal map at preview size, or null when it uses no pattern with relief.</summary>
        public static RenderTexture RenderPreviewNormal(WardrobeGarment g, WardrobeLook look, RenderTexture rt) {
            if (!HasRelief(g, look)) {
                if (rt != null) Object.DestroyImmediate(rt);
                return null;
            }
            rt = PreviewTarget(g, rt, "Wardrobe Preview Normal", RenderTextureReadWrite.Linear);
            RenderNormal(g, look, rt, g.normalMap);
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

        /// <summary>Full-resolution look as a readable texture (over the base's source file when possible, for its alpha).</summary>
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

        /// <summary>Full-resolution normal map for a look with pattern relief (null when it has none), at the base's size.</summary>
        public static Texture2D BakeNormal(WardrobeGarment g, WardrobeLook look, Texture garmentNormal) {
            if (!HasRelief(g, look)) return null;
            var size = SourceSize(g.baseColor);
            var rt = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try {
                RenderNormal(g, look, rt, garmentNormal);
                return ReadBack(rt, TextureFormat.RGB24, true);
            } finally {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        internal static Vector2Int SourceSize(Texture2D texture) {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null) {
                importer.GetSourceTextureWidthAndHeight(out int w, out int h);
                if (w > 0 && h > 0) return new Vector2Int(w, h);
            }
            return new Vector2Int(texture.width, texture.height);
        }

        // ---------------------------------------------------------------- apply

        /// <summary>
        /// Bakes the component's look into its material type: "&lt;Garment&gt; - &lt;Look&gt;.png/.mat" in the output
        /// folder (" (&lt;Type&gt;)" for types other than the garment's default), with every texture property the look
        /// changes (albedo, relief, patterns' surface) baked and the rest as the type's material for the garment has them,
        /// and puts it on the renderer. Without <paramref name="recordUndo"/> the material change isn't an undo step
        /// (re-baking after an undo).
        /// </summary>
        public static void Apply(WardrobeRecolor rc, bool recordUndo = true) {
            string problem = Problem(rc);
            if (problem != null) throw new InvalidOperationException(problem);
            var g = rc.garment;
            var type = MaterialType(rc);
            var start = WardrobeMaterialTypes.BaseMaterial(g, type);

            string folder = OutputFolder(g);
            WardrobeAssets.EnsureFolder(folder);
            string look = string.IsNullOrWhiteSpace(rc.lookName) ? "Custom" : rc.lookName.Trim();
            bool isDefault = WardrobeMaterialTypes.For(g).FirstOrDefault() == type;
            string name = WardrobeAssets.FileName($"{WardrobeAssets.BaseName(g)} - {look}{(isDefault ? "" : $" ({type.DisplayName})")}");

            var baked = BakeTexture(g, rc.look);
            byte[] png;
            try { png = baked.EncodeToPNG(); }
            finally { Object.DestroyImmediate(baked); }
            var albedo = WardrobeAssets.WriteTexture($"{folder}/{name}.png", png, WardrobeAssets.CopySettingsFrom(g.baseColor));

            string materialPath = $"{folder}/{name}.mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (result == null) {
                result = new Material(start);
                AssetDatabase.CreateAsset(result, materialPath);
            } else {
                EditorUtility.CopySerialized(start, result);
            }
            result.name = name;

            bool surfaceLook = HasSurface(g, rc.look);
            Texture normal = null;
            RenderTexture surface = null;
            try {
                foreach (var output in type.outputs) {
                    Texture texture;
                    if (output.albedo) {
                        texture = WardrobeMaterialTypes.OpacityIsAlbedoAlpha(g) ? albedo
                            : WardrobeMaterialTypes.WritePacked(g, $"{folder}/{name} {output.property.TrimStart('_')}.png", output, albedo, null);
                    } else if (output.normal) {
                        if (!HasRelief(g, rc.look)) continue;   // the type's own (the garment's) is right
                        if (normal == null) normal = WriteNormal(g, rc.look, $"{folder}/{name}_Normal.png");
                        texture = normal;
                    } else {
                        if (!output.UsesAlbedo && !(output.UsesSurface && surfaceLook)) continue;
                        if (output.UsesSurface && surface == null) {
                            var size = SourceSize(g.baseColor);
                            surface = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
                                hideFlags = HideFlags.HideAndDontSave,
                            };
                            if (!RenderSurface(g, rc.look, surface)) FillGarmentSurface(g, surface);
                        }
                        texture = WardrobeMaterialTypes.WritePacked(g, $"{folder}/{name} {output.property.TrimStart('_')}.png", output, albedo, surface);
                    }
                    foreach (string property in output.Properties)
                        if (result.HasProperty(property)) result.SetTexture(property, texture);
                    if (!string.IsNullOrEmpty(output.keyword)) result.EnableKeyword(output.keyword);
                }
            } finally {
                if (surface != null) Object.DestroyImmediate(surface);
            }
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssetIfDirty(result);

            if (recordUndo) Undo.RecordObjects(new Object[] { rc, rc.target }, "Apply Recolour");
            var materials = rc.target.sharedMaterials;
            materials[rc.materialSlot] = result;
            rc.target.sharedMaterials = materials;
            rc.bakedTexture = albedo;
            rc.bakedMaterial = result;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc.target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc);
            EditorUtility.SetDirty(rc.target);
            EditorUtility.SetDirty(rc);
        }

        private static Texture2D WriteNormal(WardrobeGarment g, WardrobeLook look, string path) {
            var normal = BakeNormal(g, look, g.normalMap);
            byte[] png;
            try { png = normal.EncodeToPNG(); }
            finally { Object.DestroyImmediate(normal); }
            var settings = g.normalMap != null ? WardrobeAssets.CopySettingsFrom(g.normalMap) : null;
            return WardrobeAssets.WriteTexture(path, png, ti => {
                settings?.Invoke(ti);
                ti.textureType = TextureImporterType.NormalMap;
            });
        }

        /// <summary>The material type the component uses: the garment's type of that id (or name), else its default; null when none fits.</summary>
        public static WardrobeMaterialType MaterialType(WardrobeRecolor rc) =>
            rc.garment != null ? WardrobeMaterialTypes.For(rc.garment, rc.materialType) : null;

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

        internal static Color32[] SourcePixels(Texture2D texture, out int width, out int height) {
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
        private static void Blit(Texture source, RenderTexture target, Material material, int pass = 0) {
            var previous = RenderTexture.active;
            if (material != null) Graphics.Blit(source, target, material, pass);
            else Graphics.Blit(source, target);
            RenderTexture.active = previous;
        }

        internal static Texture2D ReadBack(RenderTexture rt, TextureFormat format, bool linear) {
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
