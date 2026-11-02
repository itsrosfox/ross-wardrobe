using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// An avatar's body in a scene: its base's sliders (.ravt) and every other shape key on its body, read from and written
    /// to the renderers' shape key weights (and the objects choices turn on and off). Worn garments take the body's weights
    /// of the shape keys they share, as their own setup (VRCFury) does in-game.
    /// </summary>
    public static class WardrobeBody {
        public static SkinnedMeshRenderer BodyOf(GameObject avatar, WardrobeAvatar b) {
            if (avatar == null) return null;
            var named = b != null ? avatar.transform.Find(b.body)?.GetComponent<SkinnedMeshRenderer>() : null;
            if (named != null) return named;
            // else the renderer with the most shape keys that isn't a garment
            return avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && s.GetComponentInParent<WardrobeRecolor>() == null)
                .OrderByDescending(s => s.sharedMesh.blendShapeCount).FirstOrDefault();
        }

        // ---------------------------------------------------------------- sliders

        /// <summary>A slider's value, 0-100: its first shape key's weight as a share of the weight it sets.</summary>
        public static float Value(GameObject avatar, WardrobeSlider slider) {
            var first = slider.set.FirstOrDefault(v => !v.IsObject && Mathf.Abs(v.weight) > 0.001f);
            if (first == null) return 0;
            return Mathf.Clamp(Weight(avatar, first) / first.weight * 100f, 0, 100);
        }

        /// <summary>A choice's option: the one whose values the avatar has (the closest when none matches).</summary>
        public static int Option(GameObject avatar, WardrobeSlider slider) {
            int best = 0, bestScore = int.MinValue;
            for (int i = 0; i < slider.options.Count; i++) {
                int score = slider.options[i].set.Count(v => Has(avatar, v)) - slider.options[i].set.Count(v => !Has(avatar, v)) * 2;
                if (score > bestScore) (best, bestScore) = (i, score);
            }
            return best;
        }

        public static void SetValue(GameObject avatar, WardrobeSlider slider, float value) =>
            Write(avatar, slider.set.Select(v => v.IsObject ? v : new WardrobeShapeValue { path = v.path, shape = v.shape, weight = v.weight * value / 100f }));

        public static void SetOption(GameObject avatar, WardrobeSlider slider, int option) {
            if (option >= 0 && option < slider.options.Count) Write(avatar, slider.options[option].set);
        }

        /// <summary>The body's shape keys no slider sets, by name.</summary>
        public static List<string> OtherShapes(GameObject avatar, WardrobeAvatar b) {
            var body = BodyOf(avatar, b);
            if (body == null || body.sharedMesh == null) return new List<string>();
            var covered = new HashSet<string>((b?.sliders ?? new List<WardrobeSlider>())
                .SelectMany(s => s.set.Concat(s.options.SelectMany(o => o.set)))
                .Where(v => !v.IsObject && v.path == b.body).Select(v => v.shape));
            var mesh = body.sharedMesh;
            return Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).Where(n => !covered.Contains(n)).ToList();
        }

        public static float ShapeWeight(GameObject avatar, WardrobeAvatar b, string shape) {
            var body = BodyOf(avatar, b);
            int i = body != null ? body.sharedMesh.GetBlendShapeIndex(shape) : -1;
            return i >= 0 ? body.GetBlendShapeWeight(i) : 0;
        }

        public static void SetShapeWeight(GameObject avatar, WardrobeAvatar b, string shape, float weight) {
            var body = BodyOf(avatar, b);
            int i = body != null ? body.sharedMesh.GetBlendShapeIndex(shape) : -1;
            if (i < 0) return;
            Undo.RecordObject(body, "Body Shape");
            body.SetBlendShapeWeight(i, weight);
            Follow(avatar);
        }

        private static float Weight(GameObject avatar, WardrobeShapeValue v) {
            var skin = avatar.transform.Find(v.path)?.GetComponent<SkinnedMeshRenderer>();
            int i = skin != null && skin.sharedMesh != null ? skin.sharedMesh.GetBlendShapeIndex(v.shape) : -1;
            return i >= 0 ? skin.GetBlendShapeWeight(i) : 0;
        }

        private static bool Has(GameObject avatar, WardrobeShapeValue v) {
            if (!v.IsObject) return Mathf.Abs(Weight(avatar, v) - v.weight) < 0.5f;
            var t = avatar.transform.Find(v.path);
            return t == null || t.gameObject.activeSelf == v.active;
        }

        private static void Write(GameObject avatar, IEnumerable<WardrobeShapeValue> values) {
            foreach (var v in values) {
                var t = avatar.transform.Find(v.path);
                if (t == null) continue;
                if (v.IsObject) {
                    if (t.gameObject.activeSelf == v.active) continue;
                    Undo.RecordObject(t.gameObject, "Body Shape");
                    t.gameObject.SetActive(v.active);
                    continue;
                }
                var skin = t.GetComponent<SkinnedMeshRenderer>();
                int i = skin != null && skin.sharedMesh != null ? skin.sharedMesh.GetBlendShapeIndex(v.shape) : -1;
                if (i < 0) continue;
                Undo.RecordObject(skin, "Body Shape");
                skin.SetBlendShapeWeight(i, v.weight);
            }
            Follow(avatar);
        }

        /// <summary>Gives every garment worn on the avatar the body's weights of the shape keys they share (as in-game).</summary>
        public static void Follow(GameObject avatar, bool undo = true) {
            var b = WardrobeAvatars.Match(avatar);
            var body = BodyOf(avatar, b);
            if (body == null || body.sharedMesh == null) return;
            foreach (var rc in avatar.GetComponentsInChildren<WardrobeRecolor>(true))
            foreach (var skin in rc.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var mesh = skin.sharedMesh;
                if (mesh == null || skin == body) continue;
                bool recorded = false;
                for (int i = 0; i < mesh.blendShapeCount; i++) {
                    int j = body.sharedMesh.GetBlendShapeIndex(mesh.GetBlendShapeName(i));
                    if (j < 0 || Mathf.Approximately(skin.GetBlendShapeWeight(i), body.GetBlendShapeWeight(j))) continue;
                    if (!recorded && undo) Undo.RecordObject(skin, "Body Shape");
                    recorded = true;
                    skin.SetBlendShapeWeight(i, body.GetBlendShapeWeight(j));
                }
            }
        }

        // ---------------------------------------------------------------- presets

        /// <summary>The avatar's body as a preset: every slider's value or option, and the other shape keys that aren't 0.</summary>
        public static WardrobeBodyPreset Capture(GameObject avatar, WardrobeAvatar b) {
            var preset = new WardrobeBodyPreset { avatarId = b?.id ?? "" };
            foreach (var s in b?.sliders ?? new List<WardrobeSlider>()) {
                if (s.IsChoice) preset.options[s.name] = s.options[Option(avatar, s)].name;
                else preset.sliders[s.name] = Mathf.Round(Value(avatar, s) * 10) / 10;
            }
            // shape keys garments hide the body with aren't part of a body
            var hides = new HashSet<string>(WardrobeGarments.All.SelectMany(g => g.bodyHides ?? new List<string>()));
            foreach (string shape in OtherShapes(avatar, b).Where(s => !hides.Contains(s))) {
                float w = ShapeWeight(avatar, b, shape);
                if (Mathf.Abs(w) > 0.05f) preset.shapes[shape] = Mathf.Round(w * 10) / 10;
            }
            return preset;
        }

        /// <summary>Sets the avatar to a preset: sliders and shape keys it doesn't name go back to 0, choices it doesn't name stay.</summary>
        public static void Apply(GameObject avatar, WardrobeAvatar b, WardrobeBodyPreset preset) {
            int group = Undo.GetCurrentGroup();
            foreach (var s in b?.sliders ?? new List<WardrobeSlider>()) {
                if (s.IsChoice) {
                    // choices it doesn't name stay as they are (a saved preset names them all)
                    if (preset.options.TryGetValue(s.name, out string option)) SetOption(avatar, s, s.options.FindIndex(o => o.name == option));
                } else {
                    preset.sliders.TryGetValue(s.name, out float value);
                    SetValue(avatar, s, value);
                }
            }
            var hides = new HashSet<string>(WardrobeGarments.All.SelectMany(g => g.bodyHides ?? new List<string>()));
            var body = BodyOf(avatar, b);
            foreach (string shape in OtherShapes(avatar, b).Where(s => !hides.Contains(s))) {
                preset.shapes.TryGetValue(shape, out float w);
                int i = body.sharedMesh.GetBlendShapeIndex(shape);
                if (Mathf.Approximately(body.GetBlendShapeWeight(i), w)) continue;
                Undo.RecordObject(body, "Body Shape");
                body.SetBlendShapeWeight(i, w);
            }
            Follow(avatar);
            Undo.SetCurrentGroupName("Body: " + preset.name);
            Undo.CollapseUndoOperations(group);
        }
    }

    /// <summary>A body preset (.rbody): slider values, choice options and other shape keys for one avatar base.</summary>
    public class WardrobeBodyPreset {
        public string id, name, group, avatarId, path;
        public int order;
        public readonly Dictionary<string, float> sliders = new Dictionary<string, float>();
        public readonly Dictionary<string, string> options = new Dictionary<string, string>();
        public readonly Dictionary<string, float> shapes = new Dictionary<string, float>();

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path ?? id) : name;
    }

    /// <summary>Every .rbody in the project.</summary>
    public static class WardrobeBodies {
        private static readonly string[] Order = { "format", "id", "name", "group", "order", "avatar", "sliders", "options", "shapes" };
        private static List<WardrobeBodyPreset> _all;

        public static List<WardrobeBodyPreset> All => _all ??= Load();

        public static void Forget() => _all = null;

        public static List<WardrobeBodyPreset> For(WardrobeAvatar b) =>
            All.Where(p => b == null || string.IsNullOrWhiteSpace(p.avatarId) || WardrobeConfigs.SameId(p.avatarId, b.id))
                .OrderBy(p => p.order).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        public static WardrobeBodyPreset Get(string idOrName) =>
            All.FirstOrDefault(p => WardrobeConfigs.SameId(p.id, idOrName)) ?? All.FirstOrDefault(p => WardrobeConfigs.SameId(p.DisplayName, idOrName));

        public static void Save(string path, WardrobeBodyPreset preset) {
            var j = File.Exists(path) ? WardrobeJson.Parse(File.ReadAllText(path)) : new JsonObject();
            j.Put("format", new JsonNumber(WardrobeMigrations.Current(WardrobeMigrations.Kind.Body)), Order);
            j.Put("id", string.IsNullOrWhiteSpace(preset.id) ? "body." + Guid.NewGuid().ToString("N").Substring(0, 12) : preset.id, Order);
            j.Put("name", preset.name ?? "", Order);
            if (!string.IsNullOrWhiteSpace(preset.group)) j.Put("group", preset.group, Order);
            var avatar = new JsonObject();
            avatar["id"] = preset.avatarId ?? "";
            j.Put("avatar", avatar, Order);
            j.Put("sliders", Numbers(preset.sliders), Order);
            var options = new JsonObject();
            foreach (var kv in preset.options) options[kv.Key] = kv.Value;
            j.Put("options", options, Order);
            j.Put("shapes", Numbers(preset.shapes), Order);
            File.WriteAllText(path, WardrobeJson.Write(j) + "\n");
            AssetDatabase.ImportAsset(path);
        }

        private static JsonObject Numbers(Dictionary<string, float> values) {
            var o = new JsonObject();
            foreach (var kv in values) o[kv.Key] = new JsonNumber(Math.Round(kv.Value, 2));
            return o;
        }

        private static List<WardrobeBodyPreset> Load() {
            var list = new List<WardrobeBodyPreset>();
            foreach (string path in WardrobeConfigs.Paths(WardrobeConfigs.Body)) {
                try {
                    var j = WardrobeJson.Parse(WardrobeMigrations.Read(path, WardrobeMigrations.Kind.Body));
                    var p = new WardrobeBodyPreset {
                        id = j.String("id").Trim(), name = j.String("name").Trim(), group = j.String("group").Trim(), order = j.Int("order"),
                        avatarId = j.Object("avatar")?.String("id").Trim() ?? "", path = path,
                    };
                    var sliders = j.Object("sliders");
                    for (int i = 0; sliders != null && i < sliders.Keys.Count; i++)
                        if (sliders.Values[i] is JsonNumber n) p.sliders[sliders.Keys[i]] = (float)n.Value;
                    var options = j.Object("options");
                    for (int i = 0; options != null && i < options.Keys.Count; i++)
                        if (options.Values[i] is string s) p.options[options.Keys[i]] = s;
                    var shapes = j.Object("shapes");
                    for (int i = 0; shapes != null && i < shapes.Keys.Count; i++)
                        if (shapes.Values[i] is JsonNumber n) p.shapes[shapes.Keys[i]] = (float)n.Value;
                    list.Add(p);
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {path}: {e.Message}");
                }
            }
            return list;
        }
    }
}
