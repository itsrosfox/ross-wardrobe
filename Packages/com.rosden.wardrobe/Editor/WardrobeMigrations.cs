using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Keeps Ros' Wardrobe's json files readable as their layout changes between versions. Every file has a <c>format</c>
    /// (no format = 1); when a version of Ros' Wardrobe changes what a kind of file looks like, it adds a <see cref="Step"/>
    /// to <see cref="Steps"/> that turns that kind's format N into N + 1, and the kind's current format goes up with it.
    /// <para>Older files keep working before anyone migrates them: the readers run the steps on the text as they read it
    /// (<see cref="Read"/>). Migrating (Project Settings → Ros' Wardrobe → Updates) writes the result back, so the files
    /// can ship that way, after copying the originals to Library/Ros' Wardrobe/Backups.</para>
    /// </summary>
    public static class WardrobeMigrations {
        public enum Kind { Garment, Pattern, Preset, Avatar, Material, Settings, Body, Skin }

        /// <summary>
        /// Turns a <see cref="Kind"/>'s format <see cref="from"/> into <c>from + 1</c>. It edits the json tree in place (keys
        /// it doesn't touch stay as they are); <see cref="Context.Later"/> queues asset work for after a real migration.
        /// </summary>
        public sealed class Step {
            public readonly Kind kind;
            public readonly int from;
            public readonly string description;
            public readonly Action<JsonObject, Context> apply;

            public Step(Kind kind, int from, string description, Action<JsonObject, Context> apply) {
                this.kind = kind;
                this.from = from;
                this.description = description;
                this.apply = apply;
            }
        }

        public sealed class Context {
            public readonly string path;
            public readonly bool writing;
            internal readonly List<string> notes = new List<string>();
            internal readonly List<Action> later = new List<Action>();

            internal Context(string path, bool writing) {
                this.path = path;
                this.writing = writing;
            }

            /// <summary>Something the person migrating should know (shown after migrating).</summary>
            public void Note(string text) => notes.Add(text);

            /// <summary>Work on assets (reimporting, regenerating maps) once the files are written; skipped when only reading.</summary>
            public void Later(Action action) => later.Add(action);
        }

        // Add new steps at the end, one per kind and format, e.g.
        //   new Step(Kind.Pattern, 1, "tileSize in millimetres becomes tile { width, height }", (json, _) => { ... }),
        private static readonly List<Step> Steps = new List<Step> {
        };

        /// <summary>The format this version of Ros' Wardrobe writes for a kind of file.</summary>
        public static int Current(Kind kind) => 1 + Steps.Count(s => s.kind == kind);

        /// <summary>What each step from <paramref name="format"/> up to the current format does.</summary>
        public static IEnumerable<string> Changes(Kind kind, int format) =>
            Steps.Where(s => s.kind == kind && s.from >= format).OrderBy(s => s.from).Select(s => s.description);

        // For tests: a step added for the rest of the session.
        internal static void Register(Step step) {
            Steps.Add(step);
            Check();
        }

        internal static void Unregister(Step step) => Steps.Remove(step);

        /// <summary>The kind of Ros' Wardrobe file at a path, or null.</summary>
        public static Kind? KindOf(string path) {
            switch (Path.GetExtension(path ?? "").ToLowerInvariant()) {
                case WardrobeConfigs.Garment: return Kind.Garment;
                case WardrobeConfigs.Pattern: return Kind.Pattern;
                case WardrobeConfigs.Preset: return Kind.Preset;
                case WardrobeConfigs.Avatar: return Kind.Avatar;
                case WardrobeConfigs.Material: return Kind.Material;
                case WardrobeConfigs.Body: return Kind.Body;
                case WardrobeConfigs.Skin: return Kind.Skin;
            }
            if (string.Equals(Path.GetFileName(path ?? ""), WardrobeSettings.FileName, StringComparison.OrdinalIgnoreCase)) return Kind.Settings;
            return null;
        }

        /// <summary>The kind's files, as the settings page lists them ("Garments (.rgarm)").</summary>
        public static string Label(Kind kind) {
            switch (kind) {
                case Kind.Garment: return $"Garments ({WardrobeConfigs.Garment})";
                case Kind.Pattern: return $"Patterns ({WardrobeConfigs.Pattern})";
                case Kind.Preset: return $"Presets ({WardrobeConfigs.Preset})";
                case Kind.Avatar: return $"Avatars ({WardrobeConfigs.Avatar})";
                case Kind.Material: return $"Material types ({WardrobeConfigs.Material})";
                case Kind.Body: return $"Body presets ({WardrobeConfigs.Body})";
                case Kind.Skin: return $"Skins ({WardrobeConfigs.Skin})";
                default: return $"Settings ({WardrobeSettings.FileName})";
            }
        }

        [Serializable]
        private class FormatOnly {
            public int format = 1;
        }

        public static int FormatOf(string text) {
            var f = new FormatOnly();
            JsonUtility.FromJsonOverwrite(text, f);
            return f.format;
        }

        // ---------------------------------------------------------------- reading

        private static readonly HashSet<string> WarnedNewer = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A Ros' Wardrobe json file's text as the current format: older files go through the migration steps in memory
        /// (the file isn't changed), newer ones are read as they are, with a warning.
        /// </summary>
        public static string Read(string path, Kind kind) {
            string text = File.ReadAllText(path);
            int format;
            try {
                format = FormatOf(text);
            } catch {
                return text;   // the reader reports it
            }
            int current = Current(kind);
            if (format == current) return text;
            if (format > current) {
                if (WarnedNewer.Add(path))
                    Debug.LogWarning($"[Ros' Wardrobe] {path} was made with a newer Ros' Wardrobe (format {format}; this one reads {current}). " +
                                     "Update Ros' Wardrobe through VCC, or parts of it may not work.");
                return text;
            }
            try {
                var json = WardrobeJson.Parse(text);
                Upgrade(kind, json, new Context(path, false));
                return WardrobeJson.Write(json);
            } catch (Exception e) {
                Debug.LogWarning($"[Ros' Wardrobe] Couldn't update {path} from format {format}: {e.Message}");
                return text;
            }
        }

        /// <summary>Runs the steps on a file's json tree in memory (asset work they queue is skipped).</summary>
        internal static void Upgrade(Kind kind, JsonObject json, string path) => Upgrade(kind, json, new Context(path, false));

        private static void Upgrade(Kind kind, JsonObject json, Context context) {
            int format = Math.Max(1, json.Int("format", 1));
            foreach (var step in Steps.Where(s => s.kind == kind && s.from >= format).OrderBy(s => s.from)) {
                step.apply(json, context);
                json.Insert("format", new JsonNumber(step.from + 1));
            }
            if (json.Keys.IndexOf("format") > 0) {
                // format stays the first key
                object value = json["format"];
                json.Remove("format");
                json.Keys.Insert(0, "format");
                json.Values.Insert(0, value);
            }
        }

        // ---------------------------------------------------------------- the project's files

        public enum State { UpToDate, Outdated, Newer, Broken }

        public sealed class FileStatus {
            public string path;
            public Kind kind;
            public int format;
            public State state;
            public string error;
            public List<string> changes = new List<string>();
        }

        /// <summary>Every Ros' Wardrobe config file in the project and whether it needs migrating.</summary>
        public static List<FileStatus> Scan() {
            var settings = AssetDatabase.FindAssets("wardrobe t:TextAsset").Select(AssetDatabase.GUIDToAssetPath).Where(p => KindOf(p) == Kind.Settings);
            var paths = WardrobeConfigs.Extensions.SelectMany(WardrobeConfigs.Paths).Concat(settings)
                .Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            var list = new List<FileStatus>();
            foreach (string path in paths) {
                var kind = KindOf(path).Value;
                var status = new FileStatus { path = path, kind = kind };
                try {
                    string text = File.ReadAllText(path);
                    WardrobeJson.Parse(text);   // so broken files show up here, not halfway through migrating
                    status.format = FormatOf(text);
                    int current = Current(kind);
                    status.state = status.format == current ? State.UpToDate : status.format > current ? State.Newer : State.Outdated;
                    if (status.state == State.Outdated) status.changes = Changes(kind, status.format).ToList();
                } catch (Exception e) {
                    status.state = State.Broken;
                    status.error = e.Message;
                }
                list.Add(status);
            }
            return list;
        }

        public sealed class Result {
            public int migrated;
            public string backups;
            public readonly List<string> notes = new List<string>();
            public readonly List<string> errors = new List<string>();
        }

        /// <summary>Writes the outdated files as the current format, after backing them up.</summary>
        public static Result Migrate(IEnumerable<FileStatus> files) {
            var result = new Result();
            var outdated = files.Where(f => f.state == State.Outdated).ToList();
            if (outdated.Count == 0) return result;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            result.backups = Path.Combine(root, "Library", "Ros' Wardrobe", "Backups", DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
            var later = new List<Action>();
            try {
                AssetDatabase.StartAssetEditing();
                foreach (var file in outdated) {
                    try {
                        string text = File.ReadAllText(file.path);
                        var json = WardrobeJson.Parse(text);
                        var context = new Context(file.path, true);
                        Upgrade(file.kind, json, context);
                        string backup = Path.Combine(result.backups, file.path);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        File.WriteAllText(backup, text);
                        File.WriteAllText(file.path, WardrobeJson.Write(json));
                        AssetDatabase.ImportAsset(file.path);
                        result.migrated++;
                        result.notes.AddRange(context.notes.Select(n => $"{file.path}: {n}"));
                        later.AddRange(context.later);
                    } catch (Exception e) {
                        result.errors.Add($"{file.path}: {e.Message}");
                    }
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }
            foreach (var action in later)
                try {
                    action();
                } catch (Exception e) {
                    result.errors.Add(e.Message);
                    Debug.LogException(e);
                }
            WardrobeSettings.Forget();
            WardrobeConfigs.Changed();
            WardrobePatterns.Forget();
            WardrobePresets.Forget();
            WardrobeAvatars.Forget();
            WardrobeMaterialTypes.Forget();
            return result;
        }

        // ---------------------------------------------------------------- checks

        // Each kind's steps have to go 1 → 2 → 3… with none missing or doubled.
        private static void Check() {
            foreach (var group in Steps.GroupBy(s => s.kind)) {
                var froms = group.Select(s => s.from).OrderBy(f => f).ToList();
                if (!froms.SequenceEqual(Enumerable.Range(1, froms.Count)))
                    Debug.LogError($"[Ros' Wardrobe] The {group.Key} migration steps go from formats {string.Join(", ", froms)}; they have to be 1, 2, 3…");
            }
        }

        // Once a session, after the editor loads: say so when the project has files to migrate.
        [InitializeOnLoadMethod]
        private static void Startup() {
            Check();
            if (SessionState.GetBool("RosDen.Wardrobe.MigrationsChecked", false)) return;
            EditorApplication.delayCall += () => {
                SessionState.SetBool("RosDen.Wardrobe.MigrationsChecked", true);
                if (Application.isBatchMode) return;
                var files = Scan();
                int outdated = files.Count(f => f.state == State.Outdated);
                if (outdated > 0)
                    Debug.LogWarning($"[Ros' Wardrobe] {outdated} config files use an older layout. They still work; to update them, open " +
                                     "Edit → Project Settings → Ros' Wardrobe → Updates.");
                if (WardrobeLegacy.Scan().Count > 0)
                    Debug.LogWarning("[Ros' Wardrobe] This project has garments, patterns or presets from before .rgarm/.rpat/.rpre files. Convert " +
                                     "them in Edit → Project Settings → Ros' Wardrobe → Updates.");
            };
        }
    }
}
