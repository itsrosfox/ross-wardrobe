using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The window's Setup tab, for creators: the project's avatar bases, garments and material types on the left, the
    /// picked one's setup on the right (the same editor as its inspector), where a garment is fitted to its avatar and its
    /// shipped material is made.
    /// </summary>
    internal class WardrobeSetupPanel {
        private readonly System.Action _repaint;
        private WardrobeForm _form;
        private Vector2 _listScroll, _formScroll;

        public WardrobeSetupPanel(System.Action repaint) => _repaint = repaint;

        /// <summary>The file being set up (kept by the window across reloads).</summary>
        public string Path { get; private set; }

        public void Open(string path) {
            if (path == Path && _form != null) return;
            if (!Close()) return;
            Path = path;
            _form = string.IsNullOrEmpty(path) || !File.Exists(path) ? null : WardrobeForm.Open(path);
            if (_form != null) _form.repaint = _repaint;
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
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            Note("Prepare garments and avatar bases here. Garments ship with their .rgarm, mesh, prefab and maps in their own folder.");
            Section("Garments", WardrobeConfigs.Garment, "New Garment…", WardrobeConfigImporter.GarmentTemplate,
                "Put it in the garment's folder, next to its model and maps.");
            Section("Avatar bases", WardrobeConfigs.Avatar, "New Avatar Base…", WardrobeConfigImporter.AvatarTemplate,
                "Put it in your pack's Avatars folder so it ships with the garments.");
            Section("Material types", WardrobeConfigs.Material, "New Material Type…", WardrobeConfigImporter.MaterialTemplate,
                "Put it in your pack's Materials folder, next to its template material.");
            EditorGUILayout.EndScrollView();
        }

        public void DrawEditor() {
            if (Path != null && _form == null) Open(Path);
            if (_form == null) {
                EditorGUILayout.HelpBox("Pick a garment, avatar base or material type on the left to set it up.", MessageType.Info);
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
                _form.Draw();
            } finally {
                EditorGUIUtility.labelWidth = labelWidth;
                EditorGUILayout.EndScrollView();
            }
        }

        private void Section(string title, string extension, string create, string template, string where) {
            EditorGUILayout.Space(4);
            GUILayout.Label(title, EditorStyles.boldLabel);
            var paths = WardrobeConfigs.Paths(extension).OrderBy(System.IO.Path.GetFileNameWithoutExtension).ToList();
            if (paths.Count == 0) Note("None in the project yet.");
            foreach (string path in paths) {
                bool open = path == Path;
                var style = open ? EditorStyles.toolbarButton : EditorStyles.label;
                if (GUILayout.Button(new GUIContent(System.IO.Path.GetFileNameWithoutExtension(path), path), style)) OpenLater(path);
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

        // after the GUI has drawn (opening may ask to save the open file)
        private void OpenLater(string path) => EditorApplication.delayCall += () => {
            Open(path);
            _repaint();
        };

        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
    }
}
