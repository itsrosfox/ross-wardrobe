using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>The window's Avatar section: the body's sliders (from its .ravt) and every other shape key.</summary>
    internal class WardrobeAvatarPanel {
        private readonly Dictionary<string, bool> _groups = new Dictionary<string, bool>();
        private bool _otherShapes;
        private string _search = "";

        public void DrawEditor(GameObject avatar, WardrobeAvatar b, float width) {
            GUILayout.Label(avatar.name, EditorStyles.largeLabel);
            GUILayout.Label(b != null ? $"Built on {b.DisplayName}" : "Unknown avatar base", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            DrawBody(avatar, b);
            EditorGUILayout.Space();
            DrawHides(avatar, b);
            DrawClearance(avatar);
        }

        // The body shape keys worn garments suggest hiding. Only the user turns them on (or off); nothing sets them by itself.
        private static void DrawHides(GameObject avatar, WardrobeAvatar b) {
            var hides = WardrobeBody.SuggestedHides(avatar);
            if (hides.Count == 0) return;
            GUILayout.Label("Suggested Hides", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Body shape keys the worn garments suggest turning on under them. They're only changed when you turn them on here.",
                EditorStyles.wordWrappedMiniLabel);
            foreach (var (shape, garments) in hides) {
                bool on = WardrobeBody.ShapeWeight(avatar, b, shape) >= 99.5f;
                bool want = EditorGUILayout.ToggleLeft(new GUIContent(shape, "Suggested by " + string.Join(", ", garments)), on);
                if (want != on) WardrobeBody.SetShapeWeight(avatar, b, shape, want ? 100 : 0);
            }
            EditorGUILayout.Space();
        }

        private static void DrawClearance(GameObject avatar) {
            GUILayout.Label("Clearance", EditorStyles.boldLabel);
            bool on = EditorGUILayout.ToggleLeft(new GUIContent("Fix clipping", "After the body or what's worn changes, move worn garments' cloth " +
                "out wherever the body still comes through it (a shape key on a copy of the garment's mesh, kept under Wardrobe Recolors)"),
                WardrobeClearance.Enabled);
            if (on != WardrobeClearance.Enabled) WardrobeClearance.Enabled = on;
            if (!on) return;
            var last = WardrobeClearance.LastFor(avatar);
            if (last != null)
                foreach (var r in last)
                    EditorGUILayout.LabelField(r.error != null ? $"Failed: {r.error}"
                        : r.moved == 0 ? $"{r.garment}: doesn't clip." : $"{r.garment}: {r.moved} vertices moved out (up to {r.mm:0.#} mm).",
                        EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("Runs by itself after a change, and before a VRChat build.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Check Again", GUILayout.Width(100))) {
                    try {
                        WardrobeClearance.Apply(avatar, again: true);
                    } catch (Exception e) {
                        Debug.LogException(e, avatar);
                    }
                }
            }
        }

        private void DrawBody(GameObject avatar, WardrobeAvatar b) {
            GUILayout.Label("Body", EditorStyles.boldLabel);
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
    }
}
