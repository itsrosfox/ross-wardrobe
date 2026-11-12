using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The window's Setup tab, for creators: what the checks found in the project's files, and the project's garments,
    /// avatar bases, material types, patterns and presets on the left; the picked one's guided setup on the right (the same
    /// editor as its inspector), where a garment is fitted to its avatar and its prefab and shipped material are made.
    /// </summary>
    internal class WardrobeSetupPanel {
        private readonly System.Action _repaint;
        private WardrobeForm _form;
        private Vector2 _listScroll, _formScroll;
        private bool _problemsOpen = true, _infoOpen;
        private readonly Dictionary<string, bool> _sections = new Dictionary<string, bool>();

        public WardrobeSetupPanel(System.Action repaint) => _repaint = repaint;

        /// <summary>The file being set up (kept by the window across reloads).</summary>
        public string Path { get; private set; }

        public void Open(string path) {
            if (path == Path && _form != null) return;
            if (!Close()) return;
            Path = path;
            _form = string.IsNullOrEmpty(path) || !File.Exists(path) ? null : WardrobeForm.Open(path);
            if (_form != null) {
                _form.repaint = _repaint;
                _form.show = OpenLater;
            }
        }

        /// <summary>Lets go of the open file, asking to save its changes; false when that's cancelled.</summary>
        public bool Close() {
            if (_form == null) return true;
            if (_form.Dirty) {
                int choice = EditorUtility.DisplayDialogComplex("Ros' Wardrobe", $"Save your changes to {System.IO.Path.GetFileName(_form.path)}?",
                    "Save", "Cancel", "Discard");
                if (choice == 1) return false;
                if (choice == 0) _form.Save();
            }
            _form.Dispose();
            _form = null;
            Path = null;
            return true;
        }

        public void DrawList(float width) {
            // only scrolls down: everything in the list wraps to its width
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.scrollView);
            Note("Set up your pack here: start with the avatar base, then each garment, then material types and patterns. Each file's " +
                 "setup goes step by step; the checks below tell what's left or wrong in any of them.");
            DrawProblems();
            Section("Avatar bases", WardrobeConfigs.Avatar, "New Avatar Base…", WardrobeConfigImporter.AvatarTemplate,
                "Put it in your pack's Avatars folder so it ships with the garments.", "The avatars garments are fitted to: their body and its shape keys.");
            Section("Garments", WardrobeConfigs.Garment, "New Garment…", WardrobeConfigImporter.GarmentTemplate,
                "Put it in the garment's folder, next to its model and maps.", "One file per garment fit, next to the cloth tool's export.");
            Section("Material types", WardrobeConfigs.Material, "New Material Type…", WardrobeConfigImporter.MaterialTemplate,
                "Put it in your pack's Materials folder, next to the material it links.", "A material (Poiyomi, lilToon…) and how the garments' maps go into it.");
            Section("Patterns", WardrobeConfigs.Pattern, "New Pattern…", WardrobeConfigImporter.PatternTemplate,
                "Put it in your pack's Patterns folder, next to its textures.", "What regions can be: stripes, prints, colour layouts.");
            Section("Presets", WardrobeConfigs.Preset, "New Preset…", WardrobeConfigImporter.PresetTemplate,
                "Put it next to the garment it's for.", "Ready-made looks; the window's Save as Preset makes them.");
            EditorGUILayout.EndScrollView();
        }

        // What the checks found in every Ros' Wardrobe file: errors and warnings open, notes folded away.
        private void DrawProblems() {
            var all = WardrobeChecks.All;
            int errors = all.Count(p => p.type == MessageType.Error), warnings = all.Count(p => p.type == MessageType.Warning);
            int infos = all.Count - errors - warnings;
            EditorGUILayout.Space(4);
            string title = errors + warnings == 0 ? "Checks: all good" + (infos > 0 ? $" ({infos} notes)" : "")
                : $"Checks: {Count(errors, "error")}{(errors > 0 && warnings > 0 ? ", " : "")}{Count(warnings, "warning")}";
            _problemsOpen = EditorGUILayout.Foldout(_problemsOpen, title, true, EditorStyles.foldoutHeader);
            if (!_problemsOpen) return;
            foreach (var p in all.Where(p => p.type != MessageType.Info).Take(40)) WardrobeForm.DrawProblem(p, _form, OpenLater);
            if (errors + warnings > 40) Note($"{errors + warnings - 40} more: fix these first.");
            if (infos > 0) {
                _infoOpen = EditorGUILayout.Foldout(_infoOpen, $"Notes ({infos})", true);
                if (_infoOpen)
                    foreach (var p in all.Where(p => p.type == MessageType.Info).Take(40)) WardrobeForm.DrawProblem(p, _form, OpenLater);
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Check Again", "Read every file again"), EditorStyles.miniButton, GUILayout.Width(90))) WardrobeChecks.Forget();
            }
        }

        private static string Count(int n, string what) => n == 0 ? "" : $"{n} {what}{(n == 1 ? "" : "s")}";

        public void DrawEditor() {
            if (Path != null && _form == null) Open(Path);
            if (_form == null) {
                EditorGUILayout.HelpBox("Pick a file on the left to set it up, or make a new one. A pack usually starts with its avatar base " +
                                        "(New Avatar Base…), then a garment per cloth export (New Garment…).", MessageType.Info);
                return;
            }
            if (!File.Exists(_form.path)) {
                _form.Dispose();
                _form = null;
                Path = null;
                return;
            }
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label(System.IO.Path.GetFileName(_form.path), EditorStyles.largeLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Show", "Show the file in the Project window"), GUILayout.Width(60)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(_form.path));
            }
            _form.DrawToolbar();
            EditorGUILayout.Space();
            _formScroll = EditorGUILayout.BeginScrollView(_formScroll);
            float labelWidth = EditorGUIUtility.labelWidth;
            try {
                EditorGUIUtility.labelWidth = 150;
                _form.DrawProblems();
                _form.Draw();
            } finally {
                EditorGUIUtility.labelWidth = labelWidth;
                EditorGUILayout.EndScrollView();
            }
        }

        private void Section(string title, string extension, string create, string template, string where, string about) {
            EditorGUILayout.Space(4);
            var paths = WardrobeConfigs.Paths(extension).OrderBy(System.IO.Path.GetFileNameWithoutExtension).ToList();
            // long lists (patterns, presets) start folded, unless the open file is in them
            if (!_sections.TryGetValue(title, out bool shown)) shown = paths.Count <= 12 || paths.Contains(Path);
            _sections[title] = shown = EditorGUILayout.Foldout(shown, $"{title} ({paths.Count})", true, EditorStyles.foldoutHeader);
            if (!shown) return;
            if (paths.Count == 0) Note("None in the project yet. " + about);
            foreach (string path in paths) {
                bool open = path == Path;
                var problems = WardrobeChecks.For(path);
                var worst = problems.Count == 0 ? (MessageType?)null : problems.Min(p => Rank(p.type)) == 0 ? MessageType.Error
                    : problems.Any(p => p.type == MessageType.Warning) ? MessageType.Warning : MessageType.Info;
                using (new EditorGUILayout.HorizontalScope()) {
                    var style = open ? EditorStyles.toolbarButton : EditorStyles.label;
                    if (GUILayout.Button(new GUIContent(System.IO.Path.GetFileNameWithoutExtension(path), path), style)) OpenLater(path);
                    if (worst == MessageType.Error || worst == MessageType.Warning) {
                        var icon = EditorGUIUtility.IconContent(worst == MessageType.Error ? "console.erroricon.sml" : "console.warnicon.sml");
                        icon.tooltip = string.Join("\n", problems.Where(p => p.type != MessageType.Info).Select(p => p.message));
                        GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(16));
                    }
                }
            }
            if (GUILayout.Button(create, EditorStyles.miniButton, GUILayout.Width(150))) {
                string folder = Path != null ? System.IO.Path.GetDirectoryName(Path).Replace('\\', '/') : "Assets";
                string name = create.TrimEnd('…');
                string path = EditorUtility.SaveFilePanelInProject(name, name, extension.TrimStart('.'), where, folder);
                if (string.IsNullOrEmpty(path)) GUIUtility.ExitGUI();
                File.WriteAllText(path, template);
                AssetDatabase.ImportAsset(path);
                OpenLater(path);
                GUIUtility.ExitGUI();
            }
        }

        private static int Rank(MessageType t) => t == MessageType.Error ? 0 : t == MessageType.Warning ? 1 : 2;

        // after the GUI has drawn (opening may ask to save the open file)
        private void OpenLater(string path) => EditorApplication.delayCall += () => {
            Open(path);
            _repaint();
        };

        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
    }
}
