using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Author-side setup of a garment: source textures, ID/gradient maps, regions and shipped presets.</summary>
    [CustomEditor(typeof(WardrobeGarment))]
    public class WardrobeGarmentEditor : UnityEditor.Editor {
        private static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        private static readonly string[] SizeNames = { "512", "1024", "2048", "4096" };
        private static int _idSize = 2048;
        private static int _gradientSize = 2048;
        private static WardrobeGenerators.GradientDirection _direction;

        public override void OnInspectorGUI() {
            var g = (WardrobeGarment)target;

            serializedObject.Update();
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            Field("mesh");
            Field("baseColor");
            Field("material");
            TextureProperty(g);
            Field("detail");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Maps", EditorStyles.boldLabel);
            Field("idMap");
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                _idSize = EditorGUILayout.IntPopup(_idSize, SizeNames, Sizes, GUILayout.Width(56));
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Generate from UV islands"))
                        Later("Generating ID map…", () => WardrobeGenerators.GenerateIdMap(g, _idSize, g.regions.Count == 0));
            }
            Field("gradientMap");
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                _gradientSize = EditorGUILayout.IntPopup(_gradientSize, SizeNames, Sizes, GUILayout.Width(56));
                _direction = (WardrobeGenerators.GradientDirection)EditorGUILayout.EnumPopup(_direction);
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Generate", GUILayout.Width(70)))
                        Later("Generating gradient map…", () => WardrobeGenerators.GenerateGradientMap(g, _gradientSize, _direction));
            }
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "You can also paint these yourself (e.g. in Substance Painter). ID map: one flat colour per part, black = untouched. " +
                "Gradient map: 16-bit grayscale (R16), black = gradient start, white = end.",
                MessageType.None);

            EditorGUILayout.Space();
            DrawRegions(g);

            EditorGUILayout.Space();
            serializedObject.Update();
            EditorGUILayout.LabelField("Presets", EditorStyles.boldLabel);
            Field("presets");
            serializedObject.ApplyModifiedProperties();
        }

        private void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));

        private void TextureProperty(WardrobeGarment g) {
            var prop = serializedObject.FindProperty("textureProperty");
            var names = g.material != null ? g.material.GetTexturePropertyNames() : null;
            if (names == null || names.Length == 0) {
                EditorGUILayout.PropertyField(prop);
                return;
            }
            int index = Array.IndexOf(names, prop.stringValue);
            var label = new GUIContent(prop.displayName, prop.tooltip);
            int picked = EditorGUILayout.Popup(label, index, names.Select(n => new GUIContent(n)).ToArray());
            if (picked != index && picked >= 0) prop.stringValue = names[picked];
        }

        // ---------------------------------------------------------------- regions

        private void DrawRegions(WardrobeGarment g) {
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("A region is recoloured as one. Combine the ID map's parts into regions below.", EditorStyles.wordWrappedMiniLabel);

            List<WardrobeBaker.Part> parts;
            try {
                parts = WardrobeBaker.GetParts(g);
            } catch (Exception e) {
                EditorGUILayout.HelpBox("Couldn't read the ID map: " + e.Message, MessageType.Error);
                parts = new List<WardrobeBaker.Part>();
            }

            for (int r = 0; r < g.regions.Count; r++) {
                var region = g.regions[r];
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    string name = EditorGUILayout.DelayedTextField(region.name);
                    if (EditorGUI.EndChangeCheck()) Rename(g, region, name);
                    var swatches = GUILayoutUtility.GetRect(16 * Mathf.Max(1, region.ids.Count), 16, GUILayout.ExpandWidth(false));
                    for (int k = 0; k < region.ids.Count; k++)
                        EditorGUI.DrawRect(new Rect(swatches.x + k * 16 + 1, swatches.y + 1, 14, 14), region.ids[k]);
                    if (GUILayout.Button(new GUIContent("✕", "Remove region"), GUILayout.Width(22))) {
                        Record(g, "Remove Region");
                        g.regions.RemoveAt(r);
                        Dirty(g);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (g.regions.Count > WardrobeBaker.MaxRegions)
                EditorGUILayout.HelpBox($"Only the first {WardrobeBaker.MaxRegions} regions can be recoloured.", MessageType.Warning);
            var duplicates = g.regions.GroupBy(x => x.name).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            if (duplicates.Count > 0)
                EditorGUILayout.HelpBox("Region names must be unique: " + string.Join(", ", duplicates), MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Add Region")) {
                    Record(g, "Add Region");
                    g.regions.Add(new WardrobeRegion { name = UniqueName(g, "Region") });
                    Dirty(g);
                }
                using (new EditorGUI.DisabledScope(parts.Count == 0))
                    if (GUILayout.Button("One Region per Part") &&
                        (g.regions.Count == 0 || EditorUtility.DisplayDialog("Ros' Wardrobe", "Replace all regions with one region per part?", "Replace", "Cancel"))) {
                        Record(g, "One Region per Part");
                        g.regions.Clear();
                        for (int i = 0; i < parts.Count; i++)
                            g.regions.Add(new WardrobeRegion { name = $"Part {i + 1}", ids = new List<Color32> { parts[i].color } });
                        Dirty(g);
                    }
            }

            if (parts.Count == 0) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);
            if (g.idMap != null) {
                float size = Mathf.Min(256, EditorGUIUtility.currentViewWidth - 40);
                var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, g.idMap);
            }
            float total = Mathf.Max(1, parts.Sum(p => p.pixels));
            var options = new[] { "—" }.Concat(g.regions.Select(x => x.name)).ToArray();
            for (int i = 0; i < parts.Count; i++) {
                var part = parts[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    var swatch = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
                    EditorGUI.DrawRect(swatch, part.color);
                    EditorGUILayout.LabelField($"Part {i + 1}  ({part.pixels / total:P0})", GUILayout.Width(110));
                    int current = g.regions.FindIndex(x => x.ids.Any(c => WardrobeBaker.Same(c, part.color))) + 1;
                    int picked = EditorGUILayout.Popup(current, options);
                    if (picked != current) {
                        Record(g, "Assign Part");
                        foreach (var region in g.regions) region.ids.RemoveAll(c => WardrobeBaker.Same(c, part.color));
                        if (picked > 0) g.regions[picked - 1].ids.Add(part.color);
                        Dirty(g);
                    }
                }
            }
        }

        // Renaming a region also renames it in the garment's presets, so they keep working.
        private static void Rename(WardrobeGarment g, WardrobeRegion region, string name) {
            name = name.Trim();
            if (name.Length == 0 || name == region.name) return;
            var presets = g.presets.Where(p => p != null).Distinct().ToArray();
            Undo.RecordObjects(new Object[] { g }.Concat(presets).ToArray(), "Rename Region");
            foreach (var preset in presets) {
                foreach (var style in preset.look.styles)
                    if (style.region == region.name) style.region = name;
                EditorUtility.SetDirty(preset);
            }
            region.name = name;
            Dirty(g);
        }

        private static string UniqueName(WardrobeGarment g, string name) {
            string candidate = name;
            for (int i = 2; g.regions.Any(r => r.name == candidate); i++) candidate = $"{name} {i}";
            return candidate;
        }

        private static void Record(WardrobeGarment g, string undo) => Undo.RecordObject(g, undo);

        private static void Dirty(WardrobeGarment g) => EditorUtility.SetDirty(g);

        // Heavy work runs after the inspector has finished drawing, with a progress bar.
        private void Later(string label, Action action) {
            EditorApplication.delayCall += () => {
                try {
                    EditorUtility.DisplayProgressBar("Ros' Wardrobe", label, 0.5f);
                    action();
                } catch (Exception e) {
                    Debug.LogException(e);
                    EditorUtility.DisplayDialog("Ros' Wardrobe", e.Message, "OK");
                } finally {
                    EditorUtility.ClearProgressBar();
                    if (this != null) Repaint();
                }
            };
        }
    }
}
