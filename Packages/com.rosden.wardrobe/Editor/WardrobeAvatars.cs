using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RosDen.Wardrobe.Editor {
    /// <summary>An avatar base garments are fitted to, read from a .ravt file.</summary>
    public class WardrobeAvatar {
        public string id, name, version, author, description, path;
        /// <summary>Words in an avatar's name, or its prefab's or model's, that mean it's this base.</summary>
        public List<string> matches = new List<string>();
        /// <summary>GUIDs of the base's model or prefab files (the same in every project the base is imported into).</summary>
        public List<string> guids = new List<string>();
        /// <summary>Path of the body's renderer under the avatar root (shape keys without a path are on it).</summary>
        public string body = "Body";
        /// <summary>The body sliders the window shows, in order.</summary>
        public List<WardrobeSlider> sliders = new List<WardrobeSlider>();
        /// <summary>Body shape keys Fit to Avatar copies onto garments, so they follow the body (empty: the ones the sliders set).</summary>
        public List<string> fitShapes = new List<string>();
        /// <summary>Bones garments shouldn't move with (by the start of their name), and the bone that takes their weight instead.</summary>
        public List<(string prefix, string bone)> fitBones = new List<(string, string)>();

        public string DisplayName => string.IsNullOrWhiteSpace(name) ? id : name;

        /// <summary>The base's model or prefab in this project (the first of its files that's here), or null.</summary>
        public GameObject Model => guids.Select(AssetDatabase.GUIDToAssetPath).Where(p => !string.IsNullOrEmpty(p))
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).FirstOrDefault(go => go != null);

        /// <summary>The body shape keys garments follow: the .ravt's list, else every body shape key a slider sets.</summary>
        public List<string> FitShapes => fitShapes.Count > 0 ? fitShapes
            : sliders.SelectMany(s => s.set.Concat(s.options.SelectMany(o => o.set))).Where(v => !v.IsObject && v.path == body)
                .Select(v => v.shape).Distinct().ToList();
    }

    /// <summary>
    /// One body control of an avatar base. A slider ("set") scales shape key weights from 0 to the given ones; a choice
    /// ("options") picks one set of values, shown as a toggle when its options are Off and On.
    /// </summary>
    public class WardrobeSlider {
        public string name, group;
        public List<WardrobeShapeValue> set = new List<WardrobeShapeValue>();
        public List<(string name, List<WardrobeShapeValue> set)> options = new List<(string, List<WardrobeShapeValue>)>();

        public bool IsChoice => options.Count > 0;
        public bool IsToggle => options.Count == 2 && options[0].name == "Off" && options[1].name == "On";
    }

    /// <summary>
    /// A value a body control sets, as written in a .ravt: "Shape": weight (a shape key on the body), "Renderer:Shape": weight
    /// (a shape key on another renderer, by path), or "Path": true/false (whether an object is on).
    /// </summary>
    public class WardrobeShapeValue {
        public string path, shape;
        public float weight;
        public bool active;

        public bool IsObject => shape == null;

        public static WardrobeShapeValue Parse(string key, object value, string body) {
            if (value is bool b) return new WardrobeShapeValue { path = key, active = b };
            if (!(value is double weight)) return null;
            int colon = key.IndexOf(':');
            return colon < 0 ? new WardrobeShapeValue { path = body, shape = key, weight = (float)weight }
                : new WardrobeShapeValue { path = key.Substring(0, colon), shape = key.Substring(colon + 1), weight = (float)weight };
        }
    }

    /// <summary>
    /// Every .ravt in the project, and which one an avatar in a scene is. A garment pack usually ships the .ravt of the
    /// base it's fitted to, so Ros' Wardrobe knows the base without the avatar creator's help.
    /// </summary>
    public static class WardrobeAvatars {
        private static List<WardrobeAvatar> _all;

        public static List<WardrobeAvatar> All => _all ??= Load();

        public static void Forget() => _all = null;

        public static WardrobeAvatar Get(string id) => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(a => WardrobeConfigs.SameId(a.id, id));

        /// <summary>The base an avatar in a scene is built on (by its model's or prefab's GUID, else by name), or null.</summary>
        public static WardrobeAvatar Match(GameObject avatar) {
            if (avatar == null) return null;
            var names = new List<string> { avatar.name };
            var guids = new HashSet<string>();
            void Add(GameObject source) {
                if (source == null) return;
                names.Add(source.name);
                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path)) {
                    guids.Add(AssetDatabase.AssetPathToGUID(path));
                    names.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
            for (var p = PrefabUtility.GetCorrespondingObjectFromSource(avatar); p != null; p = PrefabUtility.GetCorrespondingObjectFromSource(p)) Add(p);
            Add(PrefabUtility.GetCorrespondingObjectFromOriginalSource(avatar));
            // a model under the avatar root that isn't the root's own prefab (an unpacked avatar)
            foreach (var t in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Take(8))
                Add(PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject)?.transform.root.gameObject);

            return All.FirstOrDefault(a => a.guids.Any(guids.Contains)) ??
                   All.FirstOrDefault(a => a.matches.Any(m => !string.IsNullOrWhiteSpace(m) &&
                                                             names.Any(n => n.IndexOf(m.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private static List<WardrobeAvatar> Load() {
            var list = new List<WardrobeAvatar>();
            foreach (string path in WardrobeConfigs.Paths(WardrobeConfigs.Avatar)) {
                try {
                    var j = WardrobeJson.Parse(File.ReadAllText(path));
                    var a = new WardrobeAvatar {
                        id = j.String("id").Trim(), name = j.String("name"), version = j.String("version"), author = j.String("author"),
                        description = j.String("description"), path = path,
                        matches = (j.Array("matches") ?? new List<object>()).OfType<string>().Where(m => m.Trim().Length > 0).ToList(),
                        guids = (j.Array("guids") ?? new List<object>()).OfType<string>().Select(g => g.Trim().ToLowerInvariant()).Where(g => g.Length > 0).ToList(),
                        body = j.String("body", "Body").Trim(),
                    };
                    foreach (var o in j.Objects("sliders")) {
                        var slider = new WardrobeSlider { name = o.String("name").Trim(), group = o.String("group").Trim() };
                        slider.set = Values(o.Object("set"), a.body);
                        foreach (var option in o.Objects("options"))
                            slider.options.Add((option.String("name").Trim(), Values(option.Object("set"), a.body)));
                        if (slider.name.Length > 0) a.sliders.Add(slider);
                    }
                    var fitting = j.Object("fitting");
                    a.fitShapes = (fitting?.Array("shapeKeys") ?? new List<object>()).OfType<string>().Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    var bones = fitting?.Object("mergeBones");
                    if (bones != null)
                        for (int i = 0; i < bones.Count; i++)
                            if (bones.Values[i] is string bone && bones.Keys[i].Trim().Length > 0 && bone.Trim().Length > 0)
                                a.fitBones.Add((bones.Keys[i].Trim(), bone.Trim()));
                    if (a.id.Length == 0) Debug.LogWarning($"[Ros' Wardrobe] {path} has no id");
                    else if (list.All(o => !WardrobeConfigs.SameId(o.id, a.id))) list.Add(a);
                } catch (Exception e) {
                    Debug.LogWarning($"[Ros' Wardrobe] Couldn't read {path}: {e.Message}");
                }
            }
            return list.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<WardrobeShapeValue> Values(JsonObject set, string body) {
            var values = new List<WardrobeShapeValue>();
            if (set == null) return values;
            for (int i = 0; i < set.Keys.Count; i++) {
                var v = WardrobeShapeValue.Parse(set.Keys[i], set.Values[i], body);
                if (v != null) values.Add(v);
            }
            return values;
        }
    }
}
