using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The inspector's editor for one Ros' Wardrobe file. Changes are kept until <see cref="Save"/>; a form that has none
    /// follows the file when it changes on disk.
    /// </summary>
    internal abstract class WardrobeForm {
        public readonly string path;
        public Action repaint;
        /// <summary>Opens another Ros' Wardrobe file (the Setup tab opens it beside; else it's selected).</summary>
        public Action<string> show;

        protected WardrobeForm(string path) => this.path = path;

        public abstract bool Dirty { get; }

        public abstract void Revert();

        public abstract void Save();

        public abstract void Draw();

        public virtual void Dispose() { }

        public static WardrobeForm Open(string path) {
            switch (Path.GetExtension(path).ToLowerInvariant()) {
                case WardrobeConfigs.Garment: return new WardrobeGarmentForm(path);
                case WardrobeConfigs.Pattern: return new WardrobePatternForm(path);
                case WardrobeConfigs.Preset: return new WardrobePresetForm(path);
                case WardrobeConfigs.Avatar: return new WardrobeAvatarForm(path);
                case WardrobeConfigs.Material: return new WardrobeMaterialForm(path);
                default: return null;
            }
        }

        /// <summary>The Save / Revert row.</summary>
        public void DrawToolbar() {
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(Dirty ? "Unsaved changes" : " ", EditorStyles.miniLabel);
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

        // ---------------------------------------------------------------- problems

        /// <summary>What the checks found in the file (as saved), each with its fix.</summary>
        public void DrawProblems() {
            var problems = WardrobeChecks.For(path);
            if (problems.Count == 0) return;
            foreach (var p in problems) DrawProblem(p, this);
            if (Dirty) EditorGUILayout.LabelField("These are about the file as saved: save to check your changes.", EditorStyles.miniLabel);
            EditorGUILayout.Space();
        }

        /// <summary>One problem: its message, and its fix as a button.</summary>
        internal static void DrawProblem(WardrobeProblem p, WardrobeForm form, Action<string> show = null) {
            if (show != null) {
                // in a list (narrow): the file, the message under it, and the fix under that
                if (GUILayout.Button(new GUIContent(p.File, p.path), EditorStyles.linkLabel)) show(p.path);
                EditorGUILayout.HelpBox(p.message, p.type);
                if (p.fix != null)
                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.FlexibleSpace();
                        var label = new GUIContent(p.fix, "Fix it");
                        if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.MaxWidth(Mathf.Max(60, EditorStyles.miniButton.CalcSize(label).x + 8))))
                            Fix(p, form);
                    }
                GUILayout.Space(4);
                return;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.HelpBox(p.message, p.type);
                if (p.fix != null && GUILayout.Button(new GUIContent(p.fix, "Fix it"), GUILayout.Width(Mathf.Max(70, EditorStyles.miniButton.CalcSize(new GUIContent(p.fix)).x + 12)),
                        GUILayout.Height(38)))
                    Fix(p, form);
            }
        }

        /// <summary>Runs a problem's fix after the GUI has drawn (saving the open file first, if it's that one).</summary>
        internal static void Fix(WardrobeProblem p, WardrobeForm form) {
            EditorApplication.delayCall += () => {
                if (form != null && form.path == p.path && form.Dirty) {
                    int choice = EditorUtility.DisplayDialogComplex("Ros' Wardrobe", $"Save your changes to {p.File} first?", "Save", "Cancel", "Discard");
                    if (choice == 1) return;
                    if (choice == 0) form.Save();
                    else form.Revert();
                }
                try {
                    EditorUtility.DisplayProgressBar("Ros' Wardrobe", $"{p.fix}…", 0.5f);
                    p.apply();
                } catch (Exception e) {
                    Debug.LogException(e);
                    EditorUtility.DisplayDialog("Ros' Wardrobe", e.Message, "OK");
                } finally {
                    EditorUtility.ClearProgressBar();
                    WardrobeChecks.Forget();
                    form?.repaint?.Invoke();
                }
            };
        }

        // ---------------------------------------------------------------- shared fields

        protected static void Header(string text) => EditorGUILayout.LabelField(text, EditorStyles.boldLabel);

        /// <summary>Opens another file: in the Setup tab when the form is there, else in the inspector.</summary>
        protected void Show(string file) {
            if (show != null) {
                show(file);
                return;
            }
            var asset = AssetDatabase.LoadMainAssetAtPath(file);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>A step of a guided setup: its number, title and whether it's done (✓), needs something (!) or isn't yet (○).</summary>
        protected static void Step(int number, string title, StepState state, string status = null) {
            EditorGUILayout.Space(6);
            var rect = EditorGUILayout.GetControlRect(false, 22);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), new Color(0.5f, 0.5f, 0.5f, 0.35f));
            string mark = state == StepState.Done ? "✓" : state == StepState.Attention ? "!" : "○";
            var colour = state == StepState.Done ? new Color(0.35f, 0.75f, 0.4f) : state == StepState.Attention ? new Color(0.95f, 0.65f, 0.2f) : Color.gray;
            var markStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = colour }, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(rect.x, rect.y, 20, rect.height), mark, markStyle);
            GUI.Label(new Rect(rect.x + 22, rect.y, rect.width - 22, rect.height), $"{number}. {title}", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(status)) {
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(status));
                GUI.Label(new Rect(rect.xMax - size.x, rect.y + 3, size.x, rect.height), status, EditorStyles.miniLabel);
            }
        }

        protected enum StepState { Todo, Attention, Done }

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
    /// dropped, unless they're always written.
    /// </summary>
    internal abstract class WardrobeJsonForm : WardrobeForm {
        protected JsonObject json;
        private string _saved, _error;
        private int _revision;
        protected readonly Dictionary<object, bool> open = new Dictionary<object, bool>();

        protected WardrobeJsonForm(string path) : base(path) => Revert();

        /// <summary>Keys written even when empty.</summary>
        protected virtual ICollection<string> Always => new[] { "id", "version", "name", "author" };

        protected abstract void DrawJson(JsonObject j);

        public override bool Dirty => json != null && WardrobeJson.Write(json) != _saved;

        public override void Revert() {
            _error = null;
            _revision = WardrobeConfigs.Revision;
            open.Clear();
            try {
                json = WardrobeJson.Parse(File.ReadAllText(path));
                _saved = WardrobeJson.Write(json);
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
            DrawJson(json);
        }

        // ---------------------------------------------------------------- fields

        /// <summary>Sets a key (dropping it when it's back to its default, unless it's one that's always written).</summary>
        protected void Set(JsonObject o, string key, object value, bool isDefault) {
            if (isDefault && (o != json || !Always.Contains(key))) o.Remove(key);
            else o[key] = value;
        }

        protected void Text(JsonObject o, string key, string label, string tooltip = null) {
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(new GUIContent(label, tooltip), o.String(key));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, value.Length == 0);
        }

        protected void TextArea(JsonObject o, string key, string label, string tooltip = null) {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));
            string value = EditorGUILayout.TextArea(o.String(key), EditorStyles.textArea, GUILayout.MinHeight(36));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, value.Length == 0);
        }

        protected void Toggle(JsonObject o, string key, string label, string tooltip = null) {
            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), o.Bool(key));
            if (EditorGUI.EndChangeCheck()) Set(o, key, value, !value);
        }

        protected void Slider(JsonObject o, string key, string label, double fallback, float min, float max, string tooltip = null) {
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.Slider(new GUIContent(label, tooltip), (float)o.Number(key, fallback), min, max);
            if (EditorGUI.EndChangeCheck()) Set(o, key, Math.Round(value, 3), Math.Abs(Math.Round(value, 3) - fallback) < 1e-6);
        }

        protected void Integer(JsonObject o, string key, string label, int fallback, string tooltip = null) {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), o.Int(key, fallback));
            if (EditorGUI.EndChangeCheck()) Set(o, key, (double)value, value == fallback);
        }

        /// <summary>A comma-separated list of strings.</summary>
        protected void Words(JsonObject o, string key, string label, string tooltip = null) {
            var list = (o.Array(key) ?? new List<object>()).OfType<string>().ToList();
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.DelayedTextField(new GUIContent(label, (tooltip ?? "") + " (comma separated)"), string.Join(", ", list));
            if (EditorGUI.EndChangeCheck()) {
                var words = value.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().Cast<object>().ToList();
                Set(o, key, words, words.Count == 0);
            }
        }

        /// <summary>An asset the file names by path (relative to it, or "path#sub-asset" for meshes in a model).</summary>
        protected T Asset<T>(JsonObject o, string key, string label, string tooltip = null) where T : Object {
            string relative = o.String(key);
            var asset = WardrobeConfigs.Load<T>(path, relative);
            EditorGUI.BeginChangeCheck();
            var picked = (T)EditorGUILayout.ObjectField(new GUIContent(label, tooltip), asset, typeof(T), false);
            if (EditorGUI.EndChangeCheck()) Set(o, key, picked == null ? "" : WardrobeConfigs.Relative(path, picked), picked == null);
            if (relative.Length > 0 && asset == null) EditorGUILayout.HelpBox($"Can't find '{relative}'.", MessageType.Warning);
            return picked;
        }

        protected Texture2D Texture(JsonObject o, string key, string label, string tooltip = null) => Asset<Texture2D>(o, key, label, tooltip);

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
                        v => Set(t, "garment", v, false));
                    if (EditorGUI.EndChangeCheck()) Set(t, "garment", garment, false);
                    EditorGUI.BeginChangeCheck();
                    string avatar = Suggest(new GUIContent("Avatar Id", "Only the garment's fit for this avatar base (.ravt id); empty = every fit"),
                        t.String("avatar"), AvatarIds(target.garment), v => Set(t, "avatar", v, v.Length == 0));
                    if (EditorGUI.EndChangeCheck()) Set(t, "avatar", avatar, avatar.Length == 0);
                    Text(t, "version", "Version", "Which of the garment's versions: \"1\" = 1.x.x, \"1.2\" = 1.2.x, empty = any");
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

        private bool _onlySome;

        /// <summary>
        /// The "garments" list as a checklist: every garment (no list), or only the ticked ones (one entry per garment id,
        /// any avatar, its major version). The entries' details (versions, avatars, maps per garment) are in a foldout.
        /// </summary>
        protected void Checklist(JsonObject j, string every, string only, Action<JsonObject> extra = null, bool detailsOpen = false,
            Action<JsonObject> added = null) {
            var list = j.Array("garments") ?? new List<object>();
            var entries = list.OfType<JsonObject>().ToList();
            bool some = entries.Count > 0 || _onlySome;
            EditorGUI.BeginChangeCheck();
            int picked = GUILayout.Toolbar(some ? 1 : 0, new[] { "Every garment", "Only some garments" });
            if (EditorGUI.EndChangeCheck()) {
                if (picked == 0 && entries.Count > 0 &&
                    !EditorUtility.DisplayDialog("Ros' Wardrobe", "Forget the garments ticked (and their details)?", "Every Garment", "Cancel")) GUIUtility.ExitGUI();
                if (picked == 0) j.Remove("garments");
                _onlySome = picked == 1;
                GUIUtility.ExitGUI();
            }
            if (!some) {
                Note(every);
                return;
            }
            Note(only);
            var garments = WardrobeGarments.All.Where(g => !string.IsNullOrWhiteSpace(g.id)).GroupBy(g => g.id.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => Title(x.First()), StringComparer.OrdinalIgnoreCase).ToList();
            string pack = Pack(path);

            void Tick(string id, string version) {
                var t = new JsonObject();
                t["garment"] = id;
                t["version"] = WardrobeConfigs.Major(version);
                added?.Invoke(t);
                list.Add(t);
                Set(j, "garments", list, false);
            }
            void Untick(string id) {
                var mine = list.OfType<JsonObject>().Where(e => WardrobeConfigs.SameId(e.String("garment"), id)).ToList();
                bool maps = mine.Any(e => e.Keys.Any(k => k != "garment" && k != "version" && k != "avatar"));
                if (maps && !EditorUtility.DisplayDialog("Ros' Wardrobe", $"Forget {id}'s maps here too?", "Untick", "Cancel")) return;
                foreach (var e in mine) list.Remove(e);
                Set(j, "garments", list, false);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                foreach (var x in garments) {
                    bool on = entries.Any(e => WardrobeConfigs.SameId(e.String("garment"), x.Key));
                    string avatars = string.Join(", ", x.Select(g => AvatarName(g.avatarId)).Distinct());
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUI.BeginChangeCheck();
                        bool now = EditorGUILayout.ToggleLeft(new GUIContent(Title(x.First()), x.Key), on, GUILayout.MinWidth(120));
                        if (EditorGUI.EndChangeCheck()) {
                            if (now) Tick(x.Key, x.First().version);
                            else Untick(x.Key);
                            GUIUtility.ExitGUI();
                        }
                        EditorGUILayout.LabelField($"on {avatars}", EditorStyles.miniLabel);
                    }
                }
                foreach (string id in entries.Select(e => e.String("garment").Trim()).Where(id => id.Length > 0 && garments.All(x => !WardrobeConfigs.SameId(x.Key, id)))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                    using (new EditorGUILayout.HorizontalScope()) {
                        if (!EditorGUILayout.ToggleLeft(new GUIContent(id, "Not in this project"), true, GUILayout.MinWidth(120))) {
                            Untick(id);
                            GUIUtility.ExitGUI();
                        }
                        EditorGUILayout.LabelField("not in this project (fine when it ships separately)", EditorStyles.miniLabel);
                    }
                if (garments.Count == 0) Note("There are no garments in the project yet.");
                using (new EditorGUILayout.HorizontalScope()) {
                    if (GUILayout.Button("Tick All", EditorStyles.miniButtonLeft, GUILayout.Width(70))) {
                        foreach (var x in garments.Where(x => entries.All(e => !WardrobeConfigs.SameId(e.String("garment"), x.Key)))) Tick(x.Key, x.First().version);
                        GUIUtility.ExitGUI();
                    }
                    if (pack != null && GUILayout.Button(new GUIContent("Tick This Pack's", $"The garments under {pack}"), EditorStyles.miniButtonMid, GUILayout.Width(110))) {
                        foreach (var x in garments.Where(x => x.Any(g => (WardrobeGarments.PathOf(g) ?? "").StartsWith(pack + "/")) &&
                                                              entries.All(e => !WardrobeConfigs.SameId(e.String("garment"), x.Key))))
                            Tick(x.Key, x.First().version);
                        GUIUtility.ExitGUI();
                    }
                    if (GUILayout.Button("Untick All", EditorStyles.miniButtonRight, GUILayout.Width(80))) {
                        list.Clear();
                        Set(j, "garments", list, false);
                        _onlySome = true;
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (entries.Count == 0)
                EditorGUILayout.HelpBox("Tick at least one: with none ticked it's for every garment.", MessageType.Info);
            if (entries.Count == 0) return;
            if (!open.TryGetValue(j, out bool details)) details = detailsOpen;
            open[j] = details = EditorGUILayout.Foldout(details, extra != null ? "Per garment: maps, versions and avatars" : "Per garment: versions and avatars", true);
            if (details) Targets(j, "", extra, added);
        }

        /// <summary>The pack a file is in: its first two folders (Assets/&lt;Pack&gt;), or null.</summary>
        internal static string Pack(string file) {
            var parts = (file ?? "").Split('/');
            return parts.Length > 2 ? parts[0] + "/" + parts[1] : null;
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
    }
}
