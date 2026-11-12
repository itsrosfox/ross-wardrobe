using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Edits one .rpat as a guided setup: the pattern's details, its type, its textures (dropped in: what each is for is
    /// told from its name, and it's imported the way that needs), its colours, the garments it's for and a preview.
    /// </summary>
    internal class WardrobePatternForm : WardrobeJsonForm {
        private static readonly string[] Types = { "simple", "hybrid", "complete" };
        private static readonly string[] TypeNames = { "Simple (tiles on any garment)", "Hybrid (colour layout per garment)", "Complete (picture per garment)" };
        private const int MaxSlots = 4;

        public WardrobePatternForm(string path) : base(path) { }

        protected override ICollection<string> Always => new[] { "id", "version", "name", "group", "description", "author", "colors" };

        // ---------------------------------------------------------------- drawing

        private string _previewGarment;

        protected override void DrawJson(JsonObject j) {
            bool details = j.String("id").Trim().Length > 0 && j.String("name").Trim().Length > 0;
            Step(1, "Pattern", details ? StepState.Done : StepState.Attention, details ? j.String("id") : "name and id");
            Text(j, "name", "Name", "Shown in the pattern picker");
            Id(j, "Stable id presets refer to the pattern by (e.g. yourname.pinstripe); don't change it once shipped",
                WardrobePatterns.All.Where(p => p.jsonPath != path).Select(p => p.id));
            if (j.String("id").Trim().Length == 0 && j.String("name").Trim().Length > 0) {
                string id = WardrobeConfigs.SuggestId(j.String("author"), j.String("name"));
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                    if (GUILayout.Button($"Use {id}", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                        j["id"] = id;
                        GUI.FocusControl(null);
                    }
                }
            }
            Text(j, "version", "Version");
            Group(j);
            Text(j, "author", "Author");
            TextArea(j, "description", "Description", "Shown as the pattern's tooltip");

            int type = Mathf.Max(0, Array.IndexOf(Types, j.String("type", "simple").Trim().ToLowerInvariant()));
            Step(2, "Type", StepState.Done, Types[type]);
            DrawType(j, ref type);

            var garments = j.Objects("garments").ToList();
            string needed = type == 1 ? "weights" : "albedo";
            bool maps = type == 0 ? new[] { "weights", "normal", "height", "surface", "occlusion" }.Any(j.Has) : type == 1 && garments.Count == 0 || garments.Any(e => e.Has(needed));
            Step(3, type == 0 ? "Textures" : "Textures per garment", maps ? StepState.Done : type == 2 ? StepState.Attention : StepState.Todo,
                type == 0 ? null : $"{garments.Count(e => e.Has(needed))} of {garments.Count} garments");
            if (type == 0) DrawTiled(j);
            else {
                Note(type == 1
                    ? "A garment's own colour layout is its Weights: an id mask on its UVs saying which colour goes where (and optionally its own gradient map). " +
                      "Tick the garments in step 5, then drop each one's textures there."
                    : "Each garment needs its Albedo: the picture painted on its UVs (alpha = where it covers). Tick the garments in step 5, then drop each one's textures there.");
                open.TryGetValue("tiled", out bool tiled);
                open["tiled"] = tiled = EditorGUILayout.Foldout(tiled, "Tiling maps for the other garments (optional)", true);
                if (tiled) {
                    EditorGUI.indentLevel++;
                    DrawTiled(j);
                    EditorGUI.indentLevel--;
                }
            }

            var slots = j.Array("colors") ?? new List<object>();
            Step(4, "Colours", StepState.Done, $"{Mathf.Max(1, slots.Count)}");
            DrawSlots(j);

            bool some = garments.Count > 0;
            Step(5, "Garments", type == 2 && !some ? StepState.Attention : StepState.Done, some ? $"{garments.Count}" : "every garment");
            DrawGarments(j, type);

            Step(6, "Preview", StepState.Done);
            DrawPreview();
        }

        private void DrawType(JsonObject j, ref int type) {
            for (int i = 0; i < Types.Length; i++) {
                EditorGUI.BeginChangeCheck();
                bool on = GUILayout.Toggle(type == i, new GUIContent(" " + TypeNames[i]), EditorStyles.radioButton);
                if (EditorGUI.EndChangeCheck() && on && i != type) {
                    type = i;
                    Set(j, "type", Types[i], i == 0);
                }
                using (new EditorGUI.IndentLevelScope()) EditorGUILayout.LabelField(TypeHelp[i], EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static readonly string[] TypeHelp = {
            "Its textures tile over any garment at a real size: stripes, checks, weaves, knits.",
            "Each garment gets its own colour layout (an id mask on its UVs): raglan sleeves, colour blocks, fades.",
            "Each garment gets a picture painted on its UVs: prints and graphics. Colour 1 shows where the picture doesn't cover.",
        };

        private void DrawTiled(JsonObject j) {
            DropArea(j, true, "Drop the pattern's textures here");
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
        }

        // ---------------------------------------------------------------- dropping textures

        // What a texture is for, from its name.
        private static readonly (string key, string[] words)[] Roles = {
            ("normal", new[] { "normal", "nrm", "nor", "norm", "normalgl", "normaldx" }),
            ("height", new[] { "height", "bump", "disp", "displacement" }),
            ("occlusion", new[] { "ao", "occlusion", "ambientocclusion" }),
            ("surface", new[] { "surface", "orm", "rough", "roughness", "metal", "metalness" }),
            ("mask", new[] { "mask", "coverage" }),
            ("gradient", new[] { "gradient", "fade" }),
            ("weights", new[] { "weights", "weight", "id", "ids", "layout", "colours", "colors", "slots" }),
            ("albedo", new[] { "albedo", "color", "colour", "diffuse", "basecolor", "print", "graphic", "picture", "art" }),
        };

        private static string RoleOf(Texture2D t, bool tiled, int type) {
            var words = t.name.ToLowerInvariant().Replace(" ", "_").Split('_', '-', '.');
            foreach (var (key, list) in Roles) {
                if (!words.Any(list.Contains)) continue;
                if (tiled) return key == "albedo" ? "weights" : key == "mask" || key == "gradient" ? null : key;
                if (key == "albedo") return type == 2 ? "albedo" : "weights";
                if (key == "gradient") return type == 1 ? key : null;
                if (key == "mask") return type == 2 ? key : null;
                return key;
            }
            // nothing in the name: the picture (complete), else the colour weights
            return type == 2 && !tiled ? "albedo" : "weights";
        }

        /// <summary>
        /// A drop area for a pattern's textures: each goes where its name says (weights, normal, height…), is imported the
        /// way that needs (normal maps as normal maps, the rest linear, tiling ones repeating), and a weights map adds the
        /// colours it uses.
        /// </summary>
        private void DropArea(JsonObject target, bool tiled, string label) {
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true)));
            GUI.Box(rect, label, EditorStyles.helpBox);
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            var textures = DragAndDrop.objectReferences.OfType<Texture2D>().ToList();
            if (textures.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) {
                e.Use();
                return;
            }
            DragAndDrop.AcceptDrag();
            e.Use();
            int type = Mathf.Max(0, Array.IndexOf(Types, json.String("type", "simple").Trim().ToLowerInvariant()));
            var placed = new List<string>();
            foreach (var t in textures) {
                string role = RoleOf(t, tiled, type);
                if (role == null) {
                    placed.Add($"{t.name}: not used here");
                    continue;
                }
                Set(target, role, WardrobeConfigs.Relative(path, t), false);
                placed.Add($"{t.name} → {char.ToUpperInvariant(role[0])}{role.Substring(1)}");
                Prepare(t, role, tiled);
                if (role == "weights") AddSlots(UsedChannels(t));
            }
            EditorUtility.DisplayDialog("Ros' Wardrobe", string.Join("\n", placed) + "\n\nCheck them below; drop a texture on a field to change it.", "OK");
            GUIUtility.ExitGUI();
        }

        // Imports a texture the way its role needs.
        private static void Prepare(Texture2D t, string role, bool tiled) {
            if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter ti)) return;
            bool changed = false;
            if (role == "normal") {
                if (ti.textureType != TextureImporterType.NormalMap) {
                    ti.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }
            } else if (role != "albedo" && ti.sRGBTexture) {
                ti.sRGBTexture = false;
                changed = true;
            }
            if (tiled && ti.wrapMode != TextureWrapMode.Repeat) {
                ti.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }
            if (changed) ti.SaveAndReimport();
        }

        // Adds colours up to what a weights map uses.
        private void AddSlots(int used) {
            var slots = json.Array("colors") ?? new List<object>();
            while (slots.Count < Mathf.Min(used, MaxSlots)) slots.Add(NewSlot(slots.Count));
            Set(json, "colors", slots, false);
        }

        private static readonly Dictionary<string, (long stamp, int used)> Used = new Dictionary<string, (long, int)>();

        /// <summary>How many colour slots a weights map uses: its channels (R, G, B, then A) that have any weight.</summary>
        internal static int UsedChannels(Texture2D weights) {
            if (weights == null) return 0;
            string file = AssetDatabase.GetAssetPath(weights);
            long stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0;
            if (Used.TryGetValue(file, out var known) && known.stamp == stamp) return known.used;
            var px = WardrobeBaker.SourcePixels(weights, out _, out _);
            byte r = 0, g = 0, b = 0, aMax = 0, aMin = 255;
            foreach (var c in px) {
                if (c.r > r) r = c.r;
                if (c.g > g) g = c.g;
                if (c.b > b) b = c.b;
                if (c.a > aMax) aMax = c.a;
                if (c.a < aMin) aMin = c.a;
            }
            // alpha counts when it varies (a texture without alpha reads as 255 everywhere)
            bool alpha = aMax > 8 && aMin < 247 && (!(AssetImporter.GetAtPath(file) is TextureImporter ti) || ti.DoesSourceTextureHaveAlpha());
            int used = alpha ? 4 : b > 8 ? 3 : g > 8 ? 2 : 1;
            Used[file] = (stamp, used);
            return used;
        }

        // ---------------------------------------------------------------- preview

        private void DrawPreview() {
            var pattern = WardrobePatterns.All.FirstOrDefault(p => p.jsonPath == path);
            if (pattern == null) {
                Note("Save it to see it.");
                return;
            }
            var fits = WardrobeGarments.All.Where(pattern.Fits).ToList();
            if (fits.Count == 0) {
                Note(pattern.type == WardrobePattern.Kind.Simple ? "No garment in the project to show it on." : "No garment it's for has its maps yet (step 3).");
                return;
            }
            int index = Mathf.Max(0, fits.FindIndex(g => WardrobeGarments.PathOf(g) == _previewGarment));
            if (fits.Count > 1) {
                index = EditorGUILayout.Popup("On", index, fits.Select(g => $"{Title(g)} on {AvatarName(g.avatarId)}".Replace("/", "∕")).ToArray());
                _previewGarment = WardrobeGarments.PathOf(fits[index]);
            }
            var garment = fits[index];
            var picture = WardrobePatternPreviews.Get(pattern, garment, garment.defaultMaterial, repaint);
            var rect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false)));
            if (picture != null) GUI.DrawTexture(rect, picture, ScaleMode.ScaleToFit);
            else GUI.Label(rect, "Rendering…", EditorStyles.centeredGreyMiniLabel);
            if (Dirty) Note("As saved: save to see your changes.");
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
                    slots.Add(NewSlot(slots.Count));
                    Set(j, "colors", slots, false);
                }
            if (j.Has("weights") && WardrobeConfigs.Load<Texture2D>(path, j.String("weights")) is Texture2D weights) {
                int used = UsedChannels(weights);
                if (used > Mathf.Max(1, slots.Count))
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.HelpBox($"The weights map uses {used} colours, but there are {Mathf.Max(1, slots.Count)}.", MessageType.Warning);
                        if (GUILayout.Button("Add Them", GUILayout.Width(80), GUILayout.Height(38))) AddSlots(used);
                    }
            }
        }

        private static JsonObject NewSlot(int index) {
            var slot = new JsonObject();
            slot["name"] = index == 0 ? "Colour" : $"Colour {index + 1}";
            slot["color"] = new[] { "#F4F4F2", "#2B3A55", "#B23A3A", "#E0B040" }[Mathf.Clamp(index, 0, 3)];
            slot["canGradient"] = true;
            return slot;
        }

        private static void Swap(List<object> list, int a, int b) {
            (list[a], list[b]) = (list[b], list[a]);
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- garments

        private void DrawGarments(JsonObject j, int type) {
            if (type == 0)
                Checklist(j, "Offered for every garment.", "Only offered for the garments ticked. Maps given for a garment are laid out on its UVs instead of tiling.",
                    GarmentMaps(type), false);
            else
                Checklist(j, type == 1 ? "Offered for every garment, along each one's own gradient map (and the tiling maps, if any). Pick Only some " +
                                         "garments to give garments a colour layout of their own."
                        : "A complete pattern needs a picture for each garment: pick Only some garments and tick them.",
                    type == 1 ? "Drop each garment's colour layout (weights) below." : "Drop each garment's picture (albedo) below.", GarmentMaps(type), true);
            if (type == 2 && !j.Has("garments"))
                EditorGUILayout.HelpBox("It isn't offered for any garment until it has a picture for one.", MessageType.Warning);
        }

        private Action<JsonObject> GarmentMaps(int type) => g => {
            DropArea(g, false, type == 1 ? "Drop this garment's weights (and its other maps) here" : type == 2 ? "Drop this garment's picture (and its mask) here"
                : "Drop maps laid out on this garment's UVs here");
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
        };
    }
}
