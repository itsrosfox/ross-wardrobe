using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Turns a Marvelous Designer / CLO fabric (.zfab) into a pattern (a folder with a .rpat): the fabric's
    /// relief (normal map) and surface (metalness, roughness, opacity) in one colour of the look's choosing. The fabric's
    /// own colour texture isn't used.
    /// </summary>
    public static class WardrobeFabricImporter {
        private const int MaxSize = 1024;
        private const float TileWidth = 100;   // millimetres; the height follows the fabric's aspect

        // How much sheen a new pattern starts with, by words in the fabric's name.
        private static readonly (string[] words, float sheen)[] Sheen = {
            (new[] { "velvet", "velour", "plush", "fleece" }, 0.9f),
            (new[] { "corduroy", "cord" }, 0.7f),
            (new[] { "flannel", "melton", "boiled", "wool", "felt" }, 0.45f),
            (new[] { "knit", "jersey", "rib", "waffle", "pique", "tricot", "pointelle", "quilted", "dobby" }, 0.3f),
        };

        [MenuItem("Assets/Ros' Wardrobe/Import Marvelous Designer Fabric…")]
        private static void ImportFromMenu() {
            string file = EditorUtility.OpenFilePanel("Import Marvelous Designer fabric", "", "zfab");
            if (string.IsNullOrEmpty(file)) return;
            string folder = SelectedFolder();
            try {
                EditorUtility.DisplayProgressBar("Ros' Wardrobe", "Importing " + Path.GetFileName(file) + "…", 0.5f);
                var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Import(file, folder));
                Selection.activeObject = json;
                EditorGUIUtility.PingObject(json);
            } catch (Exception e) {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Ros' Wardrobe", "Couldn't import the fabric:\n" + e.Message, "OK");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string SelectedFolder() {
            string path = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (string.IsNullOrEmpty(path)) return "Assets";
            return AssetDatabase.IsValidFolder(path) ? path : Path.GetDirectoryName(path).Replace('\\', '/');
        }

        /// <summary>
        /// Imports <paramref name="zfabPath"/> into "&lt;folder&gt;/&lt;Fabric&gt;/" and returns its .rpat. Re-importing
        /// updates the maps and keeps the json's id, name, size and other settings.
        /// </summary>
        public static string Import(string zfabPath, string folder) {
            var entries = ReadZip(zfabPath);
            var images = entries.Where(e => IsImage(e.Key)).ToList();
            var normalEntry = images.FirstOrDefault(e => Has(e.Key, "_NRM") || Has(e.Key, "_NORMAL"));
            if (normalEntry.Key == null && !images.Any(e => Has(e.Key, "_ROUGH") || Has(e.Key, "_MTL") || Has(e.Key, "_ALPHA")))
                throw new InvalidOperationException("The fabric has no normal or surface maps.");

            string displayName = Pretty(Path.GetFileNameWithoutExtension(zfabPath));
            string name = WardrobeAssets.FileName(displayName);
            string dir = $"{folder}/{name}";
            WardrobeAssets.EnsureFolder(dir);

            var normalSource = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try {
                bool hasNormal = normalEntry.Key != null && normalSource.LoadImage(normalEntry.Value);
                int w = hasNormal ? normalSource.width : MaxSize, h = hasNormal ? normalSource.height : MaxSize;
                WriteTexture($"{dir}/{name}_Surface.png", Encode(Surface(images, w, h), w, h));
                string normalFile = null;
                if (hasNormal) {
                    normalFile = $"{name}_Normal{Path.GetExtension(normalEntry.Key).ToLowerInvariant()}";
                    WardrobeAssets.WriteTexture($"{dir}/{normalFile}", normalEntry.Value, ti => {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.wrapMode = TextureWrapMode.Repeat;
                        ti.maxTextureSize = MaxSize;
                        ti.anisoLevel = 4;
                    });
                }

                string jsonPath = Directory.GetFiles(dir, "*" + WardrobeConfigs.Pattern).Select(p => p.Replace('\\', '/')).FirstOrDefault() ??
                                  $"{dir}/{name}{WardrobeConfigs.Pattern}";
                WardrobePatterns.Json json = null;
                if (File.Exists(jsonPath))
                    try { json = JsonUtility.FromJson<WardrobePatterns.Json>(File.ReadAllText(jsonPath)); } catch (Exception) { }
                if (json == null)
                    json = new WardrobePatterns.Json {
                        id = "imported." + Regex.Replace(displayName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'),
                        version = "1.0.0", name = displayName, group = "Weaves",
                        tileSize = new[] { TileWidth, TileWidth * h / w },
                        sheen = Sheen.FirstOrDefault(r => r.words.Any(word => Has(displayName, word))).sheen is float sheen && sheen > 0 ? sheen : 0.15f,
                        colors = new[] { new WardrobePatterns.SlotJson { name = "Colour", color = "#F4F4F2", canGradient = true } },
                    };
                json.normal = normalFile ?? "";
                json.surface = $"{name}_Surface.png";
                File.WriteAllText(jsonPath, JsonUtility.ToJson(json, true).Replace("\r\n", "\n") + "\n");
                AssetDatabase.ImportAsset(jsonPath);
                Debug.Log($"[Ros' Wardrobe] Imported fabric '{json.name}' as pattern '{json.id}'");
                return jsonPath;
            } finally {
                Object.DestroyImmediate(normalSource);
            }
        }

        // ---------------------------------------------------------------- surface

        // Surface: R metalness, G roughness, A opacity (from the fabric's MTL / ROUGH / ALPHA maps, resampled to the
        // normal map's size). Missing maps mean non-metal, roughness 0.8 (cloth) and opaque.
        private static byte[] Surface(List<KeyValuePair<string, byte[]>> images, int w, int h) {
            Texture2D Load(string key) {
                var entry = images.FirstOrDefault(e => Has(e.Key, key));
                if (entry.Key == null) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (t.LoadImage(entry.Value)) return t;
                Object.DestroyImmediate(t);
                return null;
            }
            var metal = Load("_MTL");
            var rough = Load("_ROUGH");
            var alpha = Load("_ALPHA");
            try {
                var data = new byte[w * h * 4];
                for (int y = 0; y < h; y++) {
                    float v = (y + 0.5f) / h;
                    for (int x = 0; x < w; x++) {
                        float u = (x + 0.5f) / w;
                        int o = (y * w + x) * 4;
                        data[o] = metal != null ? (byte)(metal.GetPixelBilinear(u, v).r * 255) : (byte)0;
                        data[o + 1] = rough != null ? (byte)(rough.GetPixelBilinear(u, v).r * 255) : (byte)204;
                        data[o + 3] = alpha != null ? (byte)(alpha.GetPixelBilinear(u, v).r * 255) : (byte)255;
                    }
                }
                return data;
            } finally {
                if (metal != null) Object.DestroyImmediate(metal);
                if (rough != null) Object.DestroyImmediate(rough);
                if (alpha != null) Object.DestroyImmediate(alpha);
            }
        }

        // ---------------------------------------------------------------- helpers

        private static byte[] Encode(byte[] data, int w, int h) {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            try {
                tex.SetPixelData(data, 0);
                tex.Apply(false);
                return tex.EncodeToPNG();
            } finally {
                Object.DestroyImmediate(tex);
            }
        }

        private static Texture2D WriteTexture(string path, byte[] png) =>
            WardrobeAssets.WriteTexture(path, png, ti => {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = MaxSize;
                ti.anisoLevel = 4;
            });

        private static Dictionary<string, byte[]> ReadZip(string path) {
            byte[] bytes = File.ReadAllBytes(path);
            // .zfab files start with a small header before the zip data; try as-is, then from the first zip entry.
            foreach (int start in new[] { 0, IndexOf(bytes, new byte[] { 0x50, 0x4B, 0x03, 0x04 }) }) {
                if (start < 0) continue;
                try {
                    using (var stream = new MemoryStream(bytes, start, bytes.Length - start))
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                        var result = new Dictionary<string, byte[]>();
                        foreach (var entry in zip.Entries) {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            using (var s = entry.Open())
                            using (var ms = new MemoryStream()) {
                                s.CopyTo(ms);
                                result[entry.Name] = ms.ToArray();
                            }
                        }
                        return result;
                    }
                } catch (InvalidDataException) {
                }
            }
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' isn't a readable fabric file.");
        }

        private static int IndexOf(byte[] data, byte[] pattern) {
            for (int i = 0; i <= data.Length - pattern.Length; i++) {
                int j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        private static bool IsImage(string name) {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg";
        }

        private static bool Has(string name, string key) => name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;

        // "V2_Cut_Sew_Knit_Waffle_1" → "Knit Waffle 1", "V2_Woven_Flannel_3" → "Flannel 3"
        internal static string Pretty(string fileName) {
            string s = Regex.Replace(fileName, @"^V\d+_", "");
            s = Regex.Replace(s, @"^Cut_Sew_", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^Woven_", "", RegexOptions.IgnoreCase);
            s = s.Replace('_', ' ');
            s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ");
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
