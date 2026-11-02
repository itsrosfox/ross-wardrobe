using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// An editor for one Ros' Wardrobe file, shared by the file's inspector and Project Settings → Ros' Wardrobe. Changes
    /// are kept until <see cref="Save"/>; a form that has none follows the file when it changes on disk.
    /// </summary>
    internal abstract class WardrobeForm {
        public readonly string path;
        public Action repaint;

        protected WardrobeForm(string path) => this.path = path;

        public abstract bool Dirty { get; }

        /// <summary>What the file is called in lists and dialogs.</summary>
        public abstract string Name { get; }

        public abstract void Revert();

        public abstract void Save();

        public abstract void Draw();

        public virtual void Dispose() { }

        public static WardrobeForm Open(string path) {
            switch (WardrobeMigrations.KindOf(path)) {
                case WardrobeMigrations.Kind.Garment: return new WardrobeGarmentForm(path);
                case WardrobeMigrations.Kind.Pattern: return new WardrobePatternForm(path);
                case WardrobeMigrations.Kind.Preset: return new WardrobePresetForm(path);
                case WardrobeMigrations.Kind.Avatar: return new WardrobeAvatarForm(path);
                case WardrobeMigrations.Kind.Material: return new WardrobeMaterialForm(path);
                default: return null;
            }
        }

        /// <summary>The Save / Revert row (with the name as a title when <paramref name="title"/>).</summary>
        public void DrawToolbar(bool title) {
            using (new EditorGUILayout.HorizontalScope()) {
                if (title) EditorGUILayout.LabelField(Name, EditorStyles.largeLabel);
                else EditorGUILayout.LabelField(Dirty ? "Unsaved changes" : " ", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                bool dirty = Dirty;
                using (new EditorGUI.DisabledScope(!dirty)) {
                    if (GUILayout.Button("Revert", GUILayout.Width(70))) {
                        GUI.FocusControl(null);
                        Revert();
                    }
                    if (GUILayout.Button(dirty ? "Save*" : "Save", GUILayout.Width(70))) {
                        GUI.FocusControl(null);
                        Save();
                    }
                }
            }
        }

        /// <summary>Before leaving the form: asks to save its changes. False = stay.</summary>
        public bool ConfirmLeave() {
            if (!Dirty) return true;
            int choice = EditorUtility.DisplayDialogComplex("Ros' Wardrobe", $"Save your changes to {Name}?", "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            if (choice == 0) Save();
            return true;
        }

        // ---------------------------------------------------------------- shared fields

        protected static void Header(string text) => EditorGUILayout.LabelField(text, EditorStyles.boldLabel);

        protected static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);

        // A text field with a ▾ menu of suggestions.
        protected static string Suggest(GUIContent label, string value, IEnumerable<(string value, string label)> options, Action<string> picked) {
            using (new EditorGUILayout.HorizontalScope()) {
                value = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button(EditorGUIUtility.IconContent("icon dropdown"), EditorStyles.label, GUILayout.Width(18))) {
                    var menu = new GenericMenu();
                    string current = value;
                    foreach (var (v, l) in options) {
                        string chosen = v;
                        menu.AddItem(new GUIContent(l), WardrobeConfigs.SameId(current, v), () => picked(chosen));
                    }
                    if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Nothing in the project yet"));
                    menu.ShowAsContext();
                }
            }
            return value;
        }

        protected static IEnumerable<(string, string)> GarmentIds() =>
            WardrobeGarments.All.Where(g => !string.IsNullOrWhiteSpace(g.id)).GroupBy(g => g.id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => (x.Key, $"{Title(x.First())}  ({x.Key})")).OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase);

        protected static IEnumerable<(string, string)> AvatarIds(string garment = null) {
            var ids = WardrobeAvatars.All.Select(a => (a.id, $"{a.DisplayName}  ({a.id})")).ToList();
            // avatars fits of the garment name that have no .ravt here
            foreach (var g in WardrobeGarments.All.Where(g => garment == null || WardrobeConfigs.SameId(g.id, garment)))
                if (!string.IsNullOrWhiteSpace(g.avatarId) && ids.All(i => !WardrobeConfigs.SameId(i.id, g.avatarId))) ids.Add((g.avatarId, g.avatarId));
            return new[] { ("", "Any avatar") }.Concat(ids);
        }

        public static string Title(WardrobeGarment g) => g == null ? "" : string.IsNullOrWhiteSpace(g.displayName) ? g.name : g.displayName.Trim();

        public static string AvatarName(string id) {
            if (string.IsNullOrWhiteSpace(id)) return "any avatar";
            return WardrobeAvatars.Get(id)?.DisplayName ?? id;
        }

        protected static Color ParseColor(string hex, Color fallback) =>
            !string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out var c) ? c : fallback;

        protected static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>A kebab-case id from a prefix and a name ("rosden", "Pin Stripe" → "rosden.pin-stripe").</summary>
        public static string SuggestId(string prefix, string name) {
            string Kebab(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            string p = Kebab(prefix), n = Kebab(name);
            return n.Length == 0 ? "" : p.Length == 0 ? n : $"{p}.{n}";
        }

        // Heavy work after the GUI has finished drawing, with a progress bar.
        protected void Later(string label, Action action) {
            EditorApplication.delayCall += () => {
                try {
                    EditorUtility.DisplayProgressBar("Ros' Wardrobe", label, 0.5f);
                    action();
                } catch (Exception e) {
                    Debug.LogException(e);
                    EditorUtility.DisplayDialog("Ros' Wardrobe", e.Message, "OK");
                } finally {
                    EditorUtility.ClearProgressBar();
                    repaint?.Invoke();
                }
            };
        }
    }

    /// <summary>
    /// A form over a file's json tree, so keys it doesn't show stay as they are. Keys set back to their default are
    /// dropped (unless they're always written) and new ones go in the kind's usual order, so saved files look like
    /// generated ones.
    /// </summary>
    internal abstract class WardrobeJsonForm : WardrobeForm {
        protected static readonly string[] TargetOrder = { "garment", "avatar", "version" };

        private readonly WardrobeMigrations.Kind _kind;
        protected JsonObject json;
        private string _saved, _error, _upgradedFrom;
        private bool _newer;
        private int _revision;
        protected readonly Dictionary<object, bool> open = new Dictionary<object, bool>();

        protected WardrobeJsonForm(string path, WardrobeMigrations.Kind kind) : base(path) {
            _kind = kind;
            Revert();
        }

        /// <summary>The order keys are written in.</summary>
        protected abstract string[] Order { get; }

        /// <summary>Keys written even when empty.</summary>
        protected virtual ICollection<string> Always => new[] { "format", "id", "version", "name", "author" };

        protected abstract void DrawJson(JsonObject j);

        public override bool Dirty => json != null && WardrobeJson.Write(json) != _saved;

        public override string Name => json?.String("name") is string n && n.Trim().Length > 0 ? n.Trim() : Path.GetFileNameWithoutExtension(path);

        public override void Revert() {
            _error = _upgradedFrom = null;
            _newer = false;
            _revision = WardrobeConfigs.Revision;
            open.Clear();
            try {
                string text = File.ReadAllText(path);
                json = WardrobeJson.Parse(text);
                _saved = WardrobeJson.Write(json);
                int format = WardrobeMigrations.FormatOf(text), current = WardrobeMigrations.Current(_kind);
                _newer = format > current;
                if (format < current) {
                    // edited as the current format; saving writes it that way
                    WardrobeMigrations.Upgrade(_kind, json, path);
                    _upgradedFrom = format.ToString();
                }
            } catch (Exception e) {
                json = null;
                _error = e.Message;
            }
        }

        public override void Save() {
            if (json == null) return;
            string text = WardrobeJson.Write(json);
            File.WriteAllText(path, text);
            _saved = text;
            _upgradedFrom = null;
            AssetDatabase.ImportAsset(path);
            _revision = WardrobeConfigs.Revision;
        }

        public override void Draw() {
            // changed on disk (and nothing here to lose): show the file as it is now
            if (_revision != WardrobeConfigs.Revision) {
                _revision = WardrobeConfigs.Revision;
                if (!Dirty) Revert();
            }
            if (json == null) {
                EditorGUILayout.HelpBox($"Couldn't read {path}: {_error}", MessageType.Error);
                if (GUILayout.Button("Read Again", GUILayout.Width(100))) Revert();
                return;
            }
            if (_newer) {
                EditorGUILayout.HelpBox("This file was made with a newer Ros' Wardrobe, so it can't be edited here. Update Ros' Wardrobe through VCC.",
                    MessageType.Warning);
                return;
            }
            if (_upgradedFrom != null)
                EditorGUILayout.HelpBox($"This file is in an older layout (format {_upgradedFrom}). It's shown updated; saving writes it that way.", MessageType.Info);
            DrawJson(json);
        }

        // ---------------------------------------------------------------- fields

        /// <summary>Sets a key (dropping it when it's back to its default, unless it's one that's always written).</summary>
        protected void Set(JsonObject o, string key, object value, bool isDefault, string[] order = null) {
            if (isDefault && (o != json || !Always.Contains(key))) {
                o.Remove(key);
                return;
            }
            o.Put(key, value, order ?? Order);
        }

        protected void Text(JsonObject o, string key, string label, string tooltip = null, string[] order = null) {
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(new GUIContent(label, tooltip), o.String(key));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, value.Length == 0, order);
        }

        protected void TextArea(JsonObject o, string key, string label, string tooltip = null, string[] order = null) {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));
            string value = EditorGUILayout.TextArea(o.String(key), EditorStyles.textArea, GUILayout.MinHeight(36));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, value.Length == 0, order);
        }

        protected void Toggle(JsonObject o, string key, string label, string tooltip = null, string[] order = null) {
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), o.Bool(key));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, !value, order);
        }

        protected void Slider(JsonObject o, string key, string label, double fallback, float min, float max, string tooltip = null, string[] order = null) {
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(new GUIContent(label, tooltip), (float)o.Number(key, fallback), min, max);
            if (EditorGUI.EndChangeCheck()) {
                value = (float)Math.Round(value, 3);
                Set(o, key, new JsonNumber(value), Math.Abs(value - fallback) < 1e-6, order);
            }
        }

        protected void Integer(JsonObject o, string key, string label, int fallback, string tooltip = null, string[] order = null) {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), o.Int(key, fallback));
            if (EditorGUI.EndChangeCheck()) Set(o, key, new JsonNumber(value), value == fallback, order);
        }

        /// <summary>A comma-separated list of strings.</summary>
        protected void Words(JsonObject o, string key, string label, string tooltip = null, string[] order = null) {
            var list = (o.Array(key) ?? new List<object>()).OfType<string>().ToList();
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.DelayedTextField(new GUIContent(label, (tooltip ?? "") + " (comma separated)"), string.Join(", ", list));
            if (EditorGUI.EndChangeCheck()) {
                var words = value.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().Cast<object>().ToList();
                Set(o, key, words, words.Count == 0, order);
            }
        }

        /// <summary>An asset the file names by path (relative to it, or "path#sub-asset" for meshes in a model).</summary>
        protected T Asset<T>(JsonObject o, string key, string label, string tooltip = null, string[] order = null) where T : Object {
            string relative = o.String(key);
            var asset = WardrobeConfigs.Load<T>(path, relative);
            EditorGUI.BeginChangeCheck();
            var picked = (T)EditorGUILayout.ObjectField(new GUIContent(label, tooltip), asset, typeof(T), false);
            if (EditorGUI.EndChangeCheck()) Set(o, key, picked == null ? "" : WardrobeConfigs.Relative(path, picked), picked == null, order);
            if (relative.Length > 0 && asset == null) EditorGUILayout.HelpBox($"Can't find '{relative}'.", MessageType.Warning);
            return picked;
        }

        protected Texture2D Texture(JsonObject o, string key, string label, string tooltip = null, string[] order = null) =>
            Asset<Texture2D>(o, key, label, tooltip, order);

        /// <summary>The id field, with a warning when it's empty or another file of the kind has it.</summary>
        protected void Id(JsonObject o, string tooltip, IEnumerable<string> others) {
            Text(o, "id", "Id", tooltip);
            string id = o.String("id").Trim();
            if (id.Length == 0) EditorGUILayout.HelpBox("It needs an id.", MessageType.Error);
            else if (others.Any(x => WardrobeConfigs.SameId(x, id)))
                EditorGUILayout.HelpBox($"Another file in the project has the id {id}; only one of them is used.", MessageType.Warning);
        }

        // ---------------------------------------------------------------- garments it's for

        /// <summary>
        /// The "garments" list: which garment fits the file is for (garment id, optionally an avatar, versions).
        /// <paramref name="extra"/> draws more per entry; returns nothing, entries are edited in place.
        /// </summary>
        protected void Targets(JsonObject j, string empty, Action<JsonObject> extra = null, Action<JsonObject> added = null) {
            var list = j.Array("garments") ?? new List<object>();
            if (list.Count == 0) Note(empty);
            for (int i = 0; i < list.Count; i++) {
                if (!(list[i] is JsonObject t)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    var target = new WardrobeTarget { garment = t.String("garment"), avatar = t.String("avatar"), version = t.String("version") };
                    var fits = WardrobeGarments.All.Where(target.Matches).ToList();
                    var any = WardrobeGarments.All.FirstOrDefault(g => WardrobeConfigs.SameId(g.id, target.garment));
                    using (new EditorGUILayout.HorizontalScope()) {
                        string title = any != null ? Title(any) : string.IsNullOrWhiteSpace(target.garment) ? "Garment" : target.garment;
                        EditorGUILayout.LabelField($"{title} on {AvatarName(target.avatar)}", EditorStyles.boldLabel);
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                            list.RemoveAt(i);
                            if (list.Count == 0) j.Remove("garments");
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    string garment = Suggest(new GUIContent("Garment Id", "The .rgarm id"), t.String("garment"), GarmentIds(),
                        v => Set(t, "garment", v, false, TargetOrder));
                    if (EditorGUI.EndChangeCheck()) Set(t, "garment", garment, false, TargetOrder);
                    EditorGUI.BeginChangeCheck();
                    string avatar = Suggest(new GUIContent("Avatar Id", "Only the garment's fit for this avatar base (.ravt id); empty = every fit"),
                        t.String("avatar"), AvatarIds(target.garment), v => Set(t, "avatar", v, v.Length == 0, TargetOrder));
                    if (EditorGUI.EndChangeCheck()) Set(t, "avatar", avatar, avatar.Length == 0, TargetOrder);
                    Text(t, "version", "Version", "Which of the garment's versions: \"1\" = 1.x.x, \"1.2\" = 1.2.x, empty = any", TargetOrder);
                    if (string.IsNullOrWhiteSpace(target.garment)) { }
                    else if (any == null) EditorGUILayout.LabelField("No garment with this id in the project (fine if it ships separately).", EditorStyles.miniLabel);
                    else if (fits.Count == 0) EditorGUILayout.HelpBox("No fit of it in the project matches this avatar and version.", MessageType.Warning);
                    else EditorGUILayout.LabelField($"Matches {string.Join(", ", fits.Select(g => $"{Title(g)} on {AvatarName(g.avatarId)} {g.version}"))}",
                        EditorStyles.wordWrappedMiniLabel);
                    extra?.Invoke(t);
                }
            }

            if (GUILayout.Button("Add Garment", GUILayout.Width(160))) {
                var menu = new GenericMenu();
                foreach (var g in WardrobeGarments.All.Where(g => !string.IsNullOrWhiteSpace(g.id)).OrderBy(Title)) {
                    var picked = g;
                    string fit = $"{Title(g)} on {AvatarName(g.avatarId)}";
                    menu.AddItem(new GUIContent($"{fit.Replace("/", "∕")}  ({g.id})"), false, () => AddTarget(j, list, picked.id, picked.avatarId, picked.version, added));
                }
                foreach (var x in WardrobeGarments.All.Where(g => !string.IsNullOrWhiteSpace(g.id)).GroupBy(g => g.id, StringComparer.OrdinalIgnoreCase)
                             .Where(x => x.Select(g => g.avatarId).Distinct().Count() > 1)) {
                    var first = x.First();
                    menu.AddItem(new GUIContent($"{Title(first)} on any avatar  ({first.id})"), false, () => AddTarget(j, list, first.id, "", first.version, added));
                }
                if (menu.GetItemCount() > 0) menu.AddSeparator("");
                menu.AddItem(new GUIContent("Another (type its id)"), false, () => AddTarget(j, list, "", "", "", added));
                menu.ShowAsContext();
            }
        }

        private void AddTarget(JsonObject j, List<object> list, string garment, string avatar, string version, Action<JsonObject> added) {
            var t = new JsonObject();
            t["garment"] = garment ?? "";
            if (!string.IsNullOrWhiteSpace(avatar)) t["avatar"] = avatar;
            t["version"] = WardrobeConfigs.Major(version);
            added?.Invoke(t);
            list.Add(t);
            Set(j, "garments", list, false);
            repaint?.Invoke();
        }

        protected static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
