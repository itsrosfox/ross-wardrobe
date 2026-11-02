using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// wardrobe.json: Ros' Wardrobe's own defaults (the pattern untouched parts start in, how the picker groups patterns,
    /// what imported fabrics, flat surfaces, the PBR material type and thumbnails start as). The package ships one; a
    /// wardrobe.json under Assets is read on top of it, so it only needs the values it changes.
    /// </summary>
    public static class WardrobeSettings {
        public const string FileName = "wardrobe.json";
        private static Data _current;

        [Serializable]
        public class Data {
            public int format = WardrobeMigrations.Current(WardrobeMigrations.Kind.Settings);
            [Tooltip("Pattern id of parts a look doesn't set (and of looks saved before patterns)")]
            public string defaultPattern = "";
            [Tooltip("Pattern groups in the order the picker shows them; other groups follow by name")]
            public string[] patternGroups = new string[0];
            public FlatSurface flatSurface = new FlatSurface();
            public ImportedFabric importedFabric = new ImportedFabric();
            public Thumbnails thumbnails = new Thumbnails();
        }

        /// <summary>The surface a material type without a surface map is taken to have.</summary>
        [Serializable]
        public class FlatSurface {
            public float metallic, smoothness, specular;
        }

        /// <summary>What a Marvelous Designer fabric's new .rpat starts with.</summary>
        [Serializable]
        public class ImportedFabric {
            public string idPrefix = "", group = "", colorName = "", color = "";
            public float tileWidth;   // millimetres; the height follows the fabric's aspect
            public float sheen;
            public SheenRule[] sheenByName = new SheenRule[0];
        }

        /// <summary>The first rule with a word in the fabric's name sets its sheen.</summary>
        [Serializable]
        public class SheenRule {
            public string[] words = new string[0];
            public float sheen;
        }

        [Serializable]
        public class Thumbnails {
            public string background = "", ambient = "";
        }

        public static Data Current => _current ??= Load();

        public static void Forget() => _current = null;

        /// <summary>Starting sheen of an imported fabric by its name.</summary>
        public static float SheenFor(string fabricName) {
            var f = Current.importedFabric;
            foreach (var rule in f.sheenByName ?? new SheenRule[0])
                if ((rule.words ?? new string[0]).Any(w => !string.IsNullOrWhiteSpace(w) && fabricName.IndexOf(w.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                    return rule.sheen;
            return f.sheen;
        }

        public static Color Color(string hex, Color fallback) =>
            !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? c : fallback;

        // The package's own first, then the project's (by path), each on top of the last.
        private static Data Load() {
            var data = new Data();
            var paths = AssetDatabase.FindAssets("wardrobe t:TextAsset").Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Where(p => string.Equals(Path.GetFileName(p), FileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.StartsWith("Packages/") ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) Debug.LogWarning($"[Ros' Wardrobe] No {FileName} found; reimport the Ros' Wardrobe package.");
            foreach (string path in paths)
                try {
                    JsonUtility.FromJsonOverwrite(WardrobeMigrations.Read(path, WardrobeMigrations.Kind.Settings), data);
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {path}: {e.Message}");
                }
            return data;
        }
    }
}
