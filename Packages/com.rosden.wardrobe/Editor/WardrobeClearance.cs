using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// The clearance pass: the last step of fitting a garment, on the avatar it's worn on. A garment's shape keys (from its
    /// cloth tool's exports, or following the body) can't foresee every mix of body shapes an avatar is set to, so after the
    /// body or what's worn changes, every worn garment is checked against the body as it is now (without what the worn
    /// body's shape keys hide now, e.g. the worn garments' body hides), and wherever the body comes through the cloth or closer than
    /// <see cref="WardrobeFitter.Clearance"/>, the cloth is moved out, layers and loose parts (buttons, stitching) with it.
    /// That movement is one more shape key on a copy of the garment's mesh, "<see cref="ShapeName"/>" at 100, kept (binary)
    /// in the garment's folder under Wardrobe Recolors, one per avatar and scene: "&lt;Garment&gt; - Clearance (&lt;Scene&gt; -
    /// &lt;Avatar&gt;).asset". The garment wears that copy; without clipping it wears its own mesh again.
    /// </summary>
    public static class WardrobeClearance {
        /// <summary>The shape key the pass adds; it's set to 100 and nothing else drives it.</summary>
        public const string ShapeName = "Wardrobe Clearance";
        private const int Build = 1;   // bump when the pass changes, so kept copies are made again
        private const string EnabledPref = "RosDen.Wardrobe.Clearance";
        private const double Settle = 0.4;   // seconds without changes before the pass runs

        /// <summary>Whether the pass runs (the Avatar section's Fix Clipping), on by default. Off, garments wear their own meshes.</summary>
        public static bool Enabled {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set {
                if (value == Enabled) return;
                EditorPrefs.SetBool(EnabledPref, value);
                foreach (var avatar in SceneAvatars()) {
                    if (value) Schedule(avatar);
                    else Remove(avatar);
                }
            }
        }

        /// <summary>What the pass did to one worn garment.</summary>
        public class Result {
            public string garment;
            public int moved;
            public float mm;
            public string error;
        }

        private static readonly Dictionary<GameObject, List<Result>> Last = new Dictionary<GameObject, List<Result>>();

        /// <summary>What the last pass on the avatar did, or null when it hasn't run since the editor started.</summary>
        public static List<Result> LastFor(GameObject avatar) => avatar != null && Last.TryGetValue(avatar, out var r) ? r : null;

        /// <summary>Whether the renderer's mesh is the garment's own (its fit) or a clearance copy of it.</summary>
        public static bool Wears(Mesh mesh, WardrobeGarment g) {
            if (mesh == null || g == null || g.mesh == null) return false;
            if (mesh == g.mesh) return true;
            string path = AssetDatabase.GetAssetPath(mesh);
            return path.StartsWith(Prefix(g), StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------- when it runs

        private static readonly HashSet<GameObject> Due = new HashSet<GameObject>();
        private static double _at;

        /// <summary>Runs the pass on the avatar once its body and garments have settled (a slider let go).</summary>
        public static void Schedule(GameObject avatar) {
            if (!Enabled || avatar == null || EditorUtility.IsPersistent(avatar) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Due.Add(avatar);
            _at = EditorApplication.timeSinceStartup + Settle;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        /// <summary>Runs the pass on every avatar in the open scenes that wears a garment, once they've settled.</summary>
        public static void ScheduleAll() {
            foreach (var avatar in SceneAvatars()) Schedule(avatar);
        }

        private static void Tick() {
            if (EditorApplication.timeSinceStartup < _at) return;
            if (GUIUtility.hotControl != 0) {   // a slider still held
                _at = EditorApplication.timeSinceStartup + Settle;
                return;
            }
            EditorApplication.update -= Tick;
            var due = Due.Where(a => a != null).ToList();
            Due.Clear();
            foreach (var avatar in due) Run(avatar, false);
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();   // the Avatar section shows what it did
        }

        /// <summary>The pass on every avatar in the open scenes, now (a VRChat build). False when it failed on one.</summary>
        public static bool ApplyAll() {
            if (!Enabled) return true;
            Due.Clear();
            bool ok = true;
            foreach (var avatar in SceneAvatars()) ok &= Run(avatar, false);
            return ok;
        }

        private static bool Run(GameObject avatar, bool again) {
            try {
                Apply(avatar, again);
                return true;
            } catch (Exception e) {
                Debug.LogException(e, avatar);
                Last[avatar] = new List<Result> { new Result { error = e.Message } };
                return false;
            }
        }

        [InitializeOnLoadMethod]
        private static void Hook() {
            Undo.undoRedoPerformed += ScheduleAll;   // the body's weights came back, the cleared copy didn't
        }

        private static IEnumerable<GameObject> SceneAvatars() {
            var avatars = new HashSet<GameObject>();
            foreach (var rc in Object.FindObjectsOfType<WardrobeRecolor>(true)) {
                if (rc == null || EditorUtility.IsPersistent(rc)) continue;
                var avatar = WardrobeWindow.AvatarRoot(rc.transform);
                if (avatar != null) avatars.Add(avatar);
            }
            return avatars;
        }

        // ---------------------------------------------------------------- the pass

        /// <summary>
        /// Checks every garment worn on the avatar against its body as it is now and moves the cloth out where it clips.
        /// A copy kept from an earlier pass with the same body, garment and weights is worn again without working it out;
        /// <paramref name="again"/> works it out anyway.
        /// </summary>
        public static List<Result> Apply(GameObject avatar, bool again = false) {
            var results = new List<Result>();
            var b = WardrobeAvatars.Match(avatar);
            var body = WardrobeBody.BodyOf(avatar, b);
            if (body == null || body.sharedMesh == null) return results;
            var worn = Worn(avatar, body);
            Last[avatar] = results;
            if (worn.Count == 0) return results;

            Vector3[] bodyWorld = null;
            int[] bodyTriangles = null;
            float BodyWeight(string shape) {
                int i = body.sharedMesh.GetBlendShapeIndex(shape);
                return i >= 0 ? body.GetBlendShapeWeight(i) : 0;
            }

            foreach (var (g, skin) in worn) {
                var result = new Result { garment = string.IsNullOrWhiteSpace(g.displayName) ? g.name : g.displayName };
                results.Add(result);
                var own = g.mesh;
                var shown = skin.sharedMesh;
                float WeightOf(string shape) {
                    int i = shown.GetBlendShapeIndex(shape);
                    return i >= 0 ? skin.GetBlendShapeWeight(i) : 0;
                }
                string key = Key(avatar, g, body, skin, own, WeightOf);
                string path = StorePath(g, avatar);
                var kept = Kept(path, key, out int keptMoved, out float keptMm);
                Mesh wear;
                if (!again && keptMoved >= 0) {
                    (result.moved, result.mm) = (keptMoved, keptMm);
                    wear = keptMoved > 0 && kept != null ? kept : own;
                } else {
                    bodyWorld ??= WardrobeFitter.Posed(body, body.sharedMesh, BodyWeight, out _);
                    // what the body's shape keys hide now (collapsed: fluff the worn garments' hides take away) doesn't clip
                    bodyTriangles ??= WardrobeFitter.ShownTriangles(WardrobeFitter.Posed(body, body.sharedMesh, s => 0, out _), bodyWorld, body.sharedMesh.triangles);
                    var world = WardrobeFitter.Posed(skin, own, WeightOf, out var skinning);
                    var push = WardrobeFitter.ClearancePush(world, own.triangles, bodyWorld, bodyTriangles, out float most, out int moved);
                    (result.moved, result.mm) = (moved, most * 1000);
                    Mesh made = null;
                    if (moved > 0) {
                        made = Object.Instantiate(own);
                        made.name = $"{own.name} (Clearance)";
                        var dv = new Vector3[push.Length];
                        for (int v = 0; v < push.Length; v++)
                            if (push[v].sqrMagnitude > 1e-12f) dv[v] = skinning[v].inverse.MultiplyVector(push[v]);
                        made.AddBlendShapeFrame(ShapeName, 100, dv, null, null);
                    }
                    var saved = Save(path, made, $"{key}|{moved}|{(most * 1000).ToString("0.###", CultureInfo.InvariantCulture)}");
                    wear = moved > 0 ? saved : own;
                }
                Put(skin, wear);
            }
            return results;
        }

        /// <summary>Puts every worn garment's own mesh back on it (Fix Clipping turned off).</summary>
        public static void Remove(GameObject avatar) {
            var body = WardrobeBody.BodyOf(avatar, WardrobeAvatars.Match(avatar));
            foreach (var (g, skin) in Worn(avatar, body)) Put(skin, g.mesh);
            Last.Remove(avatar);
        }

        // The garments worn on the avatar: each renderer that shows its fit or a clearance copy of it.
        private static List<(WardrobeGarment g, SkinnedMeshRenderer skin)> Worn(GameObject avatar, SkinnedMeshRenderer body) {
            var list = new List<(WardrobeGarment, SkinnedMeshRenderer)>();
            if (avatar == null) return list;
            foreach (var rc in avatar.GetComponentsInChildren<WardrobeRecolor>(true)) {
                var g = rc.garment;
                if (g == null || g.mesh == null) continue;
                foreach (var skin in rc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skin != body && Wears(skin.sharedMesh, g) && !list.Any(w => w.Item2 == skin)) list.Add((g, skin));
            }
            return list;
        }

        // What the copy is made from: the garment's fit, the body and both their weights, where the garment sits.
        private static string Key(GameObject avatar, WardrobeGarment g, SkinnedMeshRenderer body, SkinnedMeshRenderer skin, Mesh own,
            Func<string, float> weightOf) {
            var hash = new Hash128();
            hash.Append(Build);
            string ownPath = AssetDatabase.GetAssetPath(own);
            hash.Append(ownPath);
            hash.Append(own.name);
            string fitKey = AssetImporter.GetAtPath(ownPath)?.userData;
            hash.Append(string.IsNullOrEmpty(fitKey) ? AssetDatabase.GetAssetDependencyHash(ownPath).ToString() : fitKey);
            var bodyMesh = body.sharedMesh;
            hash.Append(AssetDatabase.GetAssetPath(bodyMesh));
            hash.Append(bodyMesh.name);
            for (int i = 0; i < bodyMesh.blendShapeCount; i++) hash.Append(Mathf.RoundToInt(body.GetBlendShapeWeight(i) * 10));
            for (int i = 0; i < own.blendShapeCount; i++) hash.Append(Mathf.RoundToInt(weightOf(own.GetBlendShapeName(i)) * 10));
            // where the garment's and the body's bones are, to the millimetre, in the avatar's space
            void Where(Transform t) {
                var p = t != null ? avatar.transform.InverseTransformPoint(t.position) * 1000 : Vector3.zero;
                hash.Append(Mathf.RoundToInt(p.x));
                hash.Append(Mathf.RoundToInt(p.y));
                hash.Append(Mathf.RoundToInt(p.z));
            }
            Where(body.rootBone);
            Where(skin.rootBone);
            Where(skin.bones.FirstOrDefault(t => t != null));
            var scale = avatar.transform.lossyScale * 1000;
            hash.Append(Mathf.RoundToInt(scale.x));
            hash.Append(Mathf.RoundToInt(scale.y));
            hash.Append(Mathf.RoundToInt(scale.z));
            return hash.ToString();
        }

        // ---------------------------------------------------------------- the kept copies

        private static string Prefix(WardrobeGarment g) =>
            $"{WardrobeMaterialTypes.OutputFolder(g)}/{WardrobeAssets.FileName(WardrobeAssets.BaseName(g))} - Clearance (";

        private static string StorePath(WardrobeGarment g, GameObject avatar) {
            string scene = string.IsNullOrWhiteSpace(avatar.scene.name) ? "Untitled" : avatar.scene.name;
            return Prefix(g) + WardrobeAssets.FileName($"{scene} - {avatar.name}") + ").asset";
        }

        // The copy kept at the path, when it was made with this key: what it moved (-1 when there's none for the key).
        private static Mesh Kept(string path, string key, out int moved, out float mm) {
            moved = -1;
            mm = 0;
            var store = AssetDatabase.LoadAssetAtPath<WardrobeMeshStore>(path);
            if (store == null) return null;
            var parts = (AssetImporter.GetAtPath(path)?.userData ?? "").Split('|');
            if (parts.Length != 3 || parts[0] != key || !int.TryParse(parts[1], out int m)) return null;
            if (m > 0 && store.mesh == null) return null;
            float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out mm);
            moved = m;
            return store.mesh;
        }

        // Keeps the copy (or, without one, that the garment needed none) at the path; overwrites what's there in place, so
        // every renderer wearing it keeps it. Returns the kept copy.
        private static Mesh Save(string path, Mesh made, string userData) {
            WardrobeAssets.EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var store = AssetDatabase.LoadAssetAtPath<WardrobeMeshStore>(path);
            if (store == null) {
                store = ScriptableObject.CreateInstance<WardrobeMeshStore>();
                store.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(store, path);
            }
            if (made != null) {
                if (store.mesh != null) {
                    EditorUtility.CopySerialized(made, store.mesh);
                    Object.DestroyImmediate(made);
                } else {
                    AssetDatabase.AddObjectToAsset(made, store);
                    store.mesh = made;
                }
                EditorUtility.SetDirty(store.mesh);
            }
            EditorUtility.SetDirty(store);
            AssetDatabase.SaveAssetIfDirty(store);
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = userData;
            importer.SaveAndReimport();
            var mesh = AssetDatabase.LoadAssetAtPath<WardrobeMeshStore>(path)?.mesh;
            if (mesh != null) WardrobeFitter.Refresh(mesh);
            return mesh;
        }

        // The renderer wears the mesh, its shape keys at the weights they had (by name) and the clearance at 100. Kept as
        // the scene's (a prefab override where it's an instance), not an undo step: the pass follows what's undone.
        private static void Put(SkinnedMeshRenderer skin, Mesh mesh) {
            if (mesh == null) return;
            var old = skin.sharedMesh;
            bool changed = false;
            if (old != mesh) {
                var weights = new Dictionary<string, float>();
                if (old != null)
                    for (int i = 0; i < old.blendShapeCount; i++) weights[old.GetBlendShapeName(i)] = skin.GetBlendShapeWeight(i);
                skin.sharedMesh = mesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    skin.SetBlendShapeWeight(i, weights.TryGetValue(mesh.GetBlendShapeName(i), out float w) ? w : 0);
                changed = true;
            }
            int clearance = mesh.GetBlendShapeIndex(ShapeName);
            if (clearance >= 0 && !Mathf.Approximately(skin.GetBlendShapeWeight(clearance), 100)) {
                skin.SetBlendShapeWeight(clearance, 100);
                changed = true;
            }
            if (!changed) return;
            if (PrefabUtility.IsPartOfPrefabInstance(skin)) PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
            EditorUtility.SetDirty(skin);
        }
    }
}
