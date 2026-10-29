using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Small asset helpers shared by the baker and the generators.</summary>
    internal static class WardrobeAssets {
        internal static readonly Dictionary<string, Action<TextureImporter>> PendingSettings =
            new Dictionary<string, Action<TextureImporter>>();

        public static void EnsureFolder(string folder) {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        public static string FileName(string name) {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        /// <summary>The garment's name for generated files: "Hoodie Wardrobe" → "Hoodie".</summary>
        public static string BaseName(WardrobeGarment g) {
            const string suffix = " Wardrobe";
            return g.name.EndsWith(suffix) && g.name.Length > suffix.Length ? g.name.Substring(0, g.name.Length - suffix.Length) : g.name;
        }

        public static string FolderOf(UnityEngine.Object asset) {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException($"'{asset.name}' isn't saved as an asset yet.");
            return Path.GetDirectoryName(path).Replace('\\', '/');
        }

        /// <summary>
        /// Writes an image into the project and imports it once with the given settings
        /// (new files get them on their first import, so big textures aren't compressed twice).
        /// </summary>
        public static Texture2D WriteTexture(string path, byte[] bytes, Action<TextureImporter> settings) {
            var existing = AssetImporter.GetAtPath(path) as TextureImporter;
            File.WriteAllBytes(path, bytes);
            if (existing != null) {
                settings?.Invoke(existing);
                AssetDatabase.WriteImportSettingsIfDirty(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            } else {
                if (settings != null) PendingSettings[path] = settings;
                try {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                } finally {
                    PendingSettings.Remove(path);
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Import settings of an existing texture (type, sRGB, size, compression, platform overrides).</summary>
        public static Action<TextureImporter> CopySettingsFrom(Texture texture) {
            var source = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (source == null) return null;
            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            var platforms = new List<TextureImporterPlatformSettings> { source.GetDefaultPlatformTextureSettings() };
            foreach (string platform in new[] { "Standalone", "Android", "iPhone" }) {
                var p = source.GetPlatformTextureSettings(platform);
                if (p.overridden) platforms.Add(p);
            }
            return importer => {
                importer.SetTextureSettings(settings);
                foreach (var p in platforms) importer.SetPlatformTextureSettings(p);
            };
        }
    }

    internal class WardrobeTexturePostprocessor : AssetPostprocessor {
        private void OnPreprocessTexture() {
            if (WardrobeAssets.PendingSettings.TryGetValue(assetPath, out var settings))
                settings((TextureImporter)assetImporter);
        }
    }
}
