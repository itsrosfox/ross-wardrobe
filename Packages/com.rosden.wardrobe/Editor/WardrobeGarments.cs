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
        private static readonly string[] Order = {
            "format", "id", "name", "version", "author", "category", "slot", "bodyHides", "avatar", "prefab", "thumbnail", "mesh", "material", "maps",
            "idMap", "gradientMap", "metresPerUv", "regions", "partScales",
        };

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
            Fill(g, ReadJson(path), path);
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

        internal static JsonObject ReadJson(string path) => WardrobeJson.Parse(WardrobeMigrations.Read(path, WardrobeMigrations.Kind.Garment));

        /// <summary>Fills a garment from its file's json. <paramref name="dependsOn"/> hears every asset path it refers to.</summary>
        internal static void Fill(WardrobeGarment g, JsonObject j, string path, Action<string> dependsOn = null) {
            T Ref<T>(JsonObject o, string key) where T : Object {
                string value = o?.String(key) ?? "";
                if (value.Length == 0) return null;
                dependsOn?.Invoke(WardrobeConfigs.Resolve(path, value.Split('#')[0]));
                return WardrobeConfigs.Load<T>(path, value);
            }
            MapChannel Channel(JsonObject maps, string key, float fallback) {
                object value = maps?[key];
                if (value is JsonNumber n) return new MapChannel((float)n.Value);
                if (value is string || value is JsonObject) {
                    var o = value as JsonObject;
                    var texture = o != null ? Ref<Texture2D>(o, "texture") : Ref<Texture2D>(maps, key);
                    var c = new MapChannel(o != null ? (float)o.Number("value", fallback) : fallback) { texture = texture };
                    if (o != null) {
                        c.channel = Enum.TryParse(o.String("channel", "R").Trim().ToUpperInvariant(), out TextureChannel ch) ? ch : TextureChannel.R;
                        c.invert = o.Bool("invert");
                    }
                    return c;
                }
                return new MapChannel(fallback);
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
                ids = (r.Array("parts") ?? new List<object>()).OfType<string>().Select(Hex).Where(c => c.HasValue).Select(c => c.Value).ToList(),
            }).ToList();
            g.partScales = j.Objects("partScales").Select(p => (part: Hex(p.String("part")), scale: (float)p.Number("metresPerUv")))
                .Where(p => p.part.HasValue).Select(p => new WardrobePartScale { id = p.part.Value, metresPerUv = p.scale }).ToList();
        }

        private static Color32? Hex(string text) =>
            !string.IsNullOrWhiteSpace(text) && ColorUtility.TryParseHtmlString(text.Trim(), out var c) ? (Color32)c : (Color32?)null;

        private static string Hex(Color32 c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ---------------------------------------------------------------- writing

        /// <summary>Writes the garment into its file (keys this version doesn't know stay), and imports it again. Returns the path.</summary>
        public static string Save(WardrobeGarment g, string path = null) {
            path = path ?? PathOf(g);
            if (path == null) throw new InvalidOperationException($"'{g.name}' has no .rgarm file to save to.");
            JsonObject j;
            try {
                j = File.Exists(path) ? ReadJson(path) : new JsonObject();
            } catch (FormatException) {
                j = new JsonObject();
            }
            Write(g, j, path);
            string text = WardrobeJson.Write(j);
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == text) return path;
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
            return path;
        }

        /// <summary>The garment's values into its json (for <see cref="Save"/>, and for editors comparing drafts).</summary>
        internal static void Write(WardrobeGarment g, JsonObject j, string path) {
            void Put(JsonObject o, string key, object value, string[] order) => o.Put(key, value, order);
            void Text(JsonObject o, string key, string value, string[] order) => Put(o, key, value ?? "", order);
            string Ref(Object asset) => WardrobeConfigs.Relative(path, asset);
            object Channel(MapChannel c) {
                if (c == null) return new JsonNumber(0);
                if (c.texture == null) return new JsonNumber(Math.Round(c.value, 4));
                var o = new JsonObject();
                o["texture"] = Ref(c.texture);
                o["channel"] = (c.channel == TextureChannel.None ? TextureChannel.R : c.channel).ToString();
                if (c.invert) o["invert"] = true;
                return o;
            }

            Put(j, "format", new JsonNumber(WardrobeMigrations.Current(WardrobeMigrations.Kind.Garment)), Order);
            Text(j, "id", g.id, Order);
            Text(j, "name", g.displayName, Order);
            Text(j, "version", g.version, Order);
            Text(j, "author", g.author, Order);
            Text(j, "category", g.category, Order);
            Text(j, "slot", g.slot, Order);
            var hides = (g.bodyHides ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => (object)k.Trim()).ToList();
            if (hides.Count > 0) Put(j, "bodyHides", hides, Order);
            else j.Remove("bodyHides");
            var avatar = j.Object("avatar") ?? new JsonObject();
            avatar["id"] = g.avatarId ?? "";
            avatar["version"] = g.avatarVersion ?? "";
            Put(j, "avatar", avatar, Order);
            Text(j, "prefab", Ref(g.prefab), Order);
            Text(j, "thumbnail", Ref(g.thumbnail), Order);
            Text(j, "mesh", Ref(g.mesh), Order);
            Text(j, "material", g.defaultMaterial, Order);

            var mapOrder = new[] { "baseColor", "normal", "metalness", "roughness", "occlusion", "sheen", "opacity" };
            var maps = j.Object("maps") ?? new JsonObject();
            Put(maps, "baseColor", Ref(g.baseColor), mapOrder);
            Put(maps, "normal", Ref(g.normalMap), mapOrder);
            Put(maps, "metalness", Channel(g.metalness), mapOrder);
            Put(maps, "roughness", Channel(g.roughness), mapOrder);
            Put(maps, "occlusion", Channel(g.occlusion), mapOrder);
            Put(maps, "sheen", Channel(g.sheen), mapOrder);
            if (g.opacity != null && g.opacity.texture != null) Put(maps, "opacity", Channel(g.opacity), mapOrder);
            else maps.Remove("opacity");
            Put(j, "maps", maps, Order);

            Text(j, "idMap", Ref(g.idMap), Order);
            Text(j, "gradientMap", Ref(g.gradientMap), Order);
            Put(j, "metresPerUv", new JsonNumber(Math.Round(g.metresPerUv, 6)), Order);
            Put(j, "regions", g.regions.Select(r => {
                var o = new JsonObject();
                o["name"] = r.name ?? "";
                o["parts"] = r.ids.Select(c => (object)Hex(c)).ToList();
                return (object)o;
            }).ToList(), Order);
            Put(j, "partScales", g.partScales.Select(p => {
                var o = new JsonObject();
                o["part"] = Hex(p.id);
                o["metresPerUv"] = new JsonNumber(Math.Round(p.metresPerUv, 6));
                return (object)o;
            }).ToList(), Order);
        }

        /// <summary>The garment as its file would say it (for telling whether a draft has changes).</summary>
        internal static string Text(WardrobeGarment g, string path) {
            JsonObject j;
            try {
                j = File.Exists(path) ? ReadJson(path) : new JsonObject();
            } catch (FormatException) {
                j = new JsonObject();
            }
            Write(g, j, path);
            return WardrobeJson.Write(j);
        }
    }
}
