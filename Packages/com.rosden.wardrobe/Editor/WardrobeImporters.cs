using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Imports a .rgarm as the <see cref="WardrobeGarment"/> worn garments refer to.</summary>
    [ScriptedImporter(1, "rgarm")]
    internal class WardrobeGarmentImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var g = ScriptableObject.CreateInstance<WardrobeGarment>();
            try {
                WardrobeGarments.Fill(g, WardrobeJson.Parse(File.ReadAllText(ctx.assetPath)), ctx.assetPath, path => {
                    // so what it names is imported first, and a change to it imports the garment again
                    if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) ctx.DependsOnArtifact(path);
                });
            } catch (Exception e) {
                ctx.LogImportError($"Couldn't read {ctx.assetPath}: {e.Message}");
                g.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            }
            ctx.AddObjectToAsset("garment", g, Icon("Cloth Icon"));
            ctx.SetMainObject(g);
        }

        internal static Texture2D Icon(string name) => EditorGUIUtility.FindTexture(name) ?? EditorGUIUtility.FindTexture("TextAsset Icon");
    }

    /// <summary>Imports .rpat, .rpre, .ravt and .rmat files as text (Ros' Wardrobe reads the files themselves).</summary>
    [ScriptedImporter(1, new[] { "rpat", "rpre", "ravt", "rmat" })]
    internal class WardrobeConfigImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var text = new TextAsset(File.ReadAllText(ctx.assetPath)) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("config", text, WardrobeGarmentImporter.Icon(IconOf(Path.GetExtension(ctx.assetPath).ToLowerInvariant())));
            ctx.SetMainObject(text);
        }

        private static string IconOf(string extension) {
            switch (extension) {
                case WardrobeConfigs.Pattern: return "Texture Icon";
                case WardrobeConfigs.Preset: return "Favorite Icon";
                case WardrobeConfigs.Avatar: return "Avatar Icon";
                case WardrobeConfigs.Material: return "Material Icon";
                default: return "AvatarMask Icon";
            }
        }

        // New files start from the smallest file of their kind; the inspector edits the rest.
        internal const string GarmentTemplate =
            "{\n    \"id\": \"\",\n    \"name\": \"\",\n    \"version\": \"1.0.0\",\n    \"author\": \"\",\n    \"category\": \"Tops\",\n    \"slot\": \"Top\"\n}\n";
        internal const string AvatarTemplate =
            "{\n    \"id\": \"\",\n    \"name\": \"\",\n    \"version\": \"\",\n    \"author\": \"\",\n    \"matches\": [],\n    \"guids\": []\n}\n";
        internal const string MaterialTemplate =
            "{\n    \"id\": \"\",\n    \"name\": \"\",\n    \"version\": \"1.0.0\",\n    \"author\": \"\",\n" +
            "    \"textures\": [\n        { \"property\": \"_MainTex\", \"channels\": \"albedo\" },\n" +
            "        { \"property\": \"_BumpMap\", \"channels\": \"normal\" }\n    ]\n}\n";

        [MenuItem("Assets/Create/Ros' Wardrobe/Garment", false, 2000)]
        private static void NewGarment() => New("New Garment" + WardrobeConfigs.Garment, GarmentTemplate);

        internal const string PatternTemplate =
            "{\n    \"id\": \"\",\n    \"name\": \"\",\n    \"version\": \"1.0.0\",\n    \"author\": \"\",\n    \"tileSize\": [10, 10],\n" +
            "    \"colors\": [\n        {\n            \"name\": \"Colour\",\n            \"color\": \"#F4F4F2\",\n            \"canGradient\": true\n        }\n    ]\n}\n";
        internal const string PresetTemplate =
            "{\n    \"id\": \"\",\n    \"name\": \"\",\n    \"version\": \"1.0.0\",\n    \"author\": \"\",\n" +
            "    \"parts\": [\n        {\n            \"region\": \"*\",\n            \"pattern\": \"rosden.solid\",\n            \"colors\": [\"#F4F4F2\"]\n        }\n    ]\n}\n";

        [MenuItem("Assets/Create/Ros' Wardrobe/Pattern", false, 2001)]
        private static void NewPattern() => New("New Pattern" + WardrobeConfigs.Pattern, PatternTemplate);

        [MenuItem("Assets/Create/Ros' Wardrobe/Preset", false, 2002)]
        private static void NewPreset() => New("New Preset" + WardrobeConfigs.Preset, PresetTemplate);

        [MenuItem("Assets/Create/Ros' Wardrobe/Avatar Base", false, 2003)]
        private static void NewAvatar() => New("New Avatar" + WardrobeConfigs.Avatar, AvatarTemplate);

        [MenuItem("Assets/Create/Ros' Wardrobe/Material Type", false, 2004)]
        private static void NewMaterial() => New("New Material Type" + WardrobeConfigs.Material, MaterialTemplate);

        private static void New(string file, string json) => ProjectWindowUtil.CreateAssetWithContent(file, json);
    }

    /// <summary>The inspector of a Ros' Wardrobe file: its editor, saving to the file (there are no import settings to apply).</summary>
    internal abstract class WardrobeConfigInspector : ScriptedImporterEditor {
        private WardrobeForm _form;

        protected override bool needsApplyRevert => false;

        public override bool showImportedObject => false;

        public override void OnDisable() {
            var form = _form;
            _form = null;
            if (form != null) {
                if (form.Dirty && !EditorApplication.isUpdating && !EditorApplication.isCompiling &&
                    EditorUtility.DisplayDialog("Ros' Wardrobe", $"Save your changes to {Path.GetFileName(form.path)}?", "Save", "Discard"))
                    EditorApplication.delayCall += () => {
                        form.Save();
                        form.Dispose();
                    };
                else form.Dispose();
            }
            base.OnDisable();
        }

        public override void OnInspectorGUI() {
            if (targets.Length > 1) {
                EditorGUILayout.HelpBox("Select one file to edit it.", MessageType.Info);
                return;
            }
            string path = ((AssetImporter)target).assetPath;
            if (_form == null || _form.path != path) {
                _form?.Dispose();
                _form = WardrobeForm.Open(path);
                if (_form == null) return;
                _form.repaint = Repaint;
            }
            float labelWidth = EditorGUIUtility.labelWidth;
            try {
                EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.36f, 110, 160);
                _form.DrawToolbar();
                EditorGUILayout.Space();
                _form.DrawProblems();
                _form.Draw();
            } finally {
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }
    }

    [CustomEditor(typeof(WardrobeGarmentImporter))]
    internal class WardrobeGarmentInspector : WardrobeConfigInspector { }

    [CustomEditor(typeof(WardrobeConfigImporter))]
    internal class WardrobeConfigFileInspector : WardrobeConfigInspector { }
}
