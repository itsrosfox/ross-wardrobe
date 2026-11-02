using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Project Settings → Ros' Wardrobe: where creators make and edit the project's garments (.rgarm), patterns (.rpat),
    /// presets (.rpre), avatars (.ravt) and material types (.rmat), and update those files after a Ros' Wardrobe update
    /// changes their layout (<see cref="WardrobeMigrations"/>).
    /// </summary>
    internal class WardrobeProjectSettings : SettingsProvider {
        public const string SettingsPath = "Project/Ros' Wardrobe";
        private const float ListWidth = 240;

        private enum Tab { Garments, Patterns, Presets, Avatars, Materials, Updates }
        private static readonly string[] TabNames = { "Garments", "Patterns", "Presets", "Avatars", "Materials", "Updates" };
        private static readonly string[] Extensions = {
            WardrobeConfigs.Garment, WardrobeConfigs.Pattern, WardrobeConfigs.Preset, WardrobeConfigs.Avatar, WardrobeConfigs.Material,
        };
        private static readonly WardrobeMigrations.Kind[] Kinds = {
            WardrobeMigrations.Kind.Garment, WardrobeMigrations.Kind.Pattern, WardrobeMigrations.Kind.Preset, WardrobeMigrations.Kind.Avatar,
            WardrobeMigrations.Kind.Material,
        };

        private static Tab _tab;
        private static string _select;   // a file to select next time the page draws

        private class Entry {
            public string path, title, subtitle, group;
        }

        private Dictionary<Tab, List<Entry>> _lists;
        private int _listRevision = -1;
        private List<WardrobeMigrations.FileStatus> _files;
        private List<WardrobeLegacy.OldFile> _legacy;
        private WardrobeMigrations.Result _lastMigration;

        private string _path, _filter = "";
        private WardrobeForm _form;
        private NewConfig _new;
        private Vector2 _listScroll, _detailScroll;
        private bool _showUpToDate;

        private WardrobeProjectSettings() : base(SettingsPath, SettingsScope.Project,
            new[] { "wardrobe", "garment", "pattern", "preset", "avatar", "material", "migrate", "update", "recolour", "recolor" }) { }

        [SettingsProvider]
        private static SettingsProvider Create() => new WardrobeProjectSettings();

        /// <summary>Opens the page, on a Ros' Wardrobe file if given.</summary>
        public static void Open(string path = null) {
            if (path != null && TabOf(path) is Tab tab) {
                _tab = tab;
                _select = path;
            }
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        public static void OpenUpdates() {
            _tab = Tab.Updates;
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        [MenuItem("Assets/Ros' Wardrobe/Edit in Project Settings", false, 2000)]
        private static void EditSelected() => Open(AssetDatabase.GetAssetPath(Selection.activeObject));

        [MenuItem("Assets/Ros' Wardrobe/Edit in Project Settings", true)]
        private static bool CanEditSelected() => Selection.activeObject != null && TabOf(AssetDatabase.GetAssetPath(Selection.activeObject)) != null;

        private static Tab? TabOf(string path) {
            int i = Array.IndexOf(Extensions, Path.GetExtension(path ?? "").ToLowerInvariant());
            return i < 0 ? (Tab?)null : (Tab)i;
        }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement) {
            EditorApplication.projectChanged += Reload;
            Reload();
        }

        public override void OnDeactivate() {
            EditorApplication.projectChanged -= Reload;
            if (_form != null && _form.Dirty &&
                EditorUtility.DisplayDialog("Ros' Wardrobe", $"Save your changes to {_form.Name}?", "Save", "Discard"))
                _form.Save();
            _form?.Dispose();
            _form = null;
        }

        private void Reload() {
            _lists = null;
            _files = null;
            _legacy = null;
            Repaint();
        }

        // ---------------------------------------------------------------- page

        public override void OnGUI(string searchContext) {
            if (_lists == null || _listRevision != WardrobeConfigs.Revision) {
                _lists = ReadLists();
                _listRevision = WardrobeConfigs.Revision;
            }
            if (_files == null) _files = WardrobeMigrations.Scan();
            if (_legacy == null) _legacy = WardrobeLegacy.Scan();
            if (_select != null) {
                Select(_select);
                _select = null;
            }

            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope()) {
                    int pending = _files.Count(f => f.state != WardrobeMigrations.State.UpToDate) + _legacy.Count;
                    var names = TabNames.Select((n, i) => i == (int)Tab.Updates && pending > 0 ? $"{n} ({pending})" : n).ToArray();
                    EditorGUI.BeginChangeCheck();
                    var tab = (Tab)GUILayout.Toolbar((int)_tab, names, GUILayout.Height(24));
                    if (EditorGUI.EndChangeCheck() && tab != _tab && (_form == null || _form.ConfirmLeave())) {
                        _tab = tab;
                        Deselect();
                    }
                    EditorGUILayout.Space(6);
                    float labelWidth = EditorGUIUtility.labelWidth;
                    try {
                        if (_tab == Tab.Updates) DrawUpdates();
                        else DrawFiles(_tab);
                    } finally {
                        EditorGUIUtility.labelWidth = labelWidth;
                    }
                }
                GUILayout.Space(6);
            }
        }

        // Every file of each kind, with what the lists show.
        private static Dictionary<Tab, List<Entry>> ReadLists() {
            var lists = new Dictionary<Tab, List<Entry>>();
            var groups = WardrobeSettings.Current.patternGroups ?? new string[0];
            for (int i = 0; i < Extensions.Length; i++) {
                var tab = (Tab)i;
                var list = new List<Entry>();
                foreach (string path in WardrobeConfigs.Paths(Extensions[i])) {
                    var e = new Entry { path = path, title = Path.GetFileNameWithoutExtension(path), subtitle = "", group = "" };
                    try {
                        var j = WardrobeJson.Parse(File.ReadAllText(path));
                        string name = j.String("name").Trim(), id = j.String("id").Trim(), version = j.String("version").Trim();
                        if (name.Length > 0) e.title = name;
                        var bits = new List<string> { id };
                        switch (tab) {
                            case Tab.Garments:
                                if (version.Length > 0) bits.Add("v" + version);
                                e.group = "On " + WardrobeForm.AvatarName(j.Object("avatar")?.String("id"));
                                break;
                            case Tab.Patterns:
                                string type = j.String("type", "simple");
                                if (type != "simple" && type.Length > 0) bits.Add(type);
                                e.group = j.String("group");
                                break;
                            case Tab.Presets:
                                e.group = j.String("group");
                                if (j.Array("colourways")?.Count > 0) bits.Add($"{j.Array("colourways").Count + 1} colourways");
                                break;
                            case Tab.Materials:
                                string shader = j.String("shader");
                                if (shader.Length == 0) shader = j.String("template");
                                if (shader.Length > 0) bits.Add(Path.GetFileNameWithoutExtension(shader));
                                break;
                        }
                        e.subtitle = string.Join(" · ", bits.Where(b => !string.IsNullOrWhiteSpace(b)));
                    } catch (Exception) {
                        e.subtitle = "can't be read";
                    }
                    list.Add(e);
                }
                IEnumerable<Entry> ordered = list.OrderBy(e => e.title, StringComparer.OrdinalIgnoreCase);
                if (tab == Tab.Patterns)
                    ordered = list.OrderBy(e => { int k = Array.IndexOf(groups, e.group); return k < 0 ? groups.Length : k; })
                        .ThenBy(e => e.group, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.title, StringComparer.OrdinalIgnoreCase);
                else if (tab == Tab.Garments || tab == Tab.Presets)
                    ordered = ordered.OrderBy(e => e.group, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.title, StringComparer.OrdinalIgnoreCase);
                lists[tab] = ordered.ToList();
            }
            return lists;
        }

        private void Select(string path) {
            if (_form != null && _form.path != path && !_form.ConfirmLeave()) return;
            if (_form != null && _form.path != path) {
                _form.Dispose();
                _form = null;
            }
            if (TabOf(path) is Tab tab) _tab = tab;
            _path = path;
            _new = null;
            _detailScroll = Vector2.zero;
            GUI.FocusControl(null);
        }

        private void Deselect() {
            _form?.Dispose();
            _form = null;
            _path = null;
            _new = null;
            _detailScroll = Vector2.zero;
        }

        // ---------------------------------------------------------------- files

        // The kind's files on the left, the picked one (or a new one) on the right.
        private void DrawFiles(Tab tab) {
            var kind = Kinds[(int)tab];
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth))) {
                    if (GUILayout.Button($"New {Singular(tab)}…") && (_form == null || _form.ConfirmLeave())) {
                        Deselect();
                        _new = new NewConfig(kind, DefaultFolder(tab));
                    }
                    EditorGUILayout.Space(2);
                    _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);
                    _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
                    var list = _lists[tab];
                    if (list.Count == 0) EditorGUILayout.LabelField($"No {TabNames[(int)tab].ToLowerInvariant()} in the project yet.", EditorStyles.wordWrappedMiniLabel);
                    string group = null;
                    foreach (var e in list) {
                        if (!Matches(e.title, e.subtitle, e.group)) continue;
                        if (e.group != group && !(group == null && e.group.Length == 0 && tab != Tab.Patterns && tab != Tab.Presets)) {
                            group = e.group;
                            EditorGUILayout.LabelField(group.Length == 0 ? "No group" : group, EditorStyles.miniBoldLabel);
                        }
                        if (Item(_new == null && e.path == _path, e.title, e.subtitle, Badge(e.path)) && e.path != _path) Select(e.path);
                    }
                    EditorGUILayout.EndScrollView();
                }
                var line = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(line, new Color(0, 0, 0, 0.25f));
                GUILayout.Space(6);
                using (new EditorGUILayout.VerticalScope()) {
                    _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                    EditorGUIUtility.labelWidth = 150;
                    DrawDetail(tab);
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawDetail(Tab tab) {
            if (_new != null) {
                string created = _new.Draw(() => _new = null);
                if (created != null) {
                    _new = null;
                    WardrobeConfigs.Changed();
                    _lists = null;
                    _select = created;
                    GUIUtility.ExitGUI();
                }
                return;
            }
            if (_path == null || !File.Exists(_path)) {
                EditorGUILayout.HelpBox(Intro(tab), MessageType.Info);
                return;
            }
            if (_form == null || _form.path != _path) {
                _form?.Dispose();
                _form = WardrobeForm.Open(_path);
                if (_form == null) return;
                _form.repaint = Repaint;
            }
            _form.DrawToolbar(true);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField(_path, EditorStyles.miniLabel);
                if (GUILayout.Button("Show File", EditorStyles.miniButton, GUILayout.Width(70))) {
                    var asset = AssetDatabase.LoadMainAssetAtPath(_path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }
            EditorGUILayout.Space();
            _form.Draw();
        }

        private static string Intro(Tab tab) {
            switch (tab) {
                case Tab.Garments:
                    return "Pick a garment on the left to edit it, or make a new one. A .rgarm is one garment fitted to one avatar base: its prefab, " +
                           "maps and regions. Fits for other avatars are their own .rgarm with the same id.";
                case Tab.Patterns:
                    return "Pick a pattern on the left to edit it, or make a new one. A .rpat names the garments it's for by id, so anyone can make " +
                           "patterns for any garment; put it next to its maps.";
                case Tab.Presets:
                    return "Pick a preset on the left to edit it, or make a new one: a look (patterns and colours per part) and its colourways. " +
                           "Save as Preset… in the Ros' Wardrobe window writes one from a look you made.";
                case Tab.Avatars:
                    return "Pick an avatar base on the left to edit it, or make a new one. A .ravt tells Ros' Wardrobe which avatars in a scene are " +
                           "that base, so the window shows the garments fitted to it. Ship it with the garments made for the base.";
                default:
                    return "Pick a material type on the left to edit it, or make a new one. A .rmat names a shader (or a template material) and which " +
                           "of a garment's maps go into which texture property, channel by channel. Ros' Wardrobe ships PBR.";
            }
        }

        private static string Singular(Tab tab) => tab == Tab.Materials ? "Material Type" : TabNames[(int)tab].TrimEnd('s');

        private bool Matches(params string[] fields) =>
            string.IsNullOrWhiteSpace(_filter) || fields.Any(f => (f ?? "").IndexOf(_filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool Item(bool selected, string title, string subtitle, string badge = null) {
            var rect = GUILayoutUtility.GetRect(10, 34, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) {
                if (selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.45f));
                else if (rect.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.12f));
                GUI.Label(new Rect(rect.x + 6, rect.y + 2, rect.width - 12, 16), title, EditorStyles.label);
                GUI.Label(new Rect(rect.x + 6, rect.y + 17, rect.width - 12, 14), subtitle, EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(badge)) {
                    var size = EditorStyles.miniLabel.CalcSize(new GUIContent(badge));
                    var r = new Rect(rect.xMax - size.x - 10, rect.y + 3, size.x + 6, 14);
                    EditorGUI.DrawRect(r, new Color(0.85f, 0.55f, 0.1f, 0.8f));
                    GUI.Label(new Rect(r.x + 3, r.y, size.x, 14), badge, EditorStyles.miniLabel);
                }
            }
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private string Badge(string path) {
            var f = _files?.FirstOrDefault(x => x.path == path);
            return f == null ? null : f.state == WardrobeMigrations.State.Outdated ? "old"
                : f.state == WardrobeMigrations.State.Newer ? "newer" : f.state == WardrobeMigrations.State.Broken ? "broken" : null;
        }

        // Where new files go: the selected folder, else the folder most of the kind's files are in, else Assets.
        private static string DefaultFolder(Tab tab) {
            var selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(selected) && selected.StartsWith("Assets")) return selected;
            var paths = WardrobeConfigs.Paths(Extensions[(int)tab]).Where(p => p.StartsWith("Assets/"));
            // patterns and garments are in folders of their own, in a folder of them
            bool own = tab == Tab.Patterns || tab == Tab.Garments;
            return paths.Select(p => Path.GetDirectoryName(own ? Path.GetDirectoryName(p) : p)?.Replace('\\', '/'))
                .Where(p => !string.IsNullOrEmpty(p)).GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "Assets";
        }

        // ---------------------------------------------------------------- new files

        private class NewConfig {
            private static readonly string[] PatternTypes = { "simple", "hybrid", "complete" };
            private static readonly string[] PatternTypeNames = {
                "Simple: tiling maps for any garment (stripes, checks, weaves)",
                "Hybrid: a colour layout per garment (raglan, colour blocks)",
                "Complete: a picture per garment (prints, graphics)",
            };

            private readonly WardrobeMigrations.Kind _kind;
            private string _name = "", _id = "", _author = "", _group = "", _category = "Tops", _slot = "Top", _error;
            private bool _idEdited;
            private int _type, _avatar, _material;
            private Object _folder;
            private GameObject _prefab;
            private Mesh _mesh;
            private Texture2D _baseColor, _normal;
            private Shader _shader;
            private Material _template;
            private string _pattern;
            private readonly HashSet<WardrobeGarment> _for = new HashSet<WardrobeGarment>();

            public NewConfig(WardrobeMigrations.Kind kind, string folder) {
                _kind = kind;
                _folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
                var like = WardrobeGarments.All.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.author));
                _author = like?.author ?? "";
                if (kind == WardrobeMigrations.Kind.Garment && like != null) _avatar = WardrobeAvatars.All.FindIndex(a => WardrobeConfigs.SameId(a.id, like.avatarId)) + 1;
                // a pack's patterns and presets are usually for its garments: the ones in the folder they go next to
                string pack = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                if (kind == WardrobeMigrations.Kind.Pattern || kind == WardrobeMigrations.Kind.Preset)
                    foreach (var g in WardrobeGarments.All)
                        if (pack != null && pack.Length > "Assets".Length && (WardrobeGarments.PathOf(g) ?? "").StartsWith(pack + "/")) _for.Add(g);
                _pattern = WardrobePatterns.Get(null)?.id ?? "";
            }

            private string What => _kind == WardrobeMigrations.Kind.Material ? "material type" : _kind.ToString().ToLowerInvariant();

            /// <summary>Draws the form; returns the new file's path once it's made.</summary>
            public string Draw(Action cancel) {
                EditorGUILayout.LabelField($"New {char.ToUpper(What[0])}{What.Substring(1)}", EditorStyles.largeLabel);
                EditorGUILayout.LabelField("You can change all of it later.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                _name = EditorGUILayout.TextField("Name", _name);
                _author = EditorGUILayout.TextField("Author", _author);
                if (EditorGUI.EndChangeCheck() && !_idEdited) _id = WardrobeForm.SuggestId(Prefix(), _name);
                EditorGUI.BeginChangeCheck();
                _id = EditorGUILayout.TextField(new GUIContent("Id", "Stable id other files refer to it by; don't change it once shipped"), _id);
                if (EditorGUI.EndChangeCheck()) _idEdited = true;

                switch (_kind) {
                    case WardrobeMigrations.Kind.Garment: DrawGarment(); break;
                    case WardrobeMigrations.Kind.Pattern: DrawPattern(); break;
                    case WardrobeMigrations.Kind.Preset: DrawPreset(); break;
                    case WardrobeMigrations.Kind.Avatar: DrawAvatar(); break;
                    case WardrobeMigrations.Kind.Material: DrawMaterial(); break;
                }
                _folder = EditorGUILayout.ObjectField(new GUIContent("Create In", "A folder"), _folder, typeof(DefaultAsset), false);

                string path = PathFor();
                string problem = _name.Trim().Length == 0 ? "Give it a name." : _id.Trim().Length == 0 ? "Give it an id."
                    : path == null ? "Pick a folder in Assets to create it in."
                    : File.Exists(path) ? $"{path} already exists."
                    : TakenBy() is string other ? $"{other} already has the id {_id.Trim()}."
                    : _kind == WardrobeMigrations.Kind.Pattern && _type != 0 && _for.Count == 0 ? "A hybrid or complete pattern needs at least one garment."
                    : _kind == WardrobeMigrations.Kind.Material && _shader == null && _template == null ? "Pick a shader or a template material."
                    : null;
                EditorGUILayout.Space();
                if (path != null) EditorGUILayout.LabelField(" ", $"→ {path}", EditorStyles.miniLabel);
                if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.None);
                if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);

                string created = null;
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cancel", GUILayout.Width(80))) cancel();
                    using (new EditorGUI.DisabledScope(problem != null))
                        if (GUILayout.Button("Create", GUILayout.Width(100)))
                            try {
                                created = Create(path);
                            } catch (Exception e) {
                                _error = e.Message;
                                Debug.LogException(e);
                            }
                }
                return created;
            }

            private void DrawGarment() {
                _category = EditorGUILayout.TextField(new GUIContent("Category", "Browser category: Tops, Bottoms, Shoes, Accessories…"), _category);
                _slot = EditorGUILayout.TextField(new GUIContent("Slot", "Putting it on takes off what's worn in the same slot; empty = never"), _slot);
                var avatars = WardrobeAvatars.All;
                _avatar = EditorGUILayout.Popup(new GUIContent("Made For", "The avatar base (.ravt) this fit is for"), _avatar,
                    new[] { "Any avatar" }.Concat(avatars.Select(a => a.DisplayName.Replace("/", "∕"))).ToArray());
                if (avatars.Count == 0)
                    EditorGUILayout.HelpBox("No avatar base (.ravt) in the project yet: make one in the Avatars tab so the window knows which avatars it fits.",
                        MessageType.None);
                var types = WardrobeMaterialTypes.All;
                _material = EditorGUILayout.Popup(new GUIContent("Starts In", "The material type it starts in"), _material,
                    new[] { "The first that fits" }.Concat(types.Select(t => t.DisplayName)).ToArray());
                EditorGUILayout.Space();
                EditorGUI.BeginChangeCheck();
                _prefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Prefab", "What the window puts on the avatar"), _prefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck() && _prefab != null) FillFromPrefab();
                _mesh = (Mesh)EditorGUILayout.ObjectField(new GUIContent("Mesh", "The ID and gradient maps are made from its UVs"), _mesh, typeof(Mesh), false);
                _baseColor = (Texture2D)EditorGUILayout.ObjectField("Base Colour", _baseColor, typeof(Texture2D), false);
                _normal = (Texture2D)EditorGUILayout.ObjectField("Normal", _normal, typeof(Texture2D), false);
            }

            private void FillFromPrefab() {
                var renderer = _prefab.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r is SkinnedMeshRenderer || r is MeshRenderer);
                if (renderer != null) {
                    if (_mesh == null) _mesh = renderer is SkinnedMeshRenderer s ? s.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    var material = renderer.sharedMaterial;
                    if (_baseColor == null && material != null && material.HasProperty("_MainTex")) _baseColor = material.mainTexture as Texture2D;
                    if (_normal == null && material != null && material.HasProperty("_BumpMap")) _normal = material.GetTexture("_BumpMap") as Texture2D;
                }
                if (_name.Trim().Length == 0) {
                    _name = _prefab.name;
                    if (!_idEdited) _id = WardrobeForm.SuggestId(Prefix(), _name);
                }
                // next to the prefab
                _folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(_prefab)).Replace('\\', '/'));
            }

            private void DrawPattern() {
                _type = EditorGUILayout.Popup("Type", _type, PatternTypeNames);
                Group(WardrobePatterns.All.Select(p => p.group).Concat(WardrobeSettings.Current.patternGroups ?? new string[0]));
                DrawFor(_type == 0 ? "For (none = every garment)" : "For");
            }

            private void DrawPreset() {
                Group(WardrobePresets.All.Select(p => p.group));
                var patterns = WardrobePatterns.All;
                int index = Mathf.Max(0, patterns.FindIndex(p => p.id == _pattern));
                index = EditorGUILayout.Popup(new GUIContent("Pattern", "What every part starts in"), index,
                    patterns.Select(p => $"{(string.IsNullOrWhiteSpace(p.group) ? "" : p.group + "/")}{p.DisplayName}").ToArray());
                if (index < patterns.Count) _pattern = patterns[index].id;
                DrawFor("For (none = every garment)");
            }

            private void Group(IEnumerable<string> groups) {
                using (new EditorGUILayout.HorizontalScope()) {
                    _group = EditorGUILayout.TextField(new GUIContent("Group", "Where the picker lists it"), _group);
                    if (GUILayout.Button(EditorGUIUtility.IconContent("icon dropdown"), EditorStyles.label, GUILayout.Width(18))) {
                        var menu = new GenericMenu();
                        foreach (string g in groups.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct()) {
                            string picked = g;
                            menu.AddItem(new GUIContent(g), _group == g, () => _group = picked);
                        }
                        menu.ShowAsContext();
                    }
                }
            }

            private void DrawFor(string label) {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                var garments = WardrobeGarments.All.Where(g => !string.IsNullOrWhiteSpace(g.id)).OrderBy(WardrobeForm.Title).ToList();
                if (garments.Count == 0) EditorGUILayout.LabelField("No garments in the project; add them later.", EditorStyles.miniLabel);
                foreach (var g in garments) {
                    bool on = EditorGUILayout.ToggleLeft($"{WardrobeForm.Title(g)} on {WardrobeForm.AvatarName(g.avatarId)}  ({g.id})", _for.Contains(g));
                    if (on) _for.Add(g);
                    else _for.Remove(g);
                }
            }

            private void DrawAvatar() {
                EditorGUI.BeginChangeCheck();
                _prefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Model or Prefab", "The base's own model (.fbx) or prefab, to recognise it by"),
                    _prefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck() && _prefab != null && _name.Trim().Length == 0) {
                    _name = _prefab.name;
                    if (!_idEdited) _id = WardrobeForm.SuggestId(Prefix(), _name);
                }
            }

            private void DrawMaterial() {
                _template = (Material)EditorGUILayout.ObjectField(new GUIContent("Template", "A material to copy settings from"), _template, typeof(Material), false);
                _shader = (Shader)EditorGUILayout.ObjectField(new GUIContent("Shader", "Used instead of the template's"), _shader, typeof(Shader), false);
                EditorGUILayout.LabelField(" ", "Albedo and normal properties are filled in where the shader has the usual ones.", EditorStyles.wordWrappedMiniLabel);
            }

            // the prefix the project's ids already use ("rosden." from rosden.ros-t-shirt), else the author's
            private string Prefix() {
                string id = WardrobeGarments.All.Select(g => g.id ?? "").FirstOrDefault(i => i.Contains('.'));
                return id != null ? id.Substring(0, id.IndexOf('.')) : _author;
            }

            private string PathFor() {
                string parent = _folder != null ? AssetDatabase.GetAssetPath(_folder) : "";
                if (!AssetDatabase.IsValidFolder(parent) || !parent.StartsWith("Assets") || _name.Trim().Length == 0) return null;
                string file = WardrobeAssets.FileName(_name.Trim());
                switch (_kind) {
                    case WardrobeMigrations.Kind.Garment: return $"{parent}/{file}{WardrobeConfigs.Garment}";
                    case WardrobeMigrations.Kind.Pattern: return $"{parent}/{file}/{file}{WardrobeConfigs.Pattern}";   // with its maps
                    case WardrobeMigrations.Kind.Preset: return $"{parent}/{file}{WardrobeConfigs.Preset}";
                    case WardrobeMigrations.Kind.Avatar: return $"{parent}/{file}{WardrobeConfigs.Avatar}";
                    default: return $"{parent}/{file}{WardrobeConfigs.Material}";
                }
            }

            private string TakenBy() {
                string id = _id.Trim();
                switch (_kind) {
                    case WardrobeMigrations.Kind.Garment:
                        string avatar = _avatar > 0 && _avatar <= WardrobeAvatars.All.Count ? WardrobeAvatars.All[_avatar - 1].id : "";
                        var g = WardrobeGarments.All.FirstOrDefault(x => WardrobeConfigs.SameId(x.id, id) && WardrobeConfigs.SameId(x.avatarId, avatar));
                        return g != null ? WardrobeGarments.PathOf(g) : null;
                    case WardrobeMigrations.Kind.Pattern: return WardrobePatterns.All.FirstOrDefault(p => WardrobeConfigs.SameId(p.id, id))?.jsonPath;
                    case WardrobeMigrations.Kind.Preset: return WardrobePresets.All.FirstOrDefault(p => WardrobeConfigs.SameId(p.id, id))?.jsonPath;
                    case WardrobeMigrations.Kind.Avatar: return WardrobeAvatars.Get(id)?.path;
                    default: return WardrobeMaterialTypes.Get(id)?.path;
                }
            }

            private List<object> Targets(bool avatars) =>
                _for.OrderBy(WardrobeForm.Title).Select(g => {
                    var o = new JsonObject();
                    o["garment"] = g.id;
                    if (avatars && !string.IsNullOrWhiteSpace(g.avatarId)) o["avatar"] = g.avatarId;
                    o["version"] = WardrobeConfigs.Major(g.version);
                    return (object)o;
                }).ToList();

            private string Create(string path) {
                WardrobeAssets.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                if (_kind == WardrobeMigrations.Kind.Garment) return CreateGarment(path);
                var j = new JsonObject();
                j["format"] = new JsonNumber(WardrobeMigrations.Current(_kind));
                j["id"] = _id.Trim();
                switch (_kind) {
                    case WardrobeMigrations.Kind.Pattern:
                        j["version"] = "1.0.0";
                        j["name"] = _name.Trim();
                        if (_type != 0) j["type"] = PatternTypes[_type];
                        j["group"] = _group.Trim();
                        j["description"] = "";
                        j["author"] = _author.Trim();
                        if (_type == 0) j["tileSize"] = new List<object> { new JsonNumber(10), new JsonNumber(10) };
                        var slot = new JsonObject();
                        slot["name"] = "Colour";
                        slot["color"] = "#F4F4F2";
                        slot["canGradient"] = true;
                        j["colors"] = new List<object> { slot };
                        // maps on a garment's UVs are for its fit on one avatar
                        if (_for.Count > 0) j["garments"] = Targets(_type != 0);
                        break;
                    case WardrobeMigrations.Kind.Preset:
                        j["version"] = "1.0.0";
                        j["name"] = _name.Trim();
                        j["group"] = _group.Trim();
                        j["description"] = "";
                        j["author"] = _author.Trim();
                        if (_for.Count > 0) j["garments"] = Targets(false);
                        var part = new JsonObject();
                        part["region"] = WardrobePreset.AnyRegion;
                        part["pattern"] = _pattern ?? "";
                        var pattern = WardrobePatterns.Get(_pattern);
                        part["colors"] = (pattern?.slots ?? new List<WardrobePattern.Slot>()).Select(s => (object)("#" + ColorUtility.ToHtmlStringRGB(s.color))).ToList();
                        part["scale"] = new JsonNumber(1);
                        part["rotate"] = false;
                        j["parts"] = new List<object> { part };
                        break;
                    case WardrobeMigrations.Kind.Avatar:
                        j["name"] = _name.Trim();
                        j["version"] = "";
                        j["author"] = _author.Trim();
                        j["description"] = "";
                        string model = _prefab != null ? AssetDatabase.GetAssetPath(_prefab) : "";
                        j["matches"] = model.Length > 0 ? new List<object> { Path.GetFileNameWithoutExtension(model) } : new List<object>();
                        j["guids"] = model.Length > 0 ? new List<object> { AssetDatabase.AssetPathToGUID(model) } : new List<object>();
                        break;
                    case WardrobeMigrations.Kind.Material:
                        j["name"] = _name.Trim();
                        j["version"] = "1.0.0";
                        j["author"] = _author.Trim();
                        j["description"] = "";
                        if (_shader != null) j["shader"] = _shader.name;
                        if (_template != null) j["template"] = WardrobeConfigs.Relative(path, _template);
                        var shader = _shader != null ? _shader : _template.shader;
                        var textures = new List<object>();
                        void Output(string property, string channels, string keyword = null) {
                            if (shader.FindPropertyIndex(property) < 0) return;
                            var o = new JsonObject();
                            o["property"] = property;
                            o["channels"] = channels;
                            if (keyword != null) o["keyword"] = keyword;
                            textures.Add(o);
                        }
                        Output(shader.FindPropertyIndex("_BaseMap") >= 0 ? "_BaseMap" : "_MainTex", "albedo");
                        Output("_BumpMap", "normal", shader.name.StartsWith("Standard") ? "_NORMALMAP" : null);
                        j["textures"] = textures;
                        break;
                }
                File.WriteAllText(path, WardrobeJson.Write(j));
                AssetDatabase.ImportAsset(path);
                return path;
            }

            private string CreateGarment(string path) {
                var g = ScriptableObject.CreateInstance<WardrobeGarment>();
                try {
                    g.id = _id.Trim();
                    g.displayName = _name.Trim();
                    g.author = _author.Trim();
                    g.category = (_category ?? "").Trim();
                    g.slot = (_slot ?? "").Trim();
                    var avatars = WardrobeAvatars.All;
                    g.avatarId = _avatar > 0 && _avatar <= avatars.Count ? avatars[_avatar - 1].id : "";
                    var types = WardrobeMaterialTypes.All;
                    g.defaultMaterial = _material > 0 && _material <= types.Count ? types[_material - 1].id : "";
                    g.prefab = _prefab;
                    g.mesh = _mesh;
                    g.baseColor = _baseColor;
                    g.normalMap = _normal;
                    return WardrobeGarments.Save(g, path);
                } finally {
                    Object.DestroyImmediate(g);
                }
            }
        }

        // ---------------------------------------------------------------- updates

        private void DrawUpdates() {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(WardrobeMigrations).Assembly);
            EditorGUILayout.LabelField($"Ros' Wardrobe {package?.version ?? ""}", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("When an update changes how Ros' Wardrobe's files are laid out, the files in your project keep working, and " +
                                       "you can update them here so they ship in the new layout. Updating copies the originals to Library/Ros' Wardrobe/Backups first.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            foreach (WardrobeMigrations.Kind kind in Enum.GetValues(typeof(WardrobeMigrations.Kind))) {
                var files = _files.Where(f => f.kind == kind).ToList();
                EditorGUILayout.LabelField(WardrobeMigrations.Label(kind), $"format {WardrobeMigrations.Current(kind)} · {files.Count} in the project");
            }
            EditorGUILayout.Space();

            var outdated = _files.Where(f => f.state == WardrobeMigrations.State.Outdated).ToList();
            var newer = _files.Where(f => f.state == WardrobeMigrations.State.Newer).ToList();
            var broken = _files.Where(f => f.state == WardrobeMigrations.State.Broken).ToList();
            var current = _files.Where(f => f.state == WardrobeMigrations.State.UpToDate).ToList();

            using (new EditorGUILayout.HorizontalScope()) {
                if (outdated.Count == 0 && newer.Count == 0 && broken.Count == 0 && _legacy.Count == 0)
                    EditorGUILayout.HelpBox($"All {_files.Count} files are up to date.", MessageType.Info);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Check Again", GUILayout.Width(100))) {
                    _files = null;
                    _legacy = null;
                    _lastMigration = null;
                    GUIUtility.ExitGUI();
                }
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            if (_lastMigration != null) {
                var r = _lastMigration;
                EditorGUILayout.HelpBox($"Updated {r.migrated} files." + (r.backups != null ? $" The originals are in {r.backups}." : "") +
                                        (r.notes.Count > 0 ? "\n\n" + string.Join("\n", r.notes) : ""), MessageType.Info);
                if (r.errors.Count > 0) EditorGUILayout.HelpBox("Couldn't update:\n" + string.Join("\n", r.errors), MessageType.Error);
                if (r.backups != null && Directory.Exists(r.backups) && GUILayout.Button("Show Backups", GUILayout.Width(120)))
                    EditorUtility.RevealInFinder(r.backups);
            }

            if (_legacy.Count > 0) {
                EditorGUILayout.LabelField($"From before config files ({_legacy.Count})", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("garment.json files and Wardrobe Garment assets become .rgarm files (with a .ravt for the avatar they're for and " +
                                           ".rmat files for their materials), pattern.json becomes .rpat and .preset.json becomes .rpre. Scenes and prefabs " +
                                           "wearing the garments are pointed at the new files, and the old ones are removed.", EditorStyles.wordWrappedMiniLabel);
                foreach (var f in _legacy) EditorGUILayout.LabelField(f.Shown, f.kind.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.Space();
                if (GUILayout.Button($"Convert {_legacy.Count} Files", GUILayout.Width(160), GUILayout.Height(26)) &&
                    EditorUtility.DisplayDialog("Ros' Wardrobe", $"Convert {_legacy.Count} files? The originals are copied to Library/Ros' Wardrobe/Backups first.",
                        "Convert", "Cancel")) {
                    Deselect();
                    _lastMigration = WardrobeLegacy.Convert(_legacy);
                    Reload();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.Space();
            }

            if (outdated.Count > 0) {
                EditorGUILayout.LabelField($"In an older layout ({outdated.Count})", EditorStyles.boldLabel);
                foreach (var f in outdated) {
                    EditorGUILayout.LabelField(f.path, $"format {f.format} → {WardrobeMigrations.Current(f.kind)}");
                    EditorGUI.indentLevel++;
                    foreach (string change in f.changes) EditorGUILayout.LabelField("• " + change, EditorStyles.wordWrappedMiniLabel);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.Space();
                if (GUILayout.Button($"Update {outdated.Count} Files", GUILayout.Width(160), GUILayout.Height(26)) &&
                    EditorUtility.DisplayDialog("Ros' Wardrobe", $"Rewrite {outdated.Count} files in the current layout? The originals are copied to " +
                                                                 "Library/Ros' Wardrobe/Backups first.", "Update", "Cancel")) {
                    if (_form != null && _form.Dirty) _form.Save();
                    Deselect();
                    _lastMigration = WardrobeMigrations.Migrate(outdated);
                    _files = null;
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.Space();
            }
            if (newer.Count > 0) {
                EditorGUILayout.HelpBox("Made with a newer Ros' Wardrobe. They're read as well as this version can; update Ros' Wardrobe through VCC.",
                    MessageType.Warning);
                foreach (var f in newer) EditorGUILayout.LabelField(f.path, $"format {f.format}");
                EditorGUILayout.Space();
            }
            if (broken.Count > 0) {
                EditorGUILayout.HelpBox("These files aren't valid json, so Ros' Wardrobe skips them.", MessageType.Error);
                foreach (var f in broken) EditorGUILayout.LabelField(new GUIContent(f.path, f.error), new GUIContent(f.error, f.error), EditorStyles.miniLabel);
                EditorGUILayout.Space();
            }
            _showUpToDate = EditorGUILayout.Foldout(_showUpToDate, $"Up to date ({current.Count})", true);
            if (_showUpToDate)
                foreach (var f in current) EditorGUILayout.LabelField(f.path, $"format {f.format}", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
