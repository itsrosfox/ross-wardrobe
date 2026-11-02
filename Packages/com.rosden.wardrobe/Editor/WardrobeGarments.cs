using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// .rgarm files: one garment fitted to one avatar base. Ros' Wardrobe imports each as a <see cref="WardrobeGarment"/>
    /// (what worn garments refer to); the file is the source, so changes are written to it (<see cref="Save"/>) and the
    /// garment is imported again.
    /// </summary>
    public static class WardrobeGarments {
        // Garments being edited (not imported ones), and the file each is for.
        private static readonly Dictionary<WardrobeGarment, string> Drafts = new Dictionary<WardrobeGarment, string>();

        /// <summary>Every garment fit in the project.</summary>
        public static List<WardrobeGarment> All =>
            WardrobeConfigs.Paths(WardrobeConfigs.Garment).Select(AssetDatabase.LoadAssetAtPath<WardrobeGarment>).Where(g => g != null).ToList();

        /// <summary>The garment fits made for an avatar base (and those that don't name one).</summary>
        public static List<WardrobeGarment> For(WardrobeAvatar avatar) => All.Where(g => Fits(g, avatar)).ToList();

        /// <summary>The .rgarm a garment comes from (or is being edited for).</summary>
        public static string PathOf(WardrobeGarment g) {
            if (g == null) return null;
            if (Drafts.TryGetValue(g, out string path)) return path;
            path = AssetDatabase.GetAssetPath(g);
            return string.IsNullOrEmpty(path) ? null : path;
        }

        /// <summary>The folder of the garment's .rgarm, where its generated maps go.</summary>
        public static string FolderOf(WardrobeGarment g) {
            string path = PathOf(g);
            if (path == null) throw new InvalidOperationException($"'{g.name}' has no .rgarm file.");
            return Path.GetDirectoryName(path).Replace('\\', '/');
        }

        /// <summary>Whether the fit is for that avatar base (true when it doesn't name one).</summary>
        public static bool Fits(WardrobeGarment g, WardrobeAvatar avatar) =>
            string.IsNullOrWhiteSpace(g.avatarId) ||
            avatar != null && WardrobeConfigs.SameId(g.avatarId, avatar.id) && WardrobeConfigs.VersionMatches(g.avatarVersion, avatar.version);

        /// <summary>Whether the fit is for the avatar in the scene (true when it doesn't name one, or the avatar isn't recognised).</summary>
        public static bool Fits(WardrobeGarment g, GameObject avatar) {
            var known = WardrobeAvatars.Match(avatar);
            return known == null || Fits(g, known);
        }

        // ---------------------------------------------------------------- drafts

        /// <summary>An editable copy of the garment in a file, for an editor (forget it with <see cref="Release"/>).</summary>
        internal static WardrobeGarment Draft(string path) {
            var g = ScriptableObject.CreateInstance<WardrobeGarment>();
            g.hideFlags = HideFlags.DontSave;
            Drafts[g] = path;
            Fill(g, WardrobeJson.Parse(File.ReadAllText(path)), path);
            return g;
        }

        internal static void Release(WardrobeGarment draft) {
            if (draft == null) return;
            Drafts.Remove(draft);
            Object.DestroyImmediate(draft);
        }

        internal static bool IsDraft(WardrobeGarment g) => g != null && Drafts.ContainsKey(g);

        /// <summary>After a generator changed a garment: an imported one is written to its file right away; a draft waits for its editor's Save.</summary>
        public static void Changed(WardrobeGarment g) {
            if (g == null || IsDraft(g)) return;
            Save(g);
        }

        // ---------------------------------------------------------------- reading

        /// <summary>Fills a garment from its file's json. <paramref name="dependsOn"/> hears every asset path it refers to.</summary>
        internal static void Fill(WardrobeGarment g, JsonObject j, string path, Action<string> dependsOn = null) {
            T Ref<T>(JsonObject o, string key) where T : Object {
                string value = o?.String(key) ?? "";
                if (value.Length == 0) return null;
                dependsOn?.Invoke(WardrobeConfigs.Resolve(path, value.Split('#')[0]));
                return WardrobeConfigs.Load<T>(path, value);
            }
            // a number (the value everywhere), or { "texture", "channel", "invert" }
            MapChannel Channel(JsonObject maps, string key, float fallback) {
                if (maps?[key] is double value) return new MapChannel((float)value);
                if (!(maps?[key] is JsonObject o)) return new MapChannel(fallback);
                return new MapChannel(fallback) {
                    texture = Ref<Texture2D>(o, "texture"),
                    channel = Enum.TryParse(o.String("channel", "R").Trim().ToUpperInvariant(), out TextureChannel ch) ? ch : TextureChannel.R,
                    invert = o.Bool("invert"),
                };
            }

            g.id = j.String("id").Trim();
            g.displayName = j.String("name");
            g.name = string.IsNullOrWhiteSpace(g.displayName) ? Path.GetFileNameWithoutExtension(path) : g.displayName.Trim();
            g.version = j.String("version");
            g.author = j.String("author");
            g.category = j.String("category");
            g.slot = j.String("slot");
            g.bodyHides = (j.Array("bodyHides") ?? new List<object>()).OfType<string>().Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            var avatar = j.Object("avatar");
            g.avatarId = avatar?.String("id").Trim() ?? "";
            g.avatarVersion = avatar?.String("version") ?? "";
            g.prefab = Ref<GameObject>(j, "prefab");
            g.thumbnail = Ref<Texture2D>(j, "thumbnail");
            g.mesh = Ref<Mesh>(j, "mesh");
            g.defaultMaterial = j.String("material");

            var maps = j.Object("maps");
            g.baseColor = Ref<Texture2D>(maps, "baseColor");
            g.normalMap = Ref<Texture2D>(maps, "normal");
            g.metalness = Channel(maps, "metalness", 0);
            g.roughness = Channel(maps, "roughness", 0.8f);
            g.occlusion = Channel(maps, "occlusion", 1);
            g.sheen = Channel(maps, "sheen", 0.2f);
            g.opacity = Channel(maps, "opacity", 1);

            g.idMap = Ref<Texture2D>(j, "idMap");
            g.gradientMap = Ref<Texture2D>(j, "gradientMap");
            g.metresPerUv = (float)j.Number("metresPerUv", 1);
            g.regions = j.Objects("regions").Select(r => new WardrobeRegion {
                name = r.String("name", "Region"),
                ids = (r.Array("parts") ?? new List<object>()).OfType<string>().Select(Part).Where(c => c.HasValue).Select(c => c.Value).ToList(),
            }).ToList();
            g.partScales = j.Objects("partScales").Select(p => (part: Part(p.String("part")), scale: (float)p.Number("metresPerUv")))
                .Where(p => p.part.HasValue).Select(p => new WardrobePartScale { id = p.part.Value, metresPerUv = p.scale }).ToList();
        }

        // An ID map colour, "#RRGGBB".
        private static Color32? Part(string hex) =>
            !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? (Color32)c : (Color32?)null;

        // ---------------------------------------------------------------- writing

        /// <summary>Writes the garment into its file (keys this version doesn't know stay), and imports it again. Returns the path.</summary>
        public static string Save(WardrobeGarment g, string path = null) {
            path = path ?? PathOf(g);
            if (path == null) throw new InvalidOperationException($"'{g.name}' has no .rgarm file to save to.");
            string text = Text(g, path);
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text) return path;
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>The garment as its file would say it: the file's json (if it can be read) with the garment's values in it.</summary>
        internal static string Text(WardrobeGarment g, string path) {
            JsonObject j;
            try {
                j = File.Exists(path) ? WardrobeJson.Parse(File.ReadAllText(path)) : new JsonObject();
            } catch (FormatException) {
                j = new JsonObject();
            }
            string Ref(Object asset) => WardrobeConfigs.Relative(path, asset);
            object Channel(MapChannel c) {
                if (c == null) return 0.0;
                if (c.texture == null) return Math.Round(c.value, 4);
                var o = new JsonObject();
                o["texture"] = Ref(c.texture);
                o["channel"] = (c.channel == TextureChannel.None ? TextureChannel.R : c.channel).ToString();
                if (c.invert) o["invert"] = true;
                return o;
            }

            j["id"] = g.id ?? "";
            j["name"] = g.displayName ?? "";
            j["version"] = g.version ?? "";
            j["author"] = g.author ?? "";
            j["category"] = g.category ?? "";
            j["slot"] = g.slot ?? "";
            var hides = (g.bodyHides ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => (object)k.Trim()).ToList();
            if (hides.Count > 0) j["bodyHides"] = hides;
            else j.Remove("bodyHides");
            var avatar = j.Object("avatar") ?? new JsonObject();
            avatar["id"] = g.avatarId ?? "";
            avatar["version"] = g.avatarVersion ?? "";
            j["avatar"] = avatar;
            j["prefab"] = Ref(g.prefab);
            j["thumbnail"] = Ref(g.thumbnail);
            j["mesh"] = Ref(g.mesh);
            j["material"] = g.defaultMaterial ?? "";

            var maps = j.Object("maps") ?? new JsonObject();
            maps["baseColor"] = Ref(g.baseColor);
            maps["normal"] = Ref(g.normalMap);
            maps["metalness"] = Channel(g.metalness);
            maps["roughness"] = Channel(g.roughness);
            maps["occlusion"] = Channel(g.occlusion);
            maps["sheen"] = Channel(g.sheen);
            if (g.opacity != null && g.opacity.texture != null) maps["opacity"] = Channel(g.opacity);
            else maps.Remove("opacity");
            j["maps"] = maps;

            j["idMap"] = Ref(g.idMap);
            j["gradientMap"] = Ref(g.gradientMap);
            j["metresPerUv"] = Math.Round(g.metresPerUv, 6);
            j["regions"] = g.regions.Select(r => {
                var o = new JsonObject();
                o["name"] = r.name ?? "";
                o["parts"] = r.ids.Select(c => (object)WardrobeConfigs.Hex(c)).ToList();
                return (object)o;
            }).ToList();
            j["partScales"] = g.partScales.Select(p => {
                var o = new JsonObject();
                o["part"] = WardrobeConfigs.Hex(p.id);
                o["metresPerUv"] = Math.Round(p.metresPerUv, 6);
                return (object)o;
            }).ToList();
            return WardrobeJson.Write(j);
        }
    }
}
