using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The window's Avatar section: body presets and skins as cards (the browser), and the body's sliders, every other
    /// shape key and a custom skin (the editor).
    /// </summary>
    internal class WardrobeAvatarPanel {
        private const float CardWidth = 96, BodyCardHeight = 40, SkinCardHeight = 64;

        private readonly Action _repaint;
        private readonly Dictionary<string, bool> _groups = new Dictionary<string, bool>();
        private readonly Dictionary<string, Texture2D> _swatches = new Dictionary<string, Texture2D>();
        private bool _otherShapes;
        private string _search = "";
        private Gradient _custom;
        private string _customFor;

        public WardrobeAvatarPanel(Action repaint) => _repaint = repaint;

        public void Dispose() {
            foreach (var t in _swatches.Values) UnityEngine.Object.DestroyImmediate(t);
            _swatches.Clear();
        }

        // ---------------------------------------------------------------- browser: presets and skins

        public void DrawBrowser(GameObject avatar, WardrobeAvatar b, float width) {
            if (avatar == null) return;
            int columns = Mathf.Max(1, Mathf.FloorToInt((width - 20) / (CardWidth + 4)));

            GUILayout.Label("Body", EditorStyles.boldLabel);
            var bodies = WardrobeBodies.For(b);
            if (bodies.Count == 0) GUILayout.Label("No body presets yet: shape the body on the right, then Save as Body Preset.", EditorStyles.wordWrappedMiniLabel);
            Grid(bodies, columns, BodyCardHeight, (rect, p) => {
                if (GUI.Button(rect, new GUIContent(p.DisplayName, "Shape the body like this")))
                    EditorApplication.delayCall += () => WardrobeBody.Apply(avatar, b, p);
            });

            EditorGUILayout.Space(8);
            GUILayout.Label("Skin", EditorStyles.boldLabel);
            if (b == null || b.fur.Count == 0) {
                GUILayout.Label(b == null ? "Ros' Wardrobe doesn't know this avatar's base, so it can't recolour its fur."
                    : $"{b.DisplayName}'s .ravt doesn't list its fur textures, so skins can't recolour it.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            string worn = WardrobeSkins.Worn(avatar, b);
            var skins = new List<WardrobeSkin> { null }.Concat(WardrobeSkins.For(b)).ToList();
            Grid(skins, columns, SkinCardHeight, (rect, s) => {
                if (GUI.Button(rect, new GUIContent("", s == null ? "The avatar's own fur" : "Recolour the fur like this")))
                    EditorApplication.delayCall += () => {
                        WardrobeSkins.Apply(avatar, b, s);
                        _customFor = null;
                        _repaint();
                    };
                if (Event.current.type != EventType.Repaint) return;
                var swatch = new Rect(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 26);
                if (s != null) GUI.DrawTexture(swatch, Swatch(s.fur), ScaleMode.StretchToFill);
                else EditorGUI.DrawRect(swatch, new Color(0.5f, 0.5f, 0.5f, 0.2f));
                GUI.Label(new Rect(rect.x + 2, swatch.yMax + 2, rect.width - 4, 16), s?.DisplayName ?? "Original", WardrobeLookPanel.CenteredLabel);
                bool on = s == null ? worn == "" : worn == WardrobeAssets.FileName(s.DisplayName);
                if (on) WardrobeLookPanel.DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2);
            });
        }

        private static void Grid<T>(List<T> items, int columns, float height, Action<Rect, T> draw) {
            for (int i = 0; i < items.Count; i += columns) {
                using (new EditorGUILayout.HorizontalScope()) {
                    for (int j = 0; j < columns && i + j < items.Count; j++)
                        draw(GUILayoutUtility.GetRect(CardWidth, height, GUILayout.Width(CardWidth)), items[i + j]);
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private Texture2D Swatch(Gradient g) {
            string key = WardrobeSkins.Recipe(g);
            if (_swatches.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < 64; x++) t.SetPixel(x, 0, g.Evaluate(x / 63f));
            t.Apply();
            return _swatches[key] = t;
        }

        // ---------------------------------------------------------------- editor: sliders, custom skin

        public void DrawEditor(GameObject avatar, WardrobeAvatar b, float width) {
            GUILayout.Label(avatar.name, EditorStyles.largeLabel);
            GUILayout.Label(b != null ? $"Built on {b.DisplayName}" : "Unknown avatar base", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            DrawBody(avatar, b);
            EditorGUILayout.Space(12);
            if (b != null && b.fur.Count > 0) DrawCustomSkin(avatar, b);
        }

        private void DrawBody(GameObject avatar, WardrobeAvatar b) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label("Body", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save as Body Preset…", GUILayout.Width(150))) EditorApplication.delayCall += () => SaveBody(avatar, b);
            }
            if (WardrobeBody.BodyOf(avatar, b) == null) {
                EditorGUILayout.HelpBox("Couldn't find the avatar's body (a renderer with shape keys).", MessageType.Info);
                return;
            }
            var sliders = b?.sliders ?? new List<WardrobeSlider>();
            foreach (var group in sliders.GroupBy(s => string.IsNullOrWhiteSpace(s.group) ? "Body" : s.group)) {
                if (!_groups.TryGetValue(group.Key, out bool open)) open = true;
                _groups[group.Key] = open = EditorGUILayout.Foldout(open, group.Key, true);
                if (!open) continue;
                using (new EditorGUI.IndentLevelScope()) {
                    foreach (var s in group) DrawSlider(avatar, s);
                }
            }

            var other = WardrobeBody.OtherShapes(avatar, b);
            _otherShapes = EditorGUILayout.Foldout(_otherShapes, sliders.Count > 0 ? $"Other shape keys ({other.Count})" : $"Shape keys ({other.Count})", true);
            if (_otherShapes) {
                using (new EditorGUI.IndentLevelScope()) {
                    _search = EditorGUILayout.TextField("Search", _search);
                    foreach (string shape in other.Where(s => _search.Length == 0 || s.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)) {
                        float w = WardrobeBody.ShapeWeight(avatar, b, shape);
                        EditorGUI.BeginChangeCheck();
                        w = EditorGUILayout.Slider(shape, w, 0, 100);
                        if (EditorGUI.EndChangeCheck()) WardrobeBody.SetShapeWeight(avatar, b, shape, w);
                    }
                }
            }
            EditorGUILayout.LabelField("Worn garments follow the body. In-game, the avatar's own menu can still change what it animates.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawSlider(GameObject avatar, WardrobeSlider s) {
            EditorGUI.BeginChangeCheck();
            if (s.IsToggle) {
                bool on = EditorGUILayout.Toggle(s.name, WardrobeBody.Option(avatar, s) == 1);
                if (EditorGUI.EndChangeCheck()) WardrobeBody.SetOption(avatar, s, on ? 1 : 0);
            } else if (s.IsChoice) {
                int option = EditorGUILayout.Popup(s.name, WardrobeBody.Option(avatar, s), s.options.Select(o => o.name).ToArray());
                if (EditorGUI.EndChangeCheck()) WardrobeBody.SetOption(avatar, s, option);
            } else {
                float value = EditorGUILayout.Slider(s.name, WardrobeBody.Value(avatar, s), 0, 100);
                if (EditorGUI.EndChangeCheck()) WardrobeBody.SetValue(avatar, s, value);
            }
        }

        private void DrawCustomSkin(GameObject avatar, WardrobeAvatar b) {
            GUILayout.Label("Custom skin", EditorStyles.boldLabel);
            if (_custom == null || _customFor != avatar.GetInstanceID() + b.id) {
                string worn = WardrobeSkins.Worn(avatar, b);
                var skin = WardrobeSkins.For(b).FirstOrDefault(s => WardrobeAssets.FileName(s.DisplayName) == worn);
                _custom = skin != null ? SlotColor.Copy(skin.fur) : Grey();
                _customFor = avatar.GetInstanceID() + b.id;
            }
            _custom = EditorGUILayout.GradientField(new GUIContent("Fur", "From the fur's darkest parts (left) to its lightest (right)"), _custom);
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Apply", GUILayout.Width(70))) {
                    var skin = new WardrobeSkin { name = "Custom", avatarId = b.id, fur = SlotColor.Copy(_custom) };
                    EditorApplication.delayCall += () => WardrobeSkins.Apply(avatar, b, skin);
                }
                if (GUILayout.Button("Save as Skin…", GUILayout.Width(110))) EditorApplication.delayCall += () => SaveSkin(avatar, b);
            }
        }

        private static Gradient Grey() {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.27f, 0.27f, 0.27f), 0), new GradientColorKey(new Color(0.96f, 0.96f, 0.96f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        // ---------------------------------------------------------------- saving

        private static string AvatarFolder(WardrobeAvatar b) =>
            b == null ? "Assets/Wardrobe Recolors" : $"Assets/Wardrobe Recolors/{WardrobeAssets.FileName(b.DisplayName)}";

        private static void SaveBody(GameObject avatar, WardrobeAvatar b) {
            string folder = AvatarFolder(b) + "/Bodies";
            WardrobeAssets.EnsureFolder(folder);
            string path = EditorUtility.SaveFilePanelInProject("Save Body Preset", "Body", WardrobeConfigs.Body.TrimStart('.'),
                "Save the body's shape as a .rbody so it shows up under Body.", folder);
            if (string.IsNullOrEmpty(path)) return;
            var preset = WardrobeBody.Capture(avatar, b);
            preset.name = Path.GetFileNameWithoutExtension(path);
            preset.id = WardrobeBodies.All.FirstOrDefault(p => p.path == path)?.id;
            WardrobeBodies.Save(path, preset);
        }

        private void SaveSkin(GameObject avatar, WardrobeAvatar b) {
            string folder = WardrobeSkins.Folder(b);
            WardrobeAssets.EnsureFolder(folder);
            string path = EditorUtility.SaveFilePanelInProject("Save Skin", "Skin", WardrobeConfigs.Skin.TrimStart('.'),
                "Save the fur colours as a .rskin so they show up under Skin.", folder);
            if (string.IsNullOrEmpty(path)) return;
            var skin = new WardrobeSkin {
                name = Path.GetFileNameWithoutExtension(path), avatarId = b.id, fur = SlotColor.Copy(_custom),
                id = WardrobeSkins.All.FirstOrDefault(s => s.path == path)?.id,
            };
            WardrobeSkins.Save(path, skin);
            WardrobeSkins.Forget();
            WardrobeSkins.Apply(avatar, b, WardrobeSkins.All.FirstOrDefault(s => s.path == path) ?? skin);
        }
    }
}
