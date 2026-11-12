using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Edits one .ravt as a guided setup: the avatar base's details, its model (dropped in: the rest is filled from it),
    /// every body shape key in a table to pick the ones buyers control and garments follow (with a live preview on the
    /// avatar in the scene), the controls themselves (sliders, toggles, choices made from what's set on the avatar), and
    /// the bones garments don't move with. Body shape keys only change when you move a preview slider.
    /// </summary>
    internal class WardrobeAvatarForm : WardrobeJsonForm {
        private string _search = "";
        private bool _onlyPicked, _showFace;
        private Vector2 _keysScroll;
        private GameObject _preview;
        private List<GameObject> _models = new List<GameObject>();

        public WardrobeAvatarForm(string path) : base(path) { }

        protected override ICollection<string> Always => new[] { "id", "name", "version", "author" };

        /// <summary>A model's skinned meshes that have a mesh.</summary>
        internal static IEnumerable<SkinnedMeshRenderer> Renderers(GameObject model) =>
            model == null ? Enumerable.Empty<SkinnedMeshRenderer>() : model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh != null);

        private static GameObject ModelOf(JsonObject j) => ModelsOf(Guids(j)).FirstOrDefault();

        private static List<string> Guids(JsonObject j) => (j.Array("guids") ?? new List<object>()).OfType<string>().ToList();

        /// <summary>The base's files that are in this project (its model, and prefabs that add hair, physbones or outfits to it).</summary>
        internal static List<GameObject> ModelsOf(IEnumerable<string> guids) =>
            guids.Select(g => AssetDatabase.GUIDToAssetPath(g.Trim())).Where(p => !string.IsNullOrEmpty(p))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(go => go != null).Distinct().ToList();

        /// <summary>Whether a value a control sets is on the model: the object, or the renderer and its shape key.</summary>
        internal static bool Has(GameObject model, WardrobeShapeValue v) {
            var t = model.transform.Find(v.path);
            if (t == null) return false;
            if (v.IsObject) return true;
            var skin = t.GetComponent<SkinnedMeshRenderer>();
            return skin != null && skin.sharedMesh != null && skin.sharedMesh.GetBlendShapeIndex(v.shape) >= 0;
        }

        private static string Body(JsonObject j) => j.String("body", "Body").Trim();

        protected override void DrawJson(JsonObject j) {
            _models = ModelsOf(Guids(j));
            var model = _models.FirstOrDefault();
            var body = model != null ? model.transform.Find(Body(j))?.GetComponent<SkinnedMeshRenderer>() : null;
            bool hasBody = body != null && body.sharedMesh != null;
            var sliders = j.Array("sliders") ?? new List<object>();
            var fitting = j.Object("fitting");
            bool details = j.String("id").Trim().Length > 0 && j.String("name").Trim().Length > 0;

            Step(1, "Avatar model", model != null ? StepState.Done : StepState.Attention, model != null ? model.name : "drop it in");
            DrawModel(j, model);

            Step(2, "Details", details ? StepState.Done : StepState.Attention);
            Text(j, "name", "Name", "Shown in the Ros' Wardrobe window");
            Id(j, "Stable id garments name the avatar by (e.g. winterpaw.masculine-canine); don't change it once shipped",
                WardrobeAvatars.All.Where(a => a.path != path).Select(a => a.id));
            if (j.String("id").Trim().Length == 0 && j.String("name").Trim().Length > 0) SuggestIdButton(j);
            Text(j, "version", "Version", "The base's version, if garments need to tell versions apart");
            Text(j, "author", "Author", "Who made the base");
            TextArea(j, "description", "Description");

            Step(3, "Body shape keys", !hasBody ? StepState.Todo : sliders.Count > 0 || (fitting?.Array("shapeKeys")?.Count ?? 0) > 0 ? StepState.Done : StepState.Attention,
                hasBody ? $"{body.sharedMesh.blendShapeCount} on {Body(j)}" : null);
            if (!hasBody) Note("Drop the avatar's model in first (step 1).");
            else DrawKeys(j, model, body);

            Step(4, "Controls buyers get", sliders.Count > 0 ? StepState.Done : StepState.Todo, $"{sliders.Count}");
            if (!hasBody) Note("Drop the avatar's model in first (step 1).");
            else DrawControls(j, model, body);

            Step(5, "Bones garments don't move with", StepState.Done, $"{fitting?.Object("mergeBones")?.Count ?? 0}");
            DrawBones(j, model);

            Step(6, "In this project", StepState.Done);
            DrawUsage(j);
        }

        // ---------------------------------------------------------------- 1. the model

        private void DrawModel(JsonObject j, GameObject model) {
            Note("Drop the avatar's model (.fbx) or prefab here, the one buyers import from the avatar's own package. Ros' Wardrobe knows the avatar in " +
                 "any project by that file, and reads the body and its shape keys from it.");
            var guids = (j.Array("guids") ?? new List<object>()).OfType<string>().ToList();
            var added = EditorGUILayout.ObjectField(new GUIContent(model == null ? "Avatar Model" : "Add Another File", "The base's model (.fbx) or prefab"),
                null, typeof(GameObject), true) as GameObject;
            if (added != null) AddModel(j, added);
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
            if (guids.Count == 0) {
                var scene = SceneAvatars().Select(go => (go, source: SourceModel(go))).Where(x => x.source != null).ToList();
                foreach (var (go, source) in scene.Take(4))
                    if (GUILayout.Button($"Use {go.name} from the scene ({source.name})", GUILayout.ExpandWidth(false))) {
                        AddModel(j, source);
                        GUIUtility.ExitGUI();
                    }
            }
            if (model != null) {
                var renderers = Renderers(model).ToList();
                var paths = renderers.Select(r => AnimationUtility.CalculateTransformPath(r.transform, model.transform)).ToList();
                int index = paths.IndexOf(Body(j));
                var labels = renderers.Select((r, i) => $"{paths[i]}  ({r.sharedMesh.blendShapeCount} shape keys)".Replace("/", "∕")).ToList();
                if (index < 0) labels.Add($"{Body(j)} (not in {model.name})");
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup(new GUIContent("Body", "The renderer of the avatar's body: the one garments fit to and buyers' controls change"),
                    index < 0 ? labels.Count - 1 : index, labels.ToArray());
                if (EditorGUI.EndChangeCheck() && picked < paths.Count) Set(j, "body", paths[picked], paths[picked] == "Body");
                if (index < 0) EditorGUILayout.HelpBox($"{model.name} has no renderer at '{Body(j)}': pick the body.", MessageType.Error);
            }
            Words(j, "matches", "Also Recognise", "Words in an avatar's name (or its prefab's or model's) that mean it's this base, for copies made without its files");
        }

        // The model or prefab file a scene avatar comes from.
        private static GameObject SourceModel(GameObject avatar) {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatar);
            return source != null ? source : null;
        }

        private static List<GameObject> SceneAvatars() =>
            Object.FindObjectsOfType<Animator>().Select(a => WardrobeWindow.AvatarRoot(a.transform)).Where(go => go != null).Distinct().ToList();

        private void AddModel(JsonObject j, GameObject added) {
            string assetPath = AssetDatabase.GetAssetPath(added);
            if (string.IsNullOrEmpty(assetPath)) {
                var source = SourceModel(added);
                if (source == null) {
                    EditorUtility.DisplayDialog("Ros' Wardrobe", $"{added.name} isn't a file in the project: drop the avatar's model or prefab from the Project window.", "OK");
                    return;
                }
                added = source;
                assetPath = AssetDatabase.GetAssetPath(added);
            }
            var guids = (j.Array("guids") ?? new List<object>()).OfType<string>().Cast<object>().ToList();
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!guids.Contains(guid)) guids.Add(guid);
            Set(j, "guids", guids, false);
            // what the model tells: the name, the id, the body
            if (j.String("name").Trim().Length == 0) j["name"] = ObjectNames.NicifyVariableName(added.name);
            if (j.String("id").Trim().Length == 0) j["id"] = WardrobeConfigs.SuggestId(j.String("author"), j.String("name"));
            var matches = (j.Array("matches") ?? new List<object>()).OfType<string>().ToList();
            if (matches.Count == 0) Set(j, "matches", new List<object> { added.name }, false);
            if (added.transform.Find(Body(j))?.GetComponent<SkinnedMeshRenderer>() == null) {
                var renderers = Renderers(added).ToList();
                var best = renderers.FirstOrDefault(r => string.Equals(r.name, "Body", StringComparison.OrdinalIgnoreCase)) ??
                           renderers.OrderByDescending(r => r.sharedMesh.blendShapeCount).FirstOrDefault();
                if (best != null) j["body"] = AnimationUtility.CalculateTransformPath(best.transform, added.transform);
            }
            repaint?.Invoke();
        }

        private void SuggestIdButton(JsonObject j) {
            string id = WardrobeConfigs.SuggestId(j.String("author"), j.String("name"));
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                if (GUILayout.Button($"Use {id}", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) {
                    j["id"] = id;
                    GUI.FocusControl(null);
                }
            }
        }

        // ---------------------------------------------------------------- 3. the shape key table

        private void DrawKeys(JsonObject j, GameObject model, SkinnedMeshRenderer body) {
            Note("Tick Control for the shape keys buyers should get a slider for in the Avatar section (Buff, Chest, Thin…), and Follow for the ones " +
                 "garments change shape with (Fit to Avatar copies those onto garments). Keys that hide skin under clothes are picked per garment, " +
                 "in its Hides on Body. The slider previews a key on the avatar in the scene; nothing else changes the body.");
            var mesh = body.sharedMesh;
            string bodyPath = Body(j);
            var sliders = j.Array("sliders") ?? new List<object>();
            var fitting = j.Object("fitting") ?? new JsonObject();
            var follow = (fitting.Array("shapeKeys") ?? new List<object>()).OfType<string>().Select(s => s.Trim()).ToList();
            // without a list, garments follow every body key a control sets
            var implicitFollow = follow.Count == 0 ? SetKeys(sliders, bodyPath).ToList() : null;

            PreviewAvatar(j);
            var previewBody = _preview != null ? _preview.transform.Find(bodyPath)?.GetComponent<SkinnedMeshRenderer>() : null;

            using (new EditorGUILayout.HorizontalScope()) {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                _onlyPicked = GUILayout.Toggle(_onlyPicked, new GUIContent("Picked", "Only the keys that are ticked"), EditorStyles.miniButton, GUILayout.Width(56));
                _showFace = GUILayout.Toggle(_showFace, new GUIContent("Face", "Show visemes and face keys (vrc.*, v_*, blink…)"), EditorStyles.miniButton, GUILayout.Width(44));
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("Shape key", EditorStyles.miniBoldLabel, GUILayout.MinWidth(120));
                GUILayout.Label(new GUIContent("Control", "Buyers get a slider for it"), EditorStyles.miniBoldLabel, GUILayout.Width(52));
                GUILayout.Label(new GUIContent("Follow", "Garments change shape with it"), EditorStyles.miniBoldLabel, GUILayout.Width(46));
                GUILayout.Label(new GUIContent("Preview", previewBody != null ? $"On {_preview.name} in the scene" : "Put the avatar in the scene to preview"),
                    EditorStyles.miniBoldLabel, GUILayout.Width(130));
            }
            int shown = 0, total = mesh.blendShapeCount;
            _keysScroll = EditorGUILayout.BeginScrollView(_keysScroll, GUILayout.MaxHeight(320));
            for (int i = 0; i < total; i++) {
                string key = mesh.GetBlendShapeName(i);
                var own = OwnSlider(sliders, bodyPath, key);
                var inOther = own == null ? sliders.OfType<JsonObject>().FirstOrDefault(s => Sets(s, bodyPath).Contains(key)) : null;
                bool control = own != null, follows = implicitFollow?.Contains(key) ?? follow.Contains(key);
                if (_onlyPicked && !control && inOther == null && !follows) continue;
                if (!_showFace && Face(key) && !control && !follows) continue;
                if (_search.Length > 0 && key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 300) continue;
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(new GUIContent(key, inOther != null ? $"Set by the control {inOther.String("name")}" : key), GUILayout.MinWidth(120));
                    using (new EditorGUI.DisabledScope(inOther != null)) {
                        EditorGUI.BeginChangeCheck();
                        bool now = EditorGUILayout.Toggle(control || inOther != null, GUILayout.Width(52));
                        if (EditorGUI.EndChangeCheck()) {
                            if (now) AddSlider(j, sliders, key);
                            else sliders.Remove(own);
                            Set(j, "sliders", sliders, sliders.Count == 0);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    bool f = EditorGUILayout.Toggle(follows, GUILayout.Width(46));
                    if (EditorGUI.EndChangeCheck()) {
                        // the first explicit pick starts from what garments followed so far
                        var list = (implicitFollow ?? follow).Where(k => k != key).ToList();
                        if (f) list.Add(key);
                        // none ticked: garments follow what the controls set
                        if (list.Count == 0) fitting.Remove("shapeKeys");
                        else fitting["shapeKeys"] = list.Cast<object>().ToList();
                        Set(j, "fitting", fitting, false);
                        GUIUtility.ExitGUI();
                    }
                    using (new EditorGUI.DisabledScope(previewBody == null)) {
                        int index = previewBody != null && previewBody.sharedMesh != null ? previewBody.sharedMesh.GetBlendShapeIndex(key) : -1;
                        float weight = index >= 0 ? previewBody.GetBlendShapeWeight(index) : 0;
                        EditorGUI.BeginChangeCheck();
                        weight = EditorGUILayout.Slider(weight, 0, 100, GUILayout.Width(130));
                        if (EditorGUI.EndChangeCheck()) {
                            if (index >= 0) {
                                Undo.RecordObject(previewBody, "Preview Shape Key");
                                previewBody.SetBlendShapeWeight(index, weight);
                                PrefabUtility.RecordPrefabInstancePropertyModifications(previewBody);
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            if (shown > 300) Note($"{shown - 300} more: search to find them.");
            if (shown == 0) Note(total == 0 ? $"{body.name} has no shape keys." : "None match.");
            if (follow.Count == 0 && implicitFollow.Count > 0)
                Note("Nothing is ticked to follow, so garments follow what the controls set: " + string.Join(", ", implicitFollow) + ".");
        }

        private static bool Face(string key) {
            string k = key.ToLowerInvariant();
            return k.StartsWith("vrc.") || k.StartsWith("v_") || k.StartsWith("mouth") || k.StartsWith("eye") || k.Contains("blink") || k.Contains("viseme") ||
                   k.StartsWith("brow") || k.Contains("tongue") || k.Contains("smile") || k.Contains("frown");
        }

        // The avatar in the scene the previews show on.
        private void PreviewAvatar(JsonObject j) {
            var avatars = SceneAvatars();
            string id = j.String("id").Trim();
            var model = ModelOf(j);
            var mine = avatars.Where(go => WardrobeConfigs.SameId(WardrobeAvatars.Match(go)?.id, id) || model != null && SourceModel(go) == model).ToList();
            if (_preview == null || !avatars.Contains(_preview)) _preview = mine.FirstOrDefault();
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUILayout.ObjectField(new GUIContent("Preview On", "An avatar in the scene to preview the shape keys on"), _preview, typeof(GameObject), true) as GameObject;
            if (EditorGUI.EndChangeCheck()) _preview = picked != null ? WardrobeWindow.AvatarRoot(picked.transform) ?? picked : null;
            if (_preview == null) Note("Put the avatar in the scene to preview its shape keys.");
        }

        // The keys a control sets on the body (its set, or its options').
        private static IEnumerable<string> Sets(JsonObject slider, string body) {
            var sets = new[] { slider.Object("set") }.Concat(slider.Objects("options").Select(o => o.Object("set"))).Where(s => s != null);
            return sets.SelectMany(s => s.Keys.Where((k, i) => s.Values[i] is double).Select(k => k.Contains(":") ? null : k.Trim())).Where(k => k != null);
        }

        private static IEnumerable<string> SetKeys(List<object> sliders, string body) => sliders.OfType<JsonObject>().SelectMany(s => Sets(s, body)).Distinct();

        // The slider that sets just this key (what ticking Control makes).
        private static JsonObject OwnSlider(List<object> sliders, string body, string key) =>
            sliders.OfType<JsonObject>().FirstOrDefault(s => s.Object("set") is JsonObject set && set.Count == 1 && set.Keys[0].Trim() == key && !s.Has("options"));

        private static void AddSlider(JsonObject j, List<object> sliders, string key) {
            var slider = new JsonObject();
            slider["name"] = Pretty(key);
            slider["group"] = "Body";
            var set = new JsonObject();
            set[key] = 100.0;
            slider["set"] = set;
            sliders.Add(slider);
        }

        // "Breasts_Big" → "Breasts Big"
        private static string Pretty(string key) => ObjectNames.NicifyVariableName(key.Replace('_', ' ').Replace('.', ' ')).Trim();

        // ---------------------------------------------------------------- 4. controls

        private void DrawControls(JsonObject j, GameObject model, SkinnedMeshRenderer body) {
            Note("What buyers change in the window's Avatar section. A slider turns its shape keys from 0 up to the weights given; a toggle or a choice " +
                 "picks one option's values (shape keys and objects). Set the avatar in the scene the way an option looks and press From Scene.");
            var sliders = j.Array("sliders") ?? new List<object>();
            string bodyPath = Body(j);
            var groups = new[] { "Body", "Head", "Clothing", "Extras" }.Concat(sliders.OfType<JsonObject>().Select(s => s.String("group")))
                .Where(g => g.Trim().Length > 0).Distinct().Select(g => (g, g)).ToList();
            for (int i = 0; i < sliders.Count; i++) {
                if (!(sliders[i] is JsonObject s)) continue;
                bool choice = s.Has("options");
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUI.BeginChangeCheck();
                        string name = EditorGUILayout.TextField(s.String("name"));
                        if (EditorGUI.EndChangeCheck()) Set(s, "name", name, false);
                        EditorGUILayout.LabelField(choice ? IsToggle(s) ? "toggle" : "choice" : "slider", EditorStyles.miniLabel, GUILayout.Width(40));
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button(new GUIContent("▲", "Move up"), EditorStyles.miniButtonLeft, GUILayout.Width(22))) Swap(sliders, i, i - 1);
                        using (new EditorGUI.DisabledScope(i == sliders.Count - 1))
                            if (GUILayout.Button(new GUIContent("▼", "Move down"), EditorStyles.miniButtonMid, GUILayout.Width(22))) Swap(sliders, i, i + 1);
                        if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButtonRight, GUILayout.Width(22))) {
                            sliders.RemoveAt(i);
                            Set(j, "sliders", sliders, sliders.Count == 0);
                            GUIUtility.ExitGUI();
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    string group = Suggest(new GUIContent("Group", "Where the Avatar section shows it"), s.String("group"), groups, v => Set(s, "group", v, false));
                    if (EditorGUI.EndChangeCheck()) Set(s, "group", group, false);
                    if (!choice) {
                        var set = s.Object("set") ?? new JsonObject();
                        DrawSet(s, "set", set, model, bodyPath, true);
                    } else {
                        var options = s.Array("options") ?? new List<object>();
                        for (int k = 0; k < options.Count; k++) {
                            if (!(options[k] is JsonObject o)) continue;
                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
                                using (new EditorGUILayout.HorizontalScope()) {
                                    EditorGUI.BeginChangeCheck();
                                    string option = EditorGUILayout.TextField("Option", o.String("name"));
                                    if (EditorGUI.EndChangeCheck()) Set(o, "name", option, false);
                                    using (new EditorGUI.DisabledScope(_preview == null))
                                        if (GUILayout.Button(new GUIContent("From Scene", _preview == null ? "Put the avatar in the scene first"
                                                : $"This option's values as they are on {_preview.name} now (every key and object the options use)"),
                                                EditorStyles.miniButton, GUILayout.Width(80))) {
                                            FromScene(s, o, bodyPath);
                                            GUIUtility.ExitGUI();
                                        }
                                    if (GUILayout.Button(new GUIContent("✕", "Remove the option"), EditorStyles.miniButton, GUILayout.Width(22))) {
                                        options.RemoveAt(k);
                                        GUIUtility.ExitGUI();
                                    }
                                }
                                DrawSet(o, "set", o.Object("set") ?? new JsonObject(), model, bodyPath, false);
                            }
                        }
                        if (GUILayout.Button("Add Option", EditorStyles.miniButton, GUILayout.Width(90))) {
                            var o = new JsonObject();
                            o["name"] = $"Option {options.Count + 1}";
                            o["set"] = new JsonObject();
                            options.Add(o);
                            Set(s, "options", options, false);
                        }
                    }
                }
            }
            if (GUILayout.Button("Add Control", GUILayout.Width(160))) {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Slider (shape keys from 0 to a weight)"), false, () => AddControl(j, sliders, "slider"));
                menu.AddItem(new GUIContent("Toggle (Off and On)"), false, () => AddControl(j, sliders, "toggle"));
                menu.AddItem(new GUIContent("Choice (several options)"), false, () => AddControl(j, sliders, "choice"));
                menu.ShowAsContext();
            }
        }

        private void AddControl(JsonObject j, List<object> sliders, string kind) {
            var s = new JsonObject();
            s["name"] = kind == "slider" ? "Slider" : kind == "toggle" ? "Toggle" : "Choice";
            s["group"] = "Body";
            if (kind == "slider") s["set"] = new JsonObject();
            else {
                var options = new List<object>();
                foreach (string name in kind == "toggle" ? new[] { "Off", "On" } : new[] { "Option 1", "Option 2" }) {
                    var o = new JsonObject();
                    o["name"] = name;
                    o["set"] = new JsonObject();
                    options.Add(o);
                }
                s["options"] = options;
            }
            sliders.Add(s);
            Set(j, "sliders", sliders, false);
            repaint?.Invoke();
        }

        private static bool IsToggle(JsonObject s) {
            var names = s.Objects("options").Select(o => o.String("name")).ToList();
            return names.Count == 2 && names[0] == "Off" && names[1] == "On";
        }

        // A set of values: shape keys (on the body, or "Renderer:Key") with weights, and objects (by path) on or off.
        private void DrawSet(JsonObject owner, string key, JsonObject set, GameObject model, string bodyPath, bool slider) {
            EditorGUI.indentLevel++;
            for (int i = 0; i < set.Count; i++) {
                string name = set.Keys[i];
                using (new EditorGUILayout.HorizontalScope()) {
                    var value = WardrobeShapeValue.Parse(name, set.Values[i], bodyPath);
                    bool exists = model == null || value == null || _models.Any(m => Has(m, value));
                    var label = new GUIContent(name + (exists ? "" : "  (not on the avatar)"), set.Values[i] is bool ? "An object, by its path under the avatar" : "A shape key");
                    if (set.Values[i] is bool on) {
                        EditorGUI.BeginChangeCheck();
                        on = EditorGUILayout.Toggle(label, on);
                        if (EditorGUI.EndChangeCheck()) set.Values[i] = on;
                    } else {
                        EditorGUI.BeginChangeCheck();
                        float w = EditorGUILayout.Slider(label, set.Values[i] is double d ? (float)d : 0, 0, 100);
                        if (EditorGUI.EndChangeCheck()) set.Values[i] = Math.Round(w, 2);
                    }
                    if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                        set.Remove(name);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("Add Shape Key", EditorStyles.miniButton, GUILayout.Width(100))) {
                    var menu = new GenericMenu();
                    // every renderer of the base's files (a prefab can add hair or outfits to the model), once per path
                    var renderers = _models.SelectMany(m => Renderers(m).Select(r => (path: AnimationUtility.CalculateTransformPath(r.transform, m.transform), r)))
                        .GroupBy(x => x.path).Select(x => x.First()).ToList();
                    foreach (var (rPath, r) in renderers) {
                            bool isBody = rPath == bodyPath;
                            for (int k = 0; k < r.sharedMesh.blendShapeCount; k++) {
                                string shape = r.sharedMesh.GetBlendShapeName(k);
                                string entry = isBody ? shape : $"{rPath}:{shape}";
                                if (set.Has(entry)) continue;
                                string menuPath = isBody ? $"{Group(shape)}{shape}" : $"Other renderers/{rPath.Replace("/", "∕")}/{shape}";
                                menu.AddItem(new GUIContent(menuPath), false, () => {
                                    set[entry] = 100.0;
                                    Set(owner, key, set, false);
                                    repaint?.Invoke();
                                });
                            }
                        }
                    if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Drop the avatar's model in first"));
                    menu.ShowAsContext();
                }
                if (!slider) {
                    var picked = EditorGUILayout.ObjectField(new GUIContent("", "Add an object of the avatar in the scene (an outfit part, a prop) to turn on or off"),
                        null, typeof(GameObject), true, GUILayout.Width(150)) as GameObject;
                    if (picked != null) {
                        var root = WardrobeWindow.AvatarRoot(picked.transform);
                        string objectPath = root != null ? AnimationUtility.CalculateTransformPath(picked.transform, root.transform) : picked.name;
                        if (objectPath.Length > 0 && !set.Has(objectPath)) {
                            set[objectPath] = picked.activeSelf;
                            Set(owner, key, set, false);
                        }
                    }
                }
            }
            EditorGUI.indentLevel--;
        }

        // Shape key menus grouped by their first letter when there are many.
        private static string Group(string shape) => shape.Length > 0 ? char.ToUpperInvariant(shape[0]) + "/" : "";

        // Writes the scene avatar's current values of everything the control's options use into one option.
        private void FromScene(JsonObject slider, JsonObject option, string bodyPath) {
            if (_preview == null) return;
            var set = option.Object("set") ?? new JsonObject();
            var keys = slider.Objects("options").SelectMany(o => (o.Object("set") ?? new JsonObject()).Keys).Distinct().ToList();
            foreach (string entry in keys) {
                bool isObject = slider.Objects("options").Any(o => o.Object("set")?[entry] is bool);
                if (isObject) {
                    var t = _preview.transform.Find(entry);
                    if (t != null) set[entry] = t.gameObject.activeSelf;
                    continue;
                }
                int colon = entry.IndexOf(':');
                string rPath = colon < 0 ? bodyPath : entry.Substring(0, colon), shape = colon < 0 ? entry : entry.Substring(colon + 1);
                var r = _preview.transform.Find(rPath)?.GetComponent<SkinnedMeshRenderer>();
                int index = r != null && r.sharedMesh != null ? r.sharedMesh.GetBlendShapeIndex(shape) : -1;
                if (index >= 0) set[entry] = Math.Round(r.GetBlendShapeWeight(index), 2);
            }
            option["set"] = set;
        }

        private static void Swap(List<object> list, int a, int b) {
            (list[a], list[b]) = (list[b], list[a]);
            GUIUtility.ExitGUI();
        }

        // ---------------------------------------------------------------- 5. bones

        private void DrawBones(JsonObject j, GameObject model) {
            Note("Bones garments shouldn't move with (the tail, breast or hair bones), by the start of their name, and the bone that takes their weight " +
                 "instead. Fit to Avatar moves the garment's weights off them.");
            var fitting = j.Object("fitting") ?? new JsonObject();
            var bones = fitting.Object("mergeBones") ?? new JsonObject();
            var names = _models.SelectMany(m => m.GetComponentsInChildren<Transform>(true).Where(t => t != m.transform && t.GetComponent<Renderer>() == null))
                .Select(t => t.name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < bones.Count; i++) {
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUI.BeginChangeCheck();
                    string prefix = EditorGUILayout.DelayedTextField(i == 0 ? "Bones Starting With" : " ", bones.Keys[i]);
                    if (EditorGUI.EndChangeCheck() && prefix.Trim().Length > 0 && !bones.Has(prefix.Trim())) bones.Keys[i] = prefix.Trim();
                    EditorGUILayout.LabelField("→", GUILayout.Width(16));
                    string target = bones.Values[i] as string ?? "";
                    int index = names.IndexOf(target);
                    var options = names.Select(n => n.Replace("/", "∕")).ToList();
                    if (index < 0) options.Add(target.Length > 0 ? $"{target} (not on the avatar)" : "Pick a bone");
                    EditorGUI.BeginChangeCheck();
                    int picked = EditorGUILayout.Popup(index < 0 ? options.Count - 1 : index, options.ToArray(), GUILayout.Width(130));
                    if (EditorGUI.EndChangeCheck() && picked < names.Count) bones.Values[i] = names[picked];
                    if (GUILayout.Button(new GUIContent("✕", "Remove"), EditorStyles.miniButton, GUILayout.Width(22))) {
                        bones.Remove(bones.Keys[i]);
                        Set(fitting, "mergeBones", bones, bones.Count == 0);
                        Set(j, "fitting", fitting, fitting.Count == 0);
                        GUIUtility.ExitGUI();
                    }
                }
                string start = bones.Keys[i];
                var hits = names.Where(n => n.StartsWith(start, StringComparison.OrdinalIgnoreCase)).ToList();
                EditorGUILayout.LabelField(" ", hits.Count == 0 ? "no bone starts with it" : $"{hits.Count}: {string.Join(", ", hits.Take(6))}{(hits.Count > 6 ? "…" : "")}",
                    EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Add Bones", GUILayout.Width(160))) {
                var menu = new GenericMenu();
                foreach (string n in names) {
                    string picked = n;
                    menu.AddItem(new GUIContent($"{Group(n)}{n}"), false, () => {
                        if (!bones.Has(picked)) bones[picked] = _models.Select(m => Parent(m, picked)).FirstOrDefault(n => n != null) ?? "Hips";
                        Set(fitting, "mergeBones", bones, false);
                        Set(j, "fitting", fitting, false);
                        repaint?.Invoke();
                    });
                }
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Drop the avatar's model in first"));
                menu.ShowAsContext();
            }
        }

        // The bone's parent: who takes its weight by default.
        private static string Parent(GameObject model, string bone) =>
            model?.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bone)?.parent?.name;

        // ---------------------------------------------------------------- 6. usage

        private void DrawUsage(JsonObject j) {
            string id = j.String("id").Trim();
            var scene = SceneAvatars();
            var recognised = scene.Where(go => WardrobeConfigs.SameId(WardrobeAvatars.Match(go)?.id, id)).ToList();
            if (scene.Count > 0)
                Note(recognised.Count > 0 ? $"In the open scenes: {string.Join(", ", recognised.Select(go => go.name))}."
                    : "No avatar in the open scenes is recognised as this base (save to check again).");
            var fits = WardrobeGarments.All.Where(g => WardrobeConfigs.SameId(g.avatarId, id)).ToList();
            Note(fits.Count == 0 ? "No garment in the project is fitted to it yet." : $"Garments fitted to it: {string.Join(", ", fits.Select(Title).Distinct())}.");
        }
    }
}
