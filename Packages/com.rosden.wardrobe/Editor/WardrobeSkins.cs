using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// A skin (.rskin): fur colours for an avatar base, as a colour ramp from the fur's darkest to its lightest parts. It
    /// holds no textures: applying it recolours the avatar's own fur textures (the ones its .ravt lists) in the project,
    /// keeping the fur's shading, and leaves coloured and near-black parts (pads, mouth, nose, claws) as they are.
    /// </summary>
    public class WardrobeSkin {
        public string id, name, group, avatarId, path;
        public int order;
        public Gradient fur;

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path ?? id) : name;
    }

    public static class WardrobeSkins {
        private const string SourceTag = "rosden.skin.source:";
        private static readonly string[] Order = { "format", "id", "name", "group", "order", "avatar", "fur" };
        private static List<WardrobeSkin> _all;

        public static List<WardrobeSkin> All => _all ??= Load();

        public static void Forget() => _all = null;

        public static List<WardrobeSkin> For(WardrobeAvatar b) =>
            All.Where(s => b != null && WardrobeConfigs.SameId(s.avatarId, b.id))
                .OrderBy(s => s.order).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        public static WardrobeSkin Get(string idOrName) =>
            All.FirstOrDefault(s => WardrobeConfigs.SameId(s.id, idOrName)) ?? All.FirstOrDefault(s => WardrobeConfigs.SameId(s.DisplayName, idOrName));

        public static string Folder(WardrobeAvatar b) => $"Assets/Wardrobe Recolors/{WardrobeAssets.FileName(b.DisplayName)}/Skins";

        // ---------------------------------------------------------------- wearing

        /// <summary>
        /// Puts the skin on the avatar: every material using one of the base's fur textures is swapped for a copy on the
        /// recoloured texture (in Wardrobe Recolors/&lt;Avatar&gt;/Skins/&lt;Skin&gt;), baked the first time. Null = the original materials.
        /// </summary>
        public static void Apply(GameObject avatar, WardrobeAvatar b, WardrobeSkin skin) {
            if (avatar == null || b == null) return;
            var made = new Dictionary<Material, Material>();
            int group = Undo.GetCurrentGroup();
            try {
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true)) {
                    if (r.GetComponentInParent<WardrobeRecolor>() != null) continue;   // garments have their own looks
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++) {
                        var source = SourceOf(mats[i]);
                        if (source == null) continue;
                        if (!made.TryGetValue(source, out var wanted)) made[source] = wanted = skin == null ? source : SkinMaterial(source, b, skin);
                        if (wanted == null || wanted == mats[i]) continue;
                        mats[i] = wanted;
                        changed = true;
                    }
                    if (!changed) continue;
                    Undo.RecordObject(r, "Skin");
                    r.sharedMaterials = mats;
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }
            Undo.SetCurrentGroupName("Skin: " + (skin?.DisplayName ?? "Original"));
            Undo.CollapseUndoOperations(group);
        }

        /// <summary>The skin the avatar wears (by its baked folder's name), "" for the original, null when it can't tell.</summary>
        public static string Worn(GameObject avatar, WardrobeAvatar b) {
            if (avatar == null || b == null) return null;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials) {
                if (m == null) continue;
                string path = AssetDatabase.GetAssetPath(m);
                if (path.StartsWith(Folder(b) + "/")) return Path.GetFileName(Path.GetDirectoryName(path));
                if (UsesFur(m, b)) return "";
            }
            return null;
        }

        // The material a skin material was made from (the material itself when it isn't one).
        private static Material SourceOf(Material m) {
            if (m == null) return null;
            string path = AssetDatabase.GetAssetPath(m);
            var importer = string.IsNullOrEmpty(path) ? null : AssetImporter.GetAtPath(path);
            if (importer != null && importer.userData.StartsWith(SourceTag)) {
                var source = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(importer.userData.Substring(SourceTag.Length)));
                if (source != null) return source;
            }
            return m;
        }

        private static bool UsesFur(Material m, WardrobeAvatar b) =>
            m.GetTexturePropertyNames().Select(m.GetTexture).Any(t => t != null && FurOf(t, b) != null);

        private static WardrobeFur FurOf(Texture t, WardrobeAvatar b) => b.fur.FirstOrDefault(f => string.Equals(f.texture, t.name, StringComparison.OrdinalIgnoreCase));

        // A copy of the material on the skin's fur textures, or the material itself when it uses none.
        private static Material SkinMaterial(Material source, WardrobeAvatar b, WardrobeSkin skin) {
            var copy = new Material(source);
            bool any = false;
            string folder = $"{Folder(b)}/{WardrobeAssets.FileName(skin.DisplayName)}";
            foreach (string prop in source.GetTexturePropertyNames()) {
                var tex = source.GetTexture(prop);
                var fur = tex != null ? FurOf(tex, b) : null;
                if (fur == null) continue;
                var baked = Bake(tex, fur, skin, folder);
                if (baked == null) continue;
                copy.SetTexture(prop, baked);
                any = true;
            }
            if (!any) {
                Object.DestroyImmediate(copy);
                return source;
            }
            string path = $"{folder}/{WardrobeAssets.FileName(source.name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) {
                EditorUtility.CopySerialized(copy, existing);
                existing.name = Path.GetFileNameWithoutExtension(path);
                Object.DestroyImmediate(copy);
            } else {
                AssetDatabase.CreateAsset(copy, path);
                existing = copy;
            }
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = SourceTag + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            importer.SaveAndReimport();
            return existing;
        }

        // ---------------------------------------------------------------- baking

        // The fur texture recoloured with the skin, reused while the skin's colours and the fur's range are the same.
        private static Texture2D Bake(Texture source, WardrobeFur fur, WardrobeSkin skin, string folder) {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string path = $"{folder}/{WardrobeAssets.FileName(Path.GetFileNameWithoutExtension(sourcePath))}.png";
            string key = $"{Recipe(skin.fur)}|{fur.dark}|{fur.light}|{fur.span}|{AssetDatabase.AssetPathToGUID(sourcePath)}";
            var importer = AssetImporter.GetAtPath(path);
            if (importer != null && importer.userData == key) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try {
                EditorUtility.DisplayProgressBar("Ros' Wardrobe", $"Colouring {source.name} for {skin.DisplayName}…", 0.5f);
                if (!File.Exists(sourcePath) || !image.LoadImage(File.ReadAllBytes(sourcePath))) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {sourcePath} to colour it (skins work on PNG and JPG fur textures).");
                    return null;
                }
                var pixels = image.GetPixels32();
                Recolour(pixels, fur, skin.fur);
                image.SetPixels32(pixels);
                WardrobeAssets.EnsureFolder(folder);
                var baked = WardrobeAssets.WriteTexture(path, image.EncodeToPNG(), WardrobeAssets.CopySettingsFrom(source));
                importer = AssetImporter.GetAtPath(path);
                importer.userData = key;
                importer.SaveAndReimport();
                return baked;
            } finally {
                Object.DestroyImmediate(image);
            }
        }

        /// <summary>
        /// The fur's grey parts mapped through the ramp by brightness (dark → start, light → end, times the fur's span);
        /// coloured parts and, on fur that isn't itself dark, near-black ones stay as they are, with a soft edge.
        /// </summary>
        public static void Recolour(Color32[] pixels, WardrobeFur fur, Gradient ramp) {
            var lut = new Color32[256];
            for (int i = 0; i < 256; i++) lut[i] = ramp.Evaluate(i / 255f);
            float range = Mathf.Max(1, fur.light - fur.dark);
            bool darkFur = fur.dark <= 40;
            for (int i = 0; i < pixels.Length; i++) {
                var p = pixels[i];
                int max = Math.Max(p.r, Math.Max(p.g, p.b)), min = Math.Min(p.r, Math.Min(p.g, p.b));
                float lum = (p.r + p.g + p.b) / 3f;
                float w = 1 - Smooth(10, 30, max - min);
                if (!darkFur) w *= Smooth(25, 55, lum);
                if (w <= 0) continue;
                var c = lut[Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01((lum - fur.dark) / range) * fur.span * 255), 0, 255)];
                pixels[i] = new Color32((byte)Mathf.RoundToInt(p.r + (c.r - p.r) * w), (byte)Mathf.RoundToInt(p.g + (c.g - p.g) * w),
                    (byte)Mathf.RoundToInt(p.b + (c.b - p.b) * w), p.a);
            }
        }

        private static float Smooth(float a, float b, float x) {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        // ---------------------------------------------------------------- files

        /// <summary>A ramp as a .rskin writes it: "#RRGGBB@0, #RRGGBB@1".</summary>
        public static string Recipe(Gradient g) =>
            string.Join(", ", g.colorKeys.Select(k => $"#{ColorUtility.ToHtmlStringRGB(k.color)}@{Math.Round(k.time, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

        public static void Save(string path, WardrobeSkin skin) {
            var j = File.Exists(path) ? WardrobeJson.Parse(File.ReadAllText(path)) : new JsonObject();
            j.Put("format", new JsonNumber(WardrobeMigrations.Current(WardrobeMigrations.Kind.Skin)), Order);
            j.Put("id", string.IsNullOrWhiteSpace(skin.id) ? "skin." + Guid.NewGuid().ToString("N").Substring(0, 12) : skin.id, Order);
            j.Put("name", skin.name ?? "", Order);
            if (!string.IsNullOrWhiteSpace(skin.group)) j.Put("group", skin.group, Order);
            var avatar = new JsonObject();
            avatar["id"] = skin.avatarId ?? "";
            j.Put("avatar", avatar, Order);
            j.Put("fur", Recipe(skin.fur), Order);
            File.WriteAllText(path, WardrobeJson.Write(j) + "\n");
            AssetDatabase.ImportAsset(path);
        }

        private static List<WardrobeSkin> Load() {
            var list = new List<WardrobeSkin>();
            foreach (string path in WardrobeConfigs.Paths(WardrobeConfigs.Skin)) {
                try {
                    var j = WardrobeJson.Parse(WardrobeMigrations.Read(path, WardrobeMigrations.Kind.Skin));
                    var fur = WardrobePatterns.ParseGradient(j.String("fur").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray());
                    if (fur == null) {
                        Debug.LogWarning($"[Ros' Wardrobe] {path} has no fur colours");
                        continue;
                    }
                    list.Add(new WardrobeSkin {
                        id = j.String("id").Trim(), name = j.String("name").Trim(), group = j.String("group").Trim(), order = j.Int("order"),
                        avatarId = j.Object("avatar")?.String("id").Trim() ?? "", path = path, fur = fur,
                    });
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {path}: {e.Message}");
                }
            }
            return list;
        }
    }
}
