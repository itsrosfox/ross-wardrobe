using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Source = RosDen.Wardrobe.Editor.WardrobeMaterialType.Source;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Edits one .rmat: the material it links, and what goes into each of its texture properties. Every texture of the
    /// material's shader is a row: keep the material's own texture, the look's colour, the normal map, or a texture packed
    /// channel by channel (any map's channel, inverted or not, normals as X/Y/Z or octahedral), with a preview on a garment.
    /// </summary>
    internal class WardrobeMaterialForm : WardrobeJsonForm {
        private static readonly string[] Kinds = { "Keep the material's", "Look colour", "Normal map", "Packed channels" };
        private static readonly string[] ChannelNames = { "R", "G", "B", "A" };

        // Ready-made channel layouts: what common shaders want in one texture.
        private static readonly (string label, object[] channels)[] Layouts = {
            ("Unity Standard: R metal, G occlusion, A smoothness", new object[] { "metalness", "occlusion", 0.0, "smoothness" }),
            ("URP/HDRP mask map: R metal, G occlusion, B detail, A smoothness", new object[] { "metalness", "occlusion", 0.0, "smoothness" }),
            ("ORM: R occlusion, G roughness, B metal", new object[] { "occlusion", "roughness", "metalness", 1.0 }),
            ("Poiyomi metallics: R metal, G smoothness, B reflection, A specular", new object[] { "metalness", "smoothness", 1.0, 1.0 }),
            ("Roughness (greyscale)", new object[] { "roughness", "roughness", "roughness", 1.0 }),
            ("Smoothness (greyscale)", new object[] { "smoothness", "smoothness", "smoothness", 1.0 }),
            ("Metalness (greyscale)", new object[] { "metalness", "metalness", "metalness", 1.0 }),
            ("Ambient occlusion (greyscale)", new object[] { "occlusion", "occlusion", "occlusion", 1.0 }),
            ("Opacity mask (greyscale)", new object[] { "opacity", "opacity", "opacity", 1.0 }),
            ("Normal/XY in RG (Z worked out by the shader)", new object[] { "normal.x", "normal.y", 0.0, 1.0 }),
            ("Normal/DirectX: green flipped", new object[] { "normal.x", Inverted("normal.y"), "normal.z", 1.0 }),
            ("Normal/XY in AG (DXT5nm style)", new object[] { 1.0, "normal.y", 1.0, "normal.x" }),
            ("Normal/Octahedral in RG", new object[] { "normal.oct.u", "normal.oct.v", 0.0, 1.0 }),
            ("Normal/Hemi-octahedral in RG", new object[] { "normal.hemioct.u", "normal.hemioct.v", 0.0, 1.0 }),
            ("Normal/Octahedral in BA, roughness in R, occlusion in G (fur cards)", new object[] { "roughness", "occlusion", "normal.oct.u", "normal.oct.v" }),
        };

        // Shader keywords that go with a texture (on when the texture is set).
        private static readonly Dictionary<string, string> Keywords = new Dictionary<string, string> {
            { "_BumpMap", "_NORMALMAP" }, { "_NormalMap", "_NORMALMAP" }, { "_MetallicGlossMap", "_METALLICGLOSSMAP" },
            { "_SpecGlossMap", "_SPECGLOSSMAP" }, { "_MaskMap", "_MASKMAP" }, { "_OcclusionMap", "_OCCLUSIONMAP" },
        };

        private WardrobeMaterialType _type;
        private string _typeText, _search = "";
        private bool _all, _advanced;
        private string _previewGarment;
        private readonly Dictionary<string, (string key, RenderTexture rt)> _previews = new Dictionary<string, (string, RenderTexture)>();
        private RenderTexture _surface, _normal;
        private WardrobeGarment _inputsFor;
        private bool _previewQueued;

        public WardrobeMaterialForm(string path) : base(path) { }

        protected override ICollection<string> Always => new[] { "id", "name", "version", "author", "description", "textures" };

        public override void Dispose() {
            foreach (var p in _previews.Values) if (p.rt != null) Object.DestroyImmediate(p.rt);
            _previews.Clear();
            DropInputs();
        }

        private void DropInputs() {
            if (_surface != null) Object.DestroyImmediate(_surface);
            if (_normal != null) Object.DestroyImmediate(_normal);
            _surface = _normal = null;
            _inputsFor = null;
        }

        private static object Inverted(string source) {
            var o = new JsonObject();
            o["from"] = source;
            o["invert"] = true;
            return o;
        }

        protected override void DrawJson(JsonObject j) {
            // read like Ros' Wardrobe reads it, again only when it changed
            string text = WardrobeJson.Write(j);
            if (text != _typeText) {
                _typeText = text;
                _type = WardrobeMaterialTypes.Read(path, j);
            }
            var type = _type;

            Header("Material Type");
            Text(j, "name", "Name", "Shown where buyers pick it (Toon, PBR…)");
            Id(j, "Stable id garments and looks name it by (e.g. yourname.toon); don't change it once shipped",
                WardrobeMaterialTypes.All.Where(t => t.path != path).Select(t => t.id));
            Text(j, "version", "Version");
            Text(j, "author", "Author");
            TextArea(j, "description", "Description", "Shown as its tooltip");

            EditorGUILayout.Space();
            Header("Material");
            Note("Make a material in Unity the way garments in this type should look (any shader: Poiyomi, lilToon, Standard…) and link it " +
                 "here. Every garment's material is made from it, with the textures below filled in from the garment.");
            var template = Asset<Material>(j, "template", "Material", "The material garments in this type start as");
            if (template == null && type.shaderName.Length == 0)
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.HelpBox("Link a material (drag it in), or make one to start from.", MessageType.Info);
                    if (GUILayout.Button("New Material…", GUILayout.Width(110), GUILayout.Height(38))) NewMaterial(j);
                }
            if (template != null) {
                EditorGUI.BeginChangeCheck();
                bool follow = EditorGUILayout.Toggle(new GUIContent("Follow Changes", "Garments' materials are Material Variants of it: what you change on " +
                    "it shows on every garment in this type, worn ones too. Off: they're copies, made again when it changes."), j.Bool("follow", true));
                if (EditorGUI.EndChangeCheck()) Set(j, "follow", follow, follow);
                if (follow && !AssetDatabase.GetAssetPath(template).StartsWith("Assets/"))
                    Note("It's in a package, so the materials garments ship in are copies (buyers may not have the package); the ones made in projects follow it.");
                if (follow)
                    Note("Material Variants are new in Unity 2022. Do a test upload of a garment in this type before shipping, especially with shaders that " +
                         "lock materials (Poiyomi's Lock In): if one gives trouble, turn this off.");
            }
            var shader = type.Shader;

            EditorGUILayout.Space();
            Header("Textures");
            if (shader == null) {
                EditorGUILayout.HelpBox(type.shaderName.Length > 0 ? $"The shader '{type.shaderName}' isn't in this project." : "Link a material first.", MessageType.Info);
            } else {
                Note("What goes into each of the material's textures. Keep leaves the material's own; Look colour is the garment's colours with the " +
                     "buyer's look; Packed puts any channel of the garment's maps into each channel (R, G, B, A), as the shader wants them.");
                DrawTextures(j, type, shader);
            }

            EditorGUILayout.Space();
            _advanced = EditorGUILayout.Foldout(_advanced, "Advanced", true);
            if (_advanced) {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                var picked = (Shader)EditorGUILayout.ObjectField(new GUIContent("Shader Instead", "Use this shader instead of the material's (its " +
                    "materials are then copies, not variants)"), type.shader, typeof(Shader), false);
                if (EditorGUI.EndChangeCheck()) Set(j, "shader", picked != null ? picked.name : "", picked == null);
                if (type.shader == null && type.shaderName.Length > 0)
                    EditorGUILayout.HelpBox($"The shader '{type.shaderName}' isn't in this project.", MessageType.Warning);
                Note("Settings on top of the material's (set them on the material instead where you can):");
                Values(j, "floats", "Floats", shader, ShaderPropertyType.Float);
                Values(j, "colors", "Colours", shader, ShaderPropertyType.Color);
                Words(j, "keywords", "Keywords", "Shader keywords turned on");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            Header("Garments");
            Checklist(j, "Every garment can be in it.", "Only the garments ticked:");
        }

        // A new material next to the .rmat (the Standard shader, double sided), linked.
        private void NewMaterial(JsonObject j) {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string file = EditorUtility.SaveFilePanelInProject("New Material", Path.GetFileNameWithoutExtension(path), "mat",
                "The material garments in this type start as. Change its shader and settings as you like.", folder);
            if (string.IsNullOrEmpty(file)) GUIUtility.ExitGUI();
            var shader = Shader.Find("Ros' Wardrobe/Standard Double Sided") ?? Shader.Find("Standard");
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, file);
            Set(j, "template", WardrobeConfigs.Relative(path, material), false);
            Guess(j, shader, material, false);
            EditorGUIUtility.PingObject(material);
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- texture rows

        private void DrawTextures(JsonObject j, WardrobeMaterialType type, Shader shader) {
            var textures = j.Array("textures") ?? new List<object>();
            var entries = textures.OfType<JsonObject>().ToList();
            var slots = TextureSlots(shader).ToList();

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button(new GUIContent("Fill In from Names", "Picks Look colour, Normal map and packed layouts for the textures by their " +
                        "names (main texture, normal/bump map, metallic, roughness, occlusion…); textures you've set stay as they are"), GUILayout.Width(140))) {
                    Guess(j, shader, type.template, true);
                    GUIUtility.ExitGUI();
                }
                GUILayout.FlexibleSpace();
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(160));
            }
            PreviewGarment(type);

            var used = new HashSet<string>(entries.SelectMany(e => new[] { e.String("property").Trim() }.Concat((e.Array("alsoOn") ?? new List<object>()).OfType<string>().Select(s => s.Trim()))));
            var shown = slots.Where(s => _all || used.Contains(s.name) || Likely(s.name, s.description) ||
                                         type.template != null && type.template.HasProperty(s.name) && type.template.GetTexture(s.name) != null)
                .Where(s => _search.Length == 0 || s.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            s.description.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            foreach (var slot in shown) DrawSlot(j, textures, slot.name, slot.description);
            int hidden = slots.Count - shown.Count;
            if (hidden > 0 || _all)
                _all = EditorGUILayout.ToggleLeft(_all ? "Show all the shader's textures" : $"Show all the shader's textures ({hidden} more, empty on the material)", _all);

            // entries for textures the shader doesn't have
            foreach (var e in entries.Where(e => slots.All(s => s.name != e.String("property").Trim())).ToList()) {
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.HelpBox($"{shader.name} has no texture {e.String("property")}; nothing goes there.", MessageType.Warning);
                    if (GUILayout.Button("Remove", GUILayout.Width(70), GUILayout.Height(38))) {
                        textures.Remove(e);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        // The shader's 2D texture properties people set (not the hidden ones), with their inspector names.
        private static IEnumerable<(string name, string description)> TextureSlots(Shader shader) {
            for (int i = 0; i < shader.GetPropertyCount(); i++) {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture || shader.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) continue;
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
                yield return (shader.GetPropertyName(i), shader.GetPropertyDescription(i));
            }
        }

        private static bool Likely(string name, string description) => GuessChannels(name, description) != null;

        private void DrawSlot(JsonObject j, List<object> textures, string property, string description) {
            var entry = textures.OfType<JsonObject>().FirstOrDefault(e => e.String("property").Trim() == property);
            var host = entry == null ? textures.OfType<JsonObject>().FirstOrDefault(e => (e.Array("alsoOn") ?? new List<object>()).OfType<string>().Any(s => s.Trim() == property)) : null;
            object channels = entry?["channels"];
            int kind = entry == null ? 0 : channels is string s ? (s.Trim().ToLowerInvariant() == "normal" ? 2 : 1) : 3;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(new GUIContent(description, property), EditorStyles.boldLabel, GUILayout.MinWidth(80));
                    EditorGUILayout.LabelField(property, EditorStyles.miniLabel, GUILayout.MaxWidth(150));
                    if (host != null) {
                        EditorGUILayout.LabelField($"same as {host.String("property")}", EditorStyles.miniLabel, GUILayout.Width(150));
                        return;
                    }
                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(kind, Kinds, GUILayout.Width(150));
                    if (EditorGUI.EndChangeCheck() && picked != kind) {
                        SetKind(j, textures, entry, property, description, picked);
                        GUIUtility.ExitGUI();
                    }
                }
                if (entry == null) return;
                if (kind == 3) DrawChannels(entry);
                if (kind == 1 && property != "_MainTex" && !property.ToLowerInvariant().Contains("main") && !property.ToLowerInvariant().Contains("base") &&
                    !property.ToLowerInvariant().Contains("albedo"))
                    Note("The look's colours go here as they are (RGB, and alpha from the garment's opacity).");
                if (kind == 2) Note("The garment's normal map, with the relief of the look's patterns.");
                if (!open.TryGetValue(entry, out bool more)) more = false;
                open[entry] = more = EditorGUILayout.Foldout(more, "More", true);
                if (more) {
                    EditorGUI.indentLevel++;
                    Words(entry, "alsoOn", "Also On", "More texture properties that get the same texture (e.g. _OcclusionMap)");
                    Text(entry, "keyword", "Keyword", "A shader keyword turned on with the texture (e.g. _NORMALMAP)");
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void SetKind(JsonObject j, List<object> textures, JsonObject entry, string property, string description, int kind) {
            if (kind == 0) {
                textures.Remove(entry);
                Set(j, "textures", textures, false);
                return;
            }
            if (entry == null) {
                entry = new JsonObject();
                entry["property"] = property;
                textures.Add(entry);
                Set(j, "textures", textures, false);
            }
            entry["channels"] = kind == 1 ? "albedo" : kind == 2 ? "normal"
                : (object)(GuessChannels(property, description) is object[] guess && guess.Length == 4 ? guess : Layouts[4].channels).Select(Copy).ToList();
            if (entry["channels"] is string) open.Remove(entry);
            string keyword = KeywordFor(_type?.Shader, property);
            if (keyword != null && entry.String("keyword").Length == 0) entry["keyword"] = keyword;
        }

        private static string KeywordFor(Shader shader, string property) {
            if (shader == null || !Keywords.TryGetValue(property, out string keyword)) return null;
            try {
                return shader.keywordSpace.keywordNames.Contains(keyword) ? keyword : null;
            } catch (Exception) {
                return null;
            }
        }

        // R, G, B, A: each a source (a menu), inverted or not, or a value.
        private void DrawChannels(JsonObject entry) {
            var list = entry["channels"] as List<object> ?? new List<object>();
            while (list.Count < 4) list.Add(list.Count == 3 ? 1.0 : 0.0);
            for (int c = 0; c < 4; c++) {
                var (source, value, invert) = Parse(list[c]);
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Space(12);
                    EditorGUILayout.LabelField(ChannelNames[c], EditorStyles.boldLabel, GUILayout.Width(16));
                    string label = WardrobeMaterialType.SourceLabels[(int)source].Replace("/", ": ");
                    int channel = c;
                    if (EditorGUILayout.DropdownButton(new GUIContent(label, "What this channel holds"), FocusType.Keyboard, GUILayout.MinWidth(150))) {
                        var menu = new GenericMenu();
                        for (int i = 0; i < WardrobeMaterialType.SourceLabels.Length; i++) {
                            var s = (Source)i;
                            menu.AddItem(new GUIContent(WardrobeMaterialType.SourceLabels[i]), s == source, () => {
                                list[channel] = WardrobeMaterialTypes.WriteChannel(s, s == Source.Value ? (channel == 3 ? 1 : 0) : 0, false);
                                Set(entry, "channels", list, false);
                                repaint?.Invoke();
                            });
                            if (i == 0 || i == (int)Source.Opacity) menu.AddSeparator("");
                        }
                        menu.ShowAsContext();
                    }
                    if (source == Source.Value) {
                        EditorGUI.BeginChangeCheck();
                        float v = EditorGUILayout.Slider(value, 0, 1, GUILayout.Width(150));
                        if (EditorGUI.EndChangeCheck()) {
                            list[c] = WardrobeMaterialTypes.WriteChannel(Source.Value, v, false);
                            Set(entry, "channels", list, false);
                        }
                    } else {
                        EditorGUI.BeginChangeCheck();
                        bool flip = EditorGUILayout.ToggleLeft(new GUIContent("Invert", "1 − the value (e.g. a DirectX normal's green, or smoothness from roughness)"),
                            invert, GUILayout.Width(150));
                        if (EditorGUI.EndChangeCheck()) {
                            list[c] = WardrobeMaterialTypes.WriteChannel(source, 0, flip);
                            Set(entry, "channels", list, false);
                        }
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(12);
                if (EditorGUILayout.DropdownButton(new GUIContent("Common Layouts", "Fill the four channels the way common shaders pack them"),
                        FocusType.Keyboard, GUILayout.Width(130))) {
                    var menu = new GenericMenu();
                    foreach (var (label, channels) in Layouts) {
                        var picked = channels;
                        menu.AddItem(new GUIContent(label), false, () => {
                            entry["channels"] = picked.Select(Copy).ToList();
                            repaint?.Invoke();
                        });
                    }
                    menu.ShowAsContext();
                }
                GUILayout.FlexibleSpace();
            }
            DrawPreview(entry);
        }

        private static object Copy(object o) {
            if (!(o is JsonObject x)) return o;
            var copy = new JsonObject();
            for (int i = 0; i < x.Count; i++) copy[x.Keys[i]] = x.Values[i];
            return copy;
        }

        private static (Source source, float value, bool invert) Parse(object v) {
            if (v is double n) return (Source.Value, (float)n, false);
            bool invert = false;
            if (v is JsonObject o) {
                if (o["value"] is double value) return (Source.Value, (float)value, false);
                invert = o.Bool("invert");
                v = o.String("from");
            }
            int i = v is string s ? Array.IndexOf(WardrobeMaterialType.SourceNames, s.Trim().ToLowerInvariant()) : -1;
            return i > 0 ? ((Source)i, 0, invert) : (Source.Value, 0, false);
        }

        // ---------------------------------------------------------------- guessing from names

        /// <summary>Fills in the textures the names say (main texture → look colour, bump → normal, metallic → packed…); set ones stay.</summary>
        private void Guess(JsonObject j, Shader shader, Material material, bool keepSet) {
            if (shader == null) return;
            var textures = j.Array("textures") ?? new List<object>();
            bool albedo = textures.OfType<JsonObject>().Any(e => e["channels"] as string == "albedo");
            foreach (var (name, description) in TextureSlots(shader)) {
                var entry = textures.OfType<JsonObject>().FirstOrDefault(e => e.String("property").Trim() == name);
                if (entry != null && keepSet) continue;
                var guess = GuessChannels(name, description);
                if (guess == null) continue;
                bool isAlbedo = guess.Length == 1 && (string)guess[0] == "albedo";
                if (isAlbedo && albedo) continue;   // one main texture
                if (entry == null) {
                    entry = new JsonObject();
                    entry["property"] = name;
                    textures.Add(entry);
                }
                entry["channels"] = guess.Length == 1 ? guess[0] : guess.Select(Copy).ToList();
                string keyword = KeywordFor(shader, name);
                if (keyword != null) entry["keyword"] = keyword;
                albedo |= isAlbedo;
            }
            Set(j, "textures", textures, false);
        }

        // What a texture property likely wants, by its name: { "albedo" }, { "normal" }, 4 channels, or null (keep the material's).
        private static object[] GuessChannels(string name, string description) {
            string n = name.ToLowerInvariant(), d = (description ?? "").ToLowerInvariant();
            bool Has(params string[] words) => words.Any(w => n.Contains(w) || d.Contains(w));
            if (Has("detail", "emission", "emissive", "matcap", "rim", "outline", "decal", "ramp", "parallax", "height", "dissolve", "noise", "glitter",
                    "cubemap", "reflection probe", "shadow", "toon", "sss", "subsurface", "aniso", "flowmap", "lut")) return null;
            if (n == "_maintex" || n == "_basemap" || n == "_basecolormap" || n == "_albedo" || n == "_albedomap" || n == "_diffuse" ||
                Has("albedo", "base color", "base colour", "basecolor", "diffuse") || d == "main texture" || d == "main tex")
                return new object[] { "albedo" };
            if (Has("normal", "bump")) return new object[] { "normal" };
            if (n == "_mochiemetallicmaps") return Layouts[3].channels;
            if (n == "_maskmap") return Layouts[1].channels;
            if (n == "_metallicglossmap" || Has("metallic") && Has("gloss", "smooth")) return new object[] { "metalness", 0.0, 0.0, "smoothness" };
            if (Has("occlusion") || n.EndsWith("_ao") || n.Contains("aomap")) return Layouts[7].channels;
            if (Has("rough")) return Layouts[4].channels;
            if (Has("smooth", "gloss")) return Layouts[5].channels;
            if (Has("metal")) return Layouts[6].channels;
            if (Has("opacity", "alpha mask", "cutout", "transparen")) return Layouts[8].channels;
            return null;
        }

        // ---------------------------------------------------------------- preview

        private void PreviewGarment(WardrobeMaterialType type) {
            var garments = WardrobeGarments.All.Where(g => g.baseColor != null && type.Fits(g)).OrderBy(Title).ToList();
            if (garments.Count == 0) return;
            int index = Mathf.Max(0, garments.FindIndex(g => WardrobeGarments.PathOf(g) == _previewGarment));
            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.Popup(new GUIContent("Preview On", "The garment the packed textures are previewed on"), index,
                garments.Select(g => $"{Title(g)} on {AvatarName(g.avatarId)}".Replace("/", "∕")).ToArray());
            if (EditorGUI.EndChangeCheck() || _previewGarment == null) _previewGarment = WardrobeGarments.PathOf(garments[index]);
        }

        private WardrobeGarment Previewed => _previewGarment == null ? null : AssetDatabase.LoadAssetAtPath<WardrobeGarment>(_previewGarment);

        private void DrawPreview(JsonObject entry) {
            var g = Previewed;
            if (g == null || g.baseColor == null) return;
            string property = entry.String("property").Trim();
            string key = _previewGarment + "\n" + WardrobeJson.Write(entry) + "\n" + WardrobeConfigs.Revision;
            _previews.TryGetValue(property, out var cached);
            if (cached.key != key) QueuePreview();
            if (cached.rt == null) return;
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(12);
                var rect = GUILayoutUtility.GetRect(96, 96, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, cached.rt);
                GUILayout.Space(4);
                var alpha = GUILayoutUtility.GetRect(96, 96, GUILayout.ExpandWidth(false));
                EditorGUI.DrawTextureAlpha(alpha, cached.rt);
                EditorGUILayout.LabelField($"On {Title(g)}: RGB, and A", EditorStyles.wordWrappedMiniLabel);
            }
        }

        // Rendered after the GUI has drawn, so the blits don't disturb it.
        private void QueuePreview() {
            if (_previewQueued) return;
            _previewQueued = true;
            EditorApplication.delayCall += () => {
                _previewQueued = false;
                if (json == null) return;
                try {
                    RenderPreviews();
                } catch (Exception e) {
                    Debug.LogException(e);
                }
                repaint?.Invoke();
            };
        }

        private void RenderPreviews() {
            var g = Previewed;
            if (g == null || g.baseColor == null) return;
            if (_inputsFor != g) {
                DropInputs();
                _surface = WardrobeBaker.GarmentSurface(g);
                _normal = WardrobeBaker.GarmentNormal(g);
                _inputsFor = g;
            }
            var type = WardrobeMaterialTypes.Read(path, json);
            foreach (var entry in json.Objects("textures")) {
                string property = entry.String("property").Trim();
                var output = type.outputs.FirstOrDefault(o => o.property == property);
                if (output == null || !output.Packed) continue;
                string key = _previewGarment + "\n" + WardrobeJson.Write(entry) + "\n" + WardrobeConfigs.Revision;
                _previews.TryGetValue(property, out var cached);
                if (cached.key == key && cached.rt != null) continue;
                var rt = WardrobeBaker.RenderPreviewOutput(g, output, g.baseColor, _surface, _normal, cached.rt);
                _previews[property] = (key, rt);
            }
        }

        // ---------------------------------------------------------------- settings

        // Named floats or colours set on the material.
        private void Values(JsonObject j, string key, string label, Shader shader, ShaderPropertyType kind) {
            var values = j.Object(key) ?? new JsonObject();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            for (int i = 0; i < values.Count; i++) {
                string name = values.Keys[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    if (kind == ShaderPropertyType.Color) {
                        var c = EditorGUILayout.ColorField(new GUIContent(name), WardrobeConfigs.Color(values.Values[i] as string, Color.white), true, true, false);
                        if (EditorGUI.EndChangeCheck()) values.Values[i] = c.a < 1 ? "#" + ColorUtility.ToHtmlStringRGBA(c) : WardrobeConfigs.Hex(c);
                    } else {
                        float f = EditorGUILayout.FloatField(name, values.Values[i] is double n ? (float)n : 0);
                        if (EditorGUI.EndChangeCheck()) values.Values[i] = Math.Round(f, 4);
                    }
                    if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                        values.Remove(name);
                        Set(j, key, values, values.Count == 0);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (GUILayout.Button($"Add {label.TrimEnd('s')}…", EditorStyles.miniButton, GUILayout.Width(120))) {
                var menu = new GenericMenu();
                foreach (var (property, text) in Properties(shader, kind).Where(p => !values.Keys.Contains(p.Item1))) {
                    string picked = property;
                    menu.AddItem(new GUIContent(text), false, () => {
                        values[picked] = kind == ShaderPropertyType.Color ? (object)"#FFFFFF" : Default(shader, picked);
                        Set(j, key, values, false);
                        repaint?.Invoke();
                    });
                }
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(shader == null ? "Link a material first" : "Nothing more to add"));
                menu.ShowAsContext();
            }
            EditorGUI.indentLevel--;
        }

        private static double Default(Shader shader, string property) {
            int i = shader.FindPropertyIndex(property);
            try {
                return shader.GetPropertyType(i) == ShaderPropertyType.Int ? shader.GetPropertyDefaultIntValue(i) : shader.GetPropertyDefaultFloatValue(i);
            } catch (Exception) {
                return 0;
            }
        }

        private static IEnumerable<(string, string)> Properties(Shader shader, ShaderPropertyType kind) {
            if (shader == null) yield break;
            for (int i = 0; i < shader.GetPropertyCount(); i++) {
                var type = shader.GetPropertyType(i);
                bool match = kind == ShaderPropertyType.Float ? type == ShaderPropertyType.Float || type == ShaderPropertyType.Range || type == ShaderPropertyType.Int : type == kind;
                if (!match || (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
                string name = shader.GetPropertyName(i), description = shader.GetPropertyDescription(i);
                yield return (name, $"{description.Replace("/", "∕")}  ({name})");
            }
        }
    }
}
