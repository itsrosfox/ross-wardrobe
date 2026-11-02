using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Ros' Wardrobe: pick an avatar in the scene and browse the garments fitted to its base (.rgarm files whose avatar is
    /// the base's .ravt), by category. Clicking one puts it on the avatar, taking off whatever is worn in its slot, and
    /// every look change is baked onto it as you make it. That's it: the garment's own setup (VRCFury) attaches it on
    /// upload. Ctrl+Z undoes either. The Avatar section shapes the avatar's body.
    /// </summary>
    public class WardrobeWindow : EditorWindow {
        private const string All = "All", AvatarSection = "Avatar";
        private const float BrowserWidth = 330, CardWidth = 96, CardHeight = 124;

        [SerializeField] private GameObject avatar;
        [SerializeField] private WardrobeGarment selected;
        [SerializeField] private string category = All;

        private Vector2 _browserScroll, _editorScroll;
        private WardrobeLookPanel _panel;
        private WardrobeAvatarPanel _avatarPanel;
        private List<WardrobeGarment> _garments;
        private Dictionary<WardrobeGarment, GameObject> _worn = new Dictionary<WardrobeGarment, GameObject>();   // by its root on the avatar
        private readonly Dictionary<WardrobeGarment, bool> _missingScripts = new Dictionary<WardrobeGarment, bool>();
        private List<GameObject> _sceneAvatars;
        private GameObject _baseOf;
        private int _baseRevision = -1;
        private WardrobeAvatar _base;
        private bool _everyFit;

        [MenuItem("Tools/Ros' Wardrobe")]
        public static WardrobeWindow Open() {
            var window = GetWindow<WardrobeWindow>();
            window.minSize = new Vector2(BrowserWidth + 300, 420);
            return window;
        }

        /// <summary>Opens the window on the avatar wearing <paramref name="rc"/>, with that garment selected.</summary>
        public static void Open(WardrobeRecolor rc) {
            var window = Open();
            window.avatar = AvatarRoot(rc.transform) ?? rc.transform.root.gameObject;
            window.selected = rc.garment;
            window.Repaint();
        }

        private void OnEnable() {
            titleContent = new GUIContent("Ros' Wardrobe", EditorGUIUtility.IconContent("Cloth Icon").image);
            _panel = new WardrobeLookPanel(Repaint);
            _avatarPanel = new WardrobeAvatarPanel();
            EditorApplication.projectChanged += Reload;
            EditorApplication.hierarchyChanged += HierarchyChanged;
        }

        private void OnDisable() {
            EditorApplication.projectChanged -= Reload;
            EditorApplication.hierarchyChanged -= HierarchyChanged;
            _panel.Dispose();
        }

        private void OnSelectionChange() {
            if (avatar != null) return;
            avatar = FindAvatar();
            Repaint();
        }

        private void Reload() {
            _garments = null;
            _baseRevision = -1;
            _missingScripts.Clear();
            Repaint();
        }

        private void HierarchyChanged() {
            _sceneAvatars = null;
            _baseOf = null;
            Repaint();
        }

        // The avatar base the avatar is built on (.ravt), or null when Ros' Wardrobe doesn't know it.
        private WardrobeAvatar Base {
            get {
                if (_baseOf != avatar || _baseRevision != WardrobeConfigs.Revision) {
                    _baseOf = avatar;
                    _baseRevision = WardrobeConfigs.Revision;
                    _base = avatar != null ? WardrobeAvatars.Match(avatar) : null;
                }
                return _base;
            }
        }

        private void OnGUI() {
            if (avatar == null) avatar = FindAvatar();
            if (_garments == null || _baseOf != avatar) _garments = FindGarments();
            _worn = Worn();

            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(BrowserWidth))) DrawBrowser();
                var line = GUILayoutUtility.GetRect(1, 1, GUILayout.Width(1), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(line, new Color(0, 0, 0, 0.25f));
                using (new EditorGUILayout.VerticalScope()) DrawEditor(position.width - BrowserWidth - 24);
            }
        }

        // ---------------------------------------------------------------- toolbar + browser

        private void DrawToolbar() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("Avatar", GUILayout.Width(44));
                if (_sceneAvatars == null) _sceneAvatars = SceneAvatars();
                var avatars = _sceneAvatars.Where(a => a != null).ToList();
                if (avatar != null && !avatars.Contains(avatar)) avatars.Insert(0, avatar);
                var names = avatars.Select(a => a.name).ToList();
                if (names.Count == 0) names.Add("No avatar in the open scenes");
                EditorGUI.BeginChangeCheck();
                int index = EditorGUILayout.Popup(Mathf.Max(0, avatars.IndexOf(avatar)), names.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(200));
                if (EditorGUI.EndChangeCheck() && index < avatars.Count) avatar = avatars[index];
                if (avatar != null)
                    GUILayout.Label(new GUIContent(Base != null ? Base.DisplayName : "Unknown base",
                        Base != null ? $"Built on {Base.DisplayName} ({Path.GetFileName(Base.path)}), so its garments are listed"
                            : "No .ravt in the project matches this avatar, so every garment is listed"), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Refresh", "Look for new garments and redraw the pictures"), EditorStyles.toolbarButton)) {
                    WardrobeThumbnails.Clear();
                    Reload();
                }
            }
        }

        private void DrawBrowser() {
            var categories = (avatar != null ? new[] { AvatarSection, All } : new[] { All })
                .Concat(_garments.Select(Category).Distinct().Where(c => c != AvatarSection).OrderBy(c => c)).ToArray();
            int index = Array.IndexOf(categories, category);
            if (index < 0) index = Array.IndexOf(categories, All);
            index = GUILayout.SelectionGrid(index, categories, Mathf.Min(categories.Length, 4), EditorStyles.miniButton);
            category = categories[index];
            EditorGUILayout.Space(4);
            if (category == AvatarSection) {
                EditorGUILayout.LabelField("Shape the avatar's body on the right. Worn garments follow it.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            var shown = _garments.Where(g => category == All || Category(g) == category).ToList();
            _browserScroll = EditorGUILayout.BeginScrollView(_browserScroll);
            if (avatar != null && Base == null && _garments.Count > 0)
                EditorGUILayout.HelpBox($"Ros' Wardrobe doesn't know which avatar base {avatar.name} is, so every garment is listed; check what " +
                                        "each was made for. Garment packs ship a .ravt for the base they fit.", MessageType.None);
            if (shown.Count == 0)
                EditorGUILayout.HelpBox(_everyFit || avatar == null || Base == null
                    ? "No clothing found. Garments show up here once a garment pack that supports Ros' Wardrobe (its .rgarm files) is imported."
                    : $"No clothing for {Base.DisplayName} found. Garments show up here once a pack fitted to it is imported.", MessageType.Info);
            int columns = Mathf.Max(1, Mathf.FloorToInt((BrowserWidth - 20) / (CardWidth + 4)));
            for (int i = 0; i < shown.Count; i += columns) {
                using (new EditorGUILayout.HorizontalScope()) {
                    for (int j = 0; j < columns && i + j < shown.Count; j++) {
                        var g = shown[i + j];
                        var rect = GUILayoutUtility.GetRect(CardWidth, CardHeight, GUILayout.Width(CardWidth));
                        if (Card(rect, g)) Pick(g);
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            EditorGUILayout.EndScrollView();
            if (Base != null) {
                EditorGUI.BeginChangeCheck();
                _everyFit = EditorGUILayout.ToggleLeft(new GUIContent("Show garments for other avatars", "Fits made for other bases may not fit this avatar"),
                    _everyFit, EditorStyles.miniLabel);
                if (EditorGUI.EndChangeCheck()) _garments = null;
            }
        }

        private bool Card(Rect rect, WardrobeGarment g) {
            bool clicked = GUI.Button(rect, new GUIContent("", _worn.ContainsKey(g) ? "Worn: click to edit" : "Click to put it on"));
            if (Event.current.type != EventType.Repaint) return clicked;
            var rc = _worn.TryGetValue(g, out var root) ? Recolor(root, g) : null;
            var image = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.width - 8);
            var picture = g.thumbnail != null ? g.thumbnail : WardrobeThumbnails.Get(g, rc != null ? rc.look : null, rc != null ? rc.materialType : null, Repaint);
            if (picture != null) GUI.DrawTexture(image, picture, ScaleMode.ScaleAndCrop);
            else EditorGUI.DrawRect(image, new Color(0.5f, 0.5f, 0.5f, 0.15f));
            GUI.Label(new Rect(rect.x + 2, image.yMax + 2, rect.width - 4, 16), DisplayName(g), WardrobeLookPanel.CenteredLabel);
            if (root != null) {
                var badge = new Rect(image.xMax - 40, image.y + 3, 37, 14);
                EditorGUI.DrawRect(badge, new Color(0.2f, 0.55f, 0.3f, 0.9f));
                GUI.Label(badge, "Worn", WardrobeLookPanel.CenteredLabel);
            }
            if (g == selected) WardrobeLookPanel.DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2);
            return clicked;
        }

        private void Pick(WardrobeGarment g) {
            selected = g;
            if (category == AvatarSection) category = All;
            if (avatar != null && !_worn.ContainsKey(g)) EditorApplication.delayCall += () => PutOn(g);
        }

        // ---------------------------------------------------------------- editor

        private void DrawEditor(float width) {
            _editorScroll = EditorGUILayout.BeginScrollView(_editorScroll);
            try {
                if (avatar == null) {
                    _panel.SetTarget(null);
                    EditorGUILayout.HelpBox("Open a scene with your avatar in it, then pick it in the Avatar menu above.", MessageType.Info);
                    return;
                }
                if (category == AvatarSection) {
                    _panel.SetTarget(null);
                    _avatarPanel.DrawEditor(avatar, Base, width);
                    return;
                }
                if (selected == null) {
                    _panel.SetTarget(null);
                    EditorGUILayout.HelpBox("Click a piece of clothing on the left to put it on and customise it.", MessageType.Info);
                    return;
                }

                bool worn = _worn.TryGetValue(selected, out var root);
                // put on by hand, it gets its Wardrobe Recolor when it's first edited
                var rc = worn ? Recolor(root, selected) ?? AddRecolor(root, selected) : null;
                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUILayout.VerticalScope()) {
                        GUILayout.Label(DisplayName(selected), EditorStyles.largeLabel);
                        GUILayout.Label(string.Join(" · ", new[] {
                            Category(selected), selected.slot, string.IsNullOrWhiteSpace(selected.version) ? null : "v" + selected.version.Trim(),
                            worn ? "on " + avatar.name : null,
                        }.Where(x => !string.IsNullOrWhiteSpace(x))), EditorStyles.miniLabel);
                    }
                    GUILayout.FlexibleSpace();
                    if (rc != null && GUILayout.Button(new GUIContent("Frame", "Show it in the Scene view"), GUILayout.Width(60))) Frame(rc);
                    if (worn) {
                        if (GUILayout.Button(new GUIContent("Select", "Select it in the Hierarchy"), GUILayout.Width(60))) {
                            Selection.activeGameObject = root;
                            EditorGUIUtility.PingObject(root);
                        }
                        if (GUILayout.Button("Take Off", GUILayout.Width(70))) EditorApplication.delayCall += () => TakeOff(root);
                    }
                }
                if (Base != null && !WardrobeGarments.Fits(selected, Base))
                    EditorGUILayout.HelpBox($"Made for {WardrobeForm.AvatarName(selected.avatarId)}" +
                                            $"{(string.IsNullOrWhiteSpace(selected.avatarVersion) ? "" : " " + selected.avatarVersion)}, " +
                                            $"so it may not fit {avatar.name} ({Base.DisplayName}).", MessageType.Warning);
                else if (Base == null && !string.IsNullOrWhiteSpace(selected.avatarId))
                    EditorGUILayout.LabelField($"Made for {WardrobeForm.AvatarName(selected.avatarId)}.", EditorStyles.miniLabel);
                if (MissingScripts(selected))
                    EditorGUILayout.HelpBox($"{DisplayName(selected)} uses a tool that isn't installed in this project (usually VRCFury, " +
                                            "which attaches it to the avatar on upload). Install it through VCC, or it won't work in-game.",
                        MessageType.Warning);
                EditorGUILayout.Space();

                if (rc == null) {
                    _panel.SetTarget(null);
                    if (selected.prefab == null)
                        EditorGUILayout.HelpBox($"Couldn't find the prefab for {DisplayName(selected)}.", MessageType.Warning);
                    else if (GUILayout.Button("Put It On", GUILayout.Height(28))) {
                        var g = selected;
                        EditorApplication.delayCall += () => PutOn(g);
                    }
                    return;
                }
                _panel.SetTarget(rc);
                _panel.OnGUI(width);
            } finally {
                EditorGUILayout.EndScrollView();
            }
        }

        // ---------------------------------------------------------------- wearing

        // Wears g, taking off whatever is worn in its slot, as one undo step.
        private void PutOn(WardrobeGarment g) {
            if (avatar == null || g == null || _worn.ContainsKey(g)) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var replaced = SameSlot(g);
            var worn = Wear(g);
            if (worn == null) return;
            foreach (var old in replaced) {
                if (old == null) continue;
                try {
                    Undo.DestroyObjectImmediate(old);
                } catch (InvalidOperationException) {
                    Debug.LogWarning($"[Ros' Wardrobe] {old.name} is part of the avatar's prefab, so it stays on; remove it there.");
                }
            }
            Undo.SetCurrentGroupName("Put On " + DisplayName(g));
            Undo.CollapseUndoOperations(group);
            ShowNotification(new GUIContent($"{DisplayName(g)} is on {avatar.name}"));
        }

        // Garments on the avatar worn in the same slot as g.
        private List<GameObject> SameSlot(WardrobeGarment g) =>
            string.IsNullOrWhiteSpace(g.slot) ? new List<GameObject>()
                : _worn.Where(w => w.Key != g && string.Equals(w.Key.slot?.Trim(), g.slot.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(w => w.Value).ToList();

        // What the avatar wears: whatever has a Wardrobe Recolor for a garment, and copies of a garment's prefab (put
        // on by hand, or without Ros' Wardrobe, they have none yet).
        private Dictionary<WardrobeGarment, GameObject> Worn() {
            var worn = new Dictionary<WardrobeGarment, GameObject>();
            if (avatar == null) return worn;
            foreach (var rc in avatar.GetComponentsInChildren<WardrobeRecolor>(true))
                if (rc.garment != null && !worn.ContainsKey(rc.garment)) worn[rc.garment] = WornRoot(rc.gameObject);
            var byPrefab = WardrobeGarments.All.Where(g => g.prefab != null)
                .GroupBy(g => AssetDatabase.GetAssetPath(g.prefab)).ToDictionary(x => x.Key, x => x.First());
            foreach (var t in avatar.GetComponentsInChildren<Transform>(true))
                if (t.gameObject != avatar && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && t.GetComponentInChildren<WardrobeRecolor>(true) == null &&
                    byPrefab.TryGetValue(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), out var g) && !worn.ContainsKey(g))
                    worn[g] = t.gameObject;
            return worn;
        }

        private GameObject WornRoot(GameObject go) {
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go) ?? go;
            return root == avatar ? go : root;
        }

        private WardrobeRecolor Wear(WardrobeGarment g) {
            if (avatar == null || g == null) return null;
            var prefab = g.prefab;
            if (prefab == null) {
                ShowNotification(new GUIContent($"Couldn't find the prefab for {DisplayName(g)}"));
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatar.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Put On " + DisplayName(g));
            var rc = Recolor(instance, g) ?? AddRecolor(instance, g);
            WardrobeBody.Follow(avatar);   // takes on the body's shape keys (it finds garments by their Wardrobe Recolor)
            selected = g;
            Repaint();
            return rc;
        }

        private static WardrobeRecolor Recolor(GameObject root, WardrobeGarment g) =>
            root.GetComponentsInChildren<WardrobeRecolor>(true).FirstOrDefault(r => r.garment == g);

        private static WardrobeRecolor AddRecolor(GameObject instance, WardrobeGarment g) {
            var rc = Undo.AddComponent<WardrobeRecolor>(instance);
            rc.garment = g;
            rc.target = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (rc.target == null) rc.target = instance.GetComponentInChildren<Renderer>(true);
            return rc;
        }

        // Missing scripts on the prefab mean a tool it relies on (VRCFury, usually) isn't installed.
        private bool MissingScripts(WardrobeGarment g) {
            if (_missingScripts.TryGetValue(g, out bool missing)) return missing;
            var prefab = g.prefab;
            missing = prefab != null && prefab.GetComponentsInChildren<Transform>(true)
                .Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0);
            _missingScripts[g] = missing;
            return missing;
        }

        private void TakeOff(GameObject root) {
            if (root == null) return;
            _panel.SetTarget(null);
            try {
                Undo.DestroyObjectImmediate(root);
            } catch (InvalidOperationException) {
                ShowNotification(new GUIContent("It's part of the avatar's prefab; remove it there"));
            }
            Repaint();
        }

        private static void Frame(WardrobeRecolor rc) {
            var view = SceneView.lastActiveSceneView;
            if (view == null || rc.target == null) return;
            view.Frame(rc.target.bounds, false);
        }

        // ---------------------------------------------------------------- lookup

        // The garments fitted to the avatar's base (all of them when the base isn't known, or every fit is asked for).
        private List<WardrobeGarment> FindGarments() =>
            WardrobeGarments.All.Where(g => _everyFit || Base == null || WardrobeGarments.Fits(g, Base))
                .OrderBy(Category).ThenBy(DisplayName)
                .ToList();

        // The avatars in the open scenes: VRChat avatars, else humanoids that aren't inside another.
        private static List<GameObject> SceneAvatars() {
            var list = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) {
                    var found = AvatarRoot(animator.transform);
                    if (found != null && !list.Contains(found)) list.Add(found);
                }
            }
            return list;
        }

        private static string DisplayName(WardrobeGarment g) =>
            string.IsNullOrWhiteSpace(g.displayName) ? WardrobeAssets.BaseName(g) : g.displayName;

        private static string Category(WardrobeGarment g) =>
            string.IsNullOrWhiteSpace(g.category) ? "Other" : g.category.Trim();

        private static GameObject FindAvatar() {
            var picked = Selection.activeGameObject;
            if (picked != null && picked.scene.IsValid()) {
                var root = AvatarRoot(picked.transform);
                if (root != null) return root;
            }
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                var root = scene.GetRootGameObjects().FirstOrDefault(IsAvatar);
                if (root != null) return root;
            }
            return null;
        }

        // The VRChat avatar above t, else the outermost humanoid (a garment's own model can be humanoid too).
        private static GameObject AvatarRoot(Transform t) {
            GameObject humanoid = null;
            for (; t != null; t = t.parent) {
                if (t.GetComponent("VRCAvatarDescriptor") != null) return t.gameObject;
                if (IsAvatar(t.gameObject)) humanoid = t.gameObject;
            }
            return humanoid;
        }

        // A VRChat avatar, or any humanoid.
        private static bool IsAvatar(GameObject go) =>
            go.GetComponent("VRCAvatarDescriptor") != null ||
            (go.TryGetComponent<Animator>(out var animator) && animator.isHuman);
    }
}
