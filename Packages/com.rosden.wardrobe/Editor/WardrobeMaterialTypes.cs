using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// A way garments can be rendered (Toon, PBR…), read from a .rmat file. It doesn't belong to any garment: it names a
    /// shader (or a template material to copy settings from) and says which of a garment's maps go into which texture
    /// property, channel by channel. Ros' Wardrobe builds each garment's material from that, packing textures where the
    /// garment's own don't already match.
    /// </summary>
    public class WardrobeMaterialType {
        /// <summary>What a channel of a material texture holds.</summary>
        public enum Source { Value, AlbedoR, AlbedoG, AlbedoB, Opacity, Metalness, Roughness, Smoothness, Occlusion, Sheen }

        public static readonly string[] SourceNames = { "value", "albedo.r", "albedo.g", "albedo.b", "opacity", "metalness", "roughness", "smoothness", "occlusion", "sheen" };

        /// <summary>One texture property: the garment's albedo or normal map as it is, or 4 channels picked from its maps.</summary>
        public class Output {
            public string property;
            public bool albedo, normal;   // the whole (looked) albedo or normal map
            public Source[] channels = new Source[4];
            public float[] values = new float[4];
            public List<string> alsoOn = new List<string>();
            public string keyword;

            public bool UsesSurface => !albedo && !normal && channels.Any(c => c >= Source.Metalness);

            public bool UsesAlbedo => albedo || channels.Any(c => c >= Source.AlbedoR && c <= Source.Opacity);

            public IEnumerable<string> Properties => new[] { property }.Concat(alsoOn).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct();
        }

        public string id, name, version, author, description, path;
        public string shaderName;
        public Shader shader;
        public Material template;
        public List<Output> outputs = new List<Output>();
        public Dictionary<string, float> floats = new Dictionary<string, float>();
        public Dictionary<string, Color> colors = new Dictionary<string, Color>();
        public List<string> keywords = new List<string>();
        public List<WardrobeTarget> garments = new List<WardrobeTarget>();

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? id : name;

        public Shader Shader => shader != null ? shader : template != null ? template.shader : null;

        public bool Fits(WardrobeGarment g) => garments.Count == 0 || garments.Any(t => t.Matches(g));

        /// <summary>Why it can't be used, or null.</summary>
        public string Problem {
            get {
                if (Shader == null)
                    return template == null && string.IsNullOrWhiteSpace(shaderName) ? $"{path} names no shader or template material."
                        : $"{path}: the shader '{shaderName}' isn't in this project (install it, e.g. through VCC).";
                if (outputs.All(o => !o.albedo)) return $"{path} doesn't put the albedo into any texture property, so looks wouldn't show.";
                return null;
            }
        }

        public Output AlbedoOutput => outputs.FirstOrDefault(o => o.albedo);
    }

    /// <summary>Every .rmat in the project, and the materials built from them.</summary>
    public static class WardrobeMaterialTypes {
        private static List<WardrobeMaterialType> _all;

        public static List<WardrobeMaterialType> All => _all ??= Load();

        public static void Forget() => _all = null;

        public static WardrobeMaterialType Get(string id) => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(t => WardrobeConfigs.SameId(t.id, id));

        /// <summary>The usable material types for a garment, its default first.</summary>
        public static List<WardrobeMaterialType> For(WardrobeGarment g) =>
            g == null ? new List<WardrobeMaterialType>()
                : All.Where(t => t.Fits(g) && t.Problem == null)
                    .OrderBy(t => WardrobeConfigs.SameId(t.id, g.defaultMaterial) ? 0 : 1).ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The garment's type with that id, else its default.</summary>
        public static WardrobeMaterialType For(WardrobeGarment g, string id) {
            var types = For(g);
            return types.FirstOrDefault(t => WardrobeConfigs.SameId(t.id, id)) ?? types.FirstOrDefault();
        }

        // ---------------------------------------------------------------- reading

        private static List<WardrobeMaterialType> Load() {
            var list = new List<WardrobeMaterialType>();
            foreach (string path in WardrobeConfigs.Paths(WardrobeConfigs.Material)) {
                try {
                    var t = Read(path, WardrobeJson.Parse(File.ReadAllText(path)));
                    if (t.id.Length == 0) Debug.LogWarning($"[Ros' Wardrobe] {path} has no id");
                    else if (list.All(o => !WardrobeConfigs.SameId(o.id, t.id))) list.Add(t);
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {path}: {e.Message}");
                }
            }
            return list;
        }

        internal static WardrobeMaterialType Read(string path, JsonObject j) {
            var t = new WardrobeMaterialType {
                id = j.String("id").Trim(), name = j.String("name"), version = j.String("version"), author = j.String("author"),
                description = j.String("description"), path = path, shaderName = j.String("shader").Trim(),
                template = WardrobeConfigs.Load<Material>(path, j.String("template")),
            };
            if (t.shaderName.Length > 0) t.shader = Shader.Find(t.shaderName);
            foreach (var o in j.Objects("textures")) {
                var output = new WardrobeMaterialType.Output {
                    property = o.String("property").Trim(), keyword = o.String("keyword").Trim(),
                    alsoOn = (o.Array("alsoOn") ?? new List<object>()).OfType<string>().ToList(),
                };
                if (output.property.Length == 0) continue;
                object channels = o["channels"];
                if (channels is string whole) {
                    output.albedo = whole.Trim().ToLowerInvariant() == "albedo";
                    output.normal = whole.Trim().ToLowerInvariant() == "normal";
                    if (!output.albedo && !output.normal) {
                        Debug.LogWarning($"[Ros' Wardrobe] {path}: '{output.property}' takes \"{whole}\"; a whole texture is \"albedo\" or \"normal\", else list 4 channels");
                        continue;
                    }
                } else {
                    var list = channels as List<object> ?? new List<object>();
                    for (int c = 0; c < 4; c++) {
                        object v = c < list.Count ? list[c] : null;
                        (output.channels[c], output.values[c]) = ParseSource(v, c == 3 ? 1 : 0, path, output.property);
                    }
                }
                t.outputs.Add(output);
            }
            var floats = j.Object("floats");
            if (floats != null)
                for (int i = 0; i < floats.Count; i++)
                    if (floats.Values[i] is double f) t.floats[floats.Keys[i]] = (float)f;
            var colors = j.Object("colors");
            if (colors != null)
                for (int i = 0; i < colors.Count; i++)
                    if (colors.Values[i] is string s) t.colors[colors.Keys[i]] = WardrobeConfigs.Color(s, Color.white);
            t.keywords = (j.Array("keywords") ?? new List<object>()).OfType<string>().Where(k => k.Trim().Length > 0).ToList();
            t.garments = WardrobeConfigs.Targets(j);
            return t;
        }

        private static (WardrobeMaterialType.Source, float) ParseSource(object v, float fallback, string path, string property) {
            if (v is double n) return (WardrobeMaterialType.Source.Value, Mathf.Clamp01((float)n));
            if (v is string s) {
                s = s.Trim().ToLowerInvariant();
                int i = Array.IndexOf(WardrobeMaterialType.SourceNames, s);
                if (i > 0) return ((WardrobeMaterialType.Source)i, 0);
                Debug.LogWarning($"[Ros' Wardrobe] {path}: '{property}' has a channel \"{s}\", which isn't one of {string.Join(", ", WardrobeMaterialType.SourceNames.Skip(1))} or a number");
            }
            return (WardrobeMaterialType.Source.Value, fallback);
        }

        // ---------------------------------------------------------------- building a garment's material

        /// <summary>Where a garment's materials and recolours go: "Assets/Wardrobe Recolors/&lt;Avatar&gt;/&lt;Garment&gt;"
        /// (just "&lt;Garment&gt;" when it names no avatar), outside the garment's own folder, so updating or removing the
        /// garment never touches the buyer's looks, and fits of one garment for different avatars don't share a folder.</summary>
        public static string OutputFolder(WardrobeGarment g) {
            string garment = WardrobeAssets.FileName(WardrobeAssets.BaseName(g));
            if (string.IsNullOrWhiteSpace(g.avatarId)) return "Assets/Wardrobe Recolors/" + garment;
            string avatar = WardrobeAvatars.Get(g.avatarId)?.DisplayName ?? g.avatarId.Trim();
            return $"Assets/Wardrobe Recolors/{WardrobeAssets.FileName(avatar)}/{garment}";
        }

        /// <summary>
        /// The garment's material in this type, before any look: "&lt;Garment&gt; (&lt;Type&gt;).mat" in its output folder,
        /// made (or made again) when the garment, the .rmat or its template changed. Texture properties whose channels the
        /// garment's own textures already hold use those textures; others get a packed texture next to the material.
        /// </summary>
        public static Material BaseMaterial(WardrobeGarment g, WardrobeMaterialType type) => Build(g, type, OutputFolder(g), Key(g, type), false);

        // ---------------------------------------------------------------- the material the garment ships in

        /// <summary>Where the garment's shipped material goes: "&lt;Garment&gt; (&lt;Type&gt;).mat" next to the .rgarm.</summary>
        public static string ShippedPath(WardrobeGarment g, WardrobeMaterialType type) => $"{WardrobeGarments.FolderOf(g)}/{MaterialName(g, type)}.mat";

        /// <summary>Whether the shipped material is made from the garment's maps and the type as they are now.</summary>
        public static bool ShippedUpToDate(WardrobeGarment g, WardrobeMaterialType type) =>
            AssetImporter.GetAtPath(ShippedPath(g, type))?.userData == Key(g, type, true);

        /// <summary>
        /// Make Material: the garment's material in its starting type, next to the .rgarm so it ships with the garment,
        /// with the textures the type wants packed made from the garment's maps (at most <see cref="WardrobeGarment.mapSize"/>)
        /// and the type's shader from the garment's own pack when it has a copy (so buyers don't need Ros' Wardrobe).
        /// The maps are imported as what they hold first (the normal map as one, the others linear), and the material is
        /// put on the prefab's renderers that wear the garment's mesh.
        /// </summary>
        public static Material Ship(WardrobeGarment g, WardrobeMaterialType type) {
            PrepareMaps(g);
            var material = Build(g, type, WardrobeGarments.FolderOf(g), Key(g, type, true), true);
            Wear(g, material);
            return material;
        }

        // The normal map imported as a normal map, and the maps that hold data rather than colour without sRGB.
        private static void PrepareMaps(WardrobeGarment g) {
            void Change(Texture2D texture, Func<TextureImporter, bool> change) {
                if (texture == null) return;
                string path = AssetDatabase.GetAssetPath(texture);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer && change(importer)) importer.SaveAndReimport();
            }
            Change(g.normalMap, ti => {
                if (ti.textureType == TextureImporterType.NormalMap) return false;
                ti.textureType = TextureImporterType.NormalMap;
                return true;
            });
            foreach (var map in new[] { g.metalness, g.roughness, g.occlusion, g.sheen, g.opacity })
                if (map?.texture != g.baseColor)
                    Change(map?.texture, ti => {
                        if (!ti.sRGBTexture) return false;
                        ti.sRGBTexture = false;
                        return true;
                    });
        }

        // Puts the material on every slot of the prefab's renderers that wear the garment's mesh.
        private static void Wear(WardrobeGarment g, Material material) {
            if (g.prefab == null || g.mesh == null) return;
            string path = AssetDatabase.GetAssetPath(g.prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try {
                bool changed = false;
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh == g.mesh)) {
                    var slots = Enumerable.Repeat(material, Mathf.Max(1, g.mesh.subMeshCount)).ToArray();
                    if (skin.sharedMaterials.SequenceEqual(slots)) continue;
                    skin.sharedMaterials = slots;
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // A shader asset in the project with the same name, the closest to the garment (its pack's copy), else the shader itself.
        private static Shader PackShader(WardrobeGarment g, Shader shader) {
            if (shader == null) return null;
            var folder = WardrobeGarments.FolderOf(g).Split('/');
            int Shared(string path) => path.Split('/').Zip(folder, (a, b) => a == b).TakeWhile(x => x).Count();
            return AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(p => (path: p, shader: AssetDatabase.LoadAssetAtPath<Shader>(p))).Where(x => x.shader != null && x.shader.name == shader.name)
                .OrderByDescending(x => Shared(x.path)).Select(x => x.shader).FirstOrDefault() ?? shader;
        }

        private static string MaterialName(WardrobeGarment g, WardrobeMaterialType type) =>
            WardrobeAssets.FileName($"{WardrobeAssets.BaseName(g)} ({type.DisplayName})");

        // The garment's material in a type, in a folder: kept while its key matches, else made again.
        private static Material Build(WardrobeGarment g, WardrobeMaterialType type, string folder, string key, bool shipped) {
            string problem = type.Problem;
            if (problem != null) throw new InvalidOperationException(problem);
            string name = MaterialName(g, type);
            string path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var importer = material != null ? AssetImporter.GetAtPath(path) : null;
            if (material != null && importer != null && importer.userData == key) return material;

            WardrobeAssets.EnsureFolder(folder);
            bool create = material == null;
            var made = New(type);
            if (shipped) made.shader = PackShader(g, made.shader);
            int maxSize = shipped ? g.mapSize : 0;

            RenderTexture surface = null;
            try {
                foreach (var output in type.outputs) {
                    Texture texture;
                    if (output.albedo && OpacityIsAlbedoAlpha(g)) texture = g.baseColor;
                    else if (output.normal) texture = g.normalMap;
                    else {
                        texture = Existing(g, output);
                        if (texture == null) {
                            if (output.UsesSurface && surface == null) surface = WardrobeBaker.GarmentSurface(g);
                            texture = WritePacked(g, $"{folder}/{name} {Clean(output.property)}.png", output, g.baseColor, surface, maxSize);
                        }
                    }
                    foreach (string property in output.Properties)
                        if (made.HasProperty(property)) made.SetTexture(property, texture);
                    if (!string.IsNullOrEmpty(output.keyword)) {
                        if (texture != null) made.EnableKeyword(output.keyword);
                        else made.DisableKeyword(output.keyword);
                    }
                }
            } finally {
                if (surface != null) Object.DestroyImmediate(surface);
            }

            made.name = name;
            if (create) AssetDatabase.CreateAsset(material = made, path);
            else {
                EditorUtility.CopySerialized(made, material);
                Object.DestroyImmediate(made);
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssetIfDirty(material);
            importer = AssetImporter.GetAtPath(path);
            importer.userData = key;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        /// <summary>
        /// An in-memory material of this type for the garment, for thumbnails: textures the garment already has (albedo,
        /// normal, ones that match a property's channels) set, packed ones left as the template has them. The caller destroys it.
        /// </summary>
        public static Material PreviewMaterial(WardrobeGarment g, WardrobeMaterialType type) {
            var made = New(type);
            made.hideFlags = HideFlags.HideAndDontSave;
            foreach (var output in type.outputs) {
                Texture texture = output.albedo ? g.baseColor : output.normal ? g.normalMap : Existing(g, output);
                if (texture == null) continue;
                foreach (string property in output.Properties)
                    if (made.HasProperty(property)) made.SetTexture(property, texture);
                if (!string.IsNullOrEmpty(output.keyword)) made.EnableKeyword(output.keyword);
            }
            return made;
        }

        // The type's template (or shader) with its floats, colours and keywords.
        private static Material New(WardrobeMaterialType type) {
            var made = type.template != null ? new Material(type.template) : new Material(type.Shader);
            if (type.shader != null) made.shader = type.shader;
            foreach (var f in type.floats) if (made.HasProperty(f.Key)) made.SetFloat(f.Key, f.Value);
            foreach (var c in type.colors) if (made.HasProperty(c.Key)) made.SetColor(c.Key, c.Value);
            foreach (string k in type.keywords) made.EnableKeyword(k);
            return made;
        }

        private static string Clean(string property) => property.TrimStart('_');

        // Whether the garment's opacity is its base colour's alpha (so the base colour texture can be used as it is).
        internal static bool OpacityIsAlbedoAlpha(WardrobeGarment g) =>
            g.opacity == null || g.opacity.texture == null || g.opacity.texture == g.baseColor && g.opacity.channel == TextureChannel.A && !g.opacity.invert;

        // A garment texture that already holds every channel the output wants, in place (so it's used unchanged).
        private static Texture Existing(WardrobeGarment g, WardrobeMaterialType.Output output) {
            Texture2D found = null;
            for (int c = 0; c < 4; c++) {
                var (texture, channel, invert) = Channel(g, output.channels[c]);
                if (texture == null || (int)channel != c || invert) return null;
                if (found != null && texture != found) return null;
                found = texture;
            }
            return found;
        }

        /// <summary>Where a garment keeps a source: a texture, its channel and whether it holds the opposite (null texture = a constant).</summary>
        internal static (Texture2D texture, TextureChannel channel, bool invert) Channel(WardrobeGarment g, WardrobeMaterialType.Source source) {
            (Texture2D, TextureChannel, bool) From(MapChannel m, bool flip) =>
                m?.texture != null ? (m.texture, m.channel, m.invert != flip) : ((Texture2D)null, TextureChannel.None, false);
            switch (source) {
                case WardrobeMaterialType.Source.AlbedoR: return (g.baseColor, TextureChannel.R, false);
                case WardrobeMaterialType.Source.AlbedoG: return (g.baseColor, TextureChannel.G, false);
                case WardrobeMaterialType.Source.AlbedoB: return (g.baseColor, TextureChannel.B, false);
                case WardrobeMaterialType.Source.Opacity:
                    return OpacityIsAlbedoAlpha(g) ? (g.baseColor, TextureChannel.A, false) : From(g.opacity, false);
                case WardrobeMaterialType.Source.Metalness: return From(g.metalness, false);
                case WardrobeMaterialType.Source.Roughness: return From(g.roughness, false);
                case WardrobeMaterialType.Source.Smoothness: return From(g.roughness, true);
                case WardrobeMaterialType.Source.Occlusion: return From(g.occlusion, false);
                case WardrobeMaterialType.Source.Sheen: return From(g.sheen, false);
                default: return (null, TextureChannel.None, false);
            }
        }

        /// <summary>
        /// Packs an output's channels from an albedo and a surface map (R metal, G rough, B occlusion, A sheen) into a PNG,
        /// at the base colour's size (no bigger than <paramref name="maxSize"/> when it's set).
        /// </summary>
        internal static Texture2D WritePacked(WardrobeGarment g, string path, WardrobeMaterialType.Output output, Texture albedo, Texture surface,
            int maxSize = 0) {
            var size = WardrobeBaker.SourceSize(g.baseColor);
            if (maxSize > 0 && Mathf.Max(size.x, size.y) > maxSize) {
                float scale = (float)maxSize / Mathf.Max(size.x, size.y);
                size = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x * scale)), Mathf.Max(1, Mathf.RoundToInt(size.y * scale)));
            }
            bool colour = output.channels.Any(c => c >= WardrobeMaterialType.Source.AlbedoR && c <= WardrobeMaterialType.Source.AlbedoB);
            var rt = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, colour ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            byte[] png;
            try {
                WardrobeBaker.Pack(g, output, albedo, surface, rt);
                var read = WardrobeBaker.ReadBack(rt, TextureFormat.RGBA32, !colour);
                try { png = read.EncodeToPNG(); }
                finally { Object.DestroyImmediate(read); }
            } finally {
                RenderTexture.ReleaseTemporary(rt);
            }
            int importSize = maxSize > 0 ? maxSize
                : (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(g.baseColor)) as TextureImporter)?.maxTextureSize ?? 2048;
            return WardrobeAssets.WriteTexture(path, png, ti => {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = colour;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = importSize;
            });
        }

        // What the built material depends on: the .rmat, the .rgarm and the textures and template they name.
        // (Shipped: also the map size and the shader the pack has; the .rgarm's own text isn't, as making the material doesn't change it.)
        private static string Key(WardrobeGarment g, WardrobeMaterialType type, bool shipped = false) {
            var paths = new List<string> { type.path, shipped ? null : WardrobeGarments.PathOf(g), AssetDatabase.GetAssetPath(type.template) };
            paths.AddRange(new Object[] { g.baseColor, g.normalMap, g.metalness?.texture, g.roughness?.texture, g.occlusion?.texture, g.sheen?.texture, g.opacity?.texture }
                .Where(o => o != null).Select(AssetDatabase.GetAssetPath));
            var hash = new Hash128();
            hash.Append(2);   // the build itself: bump when it changes
            if (shipped) {
                hash.Append(g.mapSize);
                foreach (var map in new[] { g.metalness, g.roughness, g.occlusion, g.sheen, g.opacity }) {
                    hash.Append(map?.texture != null ? (int)map.channel : -1);
                    hash.Append(map?.invert == true ? 1 : 0);
                    hash.Append(map?.value ?? 0);
                }
                hash.Append(AssetDatabase.GetAssetPath(PackShader(g, type.Shader)));
            }
            foreach (string p in paths.Where(p => !string.IsNullOrEmpty(p)).Distinct()) {
                hash.Append(p);
                hash.Append(AssetDatabase.GetAssetDependencyHash(p).ToString());
            }
            return hash.ToString();
        }
    }
}
