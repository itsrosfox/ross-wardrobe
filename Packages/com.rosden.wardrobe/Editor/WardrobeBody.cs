using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Gives every garment worn on the avatar the body's weights of the shape keys they share (as in-game). Without
        /// <paramref name="undo"/> the change is still kept as the scene's (a prefab override where it's an instance).
        /// </summary>
        /// <returns>Whether any garment's weights changed.</returns>
        public static bool Follow(GameObject avatar, bool undo = true) {
            var b = WardrobeAvatars.Match(avatar);
            var body = BodyOf(avatar, b);
            if (body == null || body.sharedMesh == null) return false;
            bool changed = false;
            foreach (var rc in avatar.GetComponentsInChildren<WardrobeRecolor>(true))
            foreach (var skin in rc.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var mesh = skin.sharedMesh;
                if (mesh == null || skin == body) continue;
                bool recorded = false;
                float BodyWeight(string shape) {
                    int j = body.sharedMesh.GetBlendShapeIndex(shape);
                    return j >= 0 ? body.GetBlendShapeWeight(j) : 0;
                }
                for (int i = 0; i < mesh.blendShapeCount; i++) {
                    string name = mesh.GetBlendShapeName(i);
                    float weight;
                    if (body.sharedMesh.GetBlendShapeIndex(name) >= 0) weight = BodyWeight(name);
                    // a corrective (several body keys: the product of their weights over its sim's, see WardrobeSim)
                    else if (WardrobeSim.ParseCombo(name) is List<WardrobeShapeWeight> combo) weight = WardrobeSim.ComboWeight(combo, BodyWeight);
                    else continue;
                    if (Mathf.Approximately(skin.GetBlendShapeWeight(i), weight)) continue;
                    if (!recorded && undo) Undo.RecordObject(skin, "Body Shape");
                    recorded = true;
                    skin.SetBlendShapeWeight(i, weight);
                }
                if (recorded && !undo) {
                    if (PrefabUtility.IsPartOfPrefabInstance(skin)) PrefabUtility.RecordPrefabInstancePropertyModifications(skin);
                    EditorUtility.SetDirty(skin);
                }
                changed |= recorded;
            }
            WardrobeClearance.Schedule(avatar);   // then moves the cloth out where it still clips
            return changed;
        }

        // ---------------------------------------------------------------- body hides

        /// <summary>
        /// The body shape keys the worn garments suggest hiding (their bodyHides that the body has), each with the garments
        /// that suggest it. Nothing ever sets these by itself: the Avatar section lists them, and the user turns them on.
        /// </summary>
        public static List<(string shape, List<string> garments)> SuggestedHides(GameObject avatar) {
            var body = BodyOf(avatar, WardrobeAvatars.Match(avatar));
            if (body == null || body.sharedMesh == null) return new List<(string, List<string>)>();
            return avatar.GetComponentsInChildren<WardrobeRecolor>(true).Where(rc => rc.garment != null)
                .SelectMany(rc => (rc.garment.bodyHides ?? new List<string>()).Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => (shape: h.Trim(), garment: string.IsNullOrWhiteSpace(rc.garment.displayName)
                        ? WardrobeAssets.BaseName(rc.garment) : rc.garment.displayName)))
                .Where(p => body.sharedMesh.GetBlendShapeIndex(p.shape) >= 0)
                .GroupBy(p => p.shape)
                .Select(grp => (grp.Key, grp.Select(p => p.garment).Distinct().ToList())).ToList();
        }

        // A garment's shape key weights are the scene's: unsaved, or saved before the body or the garment's fit changed,
        // a worn garment wouldn't match the body it's on. Every opened scene's avatars take the body's weights again.
        [InitializeOnLoadMethod]
        private static void FollowOnOpen() {
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => FollowAll();
            EditorApplication.delayCall += FollowAll;   // scenes already open after a script reload or at startup
        }

        private static void FollowAll() {
            var avatars = new HashSet<GameObject>();
            foreach (var rc in UnityEngine.Object.FindObjectsOfType<WardrobeRecolor>(true)) {
                if (rc == null || EditorUtility.IsPersistent(rc)) continue;
                var avatar = WardrobeWindow.AvatarRoot(rc.transform);
                if (avatar != null) avatars.Add(avatar);
            }
            foreach (var avatar in avatars) {
                try {
                    if (Follow(avatar, undo: false)) Debug.Log($"[Ros' Wardrobe] '{avatar.name}': worn garments took on the body's shape keys again.", avatar);
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
        }
    }
}
