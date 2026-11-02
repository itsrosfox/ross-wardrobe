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
    }
}
