using System;
using System.Collections.Generic;
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
        public const string Garment = ".rgarm", Pattern = ".rpat", Preset = ".rpre", Avatar = ".ravt", Material = ".rmat",
            Body = ".rbody", Skin = ".rskin";
        public static readonly string[] Extensions = { Garment, Pattern, Preset, Avatar, Material, Body, Skin };

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

        public override string ToString() =>
            garment + (string.IsNullOrWhiteSpace(version) ? "" : "@" + version) + (string.IsNullOrWhiteSpace(avatar) ? "" : " on " + avatar);
    }

    // Config files coming and going: everything read from them is read again.
    internal class WardrobeConfigPostprocessor : AssetPostprocessor {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
            bool configs = imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(WardrobeConfigs.IsConfig);
            if (configs) {
                WardrobeConfigs.Changed();
                WardrobeAvatars.Forget();
                WardrobeMaterialTypes.Forget();
                WardrobePresets.Forget();
                WardrobeBodies.Forget();
                WardrobeSkins.Forget();
            }
            // patterns also depend on their textures
            if (imported.Length + deleted.Length + moved.Length > 0) {
                WardrobePatterns.Forget();
                WardrobeThumbnails.ForgetDependencies();
            }
            if (imported.Concat(deleted).Concat(moved).Any(p => string.Equals(Path.GetFileName(p), WardrobeSettings.FileName, StringComparison.OrdinalIgnoreCase)))
                WardrobeSettings.Forget();
        }
    }
}
