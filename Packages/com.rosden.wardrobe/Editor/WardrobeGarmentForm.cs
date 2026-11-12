using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Edits one .rgarm as a guided setup, one step after the other: the garment's details, the avatar it's for and the
    /// cloth tool's export (with its maps found next to it), the maps, Fit to Avatar, the prefab, the shipped material, the
    /// recolouring maps and regions, and which material types, patterns and presets are for it. It edits a copy of the
    /// garment; Save writes it into the file. The buttons that make something (fit, prefab, material, maps) save it too.
    /// </summary>
    internal class WardrobeGarmentForm : WardrobeForm {
        private static readonly int[] Sizes = { 512, 1024, 2048, 4096 };
        private static readonly string[] SizeNames = { "512", "1024", "2048", "4096" };
        private static readonly GUIContent[] Channels = { new GUIContent("R"), new GUIContent("G"), new GUIContent("B"), new GUIContent("A") };
        private static readonly int[] ChannelValues = { 0, 1, 2, 3 };
        private static readonly int[] MapSizes = { 0, 512, 1024, 2048, 4096 };
        private static readonly GUIContent[] MapSizeNames = MapSizes.Select(s => new GUIContent(s == 0 ? "The base colour's" : s.ToString())).ToArray();
        private static readonly string[] Categories = { "Tops", "Bottoms", "Outerwear", "Full Body", "Underwear", "Shoes", "Socks", "Headwear", "Accessories" };
        private static readonly string[] Slots = { "Top", "Bottom", "Jacket", "Full Body", "Underwear", "Shoes", "Socks", "Hat", "Gloves" };
        private static readonly string[] RegionNames = { "Body", "Front", "Back", "Sleeves", "Collar", "Cuffs", "Hem", "Hood", "Pocket", "Trim", "Lining", "Buttons", "Laces", "Sole", "Waistband" };
        private static int _idSize = 2048, _gradientSize = 2048;
        private static WardrobeGenerators.GradientDirection _direction;

        private WardrobeGarment _draft;
        private SerializedObject _so;
        private string _saved, _text, _error;
        private int _dirtyCount = -1, _revision;
        private WardrobeFitter.Report _fit;
        private string _newShape, _usageSearch = "";
        private bool _mapsMore, _patternsOpen;
        private int _usageRevision = -1;
        private List<(string path, string kind, string type, JsonObject json)> _usage;

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
            _so?.ApplyModifiedProperties();
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

        // A step's button: runs after the GUI has drawn, on the copy, and saves the garment after (so what it made is kept).
        private void Act(string label, Action<WardrobeGarment> action) {
            Later(label, () => {
                if (_draft == null) return;
                _so.ApplyModifiedProperties();
                try {
                    action(_draft);
                } finally {
                    if (_draft != null) Save();
                    WardrobeChecks.Forget();
                }
            });
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
            var avatar = WardrobeAvatars.Get(g.avatarId);
            var type = WardrobeMaterialTypes.For(g, g.defaultMaterial);

            bool details = !string.IsNullOrWhiteSpace(g.id) && !string.IsNullOrWhiteSpace(g.displayName);
            bool source = avatar != null && g.source != null && WardrobeFitter.Problem(g) == null;
            bool fitted = g.mesh != null && AssetDatabase.GetAssetPath(g.mesh) == WardrobeFitter.FitPath(g);
            bool fitDone = WardrobeFitter.UpToDate(g);
            bool prefabDone = g.prefab != null && WardrobePrefabs.Mismatch(g) == null;
            bool shipped = type != null && File.Exists(WardrobeMaterialTypes.ShippedPath(g, type));
            bool shipDone = shipped && WardrobeMaterialTypes.ShippedUpToDate(g, type);
            bool recolour = g.idMap != null && g.regions.Count > 0;

            DrawMakeRest(g, source, fitDone, type, shipDone);

            Step(1, "Garment", details ? StepState.Done : StepState.Attention, details ? g.id : "name and id");
            DrawDetails(g);

            Step(2, "Avatar and cloth export", source ? StepState.Done : StepState.Attention,
                avatar != null ? avatar.DisplayName : "pick the avatar");
            DrawAvatar(g);
            DrawSource(g, avatar);

            Step(3, "Maps", g.baseColor != null ? StepState.Done : StepState.Attention, g.baseColor != null ? null : "base colour missing");
            DrawTextures(g);

            Step(4, "Fit to avatar", fitDone ? StepState.Done : source ? StepState.Attention : StepState.Todo,
                fitDone ? "up to date" : fitted ? "out of date" : "not fitted");
            DrawFit(g, avatar);

            Step(5, "Prefab", prefabDone ? StepState.Done : fitDone || g.mesh != null ? StepState.Attention : StepState.Todo,
                g.prefab != null ? g.prefab.name : "not made");
            DrawPrefab(g);

            Step(6, "Material", shipDone ? StepState.Done : type != null && g.baseColor != null ? StepState.Attention : StepState.Todo,
                type != null ? type.DisplayName : "no material type");
            DrawMaterial(g);
            DrawShipped(g, type);

            Step(7, "Recolouring", recolour ? StepState.Done : g.mesh != null ? StepState.Attention : StepState.Todo,
                recolour ? $"{g.regions.Count} regions" : "no ID map");
            DrawMaps(g);
            DrawRegions(g);

            Step(8, "Material types, patterns and presets for it", StepState.Done);
            DrawUsage(g);
        }

        // One button for every step that's left (when they can run): fit, prefab, material, recolour maps.
        private void DrawMakeRest(WardrobeGarment g, bool source, bool fitDone, WardrobeMaterialType type, bool shipDone) {
            var left = new List<string>();
            if (source && !fitDone) left.Add("fit");
            if (g.prefab == null && (source || g.mesh != null) && WardrobePrefabs.Problem(g) == null) left.Add("prefab");
            if (type != null && g.baseColor != null && !shipDone) left.Add("material");
            if (g.idMap == null && (g.mesh != null || source)) left.Add("ID map");
            if (g.gradientMap == null && (g.mesh != null || source)) left.Add("gradient map");
            if (left.Count < 2) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                EditorGUILayout.LabelField($"Left to make: {string.Join(", ", left)}.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(new GUIContent("Make Them", "Runs steps 4 to 7 in order and saves the garment"), GUILayout.Width(100), GUILayout.Height(22)))
                    Act($"Making {Title(g)}…", d => {
                        if (WardrobeFitter.Problem(d) == null && !WardrobeFitter.UpToDate(d)) _fit = WardrobeFitter.Fit(d);
                        if (d.prefab == null && WardrobePrefabs.Problem(d) == null) WardrobePrefabs.Make(d);
                        var t = WardrobeMaterialTypes.For(d, d.defaultMaterial);
                        if (t != null && d.baseColor != null && !WardrobeMaterialTypes.ShippedUpToDate(d, t)) WardrobeMaterialTypes.Ship(d, t);
                        if (d.mesh != null && d.idMap == null) WardrobeGenerators.GenerateIdMap(d, _idSize, d.regions.Count == 0);
                        if (d.mesh != null && d.gradientMap == null) WardrobeGenerators.GenerateGradientMap(d, _gradientSize, _direction);
                    });
            }
        }

        private void Field(string name, string label = null) {
            var p = _so.FindProperty(name);
            if (label == null) EditorGUILayout.PropertyField(p);
            else EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip));
        }

        // A string field with a ▾ of suggestions (and the values other garments use).
        private void Suggested(string name, string label, IEnumerable<string> common, Func<WardrobeGarment, string> used) {
            var p = _so.FindProperty(name);
            var options = common.Concat(WardrobeGarments.All.Select(used)).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(s => (s, s));
            EditorGUI.BeginChangeCheck();
            string value = Suggest(new GUIContent(label, p.tooltip), p.stringValue, options, v => {
                p.stringValue = v;
                _so.ApplyModifiedProperties();
            });
            if (EditorGUI.EndChangeCheck()) p.stringValue = value;
        }

        // ---------------------------------------------------------------- 1. details

        private void DrawDetails(WardrobeGarment g) {
            Field("displayName", "Name");
            Field("id", "Id");
            if (string.IsNullOrWhiteSpace(g.id)) {
                EditorGUILayout.HelpBox("Give the garment an id so patterns and presets made for it find it. Fits of the same garment for other " +
                                        "avatars use the same id.", MessageType.Warning);
                string suggested = WardrobeConfigs.SuggestId(g.author, string.IsNullOrWhiteSpace(g.displayName) ? Path.GetFileNameWithoutExtension(path) : g.displayName);
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                    if (GUILayout.Button($"Use {suggested}", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                        _so.FindProperty("id").stringValue = suggested;
                        GUI.FocusControl(null);
                    }
                }
            } else if (WardrobeGarments.All.Any(o => WardrobeConfigs.SameId(o.id, g.id) && WardrobeConfigs.SameId(o.avatarId, g.avatarId) &&
                                                       WardrobeGarments.PathOf(o) != path))
                EditorGUILayout.HelpBox($"Another .rgarm is {g.id} on the same avatar; only one of them is used.", MessageType.Warning);
            Field("version");
            Field("author");
            Suggested("category", "Category", Categories, o => o.category);
            Suggested("slot", "Slot", Slots, o => o.slot);
            Field("thumbnail");
            _so.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------- 2. avatar and source

        private void DrawAvatar(WardrobeGarment g) {
            var avatars = WardrobeAvatars.All;
            var options = new List<string> { "Pick the avatar base" };
            options.AddRange(avatars.Select(a => $"{a.DisplayName}  ({a.id})".Replace("/", "∕")));
            int index = string.IsNullOrWhiteSpace(g.avatarId) ? 0 : avatars.FindIndex(a => WardrobeConfigs.SameId(a.id, g.avatarId)) + 1;
            bool unknown = index == 0 && !string.IsNullOrWhiteSpace(g.avatarId);
            if (unknown) options.Add(g.avatarId + " (no .ravt here)");
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent("Made For", "The avatar base (.ravt) this fit is for. Fits of the garment for other " +
                "avatars are their own .rgarm files with the same id"), unknown ? options.Count - 1 : index, options.ToArray());
            if (EditorGUI.EndChangeCheck() && !(unknown && picked == options.Count - 1)) {
                _so.FindProperty("avatarId").stringValue = picked == 0 ? "" : avatars[picked - 1].id;
                _so.ApplyModifiedProperties();
            }
            if (avatars.Count == 0)
                EditorGUILayout.HelpBox("There's no avatar base (.ravt) in the project yet. Make one in the Setup tab's Avatar bases (it reads the avatar's " +
                                        "shape keys from its model).", MessageType.Warning);
            if (unknown)
                EditorGUILayout.HelpBox($"No .ravt in the project has the id {g.avatarId}, so Ros' Wardrobe can't tell which avatars it fits. " +
                                        "Make one in the Setup tab's Avatar bases, and ship it with the garment.", MessageType.Warning);
            if (!string.IsNullOrWhiteSpace(g.avatarId)) Field("avatarVersion", "Avatar Version");
        }

        private void DrawSource(WardrobeGarment g, WardrobeAvatar avatar) {
            Note("The garment as your cloth tool exports it (e.g. Marvelous Designer's FBX), rigged to the avatar's skeleton. Drop the export and " +
                 "Find Maps fills step 3 from the textures next to it.");
            EditorGUI.BeginChangeCheck();
            Field("source", "Source Model");
            bool changed = EditorGUI.EndChangeCheck();
            _so.ApplyModifiedProperties();
            if (changed && g.source != null && g.baseColor == null) FindMaps(g, false);
            if (g.source != null) {
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                    if (GUILayout.Button(new GUIContent("Find Maps", "Fill the maps from the textures next to the model (diffuse, normal, roughness, " +
                            "metalness, ambient occlusion, opacity…), keeping the ones already set"), EditorStyles.miniButton, GUILayout.Width(80))) {
                        FindMaps(g, true);
                        GUIUtility.ExitGUI();
                    }
                }
                if (WardrobePrefabs.SourceSkin(g.source) == null)
                    EditorGUILayout.HelpBox($"{g.source.name} has no skinned mesh: export it rigged to the avatar's skeleton.", MessageType.Error);
            }
            DrawHides(g, avatar);
        }

        // Shape keys of the body the garment covers: suggestions the window's Avatar section offers when it's worn.
        private void DrawHides(WardrobeGarment g, WardrobeAvatar avatar) {
            var body = WardrobeFitter.BodyShapes(g);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel(new GUIContent("Hides on Body", _so.FindProperty("bodyHides").tooltip));
                if (g.bodyHides.Count == 0) GUILayout.Label("nothing", EditorStyles.miniLabel);
                for (int i = 0; i < g.bodyHides.Count; i++) {
                    string key = g.bodyHides[i];
                    bool missing = body.Count > 0 && !body.Contains(key);
                    if (GUILayout.Button(new GUIContent($"{key}{(missing ? " (not on the body)" : "")}  ✕", "Remove"), EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                        Changing(g, "Remove Body Hide");
                        g.bodyHides.RemoveAt(i);
                        Changed(g);
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(body.Count == 0))
                    if (GUILayout.Button(new GUIContent("Add", body.Count == 0 ? "Pick the avatar (with its model in the project) first" : "A body shape key that hides skin under it"),
                            EditorStyles.miniButton, GUILayout.Width(44))) {
                        var menu = new GenericMenu();
                        // the keys that sound like clothing first
                        var likely = body.Where(k => Hides(k, g)).ToList();
                        foreach (string k in likely) AddHide(menu, g, k, "");
                        if (likely.Count > 0) menu.AddSeparator("");
                        foreach (string k in body.Where(k => !likely.Contains(k)))
                            AddHide(menu, g, k, body.Count > 40 ? $"All/{char.ToUpperInvariant(k.FirstOrDefault())}/" : "All/");
                        menu.ShowAsContext();
                        GUIUtility.ExitGUI();
                    }
            }
            if (g.bodyHides.Count > 0)
                Note("Only suggestions: when the garment is put on, the window's Avatar section offers to turn these on. The body changes only when " +
                     "the buyer says so.");
        }

        private void AddHide(GenericMenu menu, WardrobeGarment g, string key, string folder) {
            bool on = g.bodyHides.Contains(key);
            menu.AddItem(new GUIContent(folder + key.Replace("/", "∕")), on, () => {
                Changing(g, "Body Hide");
                if (on) g.bodyHides.Remove(key);
                else g.bodyHides.Add(key);
                Changed(g);
                repaint?.Invoke();
            });
        }

        private static bool Hides(string key, WardrobeGarment g) {
            string k = key.ToLowerInvariant();
            var words = new[] { "hide", "shrink", "clip", "cloth", "shirt", "pant", "sock", "shoe", "top", "under", "mask" }
                .Concat((g.category + " " + g.slot + " " + g.displayName).ToLowerInvariant().Split(' ', '-', '_').Where(w => w.Length > 2));
            return words.Any(k.Contains);
        }

        // ---------------------------------------------------------------- finding maps

        private static readonly (string role, string[] words)[] MapWords = {
            ("baseColor", new[] { "diffuse", "basecolor", "basecolour", "albedo", "color", "colour" }),
            ("normalMap", new[] { "normal", "normalgl", "normaldx", "nrm", "nor", "norm" }),
            ("metalness", new[] { "metalness", "metallic", "metal" }),
            ("roughness", new[] { "roughness", "rough" }),
            ("smoothness", new[] { "smoothness", "gloss", "glossiness" }),
            ("occlusion", new[] { "ambientocclusion", "occlusion", "ao" }),
            ("opacity", new[] { "opacity", "alpha", "transparency", "transmission" }),
            ("sheen", new[] { "sheen" }),
        };

        private static string[] Words(string name) =>
            name.ToLowerInvariant().Replace(" ", "_").Split('_', '-', '.').Where(w => w.Length > 0).ToArray();

        // Fills the empty maps from textures next to the source model (and the .rgarm), the way cloth tools name them.
        private void FindMaps(WardrobeGarment g, bool tell) {
            var folders = new[] { Path.GetDirectoryName(AssetDatabase.GetAssetPath(g.source)), Path.GetDirectoryName(path) }
                .Where(f => !string.IsNullOrEmpty(f)).Select(f => f.Replace('\\', '/')).Distinct().ToArray();
            var mine = new HashSet<Texture2D> { g.idMap, g.gradientMap, g.thumbnail };
            var textures = AssetDatabase.FindAssets("t:Texture2D", folders).Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .Select(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p)).Where(t => t != null && !mine.Contains(t))
                .Where(t => !Words(t.name).Any(w => w == "id" || w == "gradient" || w == "thumbnail" || w == "preview")).ToList();
            string sourceName = g.source.name.ToLowerInvariant();
            int Shared(Texture2D t) => t.name.ToLowerInvariant().Zip(sourceName, (a, b) => a == b).TakeWhile(x => x).Count();

            // named for the role, and for no other
            Texture2D Best(string role) {
                var words = MapWords.First(m => m.role == role).words;
                var others = MapWords.Where(m => m.role != role).SelectMany(m => m.words).ToList();
                return textures.Where(t => Words(t.name).Any(words.Contains) && !Words(t.name).Any(others.Contains))
                    .OrderByDescending(Shared).ThenBy(t => t.name.Length).FirstOrDefault();
            }

            var found = new List<string>();
            Changing(g, "Find Maps");
            if (g.baseColor == null && Best("baseColor") is Texture2D color) {
                g.baseColor = color;
                found.Add($"base colour {color.name}");
            }
            if (g.normalMap == null && Best("normalMap") is Texture2D normal) {
                g.normalMap = normal;
                found.Add($"normal {normal.name}");
                Import(normal, ti => {
                    if (ti.textureType == TextureImporterType.NormalMap) return false;
                    ti.textureType = TextureImporterType.NormalMap;
                    return true;
                });
            }
            void Channel(MapChannel map, string role, string label, bool invert = false) {
                if (map.texture != null) return;
                var t = Best(role);
                if (t == null) return;
                map.texture = t;
                map.channel = TextureChannel.R;
                map.invert = invert;
                found.Add($"{label} {t.name}{(invert ? " (inverted)" : "")}");
                Import(t, ti => {
                    if (!ti.sRGBTexture) return false;
                    ti.sRGBTexture = false;
                    return true;
                });
            }
            Channel(g.metalness, "metalness", "metalness");
            Channel(g.roughness, "roughness", "roughness");
            if (g.roughness.texture == null) Channel(g.roughness, "smoothness", "roughness from smoothness", true);
            Channel(g.occlusion, "occlusion", "occlusion");
            Channel(g.opacity, "opacity", "opacity");
            Channel(g.sheen, "sheen", "sheen");
            Changed(g);
            if (tell)
                EditorUtility.DisplayDialog("Ros' Wardrobe", found.Count == 0
                    ? $"Found no new maps next to {g.source.name}. Set them in step 3 by hand."
                    : "Found:\n• " + string.Join("\n• ", found) + "\n\nCheck them in step 3.", "OK");
        }

        private static void Import(Texture2D texture, Func<TextureImporter, bool> change) {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter ti && change(ti)) ti.SaveAndReimport();
        }

        // ---------------------------------------------------------------- 3. maps

        private void DrawTextures(WardrobeGarment g) {
            Note("Keep the maps as your tools export them; Make Material packs them the way each material needs. Pick the channel a map is in " +
                 "(ORM textures hold occlusion, roughness and metalness in R, G and B), and tick Inverted for a smoothness map.");
            Field("baseColor", "Base Colour");
            Field("normalMap", "Normal");
            _so.ApplyModifiedProperties();
            if (g.normalMap != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(g.normalMap)) is TextureImporter ti) {
                EditorGUI.indentLevel++;
                if (ti.textureType != TextureImporterType.NormalMap) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.HelpBox("It isn't imported as a normal map.", MessageType.Warning);
                        if (GUILayout.Button("Fix", GUILayout.Width(44), GUILayout.Height(38)))
                            Import(g.normalMap, i => {
                                i.textureType = TextureImporterType.NormalMap;
                                return true;
                            });
                    }
                } else {
                    EditorGUI.BeginChangeCheck();
                    bool flip = EditorGUILayout.Toggle(new GUIContent("DirectX (Green Down)", "Tick when the normal map is DirectX style (its green " +
                        "channel points down, as Substance's and Unreal's do): Unity flips it on import"), ti.flipGreenChannel);
                    if (EditorGUI.EndChangeCheck()) {
                        ti.flipGreenChannel = flip;
                        ti.SaveAndReimport();
                    }
                }
                EditorGUI.indentLevel--;
            }
            MapChannel("roughness", "Roughness", "Tick Inverted for a smoothness (gloss) map");
            MapChannel("metalness", "Metalness");
            MapChannel("occlusion", "Occlusion");
            MapChannel("opacity", "Opacity", "Without a texture: the base colour's alpha");
            _mapsMore = EditorGUILayout.Foldout(_mapsMore, "More", true);
            if (_mapsMore) {
                EditorGUI.indentLevel++;
                MapChannel("sheen", "Sheen", "How much rim light / sheen the toon material gives the cloth");
                EditorGUI.indentLevel--;
            }
            _so.ApplyModifiedProperties();
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
                    channel.intValue = EditorGUILayout.IntPopup(new GUIContent("Channel", "Which channel of the texture holds it"), Mathf.Clamp(channel.intValue, 0, 3),
                        Channels, ChannelValues);
                    var invert = p.FindPropertyRelative("invert");
                    invert.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Inverted", invert.tooltip), invert.boolValue, GUILayout.Width(80));
                }
            } else {
                EditorGUILayout.PropertyField(p.FindPropertyRelative("value"), new GUIContent("Value", "Everywhere, as there's no texture"));
            }
            EditorGUI.indentLevel--;
        }

        // ---------------------------------------------------------------- 4. fit

        private void DrawFit(WardrobeGarment g, WardrobeAvatar avatar) {
            Note($"Fit makes '{WardrobeAssets.FileName(WardrobeAssets.BaseName(g))}.asset' next to the .rgarm from the source model: the mesh the " +
                 "prefab wears, with the body's shape keys so it follows the body. After a new export, overwrite the file and fit again.");
            string problem = WardrobeFitter.Problem(g);
            if (problem != null) {
                EditorGUILayout.HelpBox(problem, MessageType.Info);
                return;
            }
            DrawShapes(g, avatar);
            bool fitted = g.mesh != null && AssetDatabase.GetAssetPath(g.mesh) == WardrobeFitter.FitPath(g);
            bool upToDate = WardrobeFitter.UpToDate(g);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(upToDate ? "Up to date." : fitted ? "Out of date: the source model or the avatar base changed." : "Not fitted yet.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(new GUIContent(fitted ? "Fit Again" : "Fit to Avatar", "Fits it and saves the garment"), GUILayout.Width(100)))
                    Act($"Fitting {Title(g)} to {avatar.DisplayName}…", d => _fit = WardrobeFitter.Fit(d));
            }
            if (_fit == null) return;
            if (_fit.added.Count > 0) Note("Copied: " + string.Join(", ", _fit.added.Select(a => $"{a.shape} ({a.mm:0.#} mm)")));
            if (_fit.skipped.Count > 0) Note("Left out: " + string.Join(", ", _fit.skipped.Select(s => $"{s.shape} ({s.why})")));
            foreach (string note in _fit.notes) Note(note);
        }

        // Body shapes: each body shape key the garment follows, and the cloth tool's export made with the body in it. The
        // export's movement from the Source Model is the garment's shape key at 100: a linear delta, so several keys on at
        // once add up. Without one the garment follows the body surface under it. Either way, Ros' Wardrobe's clearance
        // pass moves out what still clips on the avatar it's worn on.
        private void DrawShapes(WardrobeGarment g, WardrobeAvatar avatar) {
            var shapes = WardrobeFitter.Shapes(g);
            var body = WardrobeFitter.BodyShapes(g);
            Note($"The garment gets a shape key for each of {avatar.DisplayName}'s body shape keys below (the avatar base's Follow list), so it " +
                 "changes shape with the body. For a better fit, give a key the garment as your cloth tool exports it with that key on the body " +
                 "at 100 (e.g. Marvelous Designer's Avatar → Blend Shape): its movement becomes the key. Without one it follows the body surface.");
            EditorGUI.indentLevel++;
            if (shapes.Count == 0) Note("None: the avatar base names no body shape keys to follow (its step 3).");
            foreach (string shape in shapes) {
                var own = g.sims?.Where(s => s != null && s.shapes.Count == 1 && s.shapes[0].shape == shape).OrderBy(s => s.shapes[0].weight).ToList()
                          ?? new List<WardrobeSim>();
                var main = own.FirstOrDefault(s => Mathf.Approximately(s.shapes[0].weight, 100)) ?? own.LastOrDefault();
                bool onBody = body.Count == 0 || body.Contains(shape);
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    var picked = EditorGUILayout.ObjectField(new GUIContent(shape, "The cloth tool's export of the garment with this body shape key at " +
                        $"{(main != null ? main.shapes[0].weight : 100):0.##}. Empty: the garment follows the body."), main?.source, typeof(GameObject), false) as GameObject;
                    if (EditorGUI.EndChangeCheck()) SetExport(g, shape, main, picked);
                    EditorGUILayout.LabelField(!onBody ? "not on the body" : main?.source != null ? "from the export" : "follows the body",
                        EditorStyles.miniLabel, GUILayout.Width(92));
                }
                if (main?.source != null && main.source.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
                    EditorGUILayout.HelpBox($"{main.source.name} has no skinned mesh: export it rigged to the avatar's skeleton, as the Source Model.", MessageType.Warning);
                foreach (var other in own.Where(s => s != main && s.source != null))
                    Note($"In-between at {other.shapes[0].weight:0.##}: {other.source.name}");
            }
            // correctives: exports with several body keys on (what the keys on their own miss of it)
            foreach (var combo in g.sims?.Where(s => s != null && s.shapes.Count > 1 && s.source != null) ?? Enumerable.Empty<WardrobeSim>())
                Note($"Corrective {WardrobeSim.ComboName(combo.shapes)}: {combo.source.name}");

            // another body shape key, with its export
            var more = body.Where(s => !shapes.Contains(s) && !g.bodyHides.Contains(s)).ToList();
            if (more.Count > 0)
                using (new EditorGUILayout.HorizontalScope()) {
                    int index = Mathf.Max(0, more.IndexOf(_newShape));
                    index = EditorGUILayout.Popup(new GUIContent("Another Key", "Another body shape key to follow, with the cloth tool's export " +
                        "made with it on the body at 100"), index, more.Select(s => s.Replace("/", "∕")).ToArray());
                    _newShape = more[index];
                    var picked = EditorGUILayout.ObjectField(null, typeof(GameObject), false, GUILayout.Width(150)) as GameObject;
                    if (picked != null) SetExport(g, _newShape, null, picked);
                }
            EditorGUI.indentLevel--;
        }

        private void SetExport(WardrobeGarment g, string shape, WardrobeSim sim, GameObject export) {
            Changing(g, "Body Shape Export");
            g.sims ??= new List<WardrobeSim>();
            if (export == null) {
                if (sim != null) g.sims.Remove(sim);
            } else if (sim == null) {
                g.sims.Add(new WardrobeSim { source = export, shapes = { new WardrobeShapeWeight { shape = shape, weight = 100 } } });
            } else {
                sim.source = export;
            }
            Changed(g);
        }

        // ---------------------------------------------------------------- 5. prefab

        private void DrawPrefab(WardrobeGarment g) {
            Note(WardrobePrefabs.HasVrcFury
                ? "What buyers drop on their avatar: the fitted mesh in its material, with VRCFury's Armature Link (so it moves with the avatar), its " +
                  "shape keys linked to the body's, and a menu toggle."
                : "What buyers drop on their avatar: the fitted mesh in its material. VRCFury isn't in the project, so it gets no Armature Link or " +
                  "menu toggle; add VRCFury to make one buyers can just drop on.");
            var menu = _so.FindProperty("menuPath");
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(new GUIContent("Menu Path", menu.tooltip), menu.stringValue);
            if (EditorGUI.EndChangeCheck()) menu.stringValue = value;
            if (string.IsNullOrWhiteSpace(menu.stringValue)) EditorGUILayout.LabelField(" ", $"{WardrobePrefabs.MenuPath(g)}", EditorStyles.miniLabel);
            Field("prefab", "Prefab");
            _so.ApplyModifiedProperties();
            string problem = WardrobePrefabs.Problem(g);
            string mismatch = g.prefab != null ? WardrobePrefabs.Mismatch(g) : null;
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(problem ?? (g.prefab == null ? "Not made yet." : mismatch ?? "Made."), EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(problem != null))
                    if (GUILayout.Button(new GUIContent(g.prefab == null ? "Make Prefab" : "Make Again", "Makes it from the source model and the fit, and saves the garment"),
                            GUILayout.Width(100))) {
                        if (g.prefab == null || EditorUtility.DisplayDialog("Ros' Wardrobe", $"Make {g.prefab.name} again? What was added to it by hand is lost.",
                                "Make Again", "Cancel"))
                            Act($"Making {Title(g)}'s prefab…", d => WardrobePrefabs.Make(d));
                        GUIUtility.ExitGUI();
                    }
            }
        }

        // ---------------------------------------------------------------- 6. material

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
            int picked = EditorGUILayout.Popup(new GUIContent("Starts In", "The material type (.rmat) the garment starts and ships in"), index, options.ToArray());
            if (EditorGUI.EndChangeCheck() && picked <= types.Count) {
                _so.FindProperty("defaultMaterial").stringValue = picked == 0 ? "" : types[picked - 1].id;
                _so.ApplyModifiedProperties();
            }
            if (types.Count == 0)
                EditorGUILayout.HelpBox("No material type (.rmat) in the project is for this garment, so it can't be recoloured. Ros' Wardrobe ships PBR; " +
                                        "tick the garment in step 8, or make a material type in the Setup tab.", MessageType.Warning);
            foreach (var t in types.Where(t => t.Problem != null)) EditorGUILayout.HelpBox(t.Problem, MessageType.Warning);
            if (types.Count > 0)
                Note($"Buyers can pick: {string.Join(", ", types.Where(t => t.Problem == null).Select(t => t.DisplayName))}.");
        }

        private void DrawShipped(WardrobeGarment g, WardrobeMaterialType type) {
            if (type == null) return;
            Note($"The prefab ships in it: Make Material writes '{Path.GetFileName(WardrobeMaterialTypes.ShippedPath(g, type))}' next to the .rgarm " +
                 "from the maps, packed the way the material type says, and puts it on the prefab." +
                 (WardrobeMaterialTypes.Variant(type, true) ? $" It's a variant of {type.template.name}, so changes to that material show on it." : ""));
            var size = _so.FindProperty("mapSize");
            size.intValue = EditorGUILayout.IntPopup(new GUIContent("Packed Map Size", size.tooltip), size.intValue, MapSizeNames, MapSizes);
            _so.ApplyModifiedProperties();
            bool made = File.Exists(WardrobeMaterialTypes.ShippedPath(g, type));
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(g.baseColor == null ? "It needs the base colour map (step 3)." : !made ? "Not made yet."
                    : WardrobeMaterialTypes.ShippedUpToDate(g, type) ? "Up to date." : "Out of date: the maps, the size or the material type changed.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(g.baseColor == null))
                    if (GUILayout.Button(new GUIContent(made ? "Make Again" : "Make Material", "Makes it, puts it on the prefab, and saves the garment"), GUILayout.Width(100)))
                        Act($"Making {Title(g)}'s material…", d => WardrobeMaterialTypes.Ship(d, type));
            }
        }

        // ---------------------------------------------------------------- 7. recolouring

        private void DrawMaps(WardrobeGarment g) {
            Note("Recolouring needs an ID map (one flat colour per part of the garment, made from its UV islands) and a gradient map (where each " +
                 "point is along a gradient). Make them here, or paint your own (ID map: flat colours, black = untouched; gradient: 16-bit grey).");
            bool missing = g.idMap == null || g.gradientMap == null;
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(g.mesh == null ? "Fit it first (step 4)." : missing ? "Not made yet." : "Made.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button(new GUIContent(missing ? "Make Maps" : "Make Again", "Makes the ID and gradient maps next to the .rgarm and saves the garment " +
                            "(the regions stay; new parts are in none)"), GUILayout.Width(100)))
                        Act("Making the recolouring maps…", d => {
                            WardrobeGenerators.GenerateIdMap(d, _idSize, d.regions.Count == 0);
                            WardrobeGenerators.GenerateGradientMap(d, _gradientSize, _direction);
                        });
            }
            open.TryGetValue("maps", out bool more);
            open["maps"] = more = EditorGUILayout.Foldout(more, "Maps and sizes", true);
            if (!more) return;
            EditorGUI.indentLevel++;
            Field("idMap", "ID Map");
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel(" ");
                _idSize = EditorGUILayout.IntPopup(_idSize, SizeNames, Sizes, GUILayout.Width(70));
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Make from UV Islands", EditorStyles.miniButton))
                        Act("Making the ID map…", d => WardrobeGenerators.GenerateIdMap(d, _idSize, d.regions.Count == 0));
            }
            Field("gradientMap", "Gradient Map");
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel(" ");
                _gradientSize = EditorGUILayout.IntPopup(_gradientSize, SizeNames, Sizes, GUILayout.Width(70));
                _direction = (WardrobeGenerators.GradientDirection)EditorGUILayout.EnumPopup(_direction);
                using (new EditorGUI.DisabledScope(g.mesh == null))
                    if (GUILayout.Button("Make", EditorStyles.miniButton, GUILayout.Width(50)))
                        Act("Making the gradient map…", d => WardrobeGenerators.GenerateGradientMap(d, _gradientSize, _direction));
            }
            Field("metresPerUv", "Metres per UV");
            EditorGUI.indentLevel--;
            _so.ApplyModifiedProperties();
        }

        private readonly Dictionary<object, bool> open = new Dictionary<object, bool>();

        private void DrawRegions(WardrobeGarment g) {
            if (g.idMap == null) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            Note("A region is recoloured as one: name it for buyers (Body, Sleeves, Collar…) and put the ID map's parts in it below.");

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
                    if (GUILayout.Button(EditorGUIUtility.IconContent("icon dropdown"), EditorStyles.label, GUILayout.Width(18))) {
                        var menu = new GenericMenu();
                        var picked = region;
                        foreach (string n in RegionNames.Where(n => g.regions.All(x => x.name != n)))
                            menu.AddItem(new GUIContent(n), false, () => Rename(g, picked, n));
                        menu.ShowAsContext();
                    }
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
                using (new EditorGUI.DisabledScope(parts.Count == 0))
                    if (GUILayout.Button(new GUIContent("Copy From Another Fit…", "The regions of the same garment fitted to another avatar: each part " +
                            "goes in the region most of it is in there (the same pattern pieces have the same UVs)")))
                        CopyRegionsMenu(g);
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

        // The other fits with regions to copy: the same garment's first, then any other garment's.
        private void CopyRegionsMenu(WardrobeGarment g) {
            string self = WardrobeGarments.PathOf(g);
            var others = WardrobeGarments.All.Where(o => o != g && WardrobeGarments.PathOf(o) != self && o.idMap != null && o.regions.Count > 0).ToList();
            bool Same(WardrobeGarment o) => !string.IsNullOrWhiteSpace(g.id) && WardrobeConfigs.SameId(o.id, g.id);
            var menu = new GenericMenu();
            if (others.Count == 0) menu.AddDisabledItem(new GUIContent("No other garment in the project has regions"));
            foreach (var o in others.OrderBy(o => Same(o) ? 0 : 1).ThenBy(Title)) {
                var picked = o;
                string label = $"{Title(o)} on {AvatarName(o.avatarId)}".Replace("/", "∕");
                if (!Same(o)) label = "Other garments/" + label;
                menu.AddItem(new GUIContent(label), false, () => CopyRegions(g, picked));
            }
            menu.ShowAsContext();
        }

        private void CopyRegions(WardrobeGarment g, WardrobeGarment from) {
            if (g.regions.Count > 0 && !EditorUtility.DisplayDialog("Ros' Wardrobe",
                    $"Replace {Title(g)}'s regions with {Title(from)}'s (on {AvatarName(from.avatarId)})?", "Replace", "Cancel"))
                return;
            WardrobeGenerators.RegionCopy copy;
            Changing(g, "Copy Regions");
            try {
                copy = WardrobeGenerators.CopyRegions(g, from);
            } catch (Exception e) {
                EditorUtility.DisplayDialog("Ros' Wardrobe", e.Message, "OK");
                return;
            }
            Changed(g);
            repaint?.Invoke();
            string message = $"Copied {from.regions.Count} regions from {Title(from)} on {AvatarName(from.avatarId)}: {copy.placed} parts placed.";
            if (copy.unplaced > 0) message += $"\n\n{copy.unplaced} parts aren't in the other fit's regions, so they're in none: pick a region for them under Parts.";
            if (copy.empty.Count > 0) message += $"\n\nNo part landed in {string.Join(", ", copy.empty)}. If this fit has that piece, its UVs differ from the other fit's " +
                                                 "(or the other fit's ID map was painted): assign it by hand, or remove the region.";
            message += "\n\nSave to keep them.";
            EditorUtility.DisplayDialog("Ros' Wardrobe", message, "OK");
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

        // ---------------------------------------------------------------- 8. what's made for it

        // The material types, patterns and presets in the project, read again when one changes.
        private List<(string path, string kind, string type, JsonObject json)> Usage() {
            if (_usage != null && _usageRevision == WardrobeConfigs.Revision) return _usage;
            _usageRevision = WardrobeConfigs.Revision;
            _usage = new List<(string, string, string, JsonObject)>();
            foreach (var (ext, kind) in new[] { (WardrobeConfigs.Material, "material"), (WardrobeConfigs.Pattern, "pattern"), (WardrobeConfigs.Preset, "preset") })
            foreach (string p in WardrobeConfigs.Paths(ext))
                try {
                    var j = WardrobeJson.Parse(File.ReadAllText(p));
                    _usage.Add((p, kind, j.String("type", "simple").Trim().ToLowerInvariant(), j));
                } catch {
                    // the checks tell about files that can't be read
                }
            return _usage;
        }

        private void DrawUsage(WardrobeGarment g) {
            if (string.IsNullOrWhiteSpace(g.id)) {
                Note("Give the garment an id first (step 1).");
                return;
            }
            Note("Tick what's for this garment: the file then lists its id. Files that list no garments are for every garment. Presets come from " +
                 "the recolour editor's Save as Preset.");
            var files = Usage();
            string id = g.id.Trim();
            bool Listed(JsonObject j) => j.Objects("garments").Any(e => WardrobeConfigs.SameId(e.String("garment"), id));
            bool Every(JsonObject j) => !j.Objects("garments").Any();

            // material types
            var materials = files.Where(f => f.kind == "material").ToList();
            EditorGUILayout.LabelField("Material types", EditorStyles.miniBoldLabel);
            if (materials.Count == 0) Note("None in the project.");
            foreach (var f in materials) UsageRow(g, f.path, f.json, Name(f.json, f.path), Listed(f.json), Every(f.json), null);

            // patterns
            var patterns = files.Where(f => f.kind == "pattern").ToList();
            int forIt = patterns.Count(f => Listed(f.json) || Every(f.json));
            _patternsOpen = EditorGUILayout.Foldout(_patternsOpen, $"Patterns ({forIt} of {patterns.Count} for it)", true);
            if (_patternsOpen) {
                if (patterns.Count > 12) _usageSearch = EditorGUILayout.TextField(_usageSearch, EditorStyles.toolbarSearchField);
                foreach (var f in patterns.Where(f => _usageSearch.Length == 0 || Name(f.json, f.path).IndexOf(_usageSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                             .OrderBy(f => Name(f.json, f.path), StringComparer.OrdinalIgnoreCase)) {
                    string why = f.type == "simple" ? null : f.type == "hybrid" ? "needs a weights map for it" : "needs its picture for it";
                    UsageRow(g, f.path, f.json, Name(f.json, f.path), Listed(f.json), Every(f.json), why);
                }
            }

            // presets (made for one garment's regions)
            var presets = files.Where(f => f.kind == "preset" && (Listed(f.json) || Every(f.json))).ToList();
            EditorGUILayout.LabelField($"Presets ({presets.Count})", EditorStyles.miniBoldLabel);
            if (presets.Count == 0) Note("None yet: recolour it in the window and Save as Preset.");
            foreach (var f in presets)
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(Name(f.json, f.path), Every(f.json) ? "every garment" : "", EditorStyles.miniLabel);
                    if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(44))) Show(f.path);
                }
            if (g.partScales.Count == 0 && g.idMap != null)
                EditorGUILayout.HelpBox("Make the ID map again (step 7) so tiling patterns know each part's real size.", MessageType.Info);
        }

        private static string Name(JsonObject j, string file) {
            string name = j.String("name").Trim();
            return name.Length > 0 ? name : Path.GetFileNameWithoutExtension(file);
        }

        private void UsageRow(WardrobeGarment g, string file, JsonObject j, string name, bool listed, bool every, string needs) {
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(every)) {
                    EditorGUI.BeginChangeCheck();
                    bool on = EditorGUILayout.ToggleLeft(new GUIContent(name, file), listed || every, GUILayout.MinWidth(140));
                    if (EditorGUI.EndChangeCheck()) {
                        if (on) Tick(g, file);
                        else Untick(g, file, j);
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField(every ? "every garment" : listed && needs != null ? needs : "", EditorStyles.miniLabel);
                if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(44))) Show(file);
            }
        }

        private void Tick(WardrobeGarment g, string file) {
            string id = g.id.Trim(), version = WardrobeConfigs.Major(g.version);
            EditorApplication.delayCall += () => {
                WardrobeChecks.EditJson(file, j => {
                    var list = j.Array("garments") ?? new List<object>();
                    var entry = new JsonObject();
                    entry["garment"] = id;
                    if (version.Length > 0) entry["version"] = version;
                    list.Add(entry);
                    j["garments"] = list;
                });
                repaint?.Invoke();
            };
        }

        private void Untick(WardrobeGarment g, string file, JsonObject json) {
            string id = g.id.Trim();
            var rest = json.Objects("garments").Where(e => !WardrobeConfigs.SameId(e.String("garment"), id)).ToList();
            bool maps = json.Objects("garments").Any(e => WardrobeConfigs.SameId(e.String("garment"), id) &&
                                                          e.Keys.Any(k => k != "garment" && k != "version" && k != "avatar"));
            string message = rest.Count == 0 ? $"{Path.GetFileName(file)} then lists no garments, so it's for every garment. Untick anyway?"
                : maps ? $"{Path.GetFileName(file)} has maps for {id}; they're forgotten. Untick anyway?" : null;
            if (message != null && !EditorUtility.DisplayDialog("Ros' Wardrobe", message, "Untick", "Cancel")) return;
            EditorApplication.delayCall += () => {
                WardrobeChecks.EditJson(file, j => {
                    var list = (j.Array("garments") ?? new List<object>()).Where(e => !(e is JsonObject o && WardrobeConfigs.SameId(o.String("garment"), id))).ToList();
                    if (list.Count == 0) j.Remove("garments");
                    else j["garments"] = list;
                });
                repaint?.Invoke();
            };
        }
    }
}
