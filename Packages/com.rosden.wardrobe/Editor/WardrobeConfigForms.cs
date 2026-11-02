using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Edits one .rpre: a ready-made look, its parts and colourways.</summary>
    internal class WardrobePresetForm : WardrobeJsonForm {
        private static readonly string[] PresetOrder = {
            "format", "id", "version", "name", "lookName", "group", "description", "author", "order", "garments", "parts", "colourways",
        };
        private static readonly string[] PartOrder = { "region", "pattern", "colors", "scale", "rotate" };
        private static readonly string[] ColourwayOrder = { "name", "parts" };
        private static readonly string[] ColourwayPartOrder = { "region", "colors" };

        public WardrobePresetForm(string path) : base(path, WardrobeMigrations.Kind.Preset) { }

        protected override string[] Order => PresetOrder;

        protected override ICollection<string> Always => new[] { "format", "id", "version", "name", "group", "description", "author", "parts" };

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
            Targets(j, "None listed: offered for every garment.");

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
                        string region = Suggest(new GUIContent("Region"), part.String("region"), Regions(j), v => Set(part, "region", v, false, PartOrder));
                        if (EditorGUI.EndChangeCheck()) Set(part, "region", region, false, PartOrder);
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                            parts.RemoveAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    string pattern = Suggest(new GUIContent("Pattern", "A .rpat id"), part.String("pattern"),
                        WardrobePatterns.All.Select(p => (p.id, $"{(string.IsNullOrWhiteSpace(p.group) ? "" : p.group + "/")}{p.DisplayName}  ({p.id})")),
                        v => Set(part, "pattern", v, false, PartOrder));
                    if (EditorGUI.EndChangeCheck()) Set(part, "pattern", pattern, false, PartOrder);
                    var known = WardrobePatterns.Get(part.String("pattern"));
                    if (known == null && part.String("pattern").Trim().Length > 0)
                        EditorGUILayout.LabelField("No pattern with this id in the project.", EditorStyles.miniLabel);
                    Colours(part, "colors", known, PartOrder);
                    EditorGUI.BeginChangeCheck();
                    float scale = EditorGUILayout.FloatField(new GUIContent("Scale", "Pattern size (1 = as made)"), (float)part.Number("scale", 1));
                    if (EditorGUI.EndChangeCheck()) Set(part, "scale", new JsonNumber(Math.Round(Mathf.Max(0.01f, scale), 3)), false, PartOrder);
                    EditorGUI.BeginChangeCheck();
                    bool rotate = EditorGUILayout.Toggle(new GUIContent("Rotate", "Turned 90°"), part.Bool("rotate"));
                    if (EditorGUI.EndChangeCheck()) Set(part, "rotate", rotate, false, PartOrder);
                }
            }
            if (GUILayout.Button("Add Part", GUILayout.Width(160))) {
                var part = new JsonObject();
                part["region"] = parts.Count == 0 ? WardrobePreset.AnyRegion : "";
                part["pattern"] = WardrobePatterns.Get(null)?.id ?? "";
                part["colors"] = new List<object> { "#F4F4F2" };
                part["scale"] = new JsonNumber(1);
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
                        Text(way, "name", "Name", null, ColourwayOrder);
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
                        Colours(wp, "colors", WardrobePatterns.Get(part?.String("pattern")), ColourwayPartOrder);
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
                                Set(way, "parts", wayParts, false, ColourwayOrder);
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
            var targets = WardrobePatterns.ReadTargets(j);
            var garments = WardrobeGarments.All.Where(g => targets.Count == 0 || targets.Any(t => t.Matches(g)));
            return new[] { (WardrobePreset.AnyRegion, "* (every other part)") }
                .Concat(garments.SelectMany(g => g.regions.Select(r => r.name)).Distinct().OrderBy(r => r).Select(r => (r, r)));
        }

        // A list of slot colours: "#RRGGBB", or a gradient as "#RRGGBB@0, #RRGGBB@1".
        private void Colours(JsonObject o, string key, WardrobePattern pattern, string[] order) {
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
                        var g = WardrobePatterns.ParseGradient(text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray()) ?? SlotColor.Constant(Color.white);
                        g = EditorGUILayout.GradientField(new GUIContent(label), g);
                        value = string.Join(", ", g.colorKeys.Select(k => $"{Hex(k.color)}@{Math.Round(k.time, 3).ToString(CultureInfo.InvariantCulture)}"));
                    } else {
                        var c = EditorGUILayout.ColorField(new GUIContent(label), ParseColor(text, slot?.color ?? Color.white), true, false, false);
                        value = Hex(c);
                    }
                    bool canGradient = slot == null || slot.canGradient;
                    using (new EditorGUI.DisabledScope(!canGradient && !gradient))
                        if (GUILayout.Button(new GUIContent(gradient ? "Flat" : "Gradient", gradient ? "Make it one colour" : "Make it a gradient along the garment"),
                                EditorStyles.miniButton, GUILayout.Width(60))) {
                            var c = ParseColor(text.Split(',')[0].Split('@')[0], slot?.color ?? Color.white);
                            value = gradient ? Hex(c) : $"{Hex(c)}@0, {Hex(c)}@1";
                            GUI.changed = true;
                        }
                    if (EditorGUI.EndChangeCheck() && value != text) {
                        while (colors.Count <= i) colors.Add(Hex(slot?.color ?? Color.white));
                        colors[i] = value;
                        Set(o, key, colors, false, order);
                    }
                }
            }
            if (pattern != null && colors.Count > pattern.slots.Count)
                EditorGUILayout.LabelField($"The pattern has {pattern.slots.Count} colours; the rest aren't used.", EditorStyles.miniLabel);
        }
    }

    /// <summary>Edits one .ravt: an avatar base, and how Ros' Wardrobe recognises it in a scene.</summary>
    internal class WardrobeAvatarForm : WardrobeJsonForm {
        private static readonly string[] AvatarOrder = { "format", "id", "name", "version", "author", "description", "matches", "guids" };

        public WardrobeAvatarForm(string path) : base(path, WardrobeMigrations.Kind.Avatar) { }

        protected override string[] Order => AvatarOrder;

        protected override ICollection<string> Always => new[] { "format", "id", "name", "version", "author" };

        protected override void DrawJson(JsonObject j) {
            Header("Avatar");
            Text(j, "name", "Name", "Shown in the Ros' Wardrobe window");
            Id(j, "Stable id garments name the avatar by (e.g. winterpaw.masculine-canine); don't change it once shipped",
                WardrobeAvatars.All.Where(a => a.path != path).Select(a => a.id));
            Text(j, "version", "Version", "The base's version, if garments need to tell versions apart");
            Text(j, "author", "Author", "Who made the base");
            TextArea(j, "description", "Description");

            EditorGUILayout.Space();
            Header("Recognising it");
            Note("An avatar in a scene is this base when its prefab or model is one of the files below (the base's own files have the same GUID " +
                 "in every project), else when its name or its prefab's or model's contains one of the words.");
            var guids = (j.Array("guids") ?? new List<object>()).OfType<string>().ToList();
            for (int i = 0; i < guids.Count; i++) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadMainAssetAtPath(assetPath);
                using (new EditorGUILayout.HorizontalScope()) {
                    if (asset != null)
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.ObjectField(i == 0 ? "Files" : " ", asset, typeof(Object), false);
                    else EditorGUILayout.LabelField(i == 0 ? "Files" : " ", $"{guids[i]} (not in this project)");
                    if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                        guids.RemoveAt(i);
                        Set(j, "guids", guids.Cast<object>().ToList(), guids.Count == 0);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            var added = EditorGUILayout.ObjectField(new GUIContent(guids.Count == 0 ? "Files" : " ", "Add the base's model (.fbx) or prefab"), null, typeof(GameObject), false);
            if (added != null) {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(added));
                if (!string.IsNullOrEmpty(guid) && !guids.Contains(guid)) {
                    guids.Add(guid);
                    Set(j, "guids", guids.Cast<object>().ToList(), false);
                }
            }
            Words(j, "matches", "Names", "Words in the avatar's name, or its prefab's or model's, that mean it's this base");

            EditorGUILayout.Space();
            var id = j.String("id").Trim();
            var scene = Object.FindObjectsOfType<Animator>().Select(a => a.gameObject).Where(go => go.transform.parent == null).ToList();
            var recognised = scene.Where(go => WardrobeConfigs.SameId(WardrobeAvatars.Match(go)?.id, id)).ToList();
            if (scene.Count > 0)
                Note(recognised.Count > 0 ? $"In the open scenes: {string.Join(", ", recognised.Select(go => go.name))}."
                    : "No avatar in the open scenes is recognised as this base (save to check again).");
            var fits = WardrobeGarments.All.Where(g => WardrobeConfigs.SameId(g.avatarId, id)).ToList();
            Note(fits.Count == 0 ? "No garment in the project is fitted to it yet." : $"Garments fitted to it: {string.Join(", ", fits.Select(Title).Distinct())}.");
        }
    }

    /// <summary>Edits one .rmat: a material type, and which of a garment's maps go into which texture property.</summary>
    internal class WardrobeMaterialForm : WardrobeJsonForm {
        private static readonly string[] MaterialOrder = {
            "format", "id", "name", "version", "author", "description", "shader", "template", "textures", "floats", "colors", "keywords", "garments",
        };
        private static readonly string[] TextureOrder = { "property", "channels", "alsoOn", "keyword" };
        private static readonly string[] Holds = { "The look's albedo", "The normal map", "Channels" };
        private static readonly string[] ChannelNames = { "R", "G", "B", "A" };

        public WardrobeMaterialForm(string path) : base(path, WardrobeMigrations.Kind.Material) { }

        protected override string[] Order => MaterialOrder;

        protected override ICollection<string> Always => new[] { "format", "id", "name", "version", "author", "description", "textures" };

        private WardrobeMaterialType _type;
        private string _typeText;

        protected override void DrawJson(JsonObject j) {
            // read like Ros' Wardrobe reads it, again only when it changed (reading warns about mistakes)
            string text = WardrobeJson.Write(j);
            if (text != _typeText) {
                _typeText = text;
                _type = WardrobeMaterialTypes.Read(path, j);
            }
            var type = _type;
            Header("Material Type");
            Text(j, "name", "Name", "Shown where buyers pick it (Toon, PBR…)");
            Id(j, "Stable id garments and looks name it by; don't change it once shipped",
                WardrobeMaterialTypes.All.Where(t => t.path != path).Select(t => t.id));
            Text(j, "version", "Version");
            Text(j, "author", "Author");
            TextArea(j, "description", "Description", "Shown as its tooltip");

            EditorGUILayout.Space();
            Header("Shader");
            Note("Materials are made from the template material when there is one (its settings kept), else from the shader.");
            var template = Asset<Material>(j, "template", "Template", "A material to copy settings from");
            EditorGUI.BeginChangeCheck();
            var shader = (Shader)EditorGUILayout.ObjectField(new GUIContent("Shader", "Used instead of the template's"), type.shader, typeof(Shader), false);
            if (EditorGUI.EndChangeCheck()) Set(j, "shader", shader != null ? shader.name : "", shader == null);
            if (type.shader == null && type.shaderName.Length > 0)
                EditorGUILayout.HelpBox($"The shader '{type.shaderName}' isn't in this project.", MessageType.Warning);
            var used = type.Shader;

            EditorGUILayout.Space();
            Header("Textures");
            Note("What Ros' Wardrobe puts in each texture property. Channels are picked from the garment's maps (where a garment's own texture " +
                 "already holds them in that order, it's used as it is) and from the look's albedo.");
            var textures = j.Array("textures") ?? new List<object>();
            for (int i = 0; i < textures.Count; i++) {
                if (!(textures[i] is JsonObject t)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUI.BeginChangeCheck();
                        string property = Suggest(new GUIContent("Property"), t.String("property"), Properties(used, ShaderPropertyType.Texture),
                            v => Set(t, "property", v, false, TextureOrder));
                        if (EditorGUI.EndChangeCheck()) Set(t, "property", property, false, TextureOrder);
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                            textures.RemoveAt(i);
                            GUIUtility.ExitGUI();
                        }
                    }
                    if (used != null && t.String("property").Length > 0 && used.FindPropertyIndex(t.String("property")) < 0)
                        EditorGUILayout.HelpBox($"{used.name} has no property {t.String("property")}.", MessageType.Warning);
                    DrawChannels(t);
                    Words(t, "alsoOn", "Also On", "More texture properties that get the same texture", TextureOrder);
                    Text(t, "keyword", "Keyword", "A shader keyword turned on with the texture (e.g. _NORMALMAP)", TextureOrder);
                }
            }
            if (GUILayout.Button("Add Texture", GUILayout.Width(160))) {
                var t = new JsonObject();
                t["property"] = "";
                t["channels"] = textures.OfType<JsonObject>().Any(o => o["channels"] as string == "albedo") ? (object)Channels("metalness", "roughness", "occlusion", "sheen") : "albedo";
                textures.Add(t);
                Set(j, "textures", textures, false);
            }

            EditorGUILayout.Space();
            Header("Settings");
            Values(j, "floats", "Floats", used, ShaderPropertyType.Float);
            Values(j, "colors", "Colours", used, ShaderPropertyType.Color);
            Words(j, "keywords", "Keywords", "Shader keywords turned on");

            EditorGUILayout.Space();
            Header("Garments");
            Targets(j, "None listed: offered for every garment.");

            string problem = type.Problem;
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        private static List<object> Channels(params string[] sources) => sources.Cast<object>().ToList();

        private void DrawChannels(JsonObject t) {
            object channels = t["channels"];
            int holds = channels is string s ? (s.Trim().ToLowerInvariant() == "normal" ? 1 : 0) : 2;
            EditorGUI.BeginChangeCheck();
            holds = EditorGUILayout.Popup(new GUIContent("Holds"), holds, Holds);
            if (EditorGUI.EndChangeCheck()) {
                Set(t, "channels", holds == 0 ? "albedo" : holds == 1 ? "normal" : (object)Channels("metalness", "roughness", "occlusion", "sheen"), false, TextureOrder);
                return;
            }
            if (holds != 2) return;
            var list = channels as List<object> ?? new List<object>();
            while (list.Count < 4) list.Add(new JsonNumber(list.Count == 3 ? 1 : 0));
            EditorGUI.indentLevel++;
            for (int c = 0; c < 4; c++) {
                object v = list[c];
                string name = v is string text ? text.Trim().ToLowerInvariant() : "value";
                int index = Array.IndexOf(WardrobeMaterialType.SourceNames, name);
                if (index < 0) index = 0;
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(ChannelNames[c], index, WardrobeMaterialType.SourceNames);
                    if (EditorGUI.EndChangeCheck()) {
                        list[c] = picked == 0 ? (object)new JsonNumber(c == 3 ? 1 : 0) : WardrobeMaterialType.SourceNames[picked];
                        Set(t, "channels", list, false, TextureOrder);
                    }
                    if (index == 0) {
                        EditorGUI.BeginChangeCheck();
                        float value = EditorGUILayout.Slider(v is JsonNumber n ? (float)n.Value : 0, 0, 1, GUILayout.Width(140));
                        if (EditorGUI.EndChangeCheck()) {
                            list[c] = new JsonNumber(Math.Round(value, 3));
                            Set(t, "channels", list, false, TextureOrder);
                        }
                    }
                }
            }
            EditorGUI.indentLevel--;
        }

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
                        var c = EditorGUILayout.ColorField(new GUIContent(name), ParseColor(values.Values[i] as string, Color.white), true, true, false);
                        if (EditorGUI.EndChangeCheck()) values.Values[i] = c.a < 1 ? "#" + ColorUtility.ToHtmlStringRGBA(c) : Hex(c);
                    } else {
                        float f = EditorGUILayout.FloatField(name, values.Values[i] is JsonNumber n ? (float)n.Value : 0);
                        if (EditorGUI.EndChangeCheck()) values.Values[i] = new JsonNumber(Math.Round(f, 4));
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
                foreach (var (property, text) in Properties(shader, kind).Where(p => !values.Has(p.Item1))) {
                    string picked = property;
                    menu.AddItem(new GUIContent(text), false, () => {
                        values[picked] = kind == ShaderPropertyType.Color ? (object)"#FFFFFF" : new JsonNumber(Default(shader, picked));
                        Set(j, key, values, false);
                        repaint?.Invoke();
                    });
                }
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent(shader == null ? "Pick a shader or template first" : "Nothing more to add"));
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
