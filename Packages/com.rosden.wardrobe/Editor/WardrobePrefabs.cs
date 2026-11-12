using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Make Prefab: the prefab the Ros' Wardrobe window puts on avatars, made from the garment's source model with its
    /// fitted mesh and shipped material. With VRCFury in the project it gets VRCFury's setup for skinned clothing: an
    /// Armature Link (the garment's bones merged into the avatar's, scaled exactly), a Blendshape Link (the garment follows
    /// the body's shape keys) and a saved toggle that's on by default. VRCFury is used by reflection, so Ros' Wardrobe
    /// doesn't need it to compile.
    /// </summary>
    public static class WardrobePrefabs {
        private const string Api = "com.vrcfury.api";

        /// <summary>Whether VRCFury is in the project.</summary>
        public static bool HasVrcFury => FuryComponents != null;

        private static Type FuryComponents => Type.GetType($"com.vrcfury.api.FuryComponents, {Api}");

        /// <summary>Where a new prefab goes: "&lt;Garment&gt;.prefab" next to the .rgarm.</summary>
        public static string PrefabPath(WardrobeGarment g) => $"{WardrobeGarments.FolderOf(g)}/{WardrobeAssets.FileName(WardrobeAssets.BaseName(g))}.prefab";

        /// <summary>The toggle's menu path when the garment doesn't set one.</summary>
        public static string MenuPath(WardrobeGarment g) =>
            !string.IsNullOrWhiteSpace(g.menuPath) ? g.menuPath.Trim() : $"Clothing/{WardrobeAssets.BaseName(g)}";

        /// <summary>Why the prefab can't be made yet, or null.</summary>
        public static string Problem(WardrobeGarment g) {
            if (g.source == null) return "Pick the Source Model first (step 2).";
            if (SourceSkin(g.source) == null) return $"{g.source.name} has no skinned mesh: export it rigged to the avatar's skeleton.";
            if (HasVrcFury && Hips(g, g.source.transform) == null)
                return $"Can't find the hips bone in {g.source.name}, so VRCFury can't link it to the avatar. Export it with the avatar's skeleton.";
            return null;
        }

        /// <summary>Whether the prefab is made the way Make Prefab makes it (its armature linked, wearing the fit and the shipped material).</summary>
        public static string Mismatch(WardrobeGarment g) {
            if (g.prefab == null) return "There's no prefab yet.";
            var skins = g.prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (g.mesh != null && skins.All(s => s.sharedMesh != g.mesh)) return "The prefab doesn't wear the fitted mesh.";
            if (HasVrcFury && !Linked(g.prefab)) return "The prefab has no Armature Link (VRCFury or Modular Avatar), so it won't follow the avatar.";
            return null;
        }

        /// <summary>Whether a prefab has its armature linked to the avatar's (VRCFury's Armature Link or Modular Avatar's Merge Armature).</summary>
        public static bool Linked(GameObject prefab) =>
            prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c => {
                if (c == null) return false;
                string type = c.GetType().FullName ?? "";
                if (type.Contains("MergeArmature")) return true;
                if (type != "VF.Model.VRCFury") return false;
                var content = c.GetType().GetField("content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c);
                return content != null && content.GetType().Name == "ArmatureLink";
            });

        /// <summary>
        /// Makes (or makes again) the garment's prefab and points the garment at it. An existing prefab is replaced, so what
        /// was added to it by hand is lost.
        /// </summary>
        public static GameObject Make(WardrobeGarment g) {
            string problem = Problem(g);
            if (problem != null) throw new InvalidOperationException(problem);
            string path = g.prefab != null ? AssetDatabase.GetAssetPath(g.prefab) : PrefabPath(g);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(g.source);
            GameObject prefab;
            try {
                root.name = WardrobeAssets.BaseName(g);
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var skin = SourceSkin(root);
                if (g.mesh != null && AssetDatabase.GetAssetPath(g.mesh) == WardrobeFitter.FitPath(g)) skin.sharedMesh = g.mesh;
                var type = WardrobeMaterialTypes.For(g, g.defaultMaterial);
                var material = type != null ? AssetDatabase.LoadAssetAtPath<Material>(WardrobeMaterialTypes.ShippedPath(g, type)) : null;
                if (material != null) skin.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, skin.sharedMesh.subMeshCount)).ToArray();

                if (HasVrcFury) {
                    var hips = Hips(g, root.transform);
                    var link = Call(null, FuryComponents, "CreateArmatureLink", root);
                    Call(link, null, "LinkFrom", hips.gameObject);
                    Call(link, null, "LinkTo", HumanBodyBones.Hips, "");
                    // what VRCFury's inspector does for skinned clothing: merge every matching child bone
                    Call(link, null, "SetRecursive", true);
                    Call(link, null, "SetAlign", true);
                    ExactScale(root);
                    var avatar = WardrobeAvatars.Get(g.avatarId);
                    AddBlendShapeLink(root, avatar?.body ?? "Body", skin);
                    var toggle = Call(null, FuryComponents, "CreateToggle", root);
                    Call(toggle, null, "SetMenuPath", MenuPath(g));
                    Call(toggle, null, "SetSaved");
                    // worn as soon as it's added; the menu toggles it off. No body hides on the toggle: the body keeps what
                    // the user set on it (the .rgarm's hides are suggestions the window's Avatar section offers).
                    Call(toggle, null, "SetDefaultOn");
                    var actions = Call(toggle, null, "GetActions");
                    Call(actions, null, "AddTurnOn", skin.gameObject);
                }
                // No Wardrobe Recolor on the prefab: Ros' Wardrobe is optional for buyers (it'd be a missing script without
                // it). The window adds one when it puts the garment on.
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            } finally {
                Object.DestroyImmediate(root);
            }
            g.prefab = prefab;
            if (WardrobeGarments.IsDraft(g)) EditorUtility.SetDirty(g);
            else WardrobeGarments.Changed(g);
            return prefab;
        }

        /// <summary>The first skinned mesh with a mesh under a model.</summary>
        internal static SkinnedMeshRenderer SourceSkin(GameObject model) =>
            model == null ? null : model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh != null);

        /// <summary>
        /// The garment's hips bone under <paramref name="root"/>: the bone with the name the avatar's humanoid rig gives its
        /// hips, else one called Hips.
        /// </summary>
        internal static Transform Hips(WardrobeGarment g, Transform root) {
            var all = root.GetComponentsInChildren<Transform>(true);
            string avatarHips = AvatarHips(WardrobeAvatars.Get(g.avatarId));
            if (!string.IsNullOrEmpty(avatarHips)) {
                var same = all.FirstOrDefault(t => string.Equals(t.name, avatarHips, StringComparison.OrdinalIgnoreCase));
                if (same != null) return same;
            }
            return all.FirstOrDefault(t => string.Equals(t.name, "Hips", StringComparison.OrdinalIgnoreCase)) ??
                   all.FirstOrDefault(t => t.name.IndexOf("hips", StringComparison.OrdinalIgnoreCase) >= 0 && t != root);
        }

        // The bone name of the avatar model's humanoid Hips (its import settings), or null.
        private static string AvatarHips(WardrobeAvatar avatar) {
            var model = avatar?.Model;
            if (model == null) return null;
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.avatar != null &&
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(animator.avatar)) is ModelImporter importer)
                return importer.humanDescription.human.FirstOrDefault(h => h.humanName == "Hips").boneName;
            return null;
        }

        // The Armature Link scales the garment's bones by the ratio of its armature's scale to the avatar's, but by default
        // only by a power of 10. Cloth tools export the skeleton at their avatar's scale (x100), and a base whose bones
        // aren't a power of 10 from that (Novabeast: 2.54, a ratio of 39.37) would get 10 and shrink the garment in game.
        // The API can't set it, so it's set on the component (VRCFury's "only powers of 10" off).
        private static void ExactScale(GameObject root) {
            foreach (var component in root.GetComponents<MonoBehaviour>()) {
                using (var so = new SerializedObject(component)) {
                    var only = so.FindProperty("content.scalingFactorPowersOf10Only");
                    if (only == null || !only.boolValue) continue;
                    only.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        // VRCFury's Blendshape Link (not in its public API): the garment's shape keys follow the body's of the same name.
        private static void AddBlendShapeLink(GameObject obj, string body, SkinnedMeshRenderer skin) {
            var vrcfury = Type.GetType("VF.Model.VRCFury, VRCFury");
            var linkType = Type.GetType("VF.Model.Feature.BlendShapeLink, VRCFury");
            var skinType = linkType?.GetNestedType("LinkSkin", BindingFlags.Public | BindingFlags.NonPublic);
            if (vrcfury == null || skinType == null) {
                Debug.LogWarning("[Ros' Wardrobe] This VRCFury has no Blendshape Link; add one to the prefab by hand so the garment follows the body.");
                return;
            }
            var model = Activator.CreateInstance(linkType, true);
            linkType.GetField("baseObj")?.SetValue(model, body);
            linkType.GetField("includeAll")?.SetValue(model, true);
            var linkSkin = Activator.CreateInstance(skinType, true);
            skinType.GetField("renderer")?.SetValue(linkSkin, skin);
            ((IList)linkType.GetField("linkSkins")?.GetValue(model))?.Add(linkSkin);
            var component = obj.AddComponent(vrcfury);
            vrcfury.GetField("content")?.SetValue(component, model);
        }

        // Calls a method by name (static on type when target is null), filling in optional parameters.
        private static object Call(object target, Type type, string name, params object[] args) {
            type = type ?? target?.GetType() ?? throw new InvalidOperationException($"VRCFury: nothing to call {name} on.");
            var flags = BindingFlags.Public | (target == null ? BindingFlags.Static : BindingFlags.Instance);
            foreach (var method in type.GetMethods(flags).Where(m => m.Name == name)) {
                var parameters = method.GetParameters();
                if (parameters.Length < args.Length || parameters.Skip(args.Length).Any(p => !p.HasDefaultValue)) continue;
                bool fits = true;
                for (int i = 0; i < args.Length && fits; i++)
                    fits = args[i] == null ? !parameters[i].ParameterType.IsValueType : parameters[i].ParameterType.IsInstanceOfType(args[i]);
                if (!fits) continue;
                var all = args.Concat(parameters.Skip(args.Length).Select(p => p.DefaultValue)).ToArray();
                return method.Invoke(target, all);
            }
            throw new MissingMethodException($"VRCFury's {type.Name} has no {name} that Ros' Wardrobe knows. Update VRCFury, or Ros' Wardrobe.");
        }
    }
}
