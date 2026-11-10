using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Edits one .rgarm: the author-side setup of a garment fit (its avatar, maps, ID/gradient maps and regions). It
    /// edits a copy of the garment; Save writes it into the file, which imports the garment again.
    /// </summary>
    internal class WardrobeGarmentForm : WardrobeForm {
        private static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        private static readonly string[] SizeNames = { "512", "1024", "2048", "4096" };
        private static readonly GUIContent[] Channels = { new GUIContent("R"), new GUIContent("G"), new GUIContent("B"), new GUIContent("A") };
        private static readonly int[] ChannelValues = { 0, 1, 2, 3 };
        private static readonly int[] MapSizes = { 0, 512, 1024, 2048, 4096 };
        private static readonly GUIContent[] MapSizeNames = MapSizes.Select(s => new GUIContent(s == 0 ? "The base colour's" : s.ToString())).ToArray();
        private static int _idSize = 2048, _gradientSize = 2048;
        private static WardrobeGenerators.GradientDirection _direction;

        private WardrobeGarment _draft;
        private SerializedObject _so;
        private string _saved, _text, _error;
        private int _dirtyCount = -1, _revision;
        private bool _showMore;
        private WardrobeFitter.Report _fit;

        public WardrobeGarmentForm(string path) : base(path) => Revert();

        public override bool Dirty {
            get {
                if (_draft == null) return false;
                // what the file would say, worked out again only after the copy changed
                int count = EditorUtility.GetDirtyCount(_draft);
                if (count != _dirtyCount) {
                    _dirtyCount = count;
                    _text = WardrobeGarments.Text(_draft, path);
                }
                return _text != _saved;
            }
        }

        public override void Revert() {
            Dispose();
            _error = null;
            _revision = WardrobeConfigs.Revision;
            try {
                _draft = WardrobeGarments.Draft(path);
                _so = new SerializedObject(_draft);
                _saved = _text = WardrobeGarments.Text(_draft, path);
                _dirtyCount = EditorUtility.GetDirtyCount(_draft);
            } catch (Exception e) {
                Dispose();
                _error = e.Message;
            }
        }

        public override void Save() {
            if (_draft == null) return;
            WardrobeGarments.Save(_draft, path);
            _saved = _text = WardrobeGarments.Text(_draft, path);
            _dirtyCount = EditorUtility.GetDirtyCount(_draft);
            _revision = WardrobeConfigs.Revision;
        }

        public override void Dispose() {
            _so?.Dispose();
            _so = null;
            WardrobeGarments.Release(_draft);
            _draft = null;
        }

        // ---------------------------------------------------------------- drawing

        public override void Draw() {
            if (_revision != WardrobeConfigs.Revision) {
                _revision = WardrobeConfigs.Revision;
                if (!Dirty) Revert();
            }
            if (_draft == null) {
                EditorGUILayout.HelpBox($"Couldn't read {path}: {_error}", MessageType.Error);
                if (GUILayout.Button("Read Again", GUILayout.Width(100))) Revert();
                return;
            }
            var g = _draft;
            _so.Update();

            Header("Garment");
            Field("displayName", "Name");
            Field("id", "Id");
            if (string.IsNullOrWhiteSpace(g.id))
                EditorGUILayout.HelpBox("Give the garment an id (e.g. yourname.t-shirt) so patterns made for it can find it.", MessageType.Warning);
            else if (WardrobeGarments.All.Any(o => WardrobeConfigs.SameId(o.id, g.id) && WardrobeConfigs.SameId(o.avatarId, g.avatarId) &&
                                                   WardrobeGarments.PathOf(o) != path))
                EditorGUILayout.HelpBox($"Another .rgarm is {g.id} on the same avatar; only one of them is used.", MessageType.Warning);
            Field("version");
            Field("author");
            Field("category");
            Field("slot");
            Field("bodyHides", "Hides on body");
            Field("prefab");
            Field("thumbnail");

            EditorGUILayout.Space();
            Header("Avatar");
            Note("The avatar base this fit is for. Fits of the same garment for other avatars are their own .rgarm files with the same id.");
            DrawAvatar(g);

            EditorGUILayout.Space();
            Header("Fit to Avatar");
            DrawFit(g);

            EditorGUILayout.Space();
            Header("Material");
            DrawMaterial(g);
            DrawShipped(g);

            EditorGUILayout.Space();
            Header("Maps");
            Field("mesh");
            Field("baseColor", "Base Colour");
            Field("normalMap", "Normal");
            MapChannel("metalness", "Metalness");
            MapChannel("roughness", "Roughness", "Pick the channel and tick Inverted for a smoothness map");
            MapChannel("occlusion", "Occlusion");
            MapChannel("sheen", "Sheen", "How much rim light / sheen the toon material gives the cloth");
            MapChannel("opacity", "Opacity", "Without a texture: the base colour's alpha");
            _so.ApplyModifiedProperties();

            EditorGUILayout.Space();
            Header("Recolouring");
            DrawMaps(g);
            EditorGUILayout.Space();
            DrawRegions(g);

            EditorGUILayout.Space();
            _showMore = EditorGUILayout.Foldout(_showMore, "Patterns and presets for it", true);
            if (_showMore) DrawUsage(g);
        }

        private void Field(string name, string label = null) {
            var p = _so.FindProperty(name);
            if (label == null) EditorGUILayout.PropertyField(p);
            else EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip));
        }

        // A texture channel, or a value without a texture.
        private void MapChannel(string name, string label, string tooltip = null) {
            var p = _so.FindProperty(name);
            var texture = p.FindPropertyRelative("texture");
            EditorGUILayout.PropertyField(texture, new GUIContent(label, tooltip));
            EditorGUI.indentLevel++;
            if (texture.objectReferenceValue != null) {
                var channel = p.FindPropertyRelative("channel");
                using (new EditorGUILayout.HorizontalScope()) {
                    channel.intValue = EditorGUILayout.IntPopup(new GUIContent("Channel"), Mathf.Clamp(channel.intValue, 0, 3), Channels, ChannelValues);
                    var invert = p.FindPropertyRelative("invert");
                    invert.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Inverted", invert.tooltip), invert.boolValue, GUILayout.Width(80));
                }
            } else {
                EditorGUILayout.PropertyField(p.FindPropertyRelative("value"), new GUIContent("Value", "Everywhere, as there's no texture"));
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAvatar(WardrobeGarment g) {
            var avatars = WardrobeAvatars.All;
            var options = new List<string> { "Any avatar" };
            options.AddRange(avatars.Select(a => $"{a.DisplayName}  ({a.id})".Replace("/", "∕")));
            int index = string.IsNullOrWhiteSpace(g.avatarId) ? 0 : avatars.FindIndex(a => WardrobeConfigs.SameId(a.id, g.avatarId)) + 1;
            bool unknown = index == 0 && !string.IsNullOrWhiteSpace(g.avatarId);
            if (unknown) options.Add(g.avatarId + " (no .ravt here)");
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent("Made For", "The avatar base's .ravt"), unknown ? options.Count - 1 : index, options.ToArray());
            if (EditorGUI.EndChangeCheck() && !(unknown && picked == options.Count - 1)) {
                _so.FindProperty("avatarId").stringValue = picked == 0 ? "" : avatars[picked - 1].id;
            }
            Field("avatarId", "Avatar Id");
            Field("avatarVersion", "Avatar Version");
            if (unknown)
                EditorGUILayout.HelpBox($"No .ravt in the project has the id {g.avatarId}, so Ros' Wardrobe can't tell which avatars it fits. " +
                                        "Make one with Assets → Create → Ros' Wardrobe → Avatar Base, and ship it with the garment.", MessageType.Warning);
        }

        private void DrawMaterial(WardrobeGarment g) {
            var types = WardrobeMaterialTypes.All.Where(t => t.Fits(g)).ToList();
            var options = new List<string> { "The first that fits" };
            options.AddRange(types.Select(t => t.DisplayName + (t.Problem != null ? " (can't be used)" : "")));
            int index = string.IsNullOrWhiteSpace(g.defaultMaterial) ? 0 : types.FindIndex(t => WardrobeConfigs.SameId(t.id, g.defaultMaterial)) + 1;
            if (index == 0 && !string.IsNullOrWhiteSpace(g.defaultMaterial)) {
                options.Add(g.defaultMaterial + " (not in the project)");
                index = options.Count - 1;
            }
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent("Starts In", "The material type (.rmat) the garment starts in"), index, options.ToArray());
            if (EditorGUI.EndChangeCheck() && picked <= types.Count) _so.FindProperty("defaultMaterial").stringValue = picked == 0 ? "" : types[picked - 1].id;
            if (types.Count == 0)
                EditorGUILayout.HelpBox("No material type (.rmat) in the project is for this garment, so it can't be recoloured. Ros' Wardrobe ships PBR; " +
                                        "make others with Assets → Create → Ros' Wardrobe → Material Type.", MessageType.Warning);
            foreach (var t in types.Where(t => t.Problem != null)) EditorGUILayout.HelpBox(t.Problem, MessageType.Warning);
            if (types.Count > 0)
                Note($"Buyers can pick: {string.Join(", ", types.Where(t => t.Problem == null).Select(t => t.DisplayName))}. Each .rmat lists the garments it's for.");
        }

        private void DrawFit(WardrobeGarment g) {
            Note("The garment as your cloth tool exports it (e.g. Marvelous Designer's FBX), rigged to the avatar's skeleton. Fit makes " +
                 $"'{WardrobeAssets.FileName(WardrobeAssets.BaseName(g))}.asset' next to the .rgarm from it: the mesh the prefab wears, with " +
                 "the body's shape keys so it follows the body. After a new export, overwrite the file and fit again.");
            Field("source", "Source Model");
            _so.ApplyModifiedProperties();
            string problem = WardrobeFitter.Problem(g);
            if (problem != null) {
                EditorGUILayout.HelpBox(problem, MessageType.Info);
                return;
            }
            var avatar = WardrobeAvatars.Get(g.avatarId);
            var shapes = WardrobeFitter.Shapes(g);
            Note($"Copies {avatar.DisplayName}'s body shape keys: {(shapes.Count == 0 ? "none" : string.Join(", ", shapes))} " +
                 "(the avatar base's Fitting garments; without a list, the ones its sliders set).");
            bool fitted = g.mesh != null && AssetDatabase.GetAssetPath(g.mesh) == WardrobeFitter.FitPath(g);
            bool upToDate = WardrobeFitter.UpToDate(g);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(upToDate ? "Up to date." : fitted ? "Out of date: the source model or the avatar base changed." : "Not fitted yet.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(fitted ? "Fit Again" : "Fit to Avatar", GUILayout.Width(100)))
                    Later($"Fitting {Title(g)} to {avatar.DisplayName}…", () => _fit = WardrobeFitter.Fit(g));
            }
            if (_fit == null) return;
            if (_fit.added.Count > 0) Note("Copied: " + string.Join(", ", _fit.added.Select(a => $"{a.shape} ({a.mm:0.#} mm)")));
            if (_fit.skipped.Count > 0) Note("Left out: " + string.Join(", ", _fit.skipped.Select(s => $"{s.shape} ({s.why})")));
            foreach (string note in _fit.notes) Note(note);
        }

        private void DrawShipped(WardrobeGarment g) {
            var type = WardrobeMaterialTypes.For(g, g.defaultMaterial);
            if (type == null) return;
            EditorGUILayout.Space(2);
            Note($"The prefab ships in it: Make Material writes '{System.IO.Path.GetFileName(WardrobeMaterialTypes.ShippedPath(g, type))}' next to " +
                 "the .rgarm from the maps below, packing them the way the type needs, and puts it on the prefab. Keep the maps as your " +
                 "tools export them.");
            var size = _so.FindProperty("mapSize");
            size.intValue = EditorGUILayout.IntPopup(new GUIContent("Packed Map Size", size.tooltip), size.intValue, MapSizeNames, MapSizes);
            _so.ApplyModifiedProperties();
            bool made = System.IO.File.Exists(WardrobeMaterialTypes.ShippedPath(g, type));
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(!made ? "Not made yet." : WardrobeMaterialTypes.ShippedUpToDate(g, type) ? "Up to date."
                    : "Out of date: the maps, the size or the material type changed.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(g.baseColor == null))
                    if (GUILayout.Button(made ? "Make Again" : "Make Material", GUILayout.Width(100)))
                        Later($"Making {Title(g)}'s material…", () => WardrobeMaterialTypes.Ship(g, type));
            }
        }

        private void DrawMaps(WardrobeGarment g) {
            Field("idMap", "ID Map");
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                _idSize = EditorGUILayout.IntPopup(_idSize, SizeNames, Sizes, GUILayout.Width(56));
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Generate from UV islands"))
                        Later("Generating ID map…", () => WardrobeGenerators.GenerateIdMap(g, _idSize, g.regions.Count == 0));
            }
            Field("gradientMap", "Gradient Map");
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                _gradientSize = EditorGUILayout.IntPopup(_gradientSize, SizeNames, Sizes, GUILayout.Width(56));
                _direction = (WardrobeGenerators.GradientDirection)EditorGUILayout.EnumPopup(_direction);
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Generate", GUILayout.Width(70)))
                        Later("Generating gradient map…", () => WardrobeGenerators.GenerateGradientMap(g, _gradientSize, _direction));
            }
            Field("metresPerUv", "Metres per UV");
            _so.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "You can also paint these yourself (e.g. in Substance Painter). ID map: one flat colour per part, black = untouched. " +
                "Gradient map: 16-bit grayscale (R16), black = gradient start, white = end. The maps are written next to the .rgarm.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- regions

        private void DrawRegions(WardrobeGarment g) {
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            Note("A region is recoloured as one. Combine the ID map's parts into regions below.");

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
                        Changing(g, "Remove Region");
                        g.regions.RemoveAt(r);
                        Changed(g);
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
                    Changing(g, "Add Region");
                    g.regions.Add(new WardrobeRegion { name = UniqueName(g, "Region") });
                    Changed(g);
                }
                using (new EditorGUI.DisabledScope(parts.Count == 0))
                    if (GUILayout.Button("One Region per Part") &&
                        (g.regions.Count == 0 || EditorUtility.DisplayDialog("Ros' Wardrobe", "Replace all regions with one region per part?", "Replace", "Cancel"))) {
                        Changing(g, "One Region per Part");
                        g.regions.Clear();
                        for (int i = 0; i < parts.Count; i++)
                            g.regions.Add(new WardrobeRegion { name = $"Part {i + 1}", ids = new List<Color32> { parts[i].color } });
                        Changed(g);
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
                        Changing(g, "Assign Part");
                        foreach (var region in g.regions) region.ids.RemoveAll(c => WardrobeBaker.Same(c, part.color));
                        if (picked > 0) g.regions[picked - 1].ids.Add(part.color);
                        Changed(g);
                    }
                }
            }
        }

        // Renaming a region also renames it in the garment's .rpre files, so they keep working.
        private void Rename(WardrobeGarment g, WardrobeRegion region, string name) {
            name = name.Trim();
            if (name.Length == 0 || name == region.name) return;
            Changing(g, "Rename Region");
            WardrobePresets.RenameRegion(g, region.name, name);
            region.name = name;
            Changed(g);
        }

        private static string UniqueName(WardrobeGarment g, string name) {
            string candidate = name;
            for (int i = 2; g.regions.Any(r => r.name == candidate); i++) candidate = $"{name} {i}";
            return candidate;
        }

        private void Changing(WardrobeGarment g, string undo) {
            _so.ApplyModifiedProperties();
            Undo.RecordObject(g, undo);
        }

        private void Changed(WardrobeGarment g) {
            EditorUtility.SetDirty(g);
            _so.Update();
        }

        // ---------------------------------------------------------------- what's made for it

        private static void DrawUsage(WardrobeGarment g) {
            var presets = WardrobePresets.For(g);
            Note($"{presets.Count} presets (.rpre) in the project are for this fit (they list its id, or no garments). Save as Preset… in the " +
                 "recolour editor writes one; select it to edit it.");
            foreach (var preset in presets)
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(preset.DisplayName, preset.colourways.Count == 0 ? "" : $"{preset.colourways.Count} colourways", EditorStyles.miniLabel);
                    if (GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(44)))
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(preset.jsonPath));
                }

            EditorGUILayout.Space();
            var patterns = WardrobePatterns.All.Where(p => p.Fits(g)).ToList();
            Note($"{patterns.Count} patterns (.rpat) in the project fit it (those that list no garments, or list this one). " +
                 "Tiling patterns use the part scales from Generate from UV islands.");
            if (g.partScales.Count == 0)
                EditorGUILayout.HelpBox("Regenerate the ID map (Generate from UV islands) so patterns know each part's real-world size.", MessageType.Info);
        }
    }
}
