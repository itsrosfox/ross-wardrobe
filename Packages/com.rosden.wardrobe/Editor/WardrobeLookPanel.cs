using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The look editor for one worn garment: a gallery of presets and Custom. Right under the picked card's row, a
    /// preset shows a swatch per look (its own and its colourways) plus a Custom swatch that changes the colours the
    /// preset sets; Custom shows a pattern and colours (or gradients) per part. Every change shows on the garment right away (live preview); it's baked onto the garment
    /// (texture and material files) only when it has to be: the panel stops showing the garment, the scene is saved,
    /// play mode, a VRChat build, a script reload or quitting. Used by the Ros' Wardrobe window and the component inspector.
    /// </summary>
    internal class WardrobeLookPanel {
        private const float LookWidth = 156, LookHeight = 184, SwatchSize = 30;
        private const string CustomName = "Custom";
        private static readonly HashSet<WardrobeLookPanel> Open = new HashSet<WardrobeLookPanel>();
        private static readonly HashSet<WardrobeRecolor> Pending = new HashSet<WardrobeRecolor>();   // looks not baked yet

        private readonly Action _repaint;
        private WardrobeRecolor _target;
        private RenderTexture _preview, _previewNormal, _previewSurface, _previewGarmentSurface;
        private readonly Dictionary<string, RenderTexture> _previewOutputs = new Dictionary<string, RenderTexture>();
        private Renderer _previewRenderer;
        private int _previewSlot;
        private bool _previewQueued, _disposed;
        private int _customTab = -1;   // Custom's tab picked by hand; -1 = the one the look is in

        public WardrobeLookPanel(Action repaint) {
            _repaint = repaint;
            Undo.undoRedoPerformed += OnUndoRedo;
            Open.Add(this);
        }

        public void Dispose() {
            _disposed = true;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Open.Remove(this);
            var target = _target;
            if (Unwatched(target)) EditorApplication.delayCall += () => Bake(target);   // not while an inspector is closing
            Release();
        }

        /// <summary>Which garment instance the panel edits (null for none). Moves the live preview along.</summary>
        public void SetTarget(WardrobeRecolor rc) {
            if (rc == _target) return;
            _customTab = -1;
            var previous = _target;
            Release();
            _target = rc;
            if (Unwatched(previous)) Bake(previous);
            QueuePreview();
        }

        // Stops previewing on the garment; another panel still showing it (window and inspector both can) puts its
        // preview back, since they share the renderer.
        private void Release() {
            ClearPreview();
            foreach (var panel in Open.Where(p => p != this && p._target != null && p._target == _target)) panel.QueuePreview();
        }

        /// <summary>Call after the target's garment, renderer or slot changed.</summary>
        public void TargetChanged() {
            ClearPreview();
            QueuePreview();
        }

        public void OnGUI(float width) {
            var rc = _target;
            if (rc == null) return;
            if (rc.look == null) rc.look = new WardrobeLook();
            string problem = WardrobeBaker.Problem(rc);
            if (problem != null) {
                EditorGUILayout.HelpBox(problem, MessageType.Info);
                return;
            }
            DrawMaterialTypes(rc);
            DrawLooks(rc, width);
            EditorGUILayout.Space();
            DrawFooter(rc);
        }

        // ---------------------------------------------------------------- sections

        // Switching puts that type's material for the garment on it right away, so the live preview shows it.
        private void DrawMaterialTypes(WardrobeRecolor rc) {
            var types = WardrobeMaterialTypes.For(rc.garment);
            if (types.Count < 2) return;
            var current = WardrobeBaker.MaterialType(rc);
            int index = Mathf.Max(0, types.IndexOf(current));
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Material", EditorStyles.boldLabel, GUILayout.Width(70));
                int picked = GUILayout.Toolbar(index, types.Select(t => new GUIContent(t.DisplayName,
                    string.IsNullOrWhiteSpace(t.description) ? t.Shader.name : t.description)).ToArray());
                if (picked != index) {
                    var type = types[picked];
                    EditorApplication.delayCall += () => SwitchMaterialType(rc, type);
                }
            }
            EditorGUILayout.Space();
        }

        private void SwitchMaterialType(WardrobeRecolor rc, WardrobeMaterialType type) {
            if (rc == null || rc.target == null) return;
            Material material;
            try {
                material = WardrobeMaterialTypes.BaseMaterial(rc.garment, type);
            } catch (Exception e) {
                Debug.LogException(e);
                return;
            }
            Undo.RecordObjects(new Object[] { rc, rc.target }, "Change Material Type");
            rc.materialType = type.id;
            var materials = rc.target.sharedMaterials;
            materials[rc.materialSlot] = material;
            rc.target.sharedMaterials = materials;
            rc.bakedMaterial = null;
            rc.bakedTexture = null;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc.target);
            EditorUtility.SetDirty(rc);
            TargetChanged();
            QueueApply(rc);
            if (!_disposed) _repaint();
        }

        // The preset the look comes from; null for Custom or none picked yet.
        private static WardrobePreset PresetOf(WardrobeRecolor rc) {
            var preset = WardrobePresets.Get(rc.preset);
            return preset != null && preset.Fits(rc.garment) ? preset : null;
        }

        // Custom: picked, or from a preset that's gone (the look stays as it was).
        private static bool IsCustom(WardrobeRecolor rc) => !string.IsNullOrEmpty(rc.preset) && PresetOf(rc) == null;

        // A card per preset (the garment's own first, see each preset's order), then Custom. The picked card's options
        // open right under its row; nothing is picked until the buyer picks something.
        private void DrawLooks(WardrobeRecolor rc, float width) {
            var g = rc.garment;
            var current = PresetOf(rc);
            bool custom = IsCustom(rc);
            var cards = new List<WardrobePreset>(WardrobePresets.For(g)) { null };   // null = Custom
            EditorGUILayout.LabelField("Looks", EditorStyles.boldLabel);
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 8) / (LookWidth + 4)));
            for (int i = 0; i < cards.Count; i += columns) {
                bool pickedRow = false;
                using (new EditorGUILayout.HorizontalScope()) {
                    for (int j = 0; j < columns; j++) {
                        var rect = GUILayoutUtility.GetRect(LookWidth, LookHeight, GUILayout.Width(LookWidth));
                        if (i + j >= cards.Count) continue;
                        var preset = cards[i + j];
                        if (preset == null ? custom : preset == current) pickedRow = true;
                        if (preset == null) {
                            if (LookButton(rect, CustomName, "Pick a pattern and colours for every part yourself", rc.look, g, custom))
                                PickCustom(rc);
                        } else {
                            // the picked card shows the look it's in
                            var look = preset != current ? preset.Look(g, 0) : rc.variation >= 0 ? preset.Look(g, rc.variation) : rc.look;
                            if (LookButton(rect, preset.DisplayName, preset.description, look, g, preset == current) && preset != current)
                                Pick(rc, preset, 0);
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
                if (!pickedRow) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    if (custom) DrawRegions(rc, width - 12);
                    else {
                        DrawSwatches(rc, current, width - 12);
                        if (rc.variation < 0) DrawPresetColours(rc, current);
                    }
                }
                EditorGUILayout.Space(4);
            }
        }

        // A swatch per look of the preset (its own first), then Custom, with the picked look's name.
        private void DrawSwatches(WardrobeRecolor rc, WardrobePreset preset, float width) {
            var g = rc.garment;
            int count = preset.Variations + (Editable(preset) ? 1 : 0);
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (SwatchSize + 4)));
            for (int i = 0; i < count; i += perRow)
                using (new EditorGUILayout.HorizontalScope()) {
                    for (int v = i; v < count && v < i + perRow; v++) {
                        var rect = GUILayoutUtility.GetRect(SwatchSize, SwatchSize, GUILayout.Width(SwatchSize));
                        bool custom = v == preset.Variations;
                        bool selected = custom ? rc.variation < 0 : rc.variation == v;
                        string tip = custom ? "Custom: change the colours this look uses" : preset.VariationName(v);
                        if (GUI.Button(rect, new GUIContent("", tip), GUIStyle.none) && !selected) {
                            if (custom)
                                Edit(rc, "Customise Look", () => {
                                    rc.variation = -1;
                                    rc.lookName = $"{preset.DisplayName} ({CustomName})";
                                });
                            else Pick(rc, preset, v);
                        }
                        if (Event.current.type != EventType.Repaint) continue;
                        if (custom) {
                            DrawSwatch(rect, rc.look);
                            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.45f));
                            GUI.Label(rect, new GUIContent("✎", tip), CenteredLabel);
                        } else DrawSwatch(rect, preset.Look(g, v));
                        DrawOutline(rect, selected ? new Color(0.3f, 0.6f, 1f) : new Color(0, 0, 0, 0.5f), selected ? 2 : 1);
                    }
                    GUILayout.Space(8);
                    GUILayout.Label(rc.variation < 0 ? CustomName : preset.VariationName(rc.variation), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                }
        }

        // Whether any part of the preset has colours to change (not only finished designs).
        private static bool Editable(WardrobePreset preset) =>
            preset.parts.Any(p => WardrobePatterns.Get(p.pattern)?.type != WardrobePattern.Kind.Complete);

        // The look's colours side by side (each once, in part order), gradients running top to bottom.
        private static void DrawSwatch(Rect rect, WardrobeLook look) {
            var colours = new List<SlotColor>();
            foreach (var style in look.styles) {
                var pattern = WardrobePatterns.Get(style.pattern);
                for (int i = 0; i < Mathf.Max(1, pattern?.slots.Count ?? 1); i++) {
                    var c = WardrobeBaker.SlotOf(style, pattern, i);
                    if (!colours.Any(x => Same(x, c))) colours.Add(c);
                }
            }
            if (colours.Count == 0) colours.Add(new SlotColor());
            colours = colours.Take(4).ToList();
            float w = rect.width / colours.Count;
            for (int k = 0; k < colours.Count; k++) {
                var r = new Rect(rect.x + k * w, rect.y, w + 0.5f, rect.height);
                var c = colours[k];
                if (!c.useGradient || c.gradient == null) {
                    EditorGUI.DrawRect(r, c.color);
                    continue;
                }
                const int steps = 8;
                for (int j = 0; j < steps; j++)
                    EditorGUI.DrawRect(new Rect(r.x, r.y + r.height * j / steps, r.width, r.height / steps + 0.5f), c.gradient.Evaluate((j + 0.5f) / steps));
            }
        }

        private static bool Same(SlotColor a, SlotColor b) =>
            a.useGradient == b.useGradient && (a.useGradient && a.gradient != null && b.gradient != null
                ? a.gradient.Evaluate(0) == b.gradient.Evaluate(0) && a.gradient.Evaluate(1) == b.gradient.Evaluate(1)
                : a.color == b.color);

        // A colour row per part the preset sets ("*" = every part no other entry names), changing all its regions.
        private void DrawPresetColours(WardrobeRecolor rc, WardrobePreset preset) {
            var g = rc.garment;
            foreach (var part in preset.parts) {
                var regions = preset.RegionsOf(g, part).Select(r => g.regions.FindIndex(x => x.name == r))
                    .Where(r => r >= 0 && r < WardrobeBaker.MaxRegions).ToArray();
                if (regions.Length == 0) continue;
                var style = WardrobeBaker.StyleOf(g, rc.look, regions[0]);
                var pattern = WardrobePatterns.Get(style.pattern);
                if (pattern?.type == WardrobePattern.Kind.Complete) continue;
                using (new EditorGUILayout.VerticalScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        string label = part.region != WardrobePreset.AnyRegion ? part.region : preset.parts.Count == 1 ? "All Parts" : "Main";
                        EditorGUILayout.LabelField(new GUIContent(label, string.Join(", ", regions.Select(r => g.regions[r].name))), EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(pattern != null ? pattern.DisplayName : $"Missing pattern '{style.pattern}'", EditorStyles.miniLabel);
                    }
                    if (pattern != null) DrawSlots(rc, regions, pattern, style);
                }
            }
        }

        private void Pick(WardrobeRecolor rc, WardrobePreset preset, int v) =>
            Edit(rc, "Pick Look", () => {
                rc.preset = preset.id;
                rc.variation = v;
                rc.look = preset.Look(rc.garment, v).Clone();
                rc.lookName = preset.LookName(v);
            });

        // Custom starts from the look the garment has now.
        private void PickCustom(WardrobeRecolor rc) {
            if (IsCustom(rc)) return;
            Edit(rc, "Customise Look", () => {
                rc.preset = WardrobeRecolor.CustomPreset;
                rc.lookName = CustomName;
            });
        }

        private static readonly GUIContent[] CustomTabs = {
            new GUIContent("Repeating Patterns", "A pattern that repeats (stripes, checks, weaves, prints) for each part"),
            new GUIContent("Full Patterns", "A design for the whole garment (raglan sleeves, colour blocks, fades)"),
        };

        // Custom: repeating (simple) patterns per part, or one full (hybrid) pattern for the whole garment, on separate
        // tabs so a full pattern never replaces the parts' repeating ones by surprise. Finished (complete) designs are
        // picked as presets, not here.
        private void DrawRegions(WardrobeRecolor rc, float width) {
            var g = rc.garment;
            var regions = Enumerable.Range(0, Mathf.Min(g.regions.Count, WardrobeBaker.MaxRegions)).ToArray();
            var whole = WholePattern(rc, regions);
            int tab = _customTab >= 0 ? _customTab : whole != null ? 1 : 0;
            int picked = GUILayout.Toolbar(tab, CustomTabs);
            if (picked != tab) _customTab = picked;
            EditorGUILayout.Space(4);

            var kind = picked == 0 ? WardrobePattern.Kind.Simple : WardrobePattern.Kind.Hybrid;
            var patterns = WardrobePatterns.For(g).Where(x => x.type == kind).ToList();
            if (patterns.Count == 0) {
                EditorGUILayout.HelpBox(picked == 0
                    ? "No repeating patterns found. Ros' Wardrobe's own are in its package (Patterns); reimport it if they're missing."
                    : "No full patterns for this garment.", MessageType.Info);
                return;
            }
            if (picked == 1) {
                DrawWhole(rc, regions, whole, patterns, width);
                return;
            }
            if (whole != null)
                EditorGUILayout.LabelField($"The whole garment is in {whole.DisplayName} now; picking a pattern for a part replaces it there.",
                    EditorStyles.wordWrappedMiniLabel);
            foreach (int r in regions) {
                if (r > 0) EditorGUILayout.Space(6);
                DrawRegion(rc, r, patterns, width);
            }
        }

        // The full (or finished) pattern every part is in, if they all are in the same one.
        private static WardrobePattern WholePattern(WardrobeRecolor rc, int[] regions) {
            if (regions.Length == 0) return null;
            var first = WardrobePatterns.Get(WardrobeBaker.StyleOf(rc.garment, rc.look, regions[0]).pattern);
            return first != null && first.type != WardrobePattern.Kind.Simple &&
                   regions.All(r => WardrobePatterns.Get(WardrobeBaker.StyleOf(rc.garment, rc.look, r).pattern) == first) ? first : null;
        }

        private void DrawWhole(WardrobeRecolor rc, int[] regions, WardrobePattern pattern, List<WardrobePattern> patterns, float width) {
            int clicked = SwatchRow(patterns, pattern, width, PreviewOf, PatternTip);
            if (clicked >= 0) PickPattern(rc, regions, patterns[clicked]);
            if (pattern == null) {
                EditorGUILayout.LabelField("Pick a design for the whole garment. It replaces the parts' repeating patterns.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            EditorGUILayout.LabelField(pattern.DisplayName, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(pattern.description))
                EditorGUILayout.LabelField(pattern.description, EditorStyles.wordWrappedMiniLabel);
            if (pattern.type == WardrobePattern.Kind.Complete)
                EditorGUILayout.LabelField("A finished design: its colours are part of it.", EditorStyles.miniLabel);
            else DrawSlots(rc, regions, pattern, WardrobeBaker.StyleOf(rc.garment, rc.look, regions[0]));
        }

        private Texture PreviewOf(WardrobePattern x) =>
            WardrobePatternPreviews.Get(x, _target != null ? _target.garment : null, _target != null ? _target.materialType : null, _repaint);

        private static string PatternTip(WardrobePattern x) =>
            $"{x.DisplayName}{(string.IsNullOrWhiteSpace(x.group) ? "" : $" ({x.group})")}\n{x.description}";

        // A hybrid or complete pattern goes on every part; a simple one on the parts asked for.
        private void PickPattern(WardrobeRecolor rc, int[] regions, WardrobePattern pattern) {
            if (pattern.type != WardrobePattern.Kind.Simple)
                regions = Enumerable.Range(0, Mathf.Min(rc.garment.regions.Count, WardrobeBaker.MaxRegions)).ToArray();
            Edit(rc, "Pick Pattern", () => {
                foreach (int r in regions) {
                    var s = Own(rc, r);
                    s.pattern = pattern.id;
                    s.colors = pattern.slots.Select(x => x.Default()).ToList();   // each pattern starts in its own colours
                }
            });
        }

        // The region's pattern swatches, then its colour slots, and (tiling patterns) scale and turn.
        private void DrawRegion(WardrobeRecolor rc, int r, List<WardrobePattern> patterns, float width) {
            var g = rc.garment;
            string regionName = g.regions[r].name;
            var style = WardrobeBaker.StyleOf(g, rc.look, r);   // a stand-in until the region is first changed
            var pattern = WardrobePatterns.Get(style.pattern);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(regionName, EditorStyles.boldLabel, GUILayout.Width(Mathf.Min(160, width * 0.4f)));
                GUILayout.FlexibleSpace();
                GUILayout.Label(pattern != null ? pattern.DisplayName : $"Missing pattern '{style.pattern}'", EditorStyles.miniLabel);
            }

            int clicked = SwatchRow(patterns, pattern, width, PreviewOf, PatternTip);
            if (clicked >= 0) PickPattern(rc, new[] { r }, patterns[clicked]);
            if (pattern == null) return;
            if (!string.IsNullOrWhiteSpace(pattern.description))
                EditorGUILayout.LabelField(pattern.description, EditorStyles.wordWrappedMiniLabel);

            if (pattern.type == WardrobePattern.Kind.Simple) DrawSlots(rc, new[] { r }, pattern, style);

            var (maps, uv) = pattern.MapsFor(g);
            if (pattern.type != WardrobePattern.Kind.Simple || uv || !maps.Any) return;   // laid out on this garment, or nothing that tiles
            EditorGUI.BeginChangeCheck();
            float scale = EditorGUILayout.Slider(new GUIContent("Scale", "Size of the pattern on the garment"), style.scale, 0.25f, 4f);
            bool rotate = EditorGUILayout.Toggle(new GUIContent("Turn 90°"), style.rotate);
            if (EditorGUI.EndChangeCheck())
                Edit(rc, "Change Pattern Size", () => {
                    var s = Own(rc, r);
                    s.scale = scale;
                    s.rotate = rotate;
                });
        }

        // The region's style in the look itself (added, as it showed, if the look didn't set it yet).
        private static RegionStyle Own(WardrobeRecolor rc, int r) {
            string name = rc.garment.regions[r].name;
            var style = rc.look.Get(name);
            if (style != null) return style;
            style = WardrobeBaker.StyleOf(rc.garment, rc.look, r);
            rc.look.styles.Add(style);
            return style;
        }

        // Every slot of the pattern set in the style, so one can be changed without the others moving.
        private static void Fill(RegionStyle style, WardrobePattern pattern) {
            for (int i = 0; i < pattern.slots.Count; i++) {
                if (i < style.colors.Count && style.colors[i] != null) continue;
                var value = WardrobeBaker.SlotOf(style, pattern, i).Clone();
                if (i < style.colors.Count) style.colors[i] = value;
                else style.colors.Add(value);
            }
        }

        // The pattern's colour slots as shown in <paramref name="style"/>; a change goes to every region in
        // <paramref name="regions"/>.
        private void DrawSlots(WardrobeRecolor rc, int[] regions, WardrobePattern pattern, RegionStyle style) {
            bool gradients = rc.garment.gradientMap != null;
            for (int i = 0; i < pattern.slots.Count; i++) {
                var slot = pattern.slots[i];
                var value = WardrobeBaker.SlotOf(style, pattern, i);
                int index = i;
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Label(slot.name, EditorStyles.miniLabel, GUILayout.Width(70));
                    bool asGradient = value.useGradient && gradients && value.gradient != null;
                    EditorGUI.BeginChangeCheck();
                    if (asGradient) {
                        var gradient = EditorGUILayout.GradientField(value.gradient);
                        if (EditorGUI.EndChangeCheck()) EditSlot(rc, regions, pattern, index, "Change Gradient", c => c.gradient = SlotColor.Copy(gradient));
                        if (GUILayout.Button(new GUIContent("⇄", "Flip the gradient"), GUILayout.Width(24)))
                            EditSlot(rc, regions, pattern, index, "Flip Gradient", c => c.gradient = Flip(c.gradient));
                    } else {
                        var colour = EditorGUILayout.ColorField(GUIContent.none, value.color, true, false, false);
                        if (EditorGUI.EndChangeCheck()) EditSlot(rc, regions, pattern, index, "Change Colour", c => c.color = colour);
                    }
                    if (slot.canGradient && gradients) {
                        bool toggled = GUILayout.Toggle(asGradient, new GUIContent("Gradient", "A colour gradient along the garment instead of one colour"),
                            EditorStyles.miniButton, GUILayout.Width(62));
                        if (toggled != asGradient)
                            EditSlot(rc, regions, pattern, index, "Switch Colour/Gradient", c => {
                                c.useGradient = toggled;
                                if (toggled && c.gradient == null) c.gradient = SlotColor.Constant(c.color);
                                if (!toggled && c.gradient != null) c.color = c.gradient.Evaluate(0);
                            });
                    }
                }
            }
            if (pattern.slots.Count > 1 && GUILayout.Button(new GUIContent("Swap First Two Colours"), EditorStyles.miniButton))
                Edit(rc, "Swap Colours", () => {
                    foreach (int r in regions) {
                        var s = Own(rc, r);
                        Fill(s, pattern);
                        (s.colors[0], s.colors[1]) = (s.colors[1], s.colors[0]);
                    }
                });
        }

        private void EditSlot(WardrobeRecolor rc, int[] regions, WardrobePattern pattern, int index, string undo, Action<SlotColor> change) =>
            Edit(rc, undo, () => {
                foreach (int r in regions) {
                    var s = Own(rc, r);
                    Fill(s, pattern);
                    change(s.colors[index]);
                }
            });

        // A wrapping row of picture swatches; returns the clicked one (other than the current), else -1.
        private static int SwatchRow<T>(List<T> options, T current, float width, Func<T, Texture> picture, Func<T, string> tooltip) where T : class {
            const float size = 30, gap = 3;
            int clicked = -1;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width - 30) / (size + gap)));
            for (int i = 0; i < options.Count; i += perRow) {
                var row = GUILayoutUtility.GetRect(0, size + gap, GUILayout.ExpandWidth(true));
                for (int j = 0; j < perRow && i + j < options.Count; j++) {
                    var option = options[i + j];
                    var rect = new Rect(row.x + j * (size + gap), row.y, size, size);
                    var image = picture(option);
                    if (image != null) GUI.DrawTexture(rect, image, ScaleMode.ScaleAndCrop);
                    else {
                        EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.25f));
                        if (option == null) GUI.Label(rect, "–", CenteredLabel);
                    }
                    if (option == current) DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2);
                    if (GUI.Button(rect, new GUIContent("", tooltip(option)), GUIStyle.none) && option != current) clicked = i + j;
                }
            }
            return clicked;
        }

        private void DrawFooter(WardrobeRecolor rc) {
            EditorGUI.BeginChangeCheck();
            string lookName = EditorGUILayout.DelayedTextField("Look Name", rc.lookName);
            if (EditorGUI.EndChangeCheck()) Edit(rc, "Rename Look", () => rc.lookName = lookName);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button(new GUIContent("Reset", "No look picked: every part in the default pattern and the garment's own colour")))
                    Edit(rc, "Reset Look", () => {
                        rc.look = new WardrobeLook();
                        rc.preset = "";
                        rc.lookName = CustomName;
                    });
                if (GUILayout.Button("Save as Preset…")) EditorApplication.delayCall += () => SavePreset(rc);
            }

            if (rc.bakedMaterial != null)
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Material", rc.bakedMaterial, typeof(Material), false);
            EditorGUILayout.HelpBox(
                "Changes go straight onto the garment: each look is baked into a texture and material in " +
                $"{WardrobeMaterialTypes.OutputFolder(rc.garment)}, so it works with any shader and uploads like any other material.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- actions

        private void Edit(WardrobeRecolor rc, string undo, Action change) {
            Undo.RecordObject(rc, undo);
            change();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc);
            EditorUtility.SetDirty(rc);
            QueuePreview();
            QueueApply(rc);
        }

        // Undo/redo brings back an earlier look; the live preview shows it and it's baked like any change.
        private void OnUndoRedo() {
            QueuePreview();
            if (_target != null) QueueApply(_target);
        }

        // Baking writes full-size textures and imports them, which stalls the editor, so a change only shows as the
        // live preview until no panel shows the garment any more (or one of the hooks below).
        private static void QueueApply(WardrobeRecolor rc) {
            if (rc == null || EditorUtility.IsPersistent(rc)) return;   // only garments in a scene, not prefab assets
            Pending.Add(rc);
        }

        // A garment with a pending look that no open panel shows (so nothing previews the look any more).
        private static bool Unwatched(WardrobeRecolor rc) => rc != null && Pending.Contains(rc) && Open.All(p => p._target != rc);

        // False when baking failed; the look stays pending, so the next save, build or change tries again.
        private static bool Bake(WardrobeRecolor rc) {
            if (rc == null || !Pending.Contains(rc) || WardrobeBaker.Problem(rc) != null) return true;
            try {
                WardrobeBaker.Apply(rc, recordUndo: false);   // the material always follows the look, so it's no undo step of its own
            } catch (Exception e) {
                Debug.LogException(e);
                return false;
            }
            Pending.Remove(rc);
            foreach (var panel in Open.Where(p => p._target == rc).ToList()) {
                panel.ClearPreview();   // the garment's own material shows the look now
                panel._repaint();
            }
            return true;
        }

        /// <summary>Bakes every pending look onto its garment (scene save, play mode, VRChat build, quitting). False when one failed.</summary>
        public static bool FlushAll() {
            Pending.RemoveWhere(rc => rc == null);
            bool baked = true;
            foreach (var rc in Pending.ToList()) baked &= Bake(rc);
            return baked;
        }

        private const string PendingAcrossReload = "RosDen.Wardrobe.Pending";

        [InitializeOnLoadMethod]
        private static void Hook() {
            UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += (scene, path) => FlushAll();
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingEditMode) FlushAll();
            };
            // A script reload (a package added, say) is no time to write assets, and it forgets what's pending: the
            // looks are remembered and baked right after it.
            AssemblyReloadEvents.beforeAssemblyReload += () => SessionState.SetIntArray(PendingAcrossReload,
                Pending.Where(rc => rc != null).Select(rc => rc.GetInstanceID()).ToArray());
            EditorApplication.delayCall += () => {
                foreach (int id in SessionState.GetIntArray(PendingAcrossReload, new int[0]))
                    if (EditorUtility.InstanceIDToObject(id) is WardrobeRecolor rc) Pending.Add(rc);
                SessionState.EraseIntArray(PendingAcrossReload);
                FlushAll();
            };
            EditorApplication.quitting += () => FlushAll();
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => RepairMissing();
            EditorApplication.delayCall += RepairMissing;   // scenes already open after a script reload
        }

        // A worn garment whose baked material is gone (deleted, or Wardrobe Recolors wasn't copied along) renders
        // magenta or in its shipped material: bake the look it keeps again. When a garment update renamed what's in its
        // model, the look has lost its renderer too: it's the garment's renderer wearing its mesh.
        private static void RepairMissing() {
            foreach (var rc in Object.FindObjectsOfType<WardrobeRecolor>(true)) {
                if (EditorUtility.IsPersistent(rc) || rc.garment == null || rc.look == null) continue;
                bool retargeted = false;
                if (rc.target == null) {
                    var skins = rc.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    rc.target = skins.FirstOrDefault(s => s.sharedMesh != null && s.sharedMesh == rc.garment.mesh) ?? skins.FirstOrDefault();
                    if (rc.target == null) continue;
                    retargeted = true;
                    EditorUtility.SetDirty(rc);
                }
                var materials = rc.target.sharedMaterials;
                if (rc.materialSlot < 0 || rc.materialSlot >= materials.Length) continue;
                if (!retargeted && materials[rc.materialSlot] != null && rc.bakedMaterial != null) continue;
                if (WardrobeBaker.Problem(rc) != null) continue;
                try {
                    WardrobeBaker.Apply(rc, recordUndo: false);
                    Debug.Log($"[Ros' Wardrobe] '{rc.name}' had lost its recoloured material; baked '{rc.lookName}' again.", rc);
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
        }

        // Writes the look as "<Name>.rpre" (in Wardrobe Recolors/<Avatar>/<Garment>/Presets unless picked elsewhere).
        private void SavePreset(WardrobeRecolor rc) {
            if (rc == null || rc.garment == null) return;
            string folder = WardrobeMaterialTypes.OutputFolder(rc.garment) + "/Presets";
            WardrobeAssets.EnsureFolder(folder);
            string path = EditorUtility.SaveFilePanelInProject("Save Look as Preset", WardrobeAssets.FileName(rc.lookName), WardrobeConfigs.Preset.TrimStart('.'),
                "Save this look as a .rpre so it shows up under Looks.", folder);
            if (string.IsNullOrEmpty(path)) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            WardrobePresets.Save(path, name, rc.look, rc.garment);
            Edit(rc, "Save Preset", () => rc.lookName = name);
            if (!_disposed) _repaint();
        }

        // ---------------------------------------------------------------- live preview

        private void QueuePreview() {
            if (_previewQueued || _disposed) return;
            _previewQueued = true;
            // Rendered outside of OnGUI so the blit can't disturb the editor's drawing.
            EditorApplication.delayCall += () => {
                _previewQueued = false;
                if (!_disposed) UpdatePreview();
            };
        }

        private void UpdatePreview() {
            var rc = _target;
            if (rc == null || WardrobeBaker.Problem(rc) != null) {
                ClearPreview();
                return;
            }
            if (_previewRenderer != rc.target || _previewSlot != rc.materialSlot) ClearPreview();
            try {
                _preview = WardrobeBaker.RenderPreview(rc.garment, rc.look, _preview);
            } catch (Exception e) {
                Debug.LogException(e);
                ClearPreview();
                return;
            }
            _previewRenderer = rc.target;
            _previewSlot = rc.materialSlot;
            var g = rc.garment;
            var type = WardrobeBaker.MaterialType(rc);
            WardrobeBaker.ClearPreview(_previewRenderer, _previewSlot);
            // Patterns' relief and metalness/roughness/occlusion, where the material type has a texture for them.
            _previewNormal = type.outputs.Any(o => o.normal) ? WardrobeBaker.RenderPreviewNormal(g, rc.look, _previewNormal) : Drop(_previewNormal);
            _previewSurface = type.outputs.Any(o => o.UsesSurface) ? WardrobeBaker.RenderPreviewSurface(g, rc.look, _previewSurface) : Drop(_previewSurface);
            foreach (var output in type.outputs) {
                Texture texture = null;
                if (output.albedo && WardrobeMaterialTypes.OpacityIsAlbedoAlpha(g)) texture = _preview;
                else if (output.normal) texture = _previewNormal;
                else if (output.albedo || output.UsesAlbedo || output.UsesSurface && _previewSurface != null) {
                    var surface = _previewSurface;
                    if (surface == null && output.UsesSurface)
                        surface = _previewGarmentSurface = _previewGarmentSurface != null ? _previewGarmentSurface : WardrobeBaker.GarmentSurface(g);
                    _previewOutputs.TryGetValue(output.property, out var rt);
                    texture = _previewOutputs[output.property] = WardrobeBaker.RenderPreviewOutput(g, output, _preview, surface, rt);
                }
                if (texture == null) continue;
                foreach (string property in output.Properties) WardrobeBaker.SetPreview(_previewRenderer, _previewSlot, property, texture);
            }
            SceneView.RepaintAll();
        }

        private static RenderTexture Drop(RenderTexture rt) {
            if (rt != null) Object.DestroyImmediate(rt);
            return null;
        }

        private void ClearPreview() {
            if (_previewRenderer != null) WardrobeBaker.ClearPreview(_previewRenderer, _previewSlot);
            _previewRenderer = null;
            _preview = Drop(_preview);
            _previewNormal = Drop(_previewNormal);
            _previewSurface = Drop(_previewSurface);
            _previewGarmentSurface = Drop(_previewGarmentSurface);
            foreach (var rt in _previewOutputs.Values) Drop(rt);
            _previewOutputs.Clear();
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- drawing helpers

        // A card: the look's picture, its name and a strip of its colours.
        private bool LookButton(Rect rect, string name, string tooltip, WardrobeLook look, WardrobeGarment g, bool selected) {
            bool clicked = GUI.Button(rect, new GUIContent("", tooltip));
            if (Event.current.type != EventType.Repaint) return clicked;
            var image = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.width - 8);
            var thumbnail = WardrobeThumbnails.Get(g, look, _target != null ? _target.materialType : null, _repaint);
            if (thumbnail != null) GUI.DrawTexture(image, thumbnail, ScaleMode.ScaleAndCrop);
            else EditorGUI.DrawRect(image, new Color(0.5f, 0.5f, 0.5f, 0.15f));
            var label = new Rect(rect.x + 4, image.yMax + 1, rect.width - 8, 16);
            GUI.Label(label, name, CenteredLabel);
            DrawChips(new Rect(rect.x + 6, label.yMax + 2, rect.width - 12, 5), look, g);
            if (selected) DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2);
            return clicked;
        }

        private static GUIStyle _centeredLabel;
        internal static GUIStyle CenteredLabel => _centeredLabel ??= new GUIStyle(EditorStyles.miniBoldLabel) {
            alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip,
        };

        // A chip per colour the look uses (each once, in part order), the same width and evenly spaced, centred;
        // outlined so dark colours still show on the card.
        private static void DrawChips(Rect rect, WardrobeLook look, WardrobeGarment g) {
            var colours = new List<SlotColor>();
            for (int r = 0; r < g.regions.Count && r < WardrobeBaker.MaxRegions; r++) {
                var style = WardrobeBaker.StyleOf(g, look, r);
                var pattern = WardrobePatterns.Get(style.pattern);
                if (pattern?.type == WardrobePattern.Kind.Complete) continue;   // a finished design's colours are in its picture
                for (int i = 0; i < Mathf.Max(1, pattern?.slots.Count ?? 1); i++) {
                    var c = WardrobeBaker.SlotOf(style, pattern, i);
                    if (!colours.Any(x => Same(x, c))) colours.Add(c);
                }
            }
            if (colours.Count == 0) return;
            const int gap = 3, maxChip = 14;
            int n = Mathf.Min(colours.Count, 6);
            int chip = Mathf.Min(maxChip, Mathf.FloorToInt((rect.width - gap * (n - 1)) / n));
            float x = Mathf.Round(rect.center.x - (n * chip + (n - 1) * gap) / 2f);
            float y = Mathf.Round(rect.y);
            for (int k = 0; k < n; k++, x += chip + gap) {
                var r = new Rect(x, y, chip, rect.height);
                EditorGUI.DrawRect(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0.6f, 0.6f, 0.6f, 0.5f));
                var c = colours[k];
                if (!c.useGradient || c.gradient == null) {
                    EditorGUI.DrawRect(r, c.color);
                    continue;
                }
                for (int j = 0; j < chip; j++)
                    EditorGUI.DrawRect(new Rect(r.x + j, r.y, 1, r.height), c.gradient.Evaluate((j + 0.5f) / chip));
            }
        }

        internal static void DrawOutline(Rect r, Color color, float width) {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - width, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, width, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - width, r.y, width, r.height), color);
        }

        private static Gradient Flip(Gradient g) {
            var flipped = new Gradient { mode = g.mode };
            flipped.SetKeys(
                g.colorKeys.Select(k => new GradientColorKey(k.color, 1 - k.time)).ToArray(),
                g.alphaKeys.Select(k => new GradientAlphaKey(k.alpha, 1 - k.time)).ToArray());
            return flipped;
        }
    }
}
