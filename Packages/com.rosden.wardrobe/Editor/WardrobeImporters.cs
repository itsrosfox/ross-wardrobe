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
                WardrobeGarments.Fill(g, WardrobeGarments.ReadJson(ctx.assetPath), ctx.assetPath, path => {
                    // so what it names is imported first, and a change to it imports the garment again
                    if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))) ctx.DependsOnArtifact(path);
                });
            } catch (Exception e) {
                ctx.LogImportError($"Couldn't read {ctx.assetPath}: {e.Message}");
                g.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            }
            ctx.AddObjectToAsset("garment", g, WardrobeIcons.Garment);
            ctx.SetMainObject(g);
        }
    }

    /// <summary>Imports .rpat, .rpre, .ravt, .rmat, .rbody and .rskin files as text (Ros' Wardrobe reads the files themselves).</summary>
    [ScriptedImporter(1, new[] { "rpat", "rpre", "ravt", "rmat", "rbody", "rskin" })]
    internal class WardrobeConfigImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var text = new TextAsset(File.ReadAllText(ctx.assetPath)) { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("config", text, WardrobeIcons.For(WardrobeMigrations.KindOf(ctx.assetPath)));
            ctx.SetMainObject(text);
        }
    }

    internal static class WardrobeIcons {
        public static Texture2D Garment => Find("Cloth Icon", "ScriptableObject Icon");

        public static Texture2D For(WardrobeMigrations.Kind? kind) {
            switch (kind) {
                case WardrobeMigrations.Kind.Garment: return Garment;
                case WardrobeMigrations.Kind.Pattern: return Find("Texture Icon", "TextAsset Icon");
                case WardrobeMigrations.Kind.Preset: return Find("Favorite Icon", "TextAsset Icon");
                case WardrobeMigrations.Kind.Avatar: return Find("Avatar Icon", "TextAsset Icon");
                case WardrobeMigrations.Kind.Material: return Find("Material Icon", "TextAsset Icon");
                case WardrobeMigrations.Kind.Body: return Find("AvatarMask Icon", "Avatar Icon", "TextAsset Icon");
                case WardrobeMigrations.Kind.Skin: return Find("Texture Icon", "TextAsset Icon");
                default: return Find("TextAsset Icon");
            }
        }

        private static Texture2D Find(params string[] names) {
            foreach (string name in names) {
                var icon = EditorGUIUtility.FindTexture(name);
                if (icon != null) return icon;
            }
            return null;
        }
    }

    /// <summary>
    /// The inspector of a Ros' Wardrobe file: the same editor as Project Settings → Ros' Wardrobe, saving to the file
    /// (there are no import settings to apply).
    /// </summary>
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
                _form.DrawToolbar(false);
                EditorGUILayout.Space();
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
