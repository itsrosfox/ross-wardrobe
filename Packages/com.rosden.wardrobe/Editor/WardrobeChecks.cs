using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Something wrong (or worth knowing) about one Ros' Wardrobe file, and a way to fix it when there's one.</summary>
    public class WardrobeProblem {
        /// <summary>The file it's about.</summary>
        public string path;
        public string message;
        /// <summary>Error: it doesn't work. Warning: it likely isn't what you meant. Info: a step left to do.</summary>
        public MessageType type;
        /// <summary>The fix button's label, or null when there's no fix.</summary>
        public string fix;
        public Action apply;

        public string File => Path.GetFileName(path);
    }

    /// <summary>
    /// Checks every Ros' Wardrobe file in the project for the mistakes people make setting one up (a missing id or file,
    /// textures imported as the wrong kind, a fit that's out of date, regions that don't add up, ids that point at nothing),
    /// so the Setup tab and the inspector can show them with a fix. Checked again whenever an asset changes.
    /// </summary>
    public static class WardrobeChecks {
        private static List<WardrobeProblem> _all;

        /// <summary>Every problem in the project, worst first.</summary>
        public static List<WardrobeProblem> All => _all ??= WardrobeConfigs.Extensions.SelectMany(WardrobeConfigs.Paths).SelectMany(Check)
            .OrderBy(p => Rank(p.type)).ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase).ToList();

        public static void Forget() => _all = null;

        /// <summary>The problems of one file.</summary>
        public static List<WardrobeProblem> For(string path) => All.Where(p => p.path == path).ToList();

        public static int Count(MessageType type) => All.Count(p => p.type == type);

        private static int Rank(MessageType t) => t == MessageType.Error ? 0 : t == MessageType.Warning ? 1 : 2;

        /// <summary>Checks one file.</summary>
        public static List<WardrobeProblem> Check(string path) {
            var list = new List<WardrobeProblem>();
            JsonObject j;
            try {
                j = WardrobeJson.Parse(File.ReadAllText(path));
            } catch (Exception e) {
                list.Add(new WardrobeProblem {
                    path = path, type = MessageType.Error,
                    message = $"Can't be read: {e.Message}. Undo your last hand edit, or open it in a text editor and fix the json.",
                });
                return list;
            }
            void Add(MessageType type, string message, string fix = null, Action apply = null) =>
                list.Add(new WardrobeProblem { path = path, type = type, message = message, fix = fix, apply = apply });
            try {
                CheckIds(path, j, Add);
                Links(path, j, Add);
                switch (Path.GetExtension(path).ToLowerInvariant()) {
                    case WardrobeConfigs.Garment: Garment(path, j, Add); break;
                    case WardrobeConfigs.Avatar: Avatar(path, j, Add); break;
                    case WardrobeConfigs.Material: Material(path, j, Add); break;
                    case WardrobeConfigs.Pattern: Pattern(path, j, Add); break;
                    case WardrobeConfigs.Preset: Preset(path, j, Add); break;
                }
            } catch (Exception e) {
                Add(MessageType.Warning, $"Couldn't check it all ({e.GetType().Name}: {e.Message}).");
            }
            return list;
        }

        private delegate void Adder(MessageType type, string message, string fix = null, Action apply = null);

        // ---------------------------------------------------------------- every kind

        private static void CheckIds(string path, JsonObject j, Adder add) {
            string id = j.String("id").Trim();
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (id.Length == 0) {
                string suggestion = WardrobeConfigs.SuggestId(j.String("author"), j.String("name").Length > 0 ? j.String("name") : Path.GetFileNameWithoutExtension(path));
                add(MessageType.Error, "It has no id, so nothing can refer to it.", $"Use {suggestion}", () => EditJson(path, o => o["id"] = suggestion));
                return;
            }
            if (ext == WardrobeConfigs.Pattern) return;   // the same pattern from several packs is merged, by design
            if (ext == WardrobeConfigs.Garment) {
                string avatar = j.Object("avatar")?.String("id") ?? "";
                var same = WardrobeConfigs.Paths(ext).Where(p => p != path).Where(p => {
                    var g = AssetDatabase.LoadAssetAtPath<WardrobeGarment>(p);
                    return g != null && WardrobeConfigs.SameId(g.id, id) && WardrobeConfigs.SameId(g.avatarId, avatar);
                }).ToList();
                if (same.Count > 0)
                    add(MessageType.Warning, $"{string.Join(", ", same.Select(Path.GetFileName))} is the same garment ({id}) on the same avatar; only one of them is used. " +
                                              "Fits for other avatars keep the id; a different garment needs its own.");
                return;
            }
            var others = WardrobeConfigs.Paths(ext).Where(p => p != path && SameIdIn(p, id)).ToList();
            if (others.Count > 0)
                add(MessageType.Warning, $"{string.Join(", ", others.Select(Path.GetFileName))} has the same id ({id}); only one of them is used.");
        }

        private static bool SameIdIn(string path, string id) {
            try {
                return WardrobeConfigs.SameId(WardrobeJson.Parse(File.ReadAllText(path)).String("id"), id);
            } catch (Exception) {
                return false;
            }
        }

        // Every file the json names must be there; when one with the same name is elsewhere, offer it.
        private static void Links(string path, JsonObject j, Adder add) {
            foreach (var (owner, key, label) in References(path, j)) {
                string value = owner.String(key).Trim();
                if (value.Length == 0) continue;
                if (WardrobeConfigs.Load<Object>(path, value) != null) continue;
                string found = FindByName(value);
                string sub = value.Contains("#") ? value.Substring(value.LastIndexOf('#')) : "";
                if (found != null) {
                    string relative = WardrobeConfigs.RelativePath(path, found) + sub;
                    add(MessageType.Error, $"{label}: can't find '{value}' (moved or renamed?). There's one at {found}.", "Use it",
                        () => EditJson(path, o => SetAt(o, j, owner, key, relative)));
                } else {
                    add(MessageType.Error, $"{label}: can't find '{value}'. Pick it again (moved or renamed?), or import what it's in.");
                }
            }
        }

        /// <summary>The asset paths a file names: (the object holding it, its key, what it is).</summary>
        internal static IEnumerable<(JsonObject owner, string key, string label)> References(string path, JsonObject j) {
            switch (Path.GetExtension(path).ToLowerInvariant()) {
                case WardrobeConfigs.Garment:
                    foreach (var k in new[] { ("prefab", "Prefab"), ("thumbnail", "Thumbnail"), ("source", "Source model"), ("mesh", "Mesh"), ("idMap", "ID map"), ("gradientMap", "Gradient map") })
                        yield return (j, k.Item1, k.Item2);
                    var maps = j.Object("maps");
                    if (maps != null) {
                        yield return (maps, "baseColor", "Base colour");
                        yield return (maps, "normal", "Normal map");
                        foreach (string m in new[] { "metalness", "roughness", "occlusion", "sheen", "opacity" })
                            if (maps.Object(m) is JsonObject o) yield return (o, "texture", Title(m) + " map");
                    }
                    foreach (var sim in j.Objects("sims")) yield return (sim, "source", "Body shape export");
                    break;
                case WardrobeConfigs.Material:
                    yield return (j, "template", "Material");
                    break;
                case WardrobeConfigs.Pattern:
                    foreach (string m in PatternMaps) yield return (j, m, Title(m));
                    foreach (var g in j.Objects("garments"))
                    foreach (string m in PatternMaps.Concat(new[] { "gradient", "albedo", "mask" }))
                        yield return (g, m, $"{g.String("garment")}: {Title(m)}");
                    break;
            }
        }

        private static readonly string[] PatternMaps = { "weights", "normal", "height", "surface", "occlusion" };

        private static string Title(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

        // The project file with the same name as a missing one, when there's exactly one.
        private static string FindByName(string value) {
            string file = Path.GetFileName(value.Split('#')[0]);
            if (file.Length == 0) return null;
            var hits = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(file)).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        // ---------------------------------------------------------------- garments

        private static void Garment(string path, JsonObject j, Adder add) {
            var g = AssetDatabase.LoadAssetAtPath<WardrobeGarment>(path);
            if (g == null) return;
            string name = WardrobeForm.Title(g);
            var avatar = WardrobeAvatars.Get(g.avatarId);

            if (string.IsNullOrWhiteSpace(g.displayName)) add(MessageType.Info, "It has no name; the window shows the file's.");
            if (string.IsNullOrWhiteSpace(g.category)) add(MessageType.Info, "It has no category, so the window lists it under Other.");
            if (string.IsNullOrWhiteSpace(g.avatarId))
                add(MessageType.Warning, "It doesn't say which avatar base it's made for, so it's offered for every avatar and can't be fitted.");
            else if (avatar == null)
                add(MessageType.Warning, $"It's made for {g.avatarId}, but there's no .ravt with that id here. Ship the avatar base's .ravt with the garment.");

            // the body shape keys it names
            var body = WardrobeFitter.BodyShapes(g);
            if (body.Count > 0) {
                var missing = g.bodyHides.Where(k => !body.Contains(k)).ToList();
                if (missing.Count > 0)
                    add(MessageType.Warning, $"Hides on body: {avatar?.DisplayName}'s body has no {Join(missing)}.", "Remove them", () => {
                        g.bodyHides.RemoveAll(missing.Contains);
                        WardrobeGarments.Save(g);
                    });
                var sims = (g.sims ?? new List<WardrobeSim>()).SelectMany(s => s.shapes).Select(s => s.shape).Where(s => !body.Contains(s)).Distinct().ToList();
                if (sims.Count > 0) add(MessageType.Warning, $"Body shape exports name {Join(sims)}, which the body doesn't have.");
            }

            // maps
            if (g.baseColor == null) add(MessageType.Error, "It has no base colour map, so it can't be recoloured or get a material.");
            CheckNormal(g.normalMap, "The normal map", add);
            foreach (var (map, label) in new[] { (g.metalness, "metalness"), (g.roughness, "roughness"), (g.occlusion, "occlusion"), (g.sheen, "sheen"), (g.opacity, "opacity") })
                if (map?.texture != null && map.texture != g.baseColor) CheckLinear(map.texture, $"The {label} map", add);

            // fit
            if (g.source != null) {
                string problem = WardrobeFitter.Problem(g);
                bool fitted = g.mesh != null && AssetDatabase.GetAssetPath(g.mesh) == WardrobeFitter.FitPath(g);
                if (problem != null) add(MessageType.Warning, "It can't be fitted: " + problem);
                else if (!fitted) add(MessageType.Info, $"Not fitted to {avatar.DisplayName} yet.", "Fit", () => WardrobeFitter.Fit(g));
                else if (!WardrobeFitter.UpToDate(g))
                    add(MessageType.Warning, "The fit is out of date: the source model, the avatar or its shape keys changed.", "Fit again", () => WardrobeFitter.Fit(g));
            } else if (g.mesh == null) {
                add(MessageType.Error, "It has no source model (the cloth tool's export) or mesh.");
            }

            // prefab
            if (g.prefab == null) {
                string why = WardrobePrefabs.Problem(g);
                add(MessageType.Warning, "It has no prefab, so the window can't put it on." + (why != null ? " " + why : ""),
                    why == null ? "Make prefab" : null, why == null ? () => WardrobePrefabs.Make(g) : (Action)null);
            } else {
                string mismatch = WardrobePrefabs.Mismatch(g);
                if (mismatch != null) add(MessageType.Warning, mismatch + " Make Prefab makes it again (what you added to it by hand is lost).");
                string folder = WardrobeGarments.FolderOf(g) + "/";
                var foreign = g.prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null)
                    .Where(m => AssetDatabase.GetAssetPath(m).StartsWith("Assets/Wardrobe Recolors/")).Select(m => m.name).Distinct().ToList();
                if (foreign.Count > 0)
                    add(MessageType.Error, $"The prefab wears {Join(foreign)} from Assets/Wardrobe Recolors (a look baked in this project), which buyers won't have.",
                        "Put the shipped material back", () => Ship(g));
                else if (!AssetDatabase.GetAssetPath(g.prefab).StartsWith(folder))
                    add(MessageType.Info, "The prefab isn't in the garment's folder; ship it with the garment.");
            }

            // material
            var types = WardrobeMaterialTypes.All.Where(t => t.Fits(g)).ToList();
            if (!string.IsNullOrWhiteSpace(g.defaultMaterial) && WardrobeMaterialTypes.Get(g.defaultMaterial) == null)
                add(MessageType.Warning, $"It starts in {g.defaultMaterial}, a material type that isn't in the project; it starts in the first that fits instead.");
            if (types.Count == 0)
                add(MessageType.Error, "No material type (.rmat) is for it, so it can't be recoloured. List it in one, or make one in Setup > Material types.");
            var type = WardrobeMaterialTypes.For(g, g.defaultMaterial);
            if (type != null && g.baseColor != null) {
                bool made = File.Exists(WardrobeMaterialTypes.ShippedPath(g, type));
                if (!made) add(MessageType.Info, $"Its {type.DisplayName} material isn't made yet.", "Make material", () => Ship(g));
                else if (!WardrobeMaterialTypes.ShippedUpToDate(g, type))
                    add(MessageType.Warning, $"Its {type.DisplayName} material is out of date (the maps or the material type changed).", "Make it again", () => Ship(g));
            }

            // recolouring
            if (g.idMap == null) {
                add(MessageType.Info, "It has no ID map yet, so it can't be recoloured.", g.mesh != null ? "Make ID map" : null,
                    g.mesh != null ? () => Maps(g, true) : (Action)null);
                return;
            }
            CheckLinear(g.idMap, "The ID map", add, false);
            if (g.gradientMap == null) add(MessageType.Info, "It has no gradient map, so gradients run the same everywhere.", g.mesh != null ? "Make gradient map" : null,
                g.mesh != null ? () => { WardrobeGenerators.GenerateGradientMap(g, 2048, WardrobeGenerators.GradientDirection.TopToBottom); WardrobeGarments.Save(g); } : (Action)null);
            List<WardrobeBaker.Part> parts;
            try {
                parts = WardrobeBaker.GetParts(g);
            } catch (Exception e) {
                add(MessageType.Error, "Its ID map can't be read: " + e.Message);
                return;
            }
            if (g.regions.Count == 0)
                add(MessageType.Error, "It has no regions, so nothing can be recoloured.", "One region per part", () => {
                    for (int i = 0; i < parts.Count; i++) g.regions.Add(new WardrobeRegion { name = $"Part {i + 1}", ids = new List<Color32> { parts[i].color } });
                    WardrobeGarments.Save(g);
                });
            if (g.regions.Count > WardrobeBaker.MaxRegions)
                add(MessageType.Error, $"It has {g.regions.Count} regions; only the first {WardrobeBaker.MaxRegions} can be recoloured. Put parts together.");
            var duplicate = g.regions.GroupBy(r => r.name.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            if (duplicate.Count > 0) add(MessageType.Error, $"Two regions are called {Join(duplicate)}; looks can't tell them apart. Rename one.");
            var empty = g.regions.Where(r => r.ids.All(c => parts.All(p => !WardrobeBaker.Same(p.color, c)))).Select(r => r.name).ToList();
            if (empty.Count > 0 && parts.Count > 0) add(MessageType.Warning, $"{Join(empty)} has no part of the ID map in it.");
            var loose = parts.Where(p => g.regions.All(r => r.ids.All(c => !WardrobeBaker.Same(c, p.color)))).ToList();
            if (loose.Count > 0 && g.regions.Count > 0)
                add(MessageType.Info, $"{loose.Count} part(s) of the ID map are in no region, so they're never recoloured (fine for trims you keep as they are).");

            // part scales made with the wrong size (the maps made before the fit had a model around it): ~100x off
            var scales = g.partScales.Select(p => p.metresPerUv).Where(s => s > 0).ToList();
            if (scales.Count == 0)
                add(MessageType.Info, "Patterns don't know its parts' real size yet.", g.mesh != null ? "Make ID map again" : null, g.mesh != null ? () => Maps(g, false) : (Action)null);
            else {
                float median = scales.OrderBy(s => s).ElementAt(scales.Count / 2);
                if (median > 20 || median < 0.02f)
                    add(MessageType.Warning, $"Its parts measure {median:0.###} m per UV unit, about 100 times off, so patterns would tile at the wrong size. Make the ID map again.",
                        "Make ID map again", () => Maps(g, false));
            }

            // patterns it needs maps in
            foreach (var p in WardrobePatterns.All.Where(p => p.type != WardrobePattern.Kind.Simple && p.garments.Any(s => s.target.Matches(g)))) {
                var maps = p.MapsFor(g);
                if (p.type == WardrobePattern.Kind.Hybrid && !maps.uv)
                    add(MessageType.Warning, $"The pattern {p.DisplayName} lists it but has no maps for it.");
            }
        }

        private static void Ship(WardrobeGarment g) {
            var type = WardrobeMaterialTypes.For(g, g.defaultMaterial) ?? throw new InvalidOperationException("No material type fits it.");
            WardrobeMaterialTypes.Ship(g, type);
        }

        private static void Maps(WardrobeGarment g, bool createRegions) {
            WardrobeGenerators.GenerateIdMap(g, 2048, createRegions && g.regions.Count == 0);
            WardrobeGarments.Save(g);
        }

        // ---------------------------------------------------------------- avatars

        private static void Avatar(string path, JsonObject j, Adder add) {
            var a = WardrobeAvatars.All.FirstOrDefault(x => x.path == path);
            if (a == null) return;
            var guids = (j.Array("guids") ?? new List<object>()).OfType<string>().ToList();
            var matches = (j.Array("matches") ?? new List<object>()).OfType<string>().Where(m => m.Trim().Length > 0).ToList();
            if (guids.Count == 0 && matches.Count == 0)
                add(MessageType.Error, "Nothing says which avatars are this base: drop the avatar's model in, or add a name to recognise it by.");
            var model = a.Model;
            if (model == null) {
                if (guids.Count > 0) add(MessageType.Info, "The avatar's model isn't in this project, so garments can't be fitted to it here.");
                return;
            }
            var body = model.transform.Find(a.body)?.GetComponent<SkinnedMeshRenderer>();
            if (body == null || body.sharedMesh == null) {
                var best = WardrobeAvatarForm.Renderers(model).OrderByDescending(s => s.sharedMesh.blendShapeCount).FirstOrDefault();
                string bestPath = best != null ? AnimationUtility.CalculateTransformPath(best.transform, model.transform) : null;
                add(MessageType.Error, $"{model.name} has no body at '{a.body}'.", bestPath != null ? $"Use {bestPath}" : null,
                    bestPath != null ? () => EditJson(path, o => o["body"] = bestPath) : (Action)null);
                return;
            }
            var mesh = body.sharedMesh;
            // the base's files in this project (its model, and prefabs that add hair, physbones, outfits to it)
            var models = WardrobeAvatarForm.ModelsOf(a.guids);
            string names = Join(models.Select(m => m.name));
            var shapes = new HashSet<string>(models.Select(m => m.transform.Find(a.body)?.GetComponent<SkinnedMeshRenderer>()?.sharedMesh).Where(m => m != null)
                .SelectMany(m => Enumerable.Range(0, m.blendShapeCount).Select(m.GetBlendShapeName)));
            bool Has(WardrobeShapeValue v) => models.Any(m => WardrobeAvatarForm.Has(m, v));
            foreach (var s in a.sliders) {
                var missing = s.set.Concat(s.options.SelectMany(o => o.set)).Where(v => !Has(v)).Select(v => v.IsObject ? v.path : v.path == a.body ? v.shape : $"{v.path}:{v.shape}").Distinct().ToList();
                if (missing.Count > 0) add(MessageType.Warning, $"The control {s.name} sets {Join(missing)}, which {(models.Count > 1 ? "none of " : "")}{names} {(models.Count > 1 ? "have" : "doesn't have")}.");
            }
            var fit = a.fitShapes.Where(s => !shapes.Contains(s)).ToList();
            if (fit.Count > 0)
                add(MessageType.Warning, $"Garments follow {Join(fit)}, which the body doesn't have.", "Remove them", () => EditJson(path, o => {
                    var fitting = o.Object("fitting");
                    var list = fitting?.Array("shapeKeys");
                    list?.RemoveAll(x => x is string s && fit.Contains(s.Trim()));
                }));
            var bones = models.SelectMany(m => m.GetComponentsInChildren<Transform>(true)).Select(t => t.name).Distinct().ToList();
            foreach (var (prefix, bone) in a.fitBones) {
                if (bones.All(b => !b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    add(MessageType.Info, $"No bone of {model.name} starts with '{prefix}' (Bones garments don't move with).");
                if (bones.All(b => !string.Equals(b, bone, StringComparison.OrdinalIgnoreCase)))
                    add(MessageType.Warning, $"{model.name} has no bone '{bone}' to give the '{prefix}' bones' weight to.");
            }
            if (a.sliders.Count == 0 && a.fitShapes.Count == 0 && mesh.blendShapeCount > 0)
                add(MessageType.Info, "It has no body controls and garments follow none of its shape keys. Pick them in Body shape keys.");
        }

        // ---------------------------------------------------------------- material types

        private static void Material(string path, JsonObject j, Adder add) {
            var notes = new List<string>();
            var type = WardrobeMaterialTypes.Read(path, j, notes);
            foreach (string note in notes) add(MessageType.Warning, note);
            var shader = type.Shader;
            if (shader == null) {
                add(MessageType.Error, type.template == null && type.shaderName.Length == 0 ? "It names no material or shader." :
                    $"The shader '{type.shaderName}' isn't in this project. Install it (e.g. through VCC), or pick another.");
                return;
            }
            if (type.outputs.All(o => !o.albedo))
                add(MessageType.Error, "No texture gets the look's colour, so recolours wouldn't show. Pick the shader's main texture as Look colour.");
            foreach (var o in type.outputs)
            foreach (string p in o.Properties)
                if (shader.FindPropertyIndex(p) < 0) {
                    string property = p;
                    add(MessageType.Warning, $"{shader.name} has no texture {p}; nothing goes there.", "Remove it", () => EditJson(path, x => {
                        var list = x.Array("textures");
                        list?.RemoveAll(t => t is JsonObject t1 && t1.String("property").Trim() == property);
                        foreach (var t in x.Objects("textures")) t.Array("alsoOn")?.RemoveAll(v => v is string s && s.Trim() == property);
                    }));
                }
            foreach (var t in type.garments.Where(t => WardrobeGarments.All.All(g => !WardrobeConfigs.SameId(g.id, t.garment))))
                add(MessageType.Info, $"It's for {t.garment}, which isn't in this project (fine when it ships separately).");
        }

        // ---------------------------------------------------------------- patterns

        private static void Pattern(string path, JsonObject j, Adder add) {
            var p = WardrobePatterns.All.FirstOrDefault(x => x.jsonPath == path) ?? WardrobePatterns.All.FirstOrDefault(x => WardrobeConfigs.SameId(x.id, j.String("id")));
            string kind = j.String("type", "simple").Trim().ToLowerInvariant();
            if (kind != "simple" && kind != "hybrid" && kind != "complete") add(MessageType.Error, $"Its type '{kind}' isn't simple, hybrid or complete.");
            var garments = j.Objects("garments").ToList();
            if (kind == "complete" && garments.Count == 0) add(MessageType.Warning, "A complete pattern is only offered for the garments it has a picture for, and it lists none.");
            foreach (var g in garments) {
                string id = g.String("garment").Trim();
                if (id.Length == 0) { add(MessageType.Warning, "One of its garments has no id."); continue; }
                if (kind == "hybrid" && g.String("weights").Length == 0 && g.String("gradient").Length == 0)
                    add(MessageType.Warning, $"{id} has no weights or gradient map of its own, so it gets the tiling maps like any garment.");
                if (kind == "complete" && g.String("albedo").Length == 0) add(MessageType.Warning, $"{id} has no albedo, so the pattern isn't offered for it.");
                if (WardrobeGarments.All.All(x => !WardrobeConfigs.SameId(x.id, id)))
                    add(MessageType.Info, $"It's for {id}, which isn't in this project (fine when it ships separately).");
            }
            // textures imported as what they hold, the tiling ones repeating
            foreach (var (owner, key, label) in References(path, j)) {
                var texture = WardrobeConfigs.Load<Texture2D>(path, owner.String(key));
                if (texture == null) continue;
                if (key == "normal") CheckNormal(texture, label, add);
                else if (key != "albedo") CheckLinear(texture, label, add);
                if (owner == j && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter ti && ti.wrapMode != TextureWrapMode.Repeat)
                    add(MessageType.Warning, $"{label} tiles, but it's imported to clamp, so its edges would smear.", "Set it to repeat", () => {
                        ti.wrapMode = TextureWrapMode.Repeat;
                        ti.SaveAndReimport();
                    });
            }
            if (p != null && p.tiled.weights != null && j.Array("colors") is List<object> colors) {
                int used = WardrobePatternForm.UsedChannels(p.tiled.weights);
                if (used > colors.Count) add(MessageType.Warning, $"Its weights map uses {used} colours, but it has {colors.Count}.");
            }
        }

        // ---------------------------------------------------------------- presets

        private static void Preset(string path, JsonObject j, Adder add) {
            var targets = WardrobeConfigs.Targets(j);
            var garments = WardrobeGarments.All.Where(g => targets.Count == 0 || targets.Any(t => t.Matches(g))).ToList();
            var regions = new HashSet<string>(garments.SelectMany(g => g.regions.Select(r => r.name)), StringComparer.OrdinalIgnoreCase);
            foreach (var part in j.Objects("parts")) {
                string pattern = part.String("pattern").Trim();
                var known = WardrobePatterns.Get(pattern);
                if (known == null) add(MessageType.Warning, $"It uses the pattern {(pattern.Length == 0 ? "(none)" : pattern)}, which isn't in this project.");
                else if (known.slots.Count < (part.Array("colors")?.Count ?? 0))
                    add(MessageType.Info, $"{known.DisplayName} has {known.slots.Count} colours; the part gives {part.Array("colors").Count}.");
                if (garments.Count == 0) continue;
                var missing = WardrobePreset.Regions(part.String("region")).Where(r => r != WardrobePreset.AnyRegion && !regions.Contains(r)).ToList();
                if (missing.Count > 0) add(MessageType.Warning, $"No garment it's for has a region {Join(missing)}.");
            }
        }

        // ---------------------------------------------------------------- textures

        private static void CheckNormal(Texture2D texture, string label, Adder add) {
            if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter ti) || ti.textureType == TextureImporterType.NormalMap) return;
            add(MessageType.Warning, $"{label} ({texture.name}) isn't imported as a normal map, so its relief comes out wrong.", "Import as normal map", () => {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            });
        }

        // Maps that hold numbers, not colours: without sRGB.
        private static void CheckLinear(Texture2D texture, string label, Adder add, bool data = true) {
            if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter ti) || !ti.sRGBTexture || ti.textureType == TextureImporterType.NormalMap) return;
            add(MessageType.Warning, $"{label} ({texture.name}) holds {(data ? "values" : "ids")}, not colours, but it's imported as sRGB colour, which changes them.",
                "Turn sRGB off", () => {
                    ti.sRGBTexture = false;
                    ti.SaveAndReimport();
                });
        }

        // ---------------------------------------------------------------- editing files

        /// <summary>Changes a file's json and imports it again.</summary>
        internal static void EditJson(string path, Action<JsonObject> edit) {
            var j = WardrobeJson.Parse(File.ReadAllText(path));
            edit(j);
            File.WriteAllText(path, WardrobeJson.Write(j));
            AssetDatabase.ImportAsset(path);
        }

        // Sets owner[key] in a fresh parse of the same file: the object at the same place as in the parse it was found in.
        private static void SetAt(JsonObject fresh, JsonObject old, JsonObject owner, string key, string value) {
            var target = Find(fresh, old, owner);
            if (target != null) target[key] = value;
        }

        private static JsonObject Find(object fresh, object old, JsonObject owner) {
            if (ReferenceEquals(old, owner)) return fresh as JsonObject;
            if (old is JsonObject o && fresh is JsonObject f) {
                for (int i = 0; i < o.Count; i++) {
                    var hit = Find(f[o.Keys[i]], o.Values[i], owner);
                    if (hit != null) return hit;
                }
            } else if (old is List<object> a && fresh is List<object> b) {
                for (int i = 0; i < a.Count && i < b.Count; i++) {
                    var hit = Find(b[i], a[i], owner);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        private static string Join(IEnumerable<string> items) {
            var list = items.Select(i => $"'{i}'").ToList();
            return list.Count <= 1 ? string.Join("", list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list.Last();
        }
    }

    internal class WardrobeChecksPostprocessor : AssetPostprocessor {
        // anything can change a check (an import setting, a prefab, a texture)
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) => WardrobeChecks.Forget();
    }
}
