using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The buyer-facing recolour panel: pick a look, change colours/gradients per region, see it live on the
    /// garment, save looks as presets and apply.
    /// </summary>
    [CustomEditor(typeof(WardrobeRecolor))]
    public class WardrobeRecolorEditor : UnityEditor.Editor {
        private static readonly (string name, Color color)[] Swatches = {
            ("White", Hex(0xF4F4F2)), ("Cream", Hex(0xEFE6D2)), ("Sand", Hex(0xCDB891)), ("Light Grey", Hex(0xB9BBBE)),
            ("Charcoal", Hex(0x3C3F44)), ("Black", Hex(0x161618)), ("Red", Hex(0xB3202A)), ("Burgundy", Hex(0x6B1E2C)),
            ("Orange", Hex(0xE0712B)), ("Mustard", Hex(0xD9A520)), ("Olive", Hex(0x6B6B33)), ("Forest", Hex(0x2E5A3A)),
            ("Mint", Hex(0xA8D8C0)), ("Teal", Hex(0x1F7A7A)), ("Sky", Hex(0x8EC3E6)), ("Royal Blue", Hex(0x2B4FA8)),
            ("Navy", Hex(0x1F2A48)), ("Lavender", Hex(0xB7A6D9)), ("Purple", Hex(0x5B2C83)), ("Pink", Hex(0xEAA2B8)),
            ("Hot Pink", Hex(0xD63A7A)), ("Brown", Hex(0x6A4A35)),
        };
        private static readonly GUIContent[] ModeLabels = { new GUIContent("Original"), new GUIContent("Colour"), new GUIContent("Gradient") };

        private static bool _setupOpen;
        private static bool _livePreview = true;

        private RenderTexture _preview;
        private Renderer _previewRenderer;
        private int _previewSlot;
        private bool _previewQueued;
        private List<WardrobePreset> _presets;
        private WardrobeGarment _presetsFor;

        private void OnEnable() {
            Undo.undoRedoPerformed += QueuePreview;
            QueuePreview();
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= QueuePreview;
            ClearPreview();
        }

        public override void OnInspectorGUI() {
            var rc = (WardrobeRecolor)target;
            if (rc.look == null) rc.look = new WardrobeLook();

            DrawSetup(rc);
            string problem = WardrobeBaker.Problem(rc);
            if (problem != null) {
                EditorGUILayout.HelpBox(problem, MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            DrawLooks(rc);
            EditorGUILayout.Space();
            DrawRegions(rc);
            EditorGUILayout.Space();
            DrawFooter(rc);
        }

        // ---------------------------------------------------------------- sections

        private void DrawSetup(WardrobeRecolor rc) {
            serializedObject.Update();
            if (rc.garment == null || rc.target == null) _setupOpen = true;
            _setupOpen = EditorGUILayout.Foldout(_setupOpen, "Setup", true);
            if (_setupOpen) {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("garment"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("target"), new GUIContent("Renderer"));
                var materials = rc.target != null ? rc.target.sharedMaterials : new Material[0];
                if (materials.Length > 1) {
                    var slot = serializedObject.FindProperty("materialSlot");
                    var names = materials.Select((m, i) => $"{i}: {(m != null ? m.name : "None")}").ToArray();
                    slot.intValue = EditorGUILayout.Popup("Material Slot", slot.intValue, names);
                }
                EditorGUI.indentLevel--;
            }
            if (serializedObject.ApplyModifiedProperties()) {
                ClearPreview();
                QueuePreview();
            }
        }

        private void DrawLooks(WardrobeRecolor rc) {
            var g = rc.garment;
            // Presets can be unloaded under us (e.g. on scene changes); look them up again when that happens.
            if (_presets == null || _presetsFor != g || _presets.Any(p => p == null)) {
                _presets = FindPresets(g);
                _presetsFor = g;
            }

            EditorGUILayout.LabelField("Looks", EditorStyles.boldLabel);
            var looks = new List<(string name, WardrobeLook look)> { ("Original", null) };
            looks.AddRange(_presets.Select(p => (p.name, p.look)));
            int columns = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 24) / 104));
            for (int i = 0; i < looks.Count; i += columns) {
                using (new EditorGUILayout.HorizontalScope()) {
                    for (int j = 0; j < columns; j++) {
                        var rect = GUILayoutUtility.GetRect(96, 44, GUILayout.ExpandWidth(true));
                        if (i + j >= looks.Count) continue;
                        var (name, look) = looks[i + j];
                        if (LookButton(rect, name, look, g, rc.lookName == name))
                            Edit(rc, "Pick Look", () => {
                                rc.look = look != null ? look.Clone() : new WardrobeLook();
                                rc.lookName = name;
                            });
                    }
                }
            }
        }

        private void DrawRegions(WardrobeRecolor rc) {
            var g = rc.garment;
            EditorGUILayout.LabelField("Colours", EditorStyles.boldLabel);
            foreach (var region in g.regions.Take(WardrobeBaker.MaxRegions)) {
                var style = rc.look.Get(region.name);
                var mode = style?.mode ?? RegionMode.Keep;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.LabelField(region.name, EditorStyles.boldLabel, GUILayout.Width(Mathf.Min(140, EditorGUIUtility.labelWidth)));
                        var picked = (RegionMode)GUILayout.Toolbar((int)mode, ModeLabels);
                        if (picked != mode) Edit(rc, "Change Region Mode", () => rc.look.GetOrAdd(region.name).mode = picked);
                    }

                    if (mode == RegionMode.Solid) {
                        EditorGUI.BeginChangeCheck();
                        var color = EditorGUILayout.ColorField(GUIContent.none, style.color, true, false, false);
                        if (EditorGUI.EndChangeCheck()) Edit(rc, "Change Colour", () => style.color = color);
                        var swatch = SwatchGrid(style.color);
                        if (swatch.HasValue) Edit(rc, "Pick Colour", () => style.color = swatch.Value);
                    } else if (mode == RegionMode.Gradient) {
                        using (new EditorGUILayout.HorizontalScope()) {
                            EditorGUI.BeginChangeCheck();
                            var gradient = EditorGUILayout.GradientField(style.gradient);
                            if (EditorGUI.EndChangeCheck()) Edit(rc, "Change Gradient", () => style.gradient = Copy(gradient));
                            if (GUILayout.Button(new GUIContent("⇄", "Flip the gradient"), GUILayout.Width(26)))
                                Edit(rc, "Flip Gradient", () => style.gradient = Flip(style.gradient));
                        }
                        if (g.gradientMap == null)
                            EditorGUILayout.HelpBox("This garment has no gradient map, so only the gradient's first colour shows.", MessageType.Warning);
                    }
                }
            }
        }

        private void DrawFooter(WardrobeRecolor rc) {
            EditorGUI.BeginChangeCheck();
            string lookName = EditorGUILayout.DelayedTextField("Look Name", rc.lookName);
            if (EditorGUI.EndChangeCheck()) Edit(rc, "Rename Look", () => rc.lookName = lookName);

            EditorGUI.BeginChangeCheck();
            _livePreview = EditorGUILayout.Toggle(new GUIContent("Live Preview", "Show the recolour on the garment while this is selected"), _livePreview);
            if (EditorGUI.EndChangeCheck()) QueuePreview();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Reset"))
                    Edit(rc, "Reset Look", () => {
                        rc.look = new WardrobeLook();
                        rc.lookName = "Original";
                    });
                if (GUILayout.Button("Save as Preset…")) EditorApplication.delayCall += () => SavePreset(rc);
            }
            if (GUILayout.Button("Apply to Garment", GUILayout.Height(30))) EditorApplication.delayCall += () => ApplyNow(rc);

            if (rc.bakedMaterial != null)
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField("Applied", rc.bakedMaterial, typeof(Material), false);
            EditorGUILayout.HelpBox(
                "The preview only shows while this is selected. Apply bakes the look into a texture and material in " +
                $"{WardrobeBaker.OutputFolder(rc.garment)} and puts it on the garment, so it works with any shader and uploads like any other material.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- actions

        private void Edit(WardrobeRecolor rc, string undo, Action change) {
            Undo.RecordObject(rc, undo);
            change();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rc);
            EditorUtility.SetDirty(rc);
            QueuePreview();
        }

        private void ApplyNow(WardrobeRecolor rc) {
            if (rc == null) return;
            try {
                EditorUtility.DisplayProgressBar("Ros' Wardrobe", $"Baking \"{rc.lookName}\"…", 0.5f);
                WardrobeBaker.Apply(rc);
            } catch (Exception e) {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Ros' Wardrobe", "Couldn't apply the look:\n" + e.Message, "OK");
            } finally {
                EditorUtility.ClearProgressBar();
            }
            if (this != null) {
                QueuePreview();
                Repaint();
            }
        }

        private void SavePreset(WardrobeRecolor rc) {
            if (rc == null || rc.garment == null) return;
            string folder = WardrobeBaker.OutputFolder(rc.garment) + "/Presets";
            WardrobeAssets.EnsureFolder(folder);
            string path = EditorUtility.SaveFilePanelInProject("Save Look as Preset", WardrobeAssets.FileName(rc.lookName), "asset",
                "Save this look so it shows up under Looks.", folder);
            if (string.IsNullOrEmpty(path)) return;

            var preset = AssetDatabase.LoadAssetAtPath<WardrobePreset>(path);
            bool create = preset == null;
            if (create) preset = CreateInstance<WardrobePreset>();
            else Undo.RecordObject(preset, "Save Preset");
            preset.garment = rc.garment;
            preset.look = rc.look.Clone();
            if (create) AssetDatabase.CreateAsset(preset, path);
            else EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();

            Edit(rc, "Save Preset", () => rc.lookName = preset.name);
            _presets = null;
            if (this != null) Repaint();
        }

        // Presets shipped with the garment first, then any other preset made for it in the project.
        private static List<WardrobePreset> FindPresets(WardrobeGarment g) {
            var list = g.presets.Where(p => p != null).Distinct().ToList();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(WardrobePreset))) {
                var preset = AssetDatabase.LoadAssetAtPath<WardrobePreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (preset != null && preset.garment == g && !list.Contains(preset)) list.Add(preset);
            }
            return list;
        }

        // ---------------------------------------------------------------- live preview

        private void QueuePreview() {
            if (_previewQueued) return;
            _previewQueued = true;
            // Rendered outside of OnGUI so the blit can't disturb the inspector's drawing.
            EditorApplication.delayCall += () => {
                _previewQueued = false;
                if (this != null) UpdatePreview();
            };
        }

        private void UpdatePreview() {
            var rc = target as WardrobeRecolor;
            if (rc == null || !_livePreview || WardrobeBaker.Problem(rc) != null) {
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
            WardrobeBaker.SetPreview(_previewRenderer, _previewSlot, rc.garment.textureProperty, _preview);
            SceneView.RepaintAll();
        }

        private void ClearPreview() {
            if (_previewRenderer != null) WardrobeBaker.ClearPreview(_previewRenderer, _previewSlot);
            _previewRenderer = null;
            if (_preview != null) {
                Object.DestroyImmediate(_preview);
                _preview = null;
            }
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- drawing helpers

        private static bool LookButton(Rect rect, string name, WardrobeLook look, WardrobeGarment g, bool selected) {
            bool clicked = GUI.Button(rect, GUIContent.none);
            if (Event.current.type != EventType.Repaint) return clicked;
            if (selected) DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2);
            GUI.Label(new Rect(rect.x + 6, rect.y + 3, rect.width - 12, 16), name, EditorStyles.miniBoldLabel);
            var chips = new Rect(rect.x + 6, rect.y + 22, rect.width - 12, rect.height - 28);
            int count = Mathf.Max(1, Mathf.Min(g.regions.Count, WardrobeBaker.MaxRegions));
            float width = chips.width / count;
            for (int k = 0; k < count && k < g.regions.Count; k++)
                DrawStyle(new Rect(chips.x + k * width + 1, chips.y, width - 2, chips.height), look?.Get(g.regions[k].name));
            return clicked;
        }

        private static void DrawStyle(Rect rect, RegionStyle style) {
            if (style == null || style.mode == RegionMode.Keep) {
                EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
                return;
            }
            if (style.mode == RegionMode.Solid || style.gradient == null) {
                EditorGUI.DrawRect(rect, style.color);
                return;
            }
            const int steps = 8;
            for (int i = 0; i < steps; i++)
                EditorGUI.DrawRect(new Rect(rect.x + rect.width * i / steps, rect.y, rect.width / steps + 0.5f, rect.height),
                    style.gradient.Evaluate((i + 0.5f) / steps));
        }

        private static Color? SwatchGrid(Color current) {
            const float size = 18, gap = 3;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 50) / (size + gap)));
            Color? picked = null;
            for (int i = 0; i < Swatches.Length; i += perRow) {
                var row = GUILayoutUtility.GetRect(0, size + gap, GUILayout.ExpandWidth(true));
                for (int j = 0; j < perRow && i + j < Swatches.Length; j++) {
                    var (name, color) = Swatches[i + j];
                    var rect = new Rect(row.x + j * (size + gap), row.y, size, size);
                    EditorGUI.DrawRect(rect, color);
                    if (Similar(color, current)) DrawOutline(rect, Color.white, 2);
                    if (GUI.Button(rect, new GUIContent("", name), GUIStyle.none)) picked = color;
                }
            }
            return picked;
        }

        private static void DrawOutline(Rect r, Color color, float width) {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - width, r.width, width), color);
            EditorGUI.DrawRect(new Rect(r.x, r.y, width, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax - width, r.y, width, r.height), color);
        }

        private static bool Similar(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        private static Gradient Copy(Gradient g) {
            var copy = new Gradient { mode = g.mode };
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
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
