using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// A pattern a garment region can be in, read from a .rpat file anywhere in the project (Ros' Wardrobe ships Solid
    /// and Gradient in Packages/com.rosden.wardrobe/Patterns; clothing packs and anyone else bring the rest). A pattern has up to 4 colour
    /// slots, each a flat colour or (where the pattern allows it) a gradient, and optional maps:
    /// <list type="bullet">
    /// <item>weights: R, G, B, A = how much of colour slot 1..4 each texel is (no weights = slot 1 everywhere)</item>
    /// <item>normal: relief, blended into the garment's normal map (or, with <c>"normalMode": "replace"</c>, instead of it)</item>
    /// <item>height: R, relief as a height map (white = raised), turned into a normal at <c>heightStrength</c></item>
    /// <item>surface: R metalness, G roughness, A opacity (holes)</item>
    /// <item>occlusion: R, multiplied into the garment's ambient occlusion</item>
    /// </list>
    /// It's one of three types:
    /// <list type="bullet">
    /// <item>simple: top-level maps tile at <c>tileSize</c> (millimetres) on any garment (stripes, checks, weaves)</item>
    /// <item>hybrid: per garment, weights (an id mask: which colour slot goes where) laid out on its UVs, and optionally a
    /// gradient map of its own for the slots' gradients (else the garment's): raglan sleeves, colour blocks, fades</item>
    /// <item>complete: per garment, an albedo painted on its UVs (alpha, times the optional mask, = where it covers);
    /// colour slot 1 shows elsewhere: prints, graphics</item>
    /// </list>
    /// A pattern that lists garments (by .rgarm id and version, optionally only the fit for one avatar) is only offered
    /// for those; hybrid and complete ones need maps for the garment.
    /// </summary>
    public class WardrobePattern {
        public enum Kind { Simple, Hybrid, Complete }

        public class Maps {
            public Texture2D weights, normal, height, surface, occlusion;
            public Texture2D gradient;          // hybrid: position along the slots' gradients, 0..1
            public Texture2D albedo, mask;      // complete: the picture (alpha = coverage) and which texels it's on (R)
            public bool Any => weights != null || normal != null || height != null || surface != null || occlusion != null || gradient != null || albedo != null;
        }

        public class Slot {
            public string name;
            public Color color = Color.white;
            public bool canGradient, useGradient;
            public Gradient gradient;

            /// <summary>The slot as a look starts it.</summary>
            public SlotColor Default() => new SlotColor {
                color = color, useGradient = canGradient && useGradient,
                gradient = gradient != null ? SlotColor.Copy(gradient) : SlotColor.Constant(color),
            };
        }

        public class Support {
            public WardrobeTarget target;
            public Maps maps;   // laid out on the garment's UVs; null = the tiling maps
        }

        public string id, version, name, group, description, author, jsonPath;
        public Kind type;
        public bool disabled;
        public Maps tiled = new Maps();
        public Vector2 tileSize = new Vector2(0.01f, 0.01f);   // metres
        public float normalStrength = 1f, heightStrength = 1f, sheen = 0.3f;
        public bool replaceNormal;   // its relief replaces the garment's normal map instead of adding to it
        public List<Slot> slots = new List<Slot>();
        public List<Support> garments = new List<Support>();

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? id : name;

        // Its textures were unloaded under it (e.g. by a scene change), so it has to be read again.
        internal bool Unloaded => Gone(tiled.weights) || Gone(tiled.normal) || Gone(tiled.height) || Gone(tiled.surface) || Gone(tiled.occlusion);

        private static bool Gone(Texture2D t) => !ReferenceEquals(t, null) && t == null;

        public Slot SlotAt(int i) => i >= 0 && i < slots.Count ? slots[i] : null;

        /// <summary>Whether the pattern is made for the garment (any garment when it doesn't list any; a complete one
        /// needs its picture for the garment).</summary>
        public bool Fits(WardrobeGarment g) => (garments.Count == 0 || SupportFor(g) != null) && (type != Kind.Complete || MapsFor(g).maps.albedo != null);

        /// <summary>The maps to use on the garment, and whether they're laid out on its UVs (else they tile).</summary>
        public (Maps maps, bool uv) MapsFor(WardrobeGarment g) {
            var support = SupportFor(g);
            return support?.maps != null && support.maps.Any ? (support.maps, true) : (tiled, false);
        }

        // The most specific entry: one for the garment on this avatar before one for it on any avatar.
        private Support SupportFor(WardrobeGarment g) =>
            g == null ? null : garments.Where(s => s.target.Matches(g)).OrderBy(s => string.IsNullOrWhiteSpace(s.target.avatar) ? 1 : 0).FirstOrDefault();
    }

    /// <summary>Every .rpat in the project, read once and again whenever the project changes.</summary>
    public static class WardrobePatterns {
        internal static int Format => WardrobeMigrations.Current(WardrobeMigrations.Kind.Pattern);
        private static List<WardrobePattern> _all;

        /// <summary>Goes up whenever the patterns are read again, so caches keyed on it refresh.</summary>
        public static int Revision { get; private set; }

        [Serializable]
        internal class Json {
            public int format = Format;
            public string id, version, name, group, description, author;
            public bool disabled;
            public float[] tileSize = new float[0];
            public string weights, normal, height, surface, occlusion;
            public float normalStrength = 1f, heightStrength = 1f, sheen = 0.3f;
            public string normalMode = "blend";   // blend or replace
            public SlotJson[] colors = new SlotJson[0];
            public GarmentJson[] garments = new GarmentJson[0];
            public string type = "simple";   // simple, hybrid or complete
        }

        [Serializable]
        internal class SlotJson {
            public string name, color;
            public bool canGradient, useGradient;
            public string[] gradient = new string[0];   // "#RRGGBB@position"
        }

        [Serializable]
        internal class GarmentJson {
            public string garment, avatar, version, weights, normal, height, surface, occlusion, gradient, albedo, mask;
        }

        public static List<WardrobePattern> All {
            get {
                if (_all == null || _all.Any(p => p.Unloaded)) Load();
                return _all;
            }
        }

        /// <summary>The patterns offered for a garment (enabled ones that fit it), by group.</summary>
        public static List<WardrobePattern> For(WardrobeGarment g) => All.Where(p => !p.disabled && p.Fits(g)).ToList();

        /// <summary>The pattern with the id (empty = wardrobe.json's default pattern).</summary>
        public static WardrobePattern Get(string id) {
            if (string.IsNullOrWhiteSpace(id)) id = WardrobeSettings.Current.defaultPattern;
            return string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(p => string.Equals(p.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static void Forget() => _all = null;

        private static void Load() {
            var list = new List<WardrobePattern>();
            foreach (string path in WardrobeConfigs.Paths(WardrobeConfigs.Pattern)) {
                var pattern = Read(path);
                if (pattern == null) continue;
                // the same pattern shipped by several packs: offered for every garment any of them is for
                var same = list.FirstOrDefault(p => string.Equals(p.id, pattern.id, StringComparison.OrdinalIgnoreCase));
                if (same == null) list.Add(pattern);
                else if (same.garments.Count > 0 && pattern.garments.Count == 0) same.garments.Clear();
                else if (same.garments.Count > 0)
                    same.garments.AddRange(pattern.garments.Where(s => !same.garments.Any(o => o.target.Same(s.target))));
            }
            var groups = WardrobeSettings.Current.patternGroups ?? new string[0];
            _all = list.OrderBy(p => { int i = Array.IndexOf(groups, p.group); return i < 0 ? groups.Length : i; })
                .ThenBy(p => p.group).ThenBy(p => p.DisplayName).ToList();
            Revision++;
        }

        private static WardrobePattern Read(string jsonPath) {
            Json json;
            try {
                json = JsonUtility.FromJson<Json>(WardrobeMigrations.Read(jsonPath, WardrobeMigrations.Kind.Pattern));
            } catch (Exception e) {
                Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {jsonPath}: {e.Message}");
                return null;
            }
            if (json == null || string.IsNullOrWhiteSpace(json.id)) {
                Debug.LogWarning($"[Ros' Wardrobe] {jsonPath} has no id");
                return null;
            }
            var pattern = new WardrobePattern {
                id = json.id.Trim(), version = json.version ?? "", name = json.name, group = json.group ?? "",
                description = json.description ?? "", author = json.author ?? "", jsonPath = jsonPath, disabled = json.disabled,
                tiled = new WardrobePattern.Maps {
                    weights = Load(jsonPath, json.weights), normal = Load(jsonPath, json.normal), height = Load(jsonPath, json.height),
                    surface = Load(jsonPath, json.surface), occlusion = Load(jsonPath, json.occlusion),
                },
                normalStrength = json.normalStrength, heightStrength = json.heightStrength, sheen = json.sheen,
            };
            string normalMode = (json.normalMode ?? "").Trim().ToLowerInvariant();
            pattern.replaceNormal = normalMode == "replace";
            if (normalMode != "" && normalMode != "blend" && normalMode != "replace")
                Debug.LogWarning($"[Ros' Wardrobe] {jsonPath}: normalMode '{json.normalMode}' isn't blend or replace");
            string type = (json.type ?? "").Trim().ToLowerInvariant();
            if (type == "hybrid") pattern.type = WardrobePattern.Kind.Hybrid;
            else if (type == "complete") pattern.type = WardrobePattern.Kind.Complete;
            else if (type != "" && type != "simple") Debug.LogWarning($"[Ros' Wardrobe] {jsonPath}: type '{json.type}' isn't simple, hybrid or complete");
            if (json.tileSize != null && json.tileSize.Length >= 2 && json.tileSize[0] > 0 && json.tileSize[1] > 0)
                pattern.tileSize = new Vector2(json.tileSize[0], json.tileSize[1]) / 1000f;
            foreach (var s in (json.colors ?? new SlotJson[0]).Take(4)) {
                var slot = new WardrobePattern.Slot {
                    name = string.IsNullOrWhiteSpace(s.name) ? $"Colour {pattern.slots.Count + 1}" : s.name,
                    color = ParseColor(s.color, Color.white), canGradient = s.canGradient, useGradient = s.useGradient,
                };
                slot.gradient = ParseGradient(s.gradient) ?? SlotColor.Constant(slot.color);
                pattern.slots.Add(slot);
            }
            if (pattern.slots.Count == 0) pattern.slots.Add(new WardrobePattern.Slot { name = "Colour", canGradient = true, gradient = SlotColor.Constant(Color.white) });
            foreach (var g in json.garments ?? new GarmentJson[0]) {
                if (string.IsNullOrWhiteSpace(g.garment)) continue;
                var maps = new WardrobePattern.Maps {
                    weights = Load(jsonPath, g.weights), normal = Load(jsonPath, g.normal), height = Load(jsonPath, g.height),
                    surface = Load(jsonPath, g.surface), occlusion = Load(jsonPath, g.occlusion),
                    gradient = Load(jsonPath, g.gradient), albedo = Load(jsonPath, g.albedo), mask = Load(jsonPath, g.mask),
                };
                pattern.garments.Add(new WardrobePattern.Support {
                    target = new WardrobeTarget { garment = g.garment.Trim(), avatar = (g.avatar ?? "").Trim(), version = g.version ?? "" },
                    maps = maps.Any ? maps : null,
                });
            }
            return pattern;
        }

        private static Texture2D Load(string jsonPath, string path) {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full = WardrobeConfigs.Resolve(jsonPath, path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(full);
            if (texture == null) Debug.LogWarning($"[Ros' Wardrobe] {jsonPath}: couldn't find texture '{path}'");
            return texture;
        }

        private static Color ParseColor(string text, Color fallback) =>
            !string.IsNullOrWhiteSpace(text) && ColorUtility.TryParseHtmlString(text.Trim(), out var c) ? c : fallback;

        /// <summary>The garments a config file's "garments" list names (garment, avatar, version).</summary>
        internal static List<WardrobeTarget> ReadTargets(JsonObject j) =>
            j.Objects("garments").Where(o => o.String("garment").Trim().Length > 0).Select(o => new WardrobeTarget {
                garment = o.String("garment").Trim(), avatar = o.String("avatar").Trim(), version = o.String("version"),
            }).ToList();

        internal static Gradient ParseGradient(string[] keys) {
            if (keys == null || keys.Length == 0) return null;
            var colorKeys = new List<GradientColorKey>();
            for (int i = 0; i < keys.Length && colorKeys.Count < 8; i++) {
                var parts = keys[i].Split('@');
                float t = keys.Length == 1 ? 0 : (float)i / (keys.Length - 1);
                if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)) t = parsed;
                colorKeys.Add(new GradientColorKey(ParseColor(parts[0], Color.white), Mathf.Clamp01(t)));
            }
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys.ToArray(), new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }
    }
}
