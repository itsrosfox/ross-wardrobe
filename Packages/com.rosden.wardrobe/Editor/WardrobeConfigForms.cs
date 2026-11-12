using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Edits one .rpre: a ready-made look, its parts and colourways.</summary>
    internal class WardrobePresetForm : WardrobeJsonForm {

        public WardrobePresetForm(string path) : base(path) { }

        protected override ICollection<string> Always => new[] { "id", "version", "name", "group", "description", "author", "parts" };

        protected override void DrawJson(JsonObject j) {
            Header("Preset");
            Text(j, "name", "Name", "Shown in the Looks list");
            Id(j, "Stable id; don't change it once shipped", WardrobePresets.All.Where(p => p.jsonPath != path).Select(p => p.id));
            Text(j, "version", "Version");
            Text(j, "author", "Author");
            var groups = WardrobePresets.All.Select(p => p.group).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().Select(g => (g, g)).ToList();
            EditorGUI.BeginChangeCheck();
            string group = Suggest(new GUIContent("Group", "Where the Looks list shows it"), j.String("group"), groups, v => Set(j, "group", v, false));
            if (EditorGUI.EndChangeCheck()) Set(j, "group", group, false);
            Text(j, "lookName", "Look Name", "The name of its own colouring (\"Navy & White\"); the colourways have their own");
            TextArea(j, "description", "Description");
            Integer(j, "order", "Order", 0, "Lower comes first in the Looks list");

            EditorGUILayout.Space();
            Header("Garments");
            Checklist(j, "Offered for every garment (its parts name regions; the ones a garment doesn't have are skipped).",
                "Only offered for the garments ticked.");

            EditorGUILayout.Space();
            Header("Parts");
            Note("A pattern and its colours per part of the garment: a region name, several as \"Collar, Hem\", or * for every part no other " +
                 "entry names. Parts the garment doesn't have are skipped.");
            var parts = j.Array("parts") ?? new List<object>();
            for (int i = 0; i < parts.Count; i++) {
                if (!(parts[i] is JsonObject part)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUI.BeginChangeCheck();
                        string region = Suggest(new GUIContent("Region"), part.String("region"), Regions(j), v => Set(part, "region", v, false));
                        if (EditorGUI.EndChangeCheck()) Set(part, "region", region, false);
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                            parts.RemoveAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    string pattern = Suggest(new GUIContent("Pattern", "A .rpat id"), part.String("pattern"),
                        WardrobePatterns.All.Select(p => (p.id, $"{(string.IsNullOrWhiteSpace(p.group) ? "" : p.group + "/")}{p.DisplayName}  ({p.id})")),
                        v => Set(part, "pattern", v, false));
                    if (EditorGUI.EndChangeCheck()) Set(part, "pattern", pattern, false);
                    var known = WardrobePatterns.Get(part.String("pattern"));
                    if (known == null && part.String("pattern").Trim().Length > 0)
                        EditorGUILayout.LabelField("No pattern with this id in the project.", EditorStyles.miniLabel);
                    Colours(part, "colors", known);
                    EditorGUI.BeginChangeCheck();
                    float scale = EditorGUILayout.FloatField(new GUIContent("Scale", "Pattern size (1 = as made)"), (float)part.Number("scale", 1));
                    if (EditorGUI.EndChangeCheck()) Set(part, "scale", Math.Round(Mathf.Max(0.01f, scale), 3), false);
                    EditorGUI.BeginChangeCheck();
                    bool rotate = EditorGUILayout.Toggle(new GUIContent("Rotate", "Turned 90°"), part.Bool("rotate"));
                    if (EditorGUI.EndChangeCheck()) Set(part, "rotate", rotate, false);
                }
            }
            if (GUILayout.Button("Add Part", GUILayout.Width(160))) {
                var part = new JsonObject();
                part["region"] = parts.Count == 0 ? WardrobePreset.AnyRegion : "";
                part["pattern"] = WardrobePatterns.Get(null)?.id ?? "";
                part["colors"] = new List<object> { "#F4F4F2" };
                part["scale"] = 1.0;
                part["rotate"] = false;
                parts.Add(part);
                Set(j, "parts", parts, false);
            }

            EditorGUILayout.Space();
            Header("Colourways");
            Note("The same parts in other colours, offered next to the preset's own.");
            var ways = j.Array("colourways") ?? new List<object>();
            var regions = parts.OfType<JsonObject>().Select(p => p.String("region")).Where(r => r.Length > 0).ToList();
            for (int i = 0; i < ways.Count; i++) {
                if (!(ways[i] is JsonObject way)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        Text(way, "name", "Name");
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                            ways.RemoveAt(i);
                            if (ways.Count == 0) j.Remove("colourways");
                            GUIUtility.ExitGUI();
                        }
                    }
                    var wayParts = way.Array("parts") ?? new List<object>();
                    for (int k = 0; k < wayParts.Count; k++) {
                        if (!(wayParts[k] is JsonObject wp)) continue;
                        string region = wp.String("region");
                        var part = parts.OfType<JsonObject>().FirstOrDefault(p => p.String("region") == region);
                        using (new EditorGUILayout.HorizontalScope()) {
                            EditorGUILayout.LabelField(region, EditorStyles.miniBoldLabel);
                            if (GUILayout.Button(new GUIContent("✕", "Use the preset's own colours here"), EditorStyles.miniButton, GUILayout.Width(22))) {
                                wayParts.RemoveAt(k);
                                GUIUtility.ExitGUI();
                            }
                        }
                        if (part == null) EditorGUILayout.HelpBox("The preset has no part with this region.", MessageType.Warning);
                        Colours(wp, "colors", WardrobePatterns.Get(part?.String("pattern")));
                    }
                    var missing = regions.Where(r => wayParts.OfType<JsonObject>().All(p => p.String("region") != r)).ToList();
                    if (missing.Count > 0 && GUILayout.Button("Recolour a Part", EditorStyles.miniButton, GUILayout.Width(120))) {
                        var menu = new GenericMenu();
                        foreach (string r in missing) {
                            string picked = r;
                            menu.AddItem(new GUIContent(r.Replace("/", "∕")), false, () => {
                                var wp = new JsonObject();
                                wp["region"] = picked;
                                var from = parts.OfType<JsonObject>().First(p => p.String("region") == picked);
                                wp["colors"] = (from.Array("colors") ?? new List<object>()).ToList();
                                wayParts.Add(wp);
                                Set(way, "parts", wayParts, false);
                                repaint?.Invoke();
                            });
                        }
                        menu.ShowAsContext();
                    }
                }
            }
            if (GUILayout.Button("Add Colourway", GUILayout.Width(160))) {
                var way = new JsonObject();
                way["name"] = $"Colourway {ways.Count + 2}";
                way["parts"] = new List<object>();
                ways.Add(way);
                Set(j, "colourways", ways, false);
            }
        }

        // The regions the garments it's for have, and *.
        private static IEnumerable<(string, string)> Regions(JsonObject j) {
            var targets = WardrobeConfigs.Targets(j);
            var garments = WardrobeGarments.All.Where(g => targets.Count == 0 || targets.Any(t => t.Matches(g)));
            return new[] { (WardrobePreset.AnyRegion, "* (every other part)") }
                .Concat(garments.SelectMany(g => g.regions.Select(r => r.name)).Distinct().OrderBy(r => r).Select(r => (r, r)));
        }

        // A list of slot colours: "#RRGGBB", or a gradient as "#RRGGBB@0, #RRGGBB@1".
        private void Colours(JsonObject o, string key, WardrobePattern pattern) {
            var colors = o.Array(key) ?? new List<object>();
            int count = Mathf.Max(colors.Count, pattern?.slots.Count ?? 1);
            for (int i = 0; i < count; i++) {
                string text = i < colors.Count ? colors[i] as string ?? "" : "";
                var slot = pattern?.SlotAt(i);
                string label = slot?.name ?? $"Colour {i + 1}";
                bool gradient = text.Contains("@") || text.Contains(",");
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    string value = text;
                    if (gradient) {
                        var g = WardrobeConfigs.Gradient(text) ?? SlotColor.Constant(Color.white);
                        value = WardrobeConfigs.Write(EditorGUILayout.GradientField(new GUIContent(label), g));
                    } else {
                        var c = EditorGUILayout.ColorField(new GUIContent(label), WardrobeConfigs.Color(text, slot?.color ?? Color.white), true, false, false);
                        value = WardrobeConfigs.Hex(c);
                    }
                    bool canGradient = slot == null || slot.canGradient;
                    using (new EditorGUI.DisabledScope(!canGradient && !gradient))
                        if (GUILayout.Button(new GUIContent(gradient ? "Flat" : "Gradient", gradient ? "Make it one colour" : "Make it a gradient along the garment"),
                                EditorStyles.miniButton, GUILayout.Width(60))) {
                            var c = WardrobeConfigs.Color(text.Split(',')[0].Split('@')[0], slot?.color ?? Color.white);
                            value = gradient ? WardrobeConfigs.Hex(c) : $"{WardrobeConfigs.Hex(c)}@0, {WardrobeConfigs.Hex(c)}@1";
                            GUI.changed = true;
                        }
                    if (EditorGUI.EndChangeCheck() && value != text) {
                        while (colors.Count <= i) colors.Add(WardrobeConfigs.Hex(slot?.color ?? Color.white));
                        colors[i] = value;
                        Set(o, key, colors, false);
                    }
                }
            }
            if (pattern != null && colors.Count > pattern.slots.Count)
                EditorGUILayout.LabelField($"The pattern has {pattern.slots.Count} colours; the rest aren't used.", EditorStyles.miniLabel);
        }
    }
}
