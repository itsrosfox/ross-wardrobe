using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using IOFile = System.IO.File;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Turns the files of Ros' Wardrobe before config files (a garment's garment.json and Wardrobe Garment asset,
    /// pattern.json, *.preset.json) into .rgarm, .rpat and .rpre files, with the .ravt and .rmat files they need, and
    /// points scenes and prefabs at the new garments. The originals are copied to Library/Ros' Wardrobe/Backups first.
    /// </summary>
    public static class WardrobeLegacy {
        public const string GarmentJson = "garment.json", PatternJson = "pattern.json", PresetSuffix = ".preset.json";

        public sealed class OldFile {
            public WardrobeMigrations.Kind kind;
            public string path;    // the json (null for a garment asset without one)
            public string asset;   // a garment's Wardrobe Garment asset
            public string Shown => path ?? asset;
        }

        /// <summary>The old files in the project (those that can be changed: in Assets or an embedded or local package).</summary>
        public static List<OldFile> Scan() {
            var list = new List<OldFile>();
            var assets = AssetDatabase.FindAssets("t:WardrobeGarment").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            foreach (string path in Find("garment", p => Path.GetFileName(p).Equals(GarmentJson, StringComparison.OrdinalIgnoreCase))) {
                string asset = null;
                try {
                    string named = WardrobeJson.Parse(IOFile.ReadAllText(path)).String("wardrobe");
                    if (named.Length > 0) asset = WardrobeConfigs.Resolve(path, named);
                } catch (Exception) { }
                if (asset == null || !assets.Contains(asset))
                    asset = assets.FirstOrDefault(a => Path.GetDirectoryName(a) == Path.GetDirectoryName(path));
                assets.Remove(asset);
                list.Add(new OldFile { kind = WardrobeMigrations.Kind.Garment, path = path, asset = asset });
            }
            list.AddRange(assets.Select(a => new OldFile { kind = WardrobeMigrations.Kind.Garment, asset = a }));
            list.AddRange(Find("pattern", p => Path.GetFileName(p).Equals(PatternJson, StringComparison.OrdinalIgnoreCase))
                .Select(p => new OldFile { kind = WardrobeMigrations.Kind.Pattern, path = p }));
            list.AddRange(Find("preset", p => p.EndsWith(PresetSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(p => new OldFile { kind = WardrobeMigrations.Kind.Preset, path = p }));
            return list.Where(f => Writable(f.Shown)).ToList();
        }

        private static IEnumerable<string> Find(string word, Func<string, bool> match) =>
            AssetDatabase.FindAssets($"{word} t:TextAsset").Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(match).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

        private static bool Writable(string path) {
            if (path.StartsWith("Assets/")) return true;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return package != null && (package.source == PackageSource.Embedded || package.source == PackageSource.Local);
        }

        /// <summary>
        /// Converts every old file in the project; for batch mode
        /// (<c>-executeMethod RosDen.Wardrobe.Editor.WardrobeLegacy.ConvertAll</c>), where it exits with 1 when something went wrong.
        /// </summary>
        public static void ConvertAll() {
            var result = Convert(Scan());
            foreach (string note in result.notes) Debug.Log("[Ros' Wardrobe] " + note);
            foreach (string error in result.errors) Debug.LogError("[Ros' Wardrobe] " + error);
            Debug.Log($"[Ros' Wardrobe] Converted {result.migrated} files" + (result.backups != null ? $" (backups in {result.backups})" : ""));
            if (Application.isBatchMode) EditorApplication.Exit(result.errors.Count == 0 ? 0 : 1);
        }

        // ---------------------------------------------------------------- converting

        /// <summary>Converts the files; the result says what was made and what went wrong.</summary>
        public static WardrobeMigrations.Result Convert(List<OldFile> files) {
            var result = new WardrobeMigrations.Result();
            if (files.Count == 0) return result;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                result.errors.Add("Cancelled: the open scenes have unsaved changes.");
                return result;
            }
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            result.backups = Path.Combine(root, "Library", "Ros' Wardrobe", "Backups", DateTime.Now.ToString("yyyy-MM-dd HHmmss") + " conversion");
            foreach (var f in files)
            foreach (string p in new[] { f.path, f.asset }.Where(p => p != null)) {
                Backup(result.backups, p);
                Backup(result.backups, p + ".meta");
            }

            var repoint = new Dictionary<string, (string guid, long id)>();   // old garment asset guid → the new garment
            var delete = new List<string>();
            foreach (var f in files.Where(f => f.kind == WardrobeMigrations.Kind.Garment))
                Try(result, f.Shown, () => {
                    string made = ConvertGarment(f, result, out string oldGuid);
                    var garment = AssetDatabase.LoadAssetAtPath<WardrobeGarment>(made);
                    if (garment == null) throw new InvalidOperationException($"{made} didn't import as a garment.");
                    if (oldGuid != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(garment, out string guid, out long id)) repoint[oldGuid] = (guid, id);
                    if (f.path != null) delete.Add(f.path);
                    if (f.asset != null) delete.Add(f.asset);
                    result.migrated++;
                    result.notes.Add($"{f.Shown} → {made}");
                });
            foreach (var f in files.Where(f => f.kind != WardrobeMigrations.Kind.Garment))
                Try(result, f.path, () => {
                    string made = ConvertConfig(f);
                    result.migrated++;
                    result.notes.Add($"{f.path} → {made}");
                });

            if (repoint.Count > 0) Try(result, "scenes and prefabs", () => Repoint(repoint, result));
            foreach (string p in delete)
                if (!AssetDatabase.DeleteAsset(p)) result.errors.Add($"Couldn't remove {p}.");
            Refresh();
            return result;
        }

        private static void Try(WardrobeMigrations.Result result, string what, Action action) {
            try {
                action();
            } catch (Exception e) {
                result.errors.Add($"{what}: {e.Message}");
                Debug.LogException(e);
            }
        }

        private static void Backup(string folder, string path) {
            if (!IOFile.Exists(path)) return;
            string to = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            IOFile.Copy(path, to, true);
        }

        private static void Refresh() {
            WardrobeConfigs.Changed();
            WardrobeAvatars.Forget();
            WardrobeMaterialTypes.Forget();
            WardrobePatterns.Forget();
            WardrobePresets.Forget();
        }

        // A pack's root: the folder its Garments (or Patterns) folder is in.
        private static string PackRoot(string path) {
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/') ?? folder;
            string name = Path.GetFileName(parent);
            return name == "Garments" || name == "Patterns" || name == "Presets" ? Path.GetDirectoryName(parent).Replace('\\', '/') : parent;
        }

        // ---------------------------------------------------------------- garments

        private static string ConvertGarment(OldFile f, WardrobeMigrations.Result result, out string oldGuid) {
            var json = f.path != null ? WardrobeJson.Parse(IOFile.ReadAllText(f.path)) : new JsonObject();
            var old = f.asset != null ? AssetDatabase.LoadAssetAtPath<WardrobeGarment>(f.asset) : null;
            oldGuid = f.asset != null ? AssetDatabase.AssetPathToGUID(f.asset) : null;
            string where = f.path ?? f.asset;
            string root = PackRoot(where);

            var g = ScriptableObject.CreateInstance<WardrobeGarment>();
            try {
                if (old != null) {
                    EditorUtility.CopySerialized(old, g);
                    g.legacyPbr = old.legacyPbr;
                }
                string Text(string key, string fallback) => json.Has(key) ? json.String(key) : fallback;
                g.id = Text("id", g.id ?? "").Trim();
                g.displayName = Text("name", g.displayName ?? "").Trim();
                if (g.displayName.Length == 0) g.displayName = old != null ? WardrobeAssets.BaseName(old) : Path.GetFileName(Path.GetDirectoryName(where));
                g.version = Text("version", g.version ?? "1.0.0");
                g.author = Text("author", g.author ?? "");
                g.category = Text("category", g.category ?? "");
                g.slot = Text("slot", g.slot ?? "");
                if (json.Has("prefab")) g.prefab = WardrobeConfigs.Load<GameObject>(f.path, json.String("prefab")) ?? g.prefab;
                if (json.Has("thumbnail")) g.thumbnail = WardrobeConfigs.Load<Texture2D>(f.path, json.String("thumbnail")) ?? g.thumbnail;

                // the avatar it was made for
                var avatar = json.Object("avatar");
                if (avatar != null) {
                    var a = Avatar(avatar.String("name"), (avatar.Array("matches") ?? new List<object>()).OfType<string>().ToList(), g, root, result);
                    g.avatarId = a ?? "";
                    g.avatarVersion = avatar.String("version");
                }

                // its maps, from the PBR maps it had
                var pbr = g.legacyPbr ?? new PbrSource();
                g.normalMap = pbr.normalMap;
                if (pbr.packedMap != null) {
                    MapChannel From(TextureChannel c, bool invert, float fallback) =>
                        c == TextureChannel.None ? new MapChannel(fallback) : new MapChannel(fallback) { texture = pbr.packedMap, channel = c, invert = invert };
                    g.metalness = From(pbr.metallic, false, 0);
                    g.roughness = From(pbr.roughness, pbr.smoothness, 0.8f);
                    g.occlusion = From(pbr.occlusion, false, 1);
                    // the toon layout kept sheen in the packed map's alpha
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pbr.packedMap)) as TextureImporter;
                    g.sheen = importer != null && importer.DoesSourceTextureHaveAlpha() ? From(TextureChannel.A, false, 0.2f) : new MapChannel(0.2f);
                }

                // its materials, as material types
                string first = null;
                foreach (var m in json.Objects("materials")) {
                    string id = MaterialType(m, f.path, g, root, result);
                    first = first ?? id;
                }
                g.defaultMaterial = first ?? "";

                string folder = Path.GetDirectoryName(where).Replace('\\', '/');
                string path = $"{folder}/{WardrobeAssets.FileName(g.displayName)}{WardrobeConfigs.Garment}";
                if (IOFile.Exists(path)) path = AssetDatabase.GenerateUniqueAssetPath(path);
                WardrobeGarments.Save(g, path);
                Refresh();
                return path;
            } finally {
                Object.DestroyImmediate(g);
            }
        }

        // The .ravt of the avatar a garment named (made next to the pack's garments when there's none), or null.
        private static string Avatar(string name, List<string> matches, WardrobeGarment g, string root, WardrobeMigrations.Result result) {
            name = (name ?? "").Trim();
            if (name.Length == 0 && matches.Count == 0) return null;
            var known = WardrobeAvatars.All.FirstOrDefault(a => name.Length > 0 && (WardrobeConfigs.SameId(a.name, name) || WardrobeConfigs.SameId(a.id, name))) ??
                        WardrobeAvatars.All.FirstOrDefault(a => a.matches.Any(m => matches.Any(x => WardrobeConfigs.SameId(m, x))));
            if (known != null) return known.id;

            string prefix = (g.id ?? "").Contains('.') ? g.id.Substring(0, g.id.IndexOf('.')) : "";
            string display = name.Length > 0 ? name : matches[0];
            var j = new JsonObject();
            j["format"] = new JsonNumber(WardrobeMigrations.Current(WardrobeMigrations.Kind.Avatar));
            j["id"] = WardrobeForm.SuggestId(prefix, display);
            j["name"] = display;
            j["version"] = "";
            j["author"] = "";
            j["description"] = "";
            j["matches"] = matches.Cast<object>().ToList();
            // the base's model in this project, by those words
            j["guids"] = AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => matches.Any(m => Path.GetFileNameWithoutExtension(p).Equals(m, StringComparison.OrdinalIgnoreCase)))
                .Select(p => (object)AssetDatabase.AssetPathToGUID(p)).ToList();
            string folder = root + "/Avatars";
            WardrobeAssets.EnsureFolder(folder);
            string path = $"{folder}/{WardrobeAssets.FileName(display)}{WardrobeConfigs.Avatar}";
            Write(path, j);
            result.notes.Add($"Made {path} for the avatar the garments were made for. Check how it's recognised there.");
            return j.String("id");
        }

        // The .rmat for one of a garment's old materials (found by name, else made from it), returning its id.
        private static string MaterialType(JsonObject m, string jsonPath, WardrobeGarment g, string root, WardrobeMigrations.Result result) {
            string name = m.String("name", "Default").Trim();
            var known = WardrobeMaterialTypes.All.FirstOrDefault(t => WardrobeConfigs.SameId(t.name, name) || WardrobeConfigs.SameId(t.id, name));
            if (known != null) return known.id;

            var material = WardrobeConfigs.Load<Material>(jsonPath, m.String("material"));
            if (material == null) throw new InvalidOperationException($"its material '{name}' ({m.String("material")}) isn't in the project.");
            string folder = root + "/Materials";
            WardrobeAssets.EnsureFolder(folder);
            string template = $"{folder}/{WardrobeAssets.FileName(name)} Template.mat";
            if (!IOFile.Exists(template)) AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(material), template);

            string prefix = (g.id ?? "").Contains('.') ? g.id.Substring(0, g.id.IndexOf('.')) : "";
            bool standard = material.shader != null && material.shader.name.StartsWith("Standard");
            var j = new JsonObject();
            j["format"] = new JsonNumber(WardrobeMigrations.Current(WardrobeMigrations.Kind.Material));
            j["id"] = WardrobeForm.SuggestId(prefix, name);
            j["name"] = name;
            j["version"] = "1.0.0";
            j["author"] = g.author ?? "";
            j["description"] = "";
            string path = $"{folder}/{WardrobeAssets.FileName(name)}{WardrobeConfigs.Material}";
            j["template"] = WardrobeConfigs.Relative(path, AssetDatabase.LoadAssetAtPath<Material>(template));
            var textures = new List<object>();
            JsonObject Output(string property, object channels, string alsoOn = null, string keyword = null) {
                var o = new JsonObject();
                o["property"] = property;
                o["channels"] = channels;
                var also = (alsoOn ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Cast<object>().ToList();
                if (also.Count > 0) o["alsoOn"] = also;
                if (!string.IsNullOrEmpty(keyword)) o["keyword"] = keyword;
                return o;
            }
            textures.Add(Output(m.String("textureProperty", "_MainTex"), "albedo"));
            if (m.String("normalProperty").Length > 0) textures.Add(Output(m.String("normalProperty"), "normal", null, standard ? "_NORMALMAP" : null));
            if (m.String("surfaceProperty").Length > 0) {
                // the old layouts: Standard's metallic map (occlusion in G, for _OcclusionMap), or the toon one
                bool toon = m.String("surfaceLayout") == "MetallicRoughnessOcclusionSheen";
                var channels = toon ? new List<object> { "metalness", "smoothness", "occlusion", "sheen" }
                    : new List<object> { "metalness", "occlusion", new JsonNumber(0), "smoothness" };
                textures.Add(Output(m.String("surfaceProperty"), channels, m.String("surfaceAlsoOn"), standard ? "_METALLICGLOSSMAP" : null));
            }
            j["textures"] = textures;
            Write(path, j);
            Refresh();
            result.notes.Add($"Made the material type {path} from {name}; it's offered for every garment (list garments in it to change that).");
            return j.String("id");
        }

        private static void Write(string path, JsonObject j) {
            IOFile.WriteAllText(path, WardrobeJson.Write(j));
            AssetDatabase.ImportAsset(path);
            Refresh();
        }

        // ---------------------------------------------------------------- patterns and presets

        private static string ConvertConfig(OldFile f) {
            var j = WardrobeJson.Parse(IOFile.ReadAllText(f.path));
            // garments were { id, version }; now { garment, avatar, version }
            foreach (var t in j.Objects("garments"))
                if (t.Has("id") && !t.Has("garment")) t.Rename("id", "garment");

            string folder = Path.GetDirectoryName(f.path).Replace('\\', '/');
            string name = f.kind == WardrobeMigrations.Kind.Pattern ? Path.GetFileName(folder)
                : Path.GetFileName(f.path).Substring(0, Path.GetFileName(f.path).Length - PresetSuffix.Length);
            string path = $"{folder}/{WardrobeAssets.FileName(name)}{(f.kind == WardrobeMigrations.Kind.Pattern ? WardrobeConfigs.Pattern : WardrobeConfigs.Preset)}";
            if (IOFile.Exists(path)) path = AssetDatabase.GenerateUniqueAssetPath(path);
            // moved, so it keeps its GUID
            string error = AssetDatabase.MoveAsset(f.path, path);
            if (!string.IsNullOrEmpty(error)) {
                IOFile.WriteAllText(path, WardrobeJson.Write(j));
                AssetDatabase.ImportAsset(path);
                AssetDatabase.DeleteAsset(f.path);
            } else {
                IOFile.WriteAllText(path, WardrobeJson.Write(j));
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            return path;
        }

        // ---------------------------------------------------------------- references

        // Scenes, prefabs and assets that use an old garment asset use its .rgarm instead.
        private static void Repoint(Dictionary<string, (string guid, long id)> garments, WardrobeMigrations.Result result) {
            var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt).Where(s => !string.IsNullOrEmpty(s.path))
                .Select(s => s.path).ToList();
            var pattern = new Regex(@"\{fileID: 11400000, guid: (" + string.Join("|", garments.Keys) + @"), type: 2\}");
            var changed = new List<string>();
            foreach (string file in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".unity") || p.EndsWith(".prefab") || p.EndsWith(".asset"))) {
                string text = IOFile.ReadAllText(file);
                if (!text.StartsWith("%YAML")) continue;
                string replaced = pattern.Replace(text, m => {
                    var (guid, id) = garments[m.Groups[1].Value];
                    return $"{{fileID: {id}, guid: {guid}, type: 3}}";
                });
                if (replaced == text) continue;
                IOFile.WriteAllText(file, replaced);
                changed.Add(file.Replace('\\', '/'));
            }
            foreach (string file in changed) AssetDatabase.ImportAsset(file);
            if (changed.Count > 0) result.notes.Add($"Pointed {changed.Count} scenes and prefabs at the new garments: {string.Join(", ", changed)}");
            // the open scenes were changed on disk: open them again
            if (!Application.isBatchMode && open.Any(changed.Contains))
                for (int i = 0; i < open.Count; i++) EditorSceneManager.OpenScene(open[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
        }
    }
}
