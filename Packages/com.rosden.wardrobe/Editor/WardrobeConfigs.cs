using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Ros' Wardrobe's config files: loose json files anywhere in the project, connected by id. A garment fit (.rgarm)
    /// names the avatar base it's for (.ravt); material types (.rmat), patterns (.rpat) and presets (.rpre) name the
    /// garments (and avatars) they're for. Paths inside a file are relative to it (or start with Assets/ or Packages/).
    /// </summary>
    public static class WardrobeConfigs {
        public const string Garment = ".rgarm", Pattern = ".rpat", Preset = ".rpre", Avatar = ".ravt", Material = ".rmat";
        public static readonly string[] Extensions = { Garment, Pattern, Preset, Avatar, Material };

        private static readonly Dictionary<string, List<string>> PathCache = new Dictionary<string, List<string>>();

        /// <summary>Goes up whenever a config file is added, changed, moved or removed.</summary>
        public static int Revision { get; private set; }

        public static bool IsConfig(string path) => Extensions.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());

        /// <summary>Every file in the project with the extension, by path.</summary>
        public static List<string> Paths(string extension) {
            if (!PathCache.TryGetValue(extension, out var paths)) {
                paths = AssetDatabase.GetAllAssetPaths()
                    .Where(p => string.Equals(Path.GetExtension(p), extension, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                PathCache[extension] = paths;
            }
            return paths;
        }

        internal static void Changed() {
            PathCache.Clear();
            Revision++;
        }

        // ---------------------------------------------------------------- paths

        /// <summary>A path written in a config file, as a project path: Assets/… and Packages/… as they are, else relative to the file.</summary>
        public static string Resolve(string configPath, string path) {
            path = (path ?? "").Replace('\\', '/').Trim();
            if (path.StartsWith("Assets/") || path.StartsWith("Packages/")) return path;
            var parts = Path.GetDirectoryName(configPath).Replace('\\', '/').Split('/').ToList();
            foreach (string part in path.Split('/')) {
                if (part == "." || part.Length == 0) continue;
                if (part == "..") {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                } else parts.Add(part);
            }
            return string.Join("/", parts);
        }

        /// <summary>How a config file refers to an asset: relative when it's in the file's folder (or below), else its project path.</summary>
        public static string Relative(string configPath, Object asset) {
            if (asset == null) return "";
            string path = AssetDatabase.GetAssetPath(asset);
            string dir = Path.GetDirectoryName(configPath).Replace('\\', '/') + "/";
            string relative = path.StartsWith(dir) ? path.Substring(dir.Length) : path;
            // a sub-asset (a mesh in a model): path#name
            if (!AssetDatabase.IsMainAsset(asset) && !string.IsNullOrEmpty(path)) relative += "#" + asset.name;
            return relative;
        }

        /// <summary>The asset a config file names ("path" or "path#sub-asset name"), or null.</summary>
        public static T Load<T>(string configPath, string path) where T : Object {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string sub = null;
            int hash = path.LastIndexOf('#');
            if (hash > 0) {
                sub = path.Substring(hash + 1);
                path = path.Substring(0, hash);
            }
            string full = Resolve(configPath, path);
            if (sub == null) {
                var main = AssetDatabase.LoadAssetAtPath<T>(full);
                if (main != null) return main;
            }
            return AssetDatabase.LoadAllAssetsAtPath(full).OfType<T>().FirstOrDefault(a => sub == null || a.name == sub);
        }

        // ---------------------------------------------------------------- colours

        /// <summary>A "#RRGGBB" colour, or <paramref name="fallback"/>.</summary>
        public static Color Color(string hex, Color fallback) =>
            !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? c : fallback;

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>A gradient, "#RRGGBB@0, #RRGGBB@1" (a colour at each position), or null when it names no colour.</summary>
        public static Gradient Gradient(string text) {
            var stops = (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Take(8).ToArray();
            if (stops.Length == 0) return null;
            var keys = stops.Select((stop, i) => {
                var parts = stop.Split('@');
                float t = stops.Length == 1 ? 0 : (float)i / (stops.Length - 1);
                if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float at)) t = at;
                return new GradientColorKey(Color(parts[0], UnityEngine.Color.white), Mathf.Clamp01(t));
            }).ToArray();
            var gradient = new Gradient();
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }

        public static string Write(Gradient g) =>
            string.Join(", ", g.colorKeys.Select(k => $"{Hex(k.color)}@{Math.Round(k.time, 3).ToString(CultureInfo.InvariantCulture)}"));

        /// <summary>A colour slot as presets write it: a colour, or a gradient when it has an @.</summary>
        public static SlotColor Slot(string text) {
            if ((text ?? "").Contains("@")) {
                var gradient = Gradient(text);
                return new SlotColor { color = gradient.Evaluate(0), useGradient = true, gradient = gradient };
            }
            return new SlotColor { color = Color(text, UnityEngine.Color.white) };
        }

        public static string Write(SlotColor c) => c.useGradient && c.gradient != null ? Write(c.gradient) : Hex(c.color);

        // ---------------------------------------------------------------- targets

        /// <summary>Which of its versions a reference means: "", "*" = any; "1" = 1.x.x, "1.2" = 1.2.x; otherwise exact.</summary>
        public static bool VersionMatches(string wanted, string actual) {
            if (string.IsNullOrWhiteSpace(wanted) || wanted.Trim() == "*") return true;
            var w = wanted.Trim().TrimEnd('*', '.').Split('.');
            var a = (actual ?? "").Trim().Split('.');
            for (int i = 0; i < w.Length; i++)
                if (i >= a.Length || a[i] != w[i]) return false;
            return true;
        }

        public static bool SameId(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>The major version, as references to it are usually written ("1.4.0" → "1").</summary>
        public static string Major(string version) => (version ?? "").Split('.')[0].Trim();

        /// <summary>The garments a config file's "garments" list names (garment, avatar, version).</summary>
        internal static List<WardrobeTarget> Targets(JsonObject j) =>
            j.Objects("garments").Where(o => o.String("garment").Trim().Length > 0).Select(o => new WardrobeTarget {
                garment = o.String("garment").Trim(), avatar = o.String("avatar").Trim(), version = o.String("version"),
            }).ToList();
    }

    /// <summary>A garment fit a pattern, preset or material type is for: garment id, optionally an avatar id, and versions.</summary>
    [Serializable]
    public class WardrobeTarget {
        public string garment, version, avatar;

        public bool Matches(WardrobeGarment g) =>
            g != null && WardrobeConfigs.SameId(garment, g.id) && WardrobeConfigs.VersionMatches(version, g.version) &&
            (string.IsNullOrWhiteSpace(avatar) || WardrobeConfigs.SameId(avatar, g.avatarId));

        public bool Same(WardrobeTarget o) =>
            WardrobeConfigs.SameId(garment, o.garment) && WardrobeConfigs.SameId(avatar, o.avatar) && (version ?? "") == (o.version ?? "");
    }

    // Config files coming and going: everything read from them is read again.
    internal class WardrobeConfigPostprocessor : AssetPostprocessor {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            if (imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(WardrobeConfigs.IsConfig)) {
                WardrobeConfigs.Changed();
                WardrobeAvatars.Forget();
                WardrobeMaterialTypes.Forget();
                WardrobePresets.Forget();
            }
            // patterns also depend on their textures
            if (imported.Length + deleted.Length + moved.Length > 0) {
                WardrobePatterns.Forget();
                WardrobeThumbnails.ForgetDependencies();
            }
        }
    }
}
