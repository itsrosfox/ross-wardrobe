using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// A ready-made look, read from a .rpre file anywhere in the project. It lists the garments it's for (by .rgarm id
    /// and version, optionally only on one avatar; none = any garment), a pattern and colours per part (region
    /// "Collar, Hem" for several, "*" = every part no other entry names; parts the garment doesn't have are skipped),
    /// and colourways: the same patterns in other colours. Colours are
    /// "#RRGGBB", or a gradient as "#RRGGBB@0, #RRGGBB@1".
    /// </summary>
    public class WardrobePreset {
        public const string AnyRegion = "*";

        public class Part {
            public string region, pattern;
            public List<SlotColor> colors = new List<SlotColor>();
            public float scale = 1f;
            public bool rotate;
        }

        public class Colourway {
            public string name;
            public Dictionary<string, List<SlotColor>> colors = new Dictionary<string, List<SlotColor>>();   // by part region
        }

        public string id, version, name, lookName, group, description, author, jsonPath;
        public int order;
        public List<Part> parts = new List<Part>();
        public List<Colourway> colourways = new List<Colourway>();
        public List<WardrobeTarget> garments = new List<WardrobeTarget>();
        private readonly Dictionary<(WardrobeGarment, int), WardrobeLook> _looks = new Dictionary<(WardrobeGarment, int), WardrobeLook>();

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? id : name;

        /// <summary>The preset's own look (0) and its colourways (1..).</summary>
        public int Variations => 1 + colourways.Count;

        /// <summary>The name of a variation ("Lumberjack Red").</summary>
        public string VariationName(int v) =>
            v > 0 && v <= colourways.Count ? colourways[v - 1].name : string.IsNullOrWhiteSpace(lookName) ? DisplayName : lookName;

        /// <summary>The look name of a variation ("Buffalo Check - Lumberjack Red"; just the preset's name when its own look has none).</summary>
        public string LookName(int v) =>
            v <= 0 && string.IsNullOrWhiteSpace(lookName) ? DisplayName : $"{DisplayName} - {VariationName(v)}";

        /// <summary>Whether the preset is made for the garment (any garment when it doesn't list any).</summary>
        public bool Fits(WardrobeGarment g) => garments.Count == 0 || garments.Any(t => t.Matches(g));

        /// <summary>Whether the preset names this garment (not just any garment).</summary>
        public bool MadeFor(WardrobeGarment g) => garments.Count > 0 && Fits(g);

        /// <summary>The garment's regions a part covers.</summary>
        public List<string> RegionsOf(WardrobeGarment g, Part part) {
            if (g == null) return part.region == AnyRegion ? new List<string>() : Regions(part.region).ToList();
            return g.regions.Select(r => r.name).Where(r => PartFor(r) == part).ToList();
        }

        private Part PartFor(string region) =>
            parts.FirstOrDefault(p => p.region != AnyRegion && Names(p.region, region)) ?? parts.FirstOrDefault(p => p.region == AnyRegion);

        /// <summary>The regions a part's region entry names ("Collar, Hem").</summary>
        public static IEnumerable<string> Regions(string entry) => (entry ?? "").Split(',').Select(r => r.Trim()).Where(r => r.Length > 0);

        private static bool Names(string entry, string region) => Regions(entry).Contains(region);

        /// <summary>Variation <paramref name="v"/> on the garment (shared: clone it before changing it).</summary>
        public WardrobeLook Look(WardrobeGarment g, int v) {
            v = Mathf.Clamp(v, 0, colourways.Count);
            if (_looks.TryGetValue((g, v), out var look)) return look;
            look = new WardrobeLook();
            var regions = g != null ? g.regions.Select(r => r.name) : parts.Where(p => p.region != AnyRegion).SelectMany(p => Regions(p.region)).Distinct();
            foreach (string region in regions) {
                var part = PartFor(region);
                if (part == null) continue;
                var colors = part.colors;
                if (v > 0 && colourways[v - 1].colors.TryGetValue(part.region, out var other)) colors = other;
                look.styles.Add(new RegionStyle {
                    region = region, pattern = part.pattern, scale = part.scale, rotate = part.rotate,
                    colors = colors.Select(c => c.Clone()).ToList(),
                });
            }
            return _looks[(g, v)] = look;
        }

        public static bool IsPresetFile(string path) => string.Equals(Path.GetExtension(path ?? ""), WardrobeConfigs.Preset, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every .rpre in the project, read once and again whenever one changes.</summary>
    public static class WardrobePresets {
        private static int Format => WardrobeMigrations.Current(WardrobeMigrations.Kind.Preset);
        public const string SavedGroup = "Saved";
        private static List<WardrobePreset> _all;

        [Serializable]
        private class Json {
            public int format = Format;
            public string id, version, name, lookName, group, description, author;
            public int order;
            public GarmentJson[] garments = new GarmentJson[0];
            public PartJson[] parts = new PartJson[0];
            public ColourwayJson[] colourways = new ColourwayJson[0];
        }

        [Serializable]
        private class GarmentJson {
            public string garment, avatar, version;
        }

        [Serializable]
        private class PartJson {
            public string region, pattern;
            public string[] colors = new string[0];
            public float scale = 1f;
            public bool rotate;
        }

        [Serializable]
        private class ColourwayJson {
            public string name;
            public ColourwayPartJson[] parts = new ColourwayPartJson[0];
        }

        [Serializable]
        private class ColourwayPartJson {
            public string region;
            public string[] colors = new string[0];
        }

        public static List<WardrobePreset> All {
            get {
                if (_all == null) Load();
                return _all;
            }
        }

        /// <summary>The presets made for a garment, in order.</summary>
        public static List<WardrobePreset> For(WardrobeGarment g) => All.Where(p => p.Fits(g)).ToList();

        public static WardrobePreset Get(string id) =>
            string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(p => string.Equals(p.id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        public static void Forget() => _all = null;

        private static IEnumerable<string> Files() => WardrobeConfigs.Paths(WardrobeConfigs.Preset);

        private static void Load() {
            var list = new List<WardrobePreset>();
            foreach (string path in Files()) {
                var preset = Read(path);
                if (preset == null) continue;
                // the same preset shipped by several packs: offered for every garment any of them is for
                var same = list.FirstOrDefault(p => string.Equals(p.id, preset.id, StringComparison.OrdinalIgnoreCase));
                if (same == null) list.Add(preset);
                else if (same.garments.Count > 0 && preset.garments.Count == 0) same.garments.Clear();
                else if (same.garments.Count > 0)
                    same.garments.AddRange(preset.garments.Where(s => !same.garments.Any(o => o.Same(s))));
            }
            _all = list.OrderBy(p => p.order).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static Json ReadJson(string jsonPath) {
            try {
                return JsonUtility.FromJson<Json>(WardrobeMigrations.Read(jsonPath, WardrobeMigrations.Kind.Preset));
            } catch (Exception e) {
                Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {jsonPath}: {e.Message}");
                return null;
            }
        }

        private static WardrobePreset Read(string jsonPath) {
            var json = ReadJson(jsonPath);
            if (json == null) return null;
            if (string.IsNullOrWhiteSpace(json.id)) {
                Debug.LogWarning($"[Ros' Wardrobe] {jsonPath} has no id");
                return null;
            }
            var preset = new WardrobePreset {
                id = json.id.Trim(), version = json.version ?? "", name = json.name, lookName = json.lookName ?? "", group = json.group ?? "",
                description = json.description ?? "", author = json.author ?? "", order = json.order, jsonPath = jsonPath,
            };
            foreach (var g in json.garments ?? new GarmentJson[0])
                if (!string.IsNullOrWhiteSpace(g.garment))
                    preset.garments.Add(new WardrobeTarget { garment = g.garment.Trim(), avatar = (g.avatar ?? "").Trim(), version = g.version ?? "" });
            foreach (var part in json.parts ?? new PartJson[0]) {
                if (string.IsNullOrWhiteSpace(part.region) || preset.parts.Any(p => p.region == part.region.Trim())) continue;
                preset.parts.Add(new WardrobePreset.Part {
                    region = part.region.Trim(), pattern = part.pattern ?? "",
                    colors = (part.colors ?? new string[0]).Select(ParseSlot).ToList(),
                    scale = part.scale > 0 ? part.scale : 1f, rotate = part.rotate,
                });
            }
            foreach (var cw in json.colourways ?? new ColourwayJson[0]) {
                var colourway = new WardrobePreset.Colourway { name = string.IsNullOrWhiteSpace(cw.name) ? $"{preset.colourways.Count + 2}" : cw.name };
                foreach (var part in cw.parts ?? new ColourwayPartJson[0]) {
                    string region = (part.region ?? "").Trim();
                    if (preset.parts.All(p => p.region != region)) {
                        Debug.LogWarning($"[Ros' Wardrobe] {jsonPath}: colourway '{cw.name}' recolours '{part.region}', which the preset doesn't set");
                        continue;
                    }
                    colourway.colors[region] = (part.colors ?? new string[0]).Select(ParseSlot).ToList();
                }
                preset.colourways.Add(colourway);
            }
            return preset;
        }

        /// <summary>
        /// Writes a look as a .rpre for the garment (keeping the id and colourways of one already there), and
        /// returns its path.
        /// </summary>
        public static string Save(string jsonPath, string name, WardrobeLook look, WardrobeGarment g) {
            var json = File.Exists(jsonPath) ? ReadJson(jsonPath) : null;
            if (json == null)
                json = new Json {
                    id = $"custom.{(string.IsNullOrWhiteSpace(g.id) ? Kebab(WardrobeAssets.BaseName(g)) : g.id)}.{Kebab(name)}",
                    version = "1.0.0", group = SavedGroup, order = 1000,   // after the presets garments and patterns ship with
                    garments = new[] { new GarmentJson { garment = g.id ?? "", avatar = g.avatarId ?? "", version = WardrobeConfigs.Major(g.version) } },
                };
            json.name = name;
            json.parts = look.styles.Where(s => s != null && !string.IsNullOrWhiteSpace(s.region)).Select(s => new PartJson {
                region = s.region, pattern = s.pattern ?? "", scale = s.scale, rotate = s.rotate,
                colors = (s.colors ?? new List<SlotColor>()).Select(FormatSlot).ToArray(),
            }).ToArray();
            Write(jsonPath, json);
            return jsonPath;
        }

        /// <summary>Renames a region in every .rpre made for the garment, so they keep working.</summary>
        public static void RenameRegion(WardrobeGarment g, string from, string to) {
            foreach (var preset in For(g).Where(p => p.garments.Count > 0)) {
                var json = ReadJson(preset.jsonPath);
                if (json == null) continue;
                bool changed = false;
                string Rename(string entry) {
                    var names = WardrobePreset.Regions(entry).ToList();
                    if (!names.Contains(from)) return entry;
                    changed = true;
                    return string.Join(", ", names.Select(n => n == from ? to : n));
                }
                foreach (var part in json.parts ?? new PartJson[0]) part.region = Rename(part.region);
                foreach (var cw in json.colourways ?? new ColourwayJson[0])
                foreach (var part in cw.parts ?? new ColourwayPartJson[0]) part.region = Rename(part.region);
                if (changed) Write(preset.jsonPath, json);
            }
        }

        private static void Write(string jsonPath, Json json) {
            File.WriteAllText(jsonPath, JsonUtility.ToJson(json, true).Replace("\r\n", "\n") + "\n");
            AssetDatabase.ImportAsset(jsonPath);
            Forget();
        }

        // "#RRGGBB", or "#RRGGBB@0, #RRGGBB@1" for a gradient.
        private static SlotColor ParseSlot(string text) {
            var stops = (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (stops.Length <= 1 && !(text ?? "").Contains("@"))
                return new SlotColor { color = WardrobeSettings.Color(stops.FirstOrDefault(), Color.white) };
            var gradient = WardrobePatterns.ParseGradient(stops);
            return new SlotColor { color = gradient.Evaluate(0), useGradient = true, gradient = gradient };
        }

        private static string FormatSlot(SlotColor c) {
            if (c == null) return "";
            if (!c.useGradient || c.gradient == null) return "#" + ColorUtility.ToHtmlStringRGB(c.color);
            return string.Join(", ", c.gradient.colorKeys.Select(k =>
                $"#{ColorUtility.ToHtmlStringRGB(k.color)}@{Math.Round(k.time, 3).ToString(CultureInfo.InvariantCulture)}"));
        }

        private static string Kebab(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    }
}
