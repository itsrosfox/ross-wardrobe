using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Edits one .rpat.</summary>
    internal class WardrobePatternForm : WardrobeJsonForm {
        private static readonly string[] Types = { "simple", "hybrid", "complete" };
        private static readonly string[] TypeNames = { "Simple (tiles on any garment)", "Hybrid (colour layout per garment)", "Complete (picture per garment)" };
        private const int MaxSlots = 4;

        public WardrobePatternForm(string path) : base(path) { }

        protected override ICollection<string> Always => new[] { "id", "version", "name", "group", "description", "author", "colors" };

        // ---------------------------------------------------------------- drawing

        protected override void DrawJson(JsonObject j) {
            Header("Pattern");
            Text(j, "name", "Name", "Shown in the pattern picker");
            Id(j, "Stable id presets refer to the pattern by (e.g. yourname.pinstripe); don't change it once shipped",
                WardrobePatterns.All.Where(p => p.jsonPath != path).Select(p => p.id));
            Text(j, "version", "Version");
            Group(j);
            Text(j, "author", "Author");
            TextArea(j, "description", "Description", "Shown as the pattern's tooltip");

            int type = Mathf.Max(0, Array.IndexOf(Types, j.String("type", "simple").Trim().ToLowerInvariant()));
            EditorGUI.BeginChangeCheck();
            type = EditorGUILayout.Popup(new GUIContent("Type"), type, TypeNames.Select(t => new GUIContent(t)).ToArray());
            if (EditorGUI.EndChangeCheck()) Set(j, "type", Types[type], type == 0);

            EditorGUILayout.Space();
            Header("Colours");
            DrawSlots(j);

            EditorGUILayout.Space();
            Header(type == 0 ? "Maps" : "Tiling Maps");
            if (type != 0)
                Note("Optional: maps that tile over garments that don't have maps of their own below.");
            TileSize(j);
            Texture(j, "weights", "Weights", "R, G, B, A = how much of colour 1..4 each texel is (none = colour 1 everywhere)");
            Texture(j, "normal", "Normal", "Relief, added to the garment's normal map");
            if (j.Has("normal")) Slider(j, "normalStrength", "Normal Strength", 1, 0, 10);
            Texture(j, "height", "Height", "R = relief as a height map (white = raised)");
            if (j.Has("height")) Slider(j, "heightStrength", "Height Strength", 1, 0, 10);
            if (j.Has("normal") || j.Has("height") || j.Has("normalMode")) {
                bool replace = j.String("normalMode", "blend") == "replace";
                EditorGUI.BeginChangeCheck();
                replace = EditorGUILayout.Toggle(new GUIContent("Replace Garment Normal", "The relief replaces the garment's own normal map instead of adding to it"), replace);
                if (EditorGUI.EndChangeCheck()) Set(j, "normalMode", replace ? "replace" : "blend", !replace);
            }
            Texture(j, "surface", "Surface", "R metalness, G roughness, A opacity (holes)");
            Texture(j, "occlusion", "Occlusion", "R, multiplied into the garment's ambient occlusion");
            Slider(j, "sheen", "Sheen", 0.3, 0, 1);

            EditorGUILayout.Space();
            Header("Garments");
            DrawGarments(j, type);
        }

        private void Group(JsonObject j) {
            var groups = WardrobePatterns.Groups.Concat(WardrobePatterns.All.Select(p => p.group))
                .Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().Select(g => (g, g)).ToList();
            EditorGUI.BeginChangeCheck();
            string group = Suggest(new GUIContent("Group", "Where the picker lists it (Plain, Stripes, Checks…)"), j.String("group"), groups,
                picked => Set(j, "group", picked, false));
            if (EditorGUI.EndChangeCheck()) Set(j, "group", group, false);
        }

        private void TileSize(JsonObject j) {
            var size = j.Array("tileSize");
            var mm = new Vector2(Get(size, 0, 10), Get(size, 1, 10));
            EditorGUI.BeginChangeCheck();
            mm = EditorGUILayout.Vector2Field(new GUIContent("Tile Size (mm)", "How big one tile of the tiling maps is on the garment"), mm);
            if (EditorGUI.EndChangeCheck())
                Set(j, "tileSize", new List<object> { Math.Round(Mathf.Max(0.1f, mm.x), 3), Math.Round(Mathf.Max(0.1f, mm.y), 3) }, false);
        }

        private static float Get(List<object> list, int i, float fallback) =>
            list != null && i < list.Count && list[i] is double n ? (float)n : fallback;

        // ---------------------------------------------------------------- colour slots

        private void DrawSlots(JsonObject j) {
            var slots = j.Array("colors") ?? new List<object>();
            for (int i = 0; i < slots.Count; i++) {
                if (!(slots[i] is JsonObject s)) continue;
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.TextField($"Colour {i + 1}", s.String("name"));
                    if (EditorGUI.EndChangeCheck()) Set(s, "name", name, false);
                    EditorGUI.BeginChangeCheck();
                    var color = EditorGUILayout.ColorField(GUIContent.none, WardrobeConfigs.Color(s.String("color"), Color.white), true, false, false, GUILayout.Width(60));
                    if (EditorGUI.EndChangeCheck()) Set(s, "color", WardrobeConfigs.Hex(color), false);
                    using (new EditorGUI.DisabledScope(i == 0))
                        if (GUILayout.Button(new GUIContent("▲", "Move up"), EditorStyles.miniButtonLeft, GUILayout.Width(22))) Swap(slots, i, i - 1);
                    using (new EditorGUI.DisabledScope(i == slots.Count - 1))
                        if (GUILayout.Button(new GUIContent("▼", "Move down"), EditorStyles.miniButtonMid, GUILayout.Width(22))) Swap(slots, i, i + 1);
                    if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButtonRight, GUILayout.Width(22))) {
                        slots.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUI.indentLevel++;
                Toggle(s, "canGradient", "Can Be a Gradient", "Buyers may turn this colour into a gradient along the garment");
                if (s.Bool("canGradient")) {
                    Toggle(s, "useGradient", "Starts as Gradient", null);
                    var gradient = WardrobeConfigs.Gradient(s.String("gradient")) ?? SlotColor.Constant(WardrobeConfigs.Color(s.String("color"), Color.white));
                    EditorGUI.BeginChangeCheck();
                    gradient = EditorGUILayout.GradientField(new GUIContent("Gradient", "The gradient it starts with"), gradient);
                    if (EditorGUI.EndChangeCheck()) Set(s, "gradient", WardrobeConfigs.Write(gradient), false);
                }
                EditorGUI.indentLevel--;
            }
            if (slots.Count == 0) EditorGUILayout.LabelField("None: the pattern gets one white colour.", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(slots.Count >= MaxSlots))
                if (GUILayout.Button(slots.Count >= MaxSlots ? "Add Colour (4 at most)" : "Add Colour", GUILayout.Width(160))) {
                    var slot = new JsonObject();
                    slot["name"] = slots.Count == 0 ? "Colour" : $"Colour {slots.Count + 1}";
                    slot["color"] = "#F4F4F2";
                    slot["canGradient"] = true;
                    slots.Add(slot);
                    Set(j, "colors", slots, false);
                }
        }

        private static void Swap(List<object> list, int a, int b) {
            (list[a], list[b]) = (list[b], list[a]);
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- garments

        private void DrawGarments(JsonObject j, int type) {
            Note(type == 1 ? "Each needs its Weights: an id mask on its UVs saying which colour goes where."
                : type == 2 ? "Each needs its Albedo: the picture painted on its UVs (alpha = where it covers)."
                : "Maps given here are laid out on the garment's UVs instead of tiling.");
            if (!j.Has("garments") && type != 0)
                EditorGUILayout.HelpBox("A hybrid or complete pattern only shows up for the garments it lists.", MessageType.Warning);
            Targets(j, "None listed: offered for every garment.", g => {
                if (type == 1) {
                    Texture(g, "weights", "Weights", "On the garment's UVs: R, G, B, A = colour 1..4");
                    if (!g.Has("weights")) EditorGUILayout.HelpBox("Needs weights for this garment.", MessageType.Warning);
                    Texture(g, "gradient", "Gradient", "Optional: its own position along the colours' gradients (else the garment's)");
                } else if (type == 2) {
                    Texture(g, "albedo", "Albedo", "The picture on the garment's UVs; alpha = where it covers");
                    if (!g.Has("albedo")) EditorGUILayout.HelpBox("Needs an albedo for this garment, or it isn't offered for it.", MessageType.Warning);
                    Texture(g, "mask", "Mask", "Optional: R = which texels the picture may cover");
                }
                bool extra = new[] { "normal", "height", "surface", "occlusion" }.Concat(type == 0 ? new[] { "weights", "gradient" } : new string[0]).Any(g.Has);
                if (!open.TryGetValue(g, out bool shown)) shown = extra;
                open[g] = shown = EditorGUILayout.Foldout(shown, type == 0 ? "Maps on its UVs" : "More maps", true);
                if (!shown) return;
                EditorGUI.indentLevel++;
                if (type == 0) {
                    Texture(g, "weights", "Weights", null);
                    Texture(g, "gradient", "Gradient", null);
                }
                Texture(g, "normal", "Normal", null);
                Texture(g, "height", "Height", null);
                Texture(g, "surface", "Surface", "R metalness, G roughness, A opacity");
                Texture(g, "occlusion", "Occlusion", null);
                EditorGUI.indentLevel--;
            });
        }
    }
}
