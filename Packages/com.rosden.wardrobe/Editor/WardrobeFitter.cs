using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Fit to Avatar: from the garment as the cloth tool exports it (<see cref="WardrobeGarment.source"/>, rigged to the
    /// avatar's skeleton) makes "&lt;Garment&gt;.asset" next to the .rgarm, the mesh the prefab wears. It gets a copy of each
    /// body shape key garments follow (the .ravt's fitting), so the garment changes shape with the body: every vertex
    /// moves with the body surface under it, as Blender's Surface Deform does (the nearby triangles' movement weighted by
    /// distance, its offset from them turned as they turn). Then, at rest and in each shape key, wherever the body (without
    /// the garment's body hides, as the editor shows it) comes closer to the cloth than <see cref="Clearance"/> or through it, the
    /// cloth there is moved out, and the small loose parts on it (buttons, stitching) with it: a light garment's big faces
    /// can't follow every bulge on their own. Each loose part rides on the cloth point under it as one rigid piece, skinned
    /// and shaped as that point is, so buttons don't bend. Weights on bones the base says garments shouldn't move with
    /// go to the bone it names instead, at most 4 per vertex. Making it again overwrites the mesh in place, so the prefab
    /// and everything else that uses it keep working.
    /// </summary>
    public static class WardrobeFitter {
        private const int Build = 12;               // the fit itself: bump when it changes, so older fits show as out of date
        private const float MinMovement = 0.0005f;  // metres; a shape key that moves the garment less isn't copied
        private const float Falloff = 4, Near = 0.002f, Band = 0.01f, Cell = 0.02f;
        private const int MaxReach = 25;            // cells searched around a vertex for the body (50 cm)
        /// <summary>Metres the body is kept below the cloth.</summary>
        public const float Clearance = 0.002f;
        private const float ClearCell = 0.03f, ClearAhead = 0.03f, LayerReach = 0.01f;
        // body deeper than this under the cloth isn't a bulge to clear (fur volume, a hide shape not keeping up): left be
        private const float ClearBehind = 0.012f;
        private const float MaxPush = 0.008f;       // metres a vertex is moved at most, per shape key
        private const float Layer = 0.001f;         // metres kept between cloth layers when an inner one is pushed out (or their gap, if less)
        private const float Anchor = 0.02f;         // metres from the cloth a loose part may be and still ride on it, rigid

        /// <summary>What the last fit did: the shape keys it copied (furthest movement in mm) and the ones it left out, and why.</summary>
        public class Report {
            public readonly List<(string shape, float mm)> added = new List<(string, float)>();
            public readonly List<(string shape, string why)> skipped = new List<(string, string)>();
            public readonly List<string> notes = new List<string>();
        }

        /// <summary>Where the fit goes: "&lt;Garment&gt;.asset" next to the .rgarm.</summary>
        public static string FitPath(WardrobeGarment g) => $"{WardrobeGarments.FolderOf(g)}/{WardrobeAssets.FileName(WardrobeAssets.BaseName(g))}.asset";

        /// <summary>Why the garment can't be fitted yet, or null.</summary>
        public static string Problem(WardrobeGarment g) {
            if (g.source == null) return "Pick the garment as your cloth tool exports it (Source Model).";
            if (SourceSkin(g) == null) return $"{g.source.name} has no skinned mesh: export it rigged to the avatar's skeleton.";
            var avatar = WardrobeAvatars.Get(g.avatarId);
            if (avatar == null) return "It isn't made for an avatar base (.ravt) in this project, so there's no body to fit it to.";
            var model = avatar.Model;
            if (model == null) return $"None of {avatar.DisplayName}'s files (the .ravt's guids) are in this project: import the avatar.";
            var body = model.transform.Find(avatar.body)?.GetComponent<SkinnedMeshRenderer>();
            if (body == null || body.sharedMesh == null) return $"{model.name} has no body renderer at '{avatar.body}' (the .ravt's body).";
            return null;
        }

        /// <summary>
        /// The shape keys a fit copies onto the garment: the base's, and any other body shape key the garment has an export
        /// for (<see cref="WardrobeGarment.sims"/>), without the ones the garment sets to hide the body.
        /// </summary>
        public static List<string> Shapes(WardrobeGarment g) {
            var avatar = WardrobeAvatars.Get(g.avatarId);
            if (avatar == null) return new List<string>();
            var list = avatar.FitShapes.Where(s => !g.bodyHides.Contains(s)).ToList();
            foreach (var sim in g.sims ?? new List<WardrobeSim>())
            foreach (var s in sim?.shapes ?? new List<WardrobeShapeWeight>())
                if (!string.IsNullOrWhiteSpace(s.shape) && !list.Contains(s.shape) && !g.bodyHides.Contains(s.shape)) list.Add(s.shape);
            return list;
        }

        /// <summary>The body's shape keys (of the avatar base the garment is for), or none.</summary>
        public static List<string> BodyShapes(WardrobeGarment g) {
            var avatar = WardrobeAvatars.Get(g.avatarId);
            var mesh = avatar?.Model?.transform.Find(avatar.body)?.GetComponent<SkinnedMeshRenderer>()?.sharedMesh;
            return mesh == null ? new List<string>() : Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToList();
        }

        /// <summary>Whether the garment's mesh is a fit of its source as the source and the avatar are now.</summary>
        public static bool UpToDate(WardrobeGarment g) {
            if (g.mesh == null || Problem(g) != null) return false;
            string path = AssetDatabase.GetAssetPath(g.mesh);
            return path == FitPath(g) && AssetImporter.GetAtPath(path)?.userData == Key(g);
        }

        /// <summary>Fits the garment: writes its .asset, makes it the garment's mesh and puts it on the prefab in place of the source's.</summary>
        public static Report Fit(WardrobeGarment g) {
            string problem = Problem(g);
            if (problem != null) throw new InvalidOperationException(problem);
            var report = new Report();
            var avatar = WardrobeAvatars.Get(g.avatarId);
            var sourceSkin = SourceSkin(g);
            var source = sourceSkin.sharedMesh;
            var instance = Object.Instantiate(avatar.Model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            Mesh fit;
            try {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var body = instance.transform.Find(avatar.body).GetComponent<SkinnedMeshRenderer>();
                fit = Make(g, avatar, instance, body, sourceSkin, report);
            } finally {
                Object.DestroyImmediate(instance);
            }

            string path = FitPath(g);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) {
                EditorUtility.CopySerialized(fit, existing);
                Object.DestroyImmediate(fit);
                fit = existing;
                EditorUtility.SetDirty(fit);
                AssetDatabase.SaveAssetIfDirty(fit);
            } else {
                AssetDatabase.CreateAsset(fit, path);
            }
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = Key(g);
            importer.SaveAndReimport();
            fit = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Refresh(fit);
            WardrobeClearance.ScheduleAll();   // what worn copies cleared was of the old fit

            var old = g.mesh;
            g.mesh = fit;
            EditorUtility.SetDirty(g);
            WardrobeGarments.Changed(g);
            if (Wear(g.prefab, fit, source, old)) report.notes.Add($"{g.prefab.name} now wears it.");
            return report;
        }

        // ---------------------------------------------------------------- the fit

        private static Mesh Make(WardrobeGarment g, WardrobeAvatar avatar, GameObject instance, SkinnedMeshRenderer body,
            SkinnedMeshRenderer sourceSkin, Report report) {
            var source = sourceSkin.sharedMesh;
            var fit = Object.Instantiate(source);
            fit.name = WardrobeAssets.BaseName(g);

            // the garment's weights, with the bones it shouldn't follow folded into the ones the base names
            var boneNames = sourceSkin.bones.Select(b => b != null ? b.name : "").ToArray();
            var weights = MergeBones(source.boneWeights, boneNames, avatar.fitBones, report);
            if (weights.Length == fit.vertexCount) fit.boneWeights = weights;

            // the garment on its own bones: it's exported posed on the avatar's skeleton and its own bones skin it once worn
            // (its armature may be scaled differently from the avatar's, so the avatar's bones with its bindposes won't do)
            var byName = new Dictionary<string, Transform>();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            var garmentBones = new Matrix4x4[boneNames.Length];
            var root = sourceSkin.transform.root;
            int off = 0;
            for (int i = 0; i < boneNames.Length; i++) {
                var own = sourceSkin.bones[i];
                garmentBones[i] = (own != null ? own.localToWorldMatrix : sourceSkin.transform.localToWorldMatrix) * source.bindposes[i];
                if (own != null && byName.TryGetValue(boneNames[i], out var t)
                    && (root.InverseTransformPoint(own.position) - instance.transform.InverseTransformPoint(t.position)).sqrMagnitude > 0.01f * 0.01f) off++;
            }
            if (off > 0) report.notes.Add($"{off} of its bones are more than 1 cm from {avatar.DisplayName}'s: export it in the avatar's rest pose.");
            var garmentSkin = PerVertex(weights, garmentBones, fit.vertexCount, sourceSkin.transform.localToWorldMatrix);
            var clothTriangles = source.triangles;
            var garmentVertices = source.vertices;
            var garment = new Vector3[garmentVertices.Length];
            for (int v = 0; v < garment.Length; v++) garment[v] = garmentSkin[v].MultiplyPoint3x4(garmentVertices[v]);

            // loose parts (buttons, 3D stitching) are rigid: each skinned all over as the cloth vertex it sits on
            var part = LooseParts(garment, clothTriangles);
            var loose = part.Select(k => k >= 0).ToArray();
            var anchors = Anchors(garment, clothTriangles, part);
            if (weights.Length == fit.vertexCount && anchors.Length > 0) {
                for (int v = 0; v < weights.Length; v++)
                    if (part[v] >= 0 && anchors[part[v]].triangle >= 0) weights[v] = weights[anchors[part[v]].vertex];
                fit.boneWeights = weights;
                garmentSkin = PerVertex(weights, garmentBones, fit.vertexCount, sourceSkin.transform.localToWorldMatrix);
                for (int v = 0; v < garment.Length; v++) garment[v] = garmentSkin[v].MultiplyPoint3x4(garmentVertices[v]);
                int rigid = anchors.Count(a => a.triangle >= 0);
                report.notes.Add($"{rigid} loose parts (buttons, stitching) ride on the cloth as rigid pieces.");
            }
            var bodyMesh = body.sharedMesh;
            var bodyBones = body.bones.Select((b, i) => (b != null ? b.localToWorldMatrix : body.transform.localToWorldMatrix) * bodyMesh.bindposes[i]).ToArray();
            var bodySkin = PerVertex(bodyMesh.boneWeights, bodyBones, bodyMesh.vertexCount, body.transform.localToWorldMatrix);

            var bodyVertices = bodyMesh.vertices;
            var rest = new Vector3[bodyVertices.Length];
            for (int v = 0; v < rest.Length; v++) rest[v] = bodySkin[v].MultiplyPoint3x4(bodyVertices[v]);
            // the body's faces the garment's hides collapse (fluff, hair: hidden while it's worn) don't hold the cloth: it was
            // made without them, and they don't move with the body's shape keys
            var triangles = Shown(bodyMesh, g.bodyHides, report);

            // otherwise the body is cleared without the garment's body hides: Ros' Wardrobe puts them on the body when it
            // puts the garment on, but they can be turned off by hand, and the cloth has to look right either way
            var inverse = garmentSkin.Select(m => m.inverse).ToArray();

            // what's layered over what at rest, kept so through every movement: outward is away from the body under each vertex
            var outward = new Vector3[garment.Length];
            {
                var holds = Bind(garment, rest, triangles);
                for (int v = 0; v < garment.Length; v++)
                    foreach (var h in holds[v]) outward[v] += h.offset * h.weight;
            }
            var (layers, behind) = Stack(garment, clothTriangles, loose, outward);
            report.notes.Add($"{layers.Length} layered cloth points and {behind.Length} button points kept in order.");

            // at rest: the cloth off the body where the export has it touching or through
            var restPush = Clear(garment, clothTriangles, loose, rest, triangles, out float restMost, out int restMoved);
            Keep(garment, restPush, clothTriangles, layers);
            Rigid(garment, restPush, clothTriangles, part, anchors, behind);
            if (restMoved > 0) {
                for (int v = 0; v < garment.Length; v++) {
                    garment[v] += restPush[v];
                    garmentVertices[v] += inverse[v].MultiplyVector(restPush[v]);
                }
                fit.vertices = garmentVertices;
                report.notes.Add($"At rest, {restMoved} vertices were moved off the body (up to {restMost * 1000:0.#} mm).");
            }

            var binding = Bind(garment, rest, triangles);
            var restNormals = FaceNormals(rest, triangles);

            // the garment's own shape keys stay
            var existing = new List<(string name, float weight, Vector3[] dv, Vector3[] dn, Vector3[] dt)>();
            for (int s = 0; s < source.blendShapeCount; s++)
            for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++) {
                var (dv, dn, dt) = (new Vector3[source.vertexCount], new Vector3[source.vertexCount], new Vector3[source.vertexCount]);
                source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                existing.Add((source.GetBlendShapeName(s), source.GetBlendShapeFrameWeight(s, f), dv, dn, dt));
            }
            fit.ClearBlendShapes();
            var shapes = Shapes(g);
            foreach (var e in existing.Where(e => !shapes.Contains(e.name))) fit.AddBlendShapeFrame(e.name, e.weight, e.dv, e.dn, e.dt);

            var probe = Object.Instantiate(fit);
            try {
                probe.ClearBlendShapes();
                probe.RecalculateNormals();
                var baseNormals = probe.normals;

                // a shape (the garment's movement, world space) as a frame of the garment's shape key
                void AddFrame(string name, float weight, Vector3[] total) {
                    var dv = new Vector3[garment.Length];
                    for (int v = 0; v < garment.Length; v++) dv[v] = inverse[v].MultiplyVector(total[v]);
                    var shaped = new Vector3[garmentVertices.Length];
                    for (int v = 0; v < shaped.Length; v++) shaped[v] = garmentVertices[v] + dv[v];
                    probe.vertices = shaped;
                    probe.RecalculateNormals();
                    var normals = probe.normals;
                    var dn = new Vector3[normals.Length];
                    for (int v = 0; v < dn.Length; v++) dn[v] = normals[v] - baseNormals[v];
                    fit.AddBlendShapeFrame(name, weight, dv, dn, null);
                }
                float Furthest(Vector3[] total) => total.Length == 0 ? 0 : total.Max(t => t.magnitude);

                // the body with its shape keys at these weights (0..100)
                var bodyDeltas = new Dictionary<string, (Vector3[] delta, float weight)>();
                Vector3[] BodyAt(IEnumerable<WardrobeShapeWeight> at) {
                    var offset = new Vector3[bodyVertices.Length];
                    foreach (var s in at) {
                        if (!bodyDeltas.TryGetValue(s.shape, out var d)) {
                            int index = bodyMesh.GetBlendShapeIndex(s.shape);
                            if (index < 0) continue;
                            int frame = bodyMesh.GetBlendShapeFrameCount(index) - 1;
                            var delta = new Vector3[bodyMesh.vertexCount];
                            bodyMesh.GetBlendShapeFrameVertices(index, frame, delta, null, null);
                            bodyDeltas[s.shape] = d = (delta, bodyMesh.GetBlendShapeFrameWeight(index, frame));
                        }
                        float k = s.weight / d.weight;
                        for (int v = 0; v < offset.Length; v++) offset[v] += d.delta[v] * k;
                    }
                    var moved = new Vector3[bodyVertices.Length];
                    for (int v = 0; v < moved.Length; v++) moved[v] = bodySkin[v].MultiplyPoint3x4(bodyVertices[v] + offset[v]);
                    return moved;
                }

                // the garment's movement to a sim (the cloth tool's shape), off the body in that shape where it's through
                Vector3[] SimShape(WardrobeSim sim, Vector3[] simWorld, string label) {
                    var moved = BodyAt(sim.shapes);
                    var push = Clear(simWorld, clothTriangles, loose, moved, triangles, out float most, out int pushed);
                    if (pushed > 0) report.notes.Add($"{label}: {pushed} vertices of the sim moved off the body (up to {most * 1000:0.#} mm).");
                    var total = new Vector3[garment.Length];
                    for (int v = 0; v < garment.Length; v++) total[v] = simWorld[v] - garment[v] + push[v];
                    // the loose parts (stitching, buttons) ride on the sim's cloth as rigid pieces
                    Rigid(garment, total, clothTriangles, part, anchors, behind);
                    return total;
                }

                // the sims: the cloth tool's exports with the body in other shapes
                var sims = new List<(WardrobeSim sim, Vector3[] world)>();
                foreach (var sim in g.sims ?? new List<WardrobeSim>()) {
                    if (sim?.source == null || sim.shapes.Count == 0) continue;
                    string label = $"sim {WardrobeSim.ComboName(sim.shapes)} ({sim.source.name})";
                    var missing = sim.shapes.Where(s => !shapes.Contains(s.shape) || bodyMesh.GetBlendShapeIndex(s.shape) < 0).Select(s => s.shape).ToList();
                    if (missing.Count > 0) {
                        report.skipped.Add((label, $"{string.Join(", ", missing)} isn't a body shape key it follows (on the body, not one of its body hides)"));
                        continue;
                    }
                    var world = SimWorld(sim, source, garment, loose, label, report);
                    if (world != null) sims.Add((sim, world));
                }

                // each shape key's frames (weight, world movement): from its one-key sims, or by following the body
                var frames = new Dictionary<string, List<(float weight, Vector3[] total)>>();
                foreach (string shape in shapes) {
                    var own = sims.Where(s => s.sim.shapes.Count == 1 && s.sim.shapes[0].shape == shape)
                        .OrderBy(s => s.sim.shapes[0].weight).ToList();
                    if (own.Count == 0) continue;
                    var list = new List<(float, Vector3[])>();
                    foreach (var (sim, world) in own) {
                        float w = sim.shapes[0].weight;
                        if (list.Count > 0 && Mathf.Approximately(list[list.Count - 1].Item1, w)) {
                            report.skipped.Add(($"sim {shape}@{w:0.##} ({sim.source.name})", "another sim has the same weight"));
                            continue;
                        }
                        list.Add((w, SimShape(sim, world, $"{shape}@{w:0.##}")));
                    }
                    // the shape key ends at 100: past the last sim it goes on as from 0 to it
                    var (lastWeight, lastTotal) = list[list.Count - 1];
                    if (lastWeight < 100) {
                        list.Add((100, lastTotal.Select(t => t * (100 / lastWeight)).ToArray()));
                        report.notes.Add($"{shape}: its last sim is at {lastWeight:0.##}; past it the cloth goes on in the same direction to 100.");
                    }
                    frames[shape] = list;
                    foreach (var (w, total) in list) AddFrame(shape, w, total);
                    report.added.Add(($"{shape} (from {own.Count} sim{(own.Count > 1 ? "s" : "")})", Furthest(list[list.Count - 1].Item2) * 1000));
                }

                foreach (string shape in shapes) {
                    if (frames.ContainsKey(shape)) continue;
                    int index = bodyMesh.GetBlendShapeIndex(shape);
                    if (index < 0) {
                        report.skipped.Add((shape, "not on the body"));
                        continue;
                    }
                    var moved = BodyAt(new[] { new WardrobeShapeWeight { shape = shape, weight = 100 } });
                    var movedNormals = FaceNormals(moved, triangles);

                    var world = new Vector3[garment.Length];
                    var shapedWorld = new Vector3[garment.Length];
                    for (int v = 0; v < garment.Length; v++) {
                        world[v] = Follow(binding[v], garment[v], moved, triangles, restNormals, movedNormals);
                        shapedWorld[v] = garment[v] + world[v];
                    }
                    var push = Clear(shapedWorld, clothTriangles, loose, moved, triangles, out float most, out int pushed);
                    if (pushed > 0) report.notes.Add($"{shape}: {pushed} vertices moved off the body (up to {most * 1000:0.#} mm).");
                    var total = new Vector3[garment.Length];
                    for (int v = 0; v < garment.Length; v++) total[v] = world[v] + push[v];
                    int kept = Keep(garment, total, clothTriangles, layers);
                    if (kept > 0) report.notes.Add($"{shape}: {kept} layer overlaps moved apart.");
                    Rigid(garment, total, clothTriangles, part, anchors, behind);
                    float furthest = Furthest(total);
                    frames[shape] = new List<(float, Vector3[])> { (100, total) };   // what correctives build on, even if too small to keep
                    if (furthest < MinMovement) {
                        report.skipped.Add((shape, $"moves it {furthest * 1000:0.##} mm"));
                        continue;
                    }
                    AddFrame(shape, 100, total);
                    report.added.Add((shape, furthest * 1000));
                }

                // correctives: what the one-key shapes at a multi-key sim's weights miss of it, on a shape key set to the
                // product of the keys' weights over the sim's (Ros' Wardrobe sets it in the editor: WardrobeBody.Follow)
                Vector3[] At(string shape, float weight) {
                    var result = new Vector3[garment.Length];
                    if (!frames.TryGetValue(shape, out var list) || weight <= 0) return result;
                    float w0 = 0;
                    Vector3[] t0 = result;
                    foreach (var (w1, t1) in list) {
                        if (weight <= w1) {
                            float k = (weight - w0) / (w1 - w0);
                            for (int v = 0; v < result.Length; v++) result[v] = Vector3.LerpUnclamped(t0[v], t1[v], k);
                            return result;
                        }
                        (w0, t0) = (w1, t1);
                    }
                    return (Vector3[])t0.Clone();
                }
                var combos = new HashSet<string>();
                foreach (var (sim, world) in sims.Where(s => s.sim.shapes.Count > 1)) {
                    string name = WardrobeSim.ComboName(sim.shapes);
                    if (!combos.Add(name)) {
                        report.skipped.Add(($"sim {name} ({sim.source.name})", "another sim has the same shape keys and weights"));
                        continue;
                    }
                    var total = SimShape(sim, world, name);
                    foreach (var s in sim.shapes) {
                        var single = At(s.shape, s.weight);
                        for (int v = 0; v < total.Length; v++) total[v] -= single[v];
                    }
                    AddFrame(name, 100, total);
                    report.added.Add(($"{name} (corrective)", Furthest(total) * 1000));
                }
            } finally {
                Object.DestroyImmediate(probe);
            }
            fit.RecalculateBounds();
            return fit;
        }

        // ---------------------------------------------------------------- for the clearance pass (WardrobeClearance)

        // The clearance pass fixes what's left, a little further than a fit pushes out, in more passes. It looks no deeper
        // than a fit (ClearBehind): body further under the cloth is fur volume or a tight fold (a collar round the neck),
        // and pushing the cloth out of that balloons it.
        private const float PassBehind = ClearBehind, PassPush = 0.012f;
        private const int PassPasses = 4;

        /// <summary>
        /// How far each of a worn garment's vertices (world space) moves so the body (world space, the triangles given) stays
        /// <see cref="Clearance"/> under the cloth: the cloth pushed out where the body is through or too close, its layers
        /// kept in order and its loose parts (buttons, stitching) carried along as rigid pieces.
        /// </summary>
        internal static Vector3[] ClearancePush(Vector3[] garment, int[] clothTris, Vector3[] body, int[] bodyTris, out float most, out int moved) {
            var part = LooseParts(garment, clothTris);
            var loose = part.Select(k => k >= 0).ToArray();
            var push = Clear(garment, clothTris, loose, body, bodyTris, out most, out moved, PassBehind, PassPush, PassPasses);
            if (moved == 0) return push;
            // outward (away from the body) at each cloth vertex; a loose part's is the cloth's under it
            var clothOnly = Enumerable.Range(0, garment.Length).Where(v => !loose[v]).ToArray();
            var holds = Bind(clothOnly.Select(v => garment[v]).ToArray(), body, bodyTris);
            var outward = new Vector3[garment.Length];
            for (int i = 0; i < clothOnly.Length; i++)
                foreach (var h in holds[i]) outward[clothOnly[i]] += h.offset * h.weight;
            var anchors = Anchors(garment, clothTris, part);
            for (int v = 0; v < garment.Length; v++)
                if (part[v] >= 0 && anchors[part[v]].triangle >= 0) outward[v] = outward[anchors[part[v]].vertex];
            var (layers, behind) = Stack(garment, clothTris, loose, outward);
            Keep(garment, push, clothTris, layers);
            Rigid(garment, push, clothTris, part, anchors, behind);
            most = 0;
            moved = 0;
            foreach (var p in push) {
                float m = p.magnitude;
                if (m <= 1e-6f) continue;
                moved++;
                most = Mathf.Max(most, m);
            }
            return push;
        }

        /// <summary>The body's triangles but the ones its shape keys collapse now: from below this share of their rest area.</summary>
        internal static int[] ShownTriangles(Vector3[] rest, Vector3[] now, int[] triangles) {
            var shown = new List<int>(triangles.Length);
            for (int t = 0; t < triangles.Length; t += 3) {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float before = Vector3.Cross(rest[b] - rest[a], rest[c] - rest[a]).magnitude;
                float after = Vector3.Cross(now[b] - now[a], now[c] - now[a]).magnitude;
                if (before > 0 && after < before * HiddenArea) continue;
                shown.Add(a);
                shown.Add(b);
                shown.Add(c);
            }
            return shown.ToArray();
        }

        /// <summary>
        /// A skinned renderer's mesh as it's shown now, in world space: its shape keys at the weights given (by name) and
        /// skinned by its bones as they are. <paramref name="skinning"/> is each vertex's skin matrix (mesh to world).
        /// </summary>
        internal static Vector3[] Posed(SkinnedMeshRenderer skin, Mesh mesh, Func<string, float> weightOf, out Matrix4x4[] skinning) {
            int n = mesh.vertexCount;
            var bones = skin.bones;
            var bindposes = mesh.bindposes;
            var matrices = new Matrix4x4[bindposes.Length];
            for (int i = 0; i < matrices.Length; i++)
                matrices[i] = (i < bones.Length && bones[i] != null ? bones[i].localToWorldMatrix : skin.transform.localToWorldMatrix) * bindposes[i];
            skinning = PerVertex(mesh.boneWeights, matrices, n, skin.transform.localToWorldMatrix);

            var vertices = mesh.vertices;
            var a = new Vector3[n];
            var b = new Vector3[n];
            for (int s = 0; s < mesh.blendShapeCount; s++) {
                float w = weightOf(mesh.GetBlendShapeName(s));
                int frames = mesh.GetBlendShapeFrameCount(s);
                if (Mathf.Abs(w) < 0.0001f || frames == 0) continue;
                // as Unity blends a shape key's frames: from nothing to the first, then between the two around the weight
                int j = 0;
                while (j < frames - 1 && mesh.GetBlendShapeFrameWeight(s, j) < w) j++;
                float w1 = mesh.GetBlendShapeFrameWeight(s, j);
                mesh.GetBlendShapeFrameVertices(s, j, b, null, null);
                if (j == 0) {
                    float k = w1 != 0 ? w / w1 : 0;
                    for (int v = 0; v < n; v++) vertices[v] += b[v] * k;
                } else {
                    float w0 = mesh.GetBlendShapeFrameWeight(s, j - 1);
                    mesh.GetBlendShapeFrameVertices(s, j - 1, a, null, null);
                    float k = w1 != w0 ? (w - w0) / (w1 - w0) : 1;
                    for (int v = 0; v < n; v++) vertices[v] += Vector3.LerpUnclamped(a[v], b[v], k);
                }
            }
            var world = new Vector3[n];
            for (int v = 0; v < n; v++) world[v] = skinning[v].MultiplyPoint3x4(vertices[v]);
            return world;
        }

        // A mesh overwritten in place: renderers in open scenes that already show it keep their GPU buffers for the old data
        // (Unity: "data for mesh … does not match the expected mesh data size and vertex stride") and stop drawing. Giving
        // them the mesh again rebuilds those; their shape key weights stay.
        internal static void Refresh(Mesh mesh) {
            foreach (var skin in Object.FindObjectsOfType<SkinnedMeshRenderer>(true)) {
                if (skin.sharedMesh != mesh) continue;
                var weights = new float[mesh.blendShapeCount];
                for (int i = 0; i < weights.Length; i++) weights[i] = skin.GetBlendShapeWeight(i);
                skin.sharedMesh = null;
                skin.sharedMesh = mesh;
                for (int i = 0; i < weights.Length; i++) skin.SetBlendShapeWeight(i, weights[i]);
            }
        }

        // A hide collapses what it hides: a face it shrinks below this share of its area is hidden. (Shrink keys, that pull
        // the body in under the cloth, keep far more.)
        private const float HiddenArea = 0.05f;

        // The body's triangles but the ones any of the hides collapses.
        private static int[] Shown(Mesh body, IEnumerable<string> hides, Report report) {
            var triangles = body.triangles;
            var vertices = body.vertices;
            var hidden = new bool[triangles.Length / 3];
            foreach (string hide in hides ?? Enumerable.Empty<string>()) {
                int index = body.GetBlendShapeIndex(hide);
                if (index < 0) continue;
                var delta = new Vector3[body.vertexCount];
                body.GetBlendShapeFrameVertices(index, body.GetBlendShapeFrameCount(index) - 1, delta, null, null);
                int count = 0;
                for (int t = 0; t < hidden.Length; t++) {
                    int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                    float before = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude;
                    if (before <= 0 || hidden[t]) continue;
                    float after = Vector3.Cross(vertices[b] + delta[b] - vertices[a] - delta[a], vertices[c] + delta[c] - vertices[a] - delta[a]).magnitude;
                    if (after < before * HiddenArea) {
                        hidden[t] = true;
                        count++;
                    }
                }
                if (count > 0) report.notes.Add($"{hide} hides {count} of the body's faces: the cloth doesn't follow them.");
            }
            var shown = new List<int>(triangles.Length);
            for (int t = 0; t < hidden.Length; t++)
                if (!hidden[t]) shown.AddRange(new[] { triangles[t * 3], triangles[t * 3 + 1], triangles[t * 3 + 2] });
            return shown.ToArray();
        }

        // Each vertex's skinning matrix: its bones' matrices, weighted (vertices without weights: the renderer's).
        private static Matrix4x4[] PerVertex(BoneWeight[] weights, Matrix4x4[] bones, int count, Matrix4x4 fallback) {
            var result = new Matrix4x4[count];
            for (int v = 0; v < count; v++) {
                if (v >= weights.Length || bones.Length == 0) {
                    result[v] = fallback;
                    continue;
                }
                var w = weights[v];
                var m = new Matrix4x4();
                Add(ref m, bones, w.boneIndex0, w.weight0);
                Add(ref m, bones, w.boneIndex1, w.weight1);
                Add(ref m, bones, w.boneIndex2, w.weight2);
                Add(ref m, bones, w.boneIndex3, w.weight3);
                result[v] = m.m33 > 0.0001f ? m : fallback;
            }
            return result;
        }

        private static void Add(ref Matrix4x4 m, Matrix4x4[] bones, int bone, float weight) {
            if (weight <= 0 || bone < 0 || bone >= bones.Length) return;
            for (int i = 0; i < 16; i++) m[i] += bones[bone][i] * weight;
        }

        // Weights on bones whose names start with one of the prefixes go to the bone it names; then the 4 biggest, normalised.
        private static BoneWeight[] MergeBones(BoneWeight[] weights, string[] bones, List<(string prefix, string bone)> merges, Report report) {
            var into = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++) {
                into[i] = i;
                foreach (var (prefix, bone) in merges) {
                    if (!bones[i].StartsWith(prefix, StringComparison.Ordinal) || bones[i] == bone) continue;
                    int target = Array.IndexOf(bones, bone);
                    if (target < 0) {
                        string note = $"Its {prefix}… bones stay: it has no {bone} bone to give their weight to.";
                        if (!report.notes.Contains(note)) report.notes.Add(note);
                    } else {
                        into[i] = target;
                    }
                    break;
                }
            }
            var result = new BoneWeight[weights.Length];
            var sum = new Dictionary<int, float>();
            for (int v = 0; v < weights.Length; v++) {
                var w = weights[v];
                sum.Clear();
                void Put(int bone, float weight) {
                    if (weight <= 0 || bone < 0 || bone >= into.Length) return;
                    sum.TryGetValue(into[bone], out float had);
                    sum[into[bone]] = had + weight;
                }
                Put(w.boneIndex0, w.weight0);
                Put(w.boneIndex1, w.weight1);
                Put(w.boneIndex2, w.weight2);
                Put(w.boneIndex3, w.weight3);
                var top = sum.OrderByDescending(x => x.Value).Take(4).ToList();
                float total = top.Sum(x => x.Value);
                if (total <= 0) {
                    result[v] = w;
                    continue;
                }
                var r = new BoneWeight();
                if (top.Count > 0) (r.boneIndex0, r.weight0) = (top[0].Key, top[0].Value / total);
                if (top.Count > 1) (r.boneIndex1, r.weight1) = (top[1].Key, top[1].Value / total);
                if (top.Count > 2) (r.boneIndex2, r.weight2) = (top[2].Key, top[2].Value / total);
                if (top.Count > 3) (r.boneIndex3, r.weight3) = (top[3].Key, top[3].Value / total);
                result[v] = r;
            }
            return result;
        }

        // ---------------------------------------------------------------- following the body surface

        // A body triangle a vertex follows: where on it the vertex's closest point is, the vertex's offset from that point, and how much it counts.
        private struct Hold {
            public int triangle;
            public Vector3 barycentric, offset;
            public float weight;
        }

        // The vertex's movement when the body moves from rest to moved.
        private static Vector3 Follow(Hold[] holds, Vector3 p, Vector3[] moved, int[] triangles, Vector3[] restNormals, Vector3[] movedNormals) {
            var sum = Vector3.zero;
            foreach (var h in holds) {
                int t = h.triangle * 3;
                var point = moved[triangles[t]] * h.barycentric.x + moved[triangles[t + 1]] * h.barycentric.y + moved[triangles[t + 2]] * h.barycentric.z;
                var turn = Quaternion.FromToRotation(restNormals[h.triangle], movedNormals[h.triangle]);
                sum += (point + turn * h.offset - p) * h.weight;
            }
            return sum;
        }

        // For each garment vertex, the body triangles within a band of its closest one, weighted by distance.
        private static Hold[][] Bind(Vector3[] garment, Vector3[] body, int[] triangles) {
            var grid = new Dictionary<Vector3Int, List<int>>();
            int count = triangles.Length / 3;
            for (int t = 0; t < count; t++) {
                Vector3 a = body[triangles[t * 3]], b = body[triangles[t * 3 + 1]], c = body[triangles[t * 3 + 2]];
                var min = CellOf(Vector3.Min(a, Vector3.Min(b, c)));
                var max = CellOf(Vector3.Max(a, Vector3.Max(b, c)));
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++) {
                    var key = new Vector3Int(x, y, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(t);
                }
            }

            var holds = new Hold[garment.Length][];
            var seen = new HashSet<int>();
            var found = new List<(int t, float d, Vector3 bary, Vector3 point)>();
            for (int v = 0; v < garment.Length; v++) {
                var p = garment[v];
                var cell = CellOf(p);
                // the closest triangle: grow the searched cube until nothing outside it can be closer
                float best = float.MaxValue;
                seen.Clear();
                for (int reach = 0; reach <= MaxReach && best > (reach - 1) * Cell; reach++)
                    foreach (int t in Shell(grid, cell, reach))
                        if (seen.Add(t)) best = Mathf.Min(best, Closest(p, body, triangles, t, out _, out _));
                if (best == float.MaxValue) {
                    holds[v] = Array.Empty<Hold>();
                    continue;
                }
                // every triangle within the band, weighted by distance
                float limit = best + Band + best * 0.5f;
                int around = Mathf.CeilToInt(limit / Cell);
                found.Clear();
                seen.Clear();
                for (int reach = 0; reach <= around; reach++)
                    foreach (int t in Shell(grid, cell, reach)) {
                        if (!seen.Add(t)) continue;
                        float d = Closest(p, body, triangles, t, out var bary, out var point);
                        if (d <= limit) found.Add((t, d, bary, point));
                    }
                var hold = found.Select(f => new Hold {
                    triangle = f.t, barycentric = f.bary, offset = p - f.point, weight = 1 / Mathf.Pow(f.d + Near, Falloff),
                }).ToArray();
                float total = hold.Sum(h => h.weight);
                for (int i = 0; i < hold.Length; i++) hold[i].weight /= total;
                holds[v] = hold;
            }
            return holds;
        }

        private static Vector3Int CellOf(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

        // The triangles in the cells exactly reach cells away from the centre (a cube's surface).
        private static IEnumerable<int> Shell(Dictionary<Vector3Int, List<int>> grid, Vector3Int centre, int reach) {
            for (int x = -reach; x <= reach; x++)
            for (int y = -reach; y <= reach; y++)
            for (int z = -reach; z <= reach; z++) {
                if (Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != reach) continue;
                if (!grid.TryGetValue(new Vector3Int(centre.x + x, centre.y + y, centre.z + z), out var list)) continue;
                foreach (int t in list) yield return t;
            }
        }

        // ---------------------------------------------------------------- clearance

        // Which vertices are on small loose parts (buttons, 3D stitching) rather than the cloth: parts under 2% of the
        // garment's area. They don't take part in the clearance test; they're carried along with the cloth under them.
        // Returns each vertex's loose part (0, 1, ...), or -1 for the cloth.
        private static int[] LooseParts(Vector3[] cloth, int[] tris) {
            var parent = Enumerable.Range(0, cloth.Length).ToArray();
            int Find(int x) {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            // vertices split at a seam (UVs, normals) are one point of the cloth
            var weld = Welds(cloth);
            for (int v = 0; v < cloth.Length; v++) parent[Find(v)] = Find(weld[v]);
            for (int t = 0; t < tris.Length; t += 3) {
                int a = Find(tris[t]);
                parent[Find(tris[t + 1])] = a;
                parent[Find(tris[t + 2])] = a;
            }
            var area = new Dictionary<int, float>();
            float total = 0;
            for (int t = 0; t < tris.Length; t += 3) {
                float A = Vector3.Cross(cloth[tris[t + 1]] - cloth[tris[t]], cloth[tris[t + 2]] - cloth[tris[t]]).magnitude * 0.5f;
                int r = Find(tris[t]);
                area.TryGetValue(r, out float had);
                area[r] = had + A;
                total += A;
            }
            var part = new int[cloth.Length];
            var index = new Dictionary<int, int>();
            for (int v = 0; v < cloth.Length; v++) {
                int r = Find(v);
                if (area.TryGetValue(r, out float a) && a >= total * 0.02f) {
                    part[v] = -1;
                    continue;
                }
                if (!index.TryGetValue(r, out part[v])) index[r] = part[v] = index.Count;
            }
            return part;
        }

        // Where a loose part sits on the cloth: the cloth triangle closest to its middle, the point on it, and the
        // triangle's vertex nearest that point (whose weights the part takes). Triangle -1: nothing within Anchor.
        private struct Seat {
            public int triangle, vertex;
            public Vector3 barycentric;
        }

        private static Seat[] Anchors(Vector3[] cloth, int[] tris, int[] part) {
            int parts = part.Length == 0 ? 0 : part.Max() + 1;
            var middle = new Vector3[parts];
            var count = new int[parts];
            for (int v = 0; v < cloth.Length; v++)
                if (part[v] >= 0) {
                    middle[part[v]] += cloth[v];
                    count[part[v]]++;
                }
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int t = 0; t < tris.Length; t += 3) {
                if (part[tris[t]] >= 0) continue;
                Vector3 a = cloth[tris[t]], b = cloth[tris[t + 1]], c = cloth[tris[t + 2]];
                var min = ClearCellOf(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * Anchor);
                var max = ClearCellOf(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * Anchor);
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++) {
                    var key = new Vector3Int(x, y, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(t);
                }
            }
            var seats = new Seat[parts];
            for (int k = 0; k < parts; k++) {
                seats[k].triangle = -1;
                if (count[k] == 0) continue;
                var m = middle[k] / count[k];
                if (!grid.TryGetValue(ClearCellOf(m), out var list)) continue;
                float best = Anchor;
                foreach (int t in list) {
                    float d = Closest(m, cloth, tris, t / 3, out var bary, out _);
                    if (d >= best) continue;
                    best = d;
                    int corner = bary.x >= bary.y && bary.x >= bary.z ? 0 : bary.y >= bary.z ? 1 : 2;
                    seats[k] = new Seat { triangle = t, vertex = tris[t + corner], barycentric = bary };
                }
            }
            return seats;
        }

        // The loose parts' movement: each moves as one rigid piece with the cloth point it sits on, turning as that
        // triangle turns, instead of every vertex on its own (which bends a button). move holds the cloth's movement.
        private static void Rigid(Vector3[] p, Vector3[] move, int[] tris, int[] part, Seat[] seats, Pair[] behind) {
            if (seats.Length == 0) return;
            var at = new Vector3[seats.Length];
            var shift = new Vector3[seats.Length];
            var turn = new Quaternion[seats.Length];
            for (int k = 0; k < seats.Length; k++) {
                int t = seats[k].triangle;
                if (t < 0) continue;
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var bc = seats[k].barycentric;
                at[k] = p[a] * bc.x + p[b] * bc.y + p[c] * bc.z;
                Vector3 a2 = p[a] + move[a], b2 = p[b] + move[b], c2 = p[c] + move[c];
                shift[k] = a2 * bc.x + b2 * bc.y + c2 * bc.z - at[k];
                turn[k] = Frame(a2, b2, c2) * Quaternion.Inverse(Frame(p[a], p[b], p[c]));
            }
            for (int v = 0; v < p.Length; v++) {
                int k = part[v];
                if (k < 0 || seats[k].triangle < 0) continue;
                var offset = p[v] - at[k];
                move[v] = shift[k] + turn[k] * offset - offset;
            }
            // then lifted, as one piece, by however much the cloth behind it came closer than at rest, so the cloth
            // doesn't cut into it
            var rise = new float[seats.Length];
            var up = new Vector3[seats.Length];
            foreach (var b in behind) {
                int k = part[b.vertex];
                if (k < 0 || seats[k].triangle < 0) continue;
                float h = b.Height(p, move, tris, out var n);
                float deficit = b.rest - h;
                if (deficit > rise[k]) (rise[k], up[k]) = (deficit, n);
            }
            for (int v = 0; v < p.Length; v++) {
                int k = part[v];
                if (k >= 0 && rise[k] > 0) move[v] += up[k] * rise[k];
            }
        }

        // A vertex and the cloth triangle just over or under it at rest (along the vertex's outward normal): pairs whose
        // order the fit keeps, so no layer comes through another (a placket's underlap through it, a collar's stand
        // through the collar) and the cloth doesn't come through a button.
        private struct Pair {
            public int vertex, triangle;
            public Vector3 barycentric;
            public float sign;    // turns the triangle's normal outward
            public float rest;    // the vertex's height over the triangle at rest, along that normal (negative: under it)
            public bool under;    // the vertex is under the triangle

            // The vertex's height over the triangle now, and the triangle's outward normal.
            public float Height(Vector3[] p, Vector3[] move, int[] tris, out Vector3 n) {
                int a = tris[triangle], b = tris[triangle + 1], c = tris[triangle + 2];
                Vector3 pa = p[a] + move[a], pb = p[b] + move[b], pc = p[c] + move[c];
                n = Vector3.Cross(pb - pa, pc - pa).normalized * sign;
                var at = pa * barycentric.x + pb * barycentric.y + pc * barycentric.z;
                return Vector3.Dot(p[vertex] + move[vertex] - at, n);
            }
        }

        // The pairs at rest. outward: each vertex's direction away from the body. Cloth vertices pair with the nearest
        // cloth triangle over and under them (not ones around them); loose vertices (buttons) with the cloth behind them.
        private static (Pair[] cloth, Pair[] behind) Stack(Vector3[] p, int[] tris, bool[] loose, Vector3[] outward) {
            int n = p.Length;
            var still = new Vector3[n];   // no movement: for the heights at rest
            var weld = Welds(p);
            var neighbours = new HashSet<int>[n];
            for (int v = 0; v < n; v++) neighbours[weld[v]] ??= new HashSet<int>();
            var normal = new Vector3[n];
            for (int t = 0; t < tris.Length; t += 3) {
                if (loose[tris[t]]) continue;
                var f = Vector3.Cross(p[tris[t + 1]] - p[tris[t]], p[tris[t + 2]] - p[tris[t]]);
                for (int k = 0; k < 3; k++) {
                    int a = weld[tris[t + k]], b = weld[tris[t + (k + 1) % 3]];
                    normal[a] += f;
                    if (a == b) continue;
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }
            }
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int t = 0; t < tris.Length; t += 3) {
                if (loose[tris[t]]) continue;
                Vector3 a = p[tris[t]], b = p[tris[t + 1]], c = p[tris[t + 2]];
                var min = ClearCellOf(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * LayerReach);
                var max = ClearCellOf(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * LayerReach);
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++) {
                    var key = new Vector3Int(x, y, z);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(t);
                }
            }
            bool Near(int i, int t) {
                for (int k = 0; k < 3; k++) {
                    int j = weld[tris[t + k]];
                    if (j == i || neighbours[i] != null && neighbours[i].Contains(j)) return true;
                }
                return false;
            }
            Pair? Cast(int v, Vector3 d, bool under, bool skipNear) {
                if (!grid.TryGetValue(ClearCellOf(p[v]), out var list)) return null;
                float best = LayerReach;
                Pair? found = null;
                foreach (int t in list) {
                    if (skipNear && Near(weld[v], t)) continue;
                    Vector3 a = p[tris[t]], b = p[tris[t + 1]], c = p[tris[t + 2]];
                    var fn = Vector3.Cross(b - a, c - a).normalized;
                    float facing = Vector3.Dot(fn, d);
                    if (Mathf.Abs(facing) < 0.5f) continue;
                    if (!Ray(p[v], d, a, b, c, out float hit) || hit <= 1e-5f || hit >= best) continue;
                    best = hit;
                    var at = p[v] + d * hit;
                    Closest(at, p, tris, t / 3, out var bary, out _);
                    float sign = Mathf.Sign(facing);   // the normal along the ray; callers turn it outward
                    var pr = new Pair { vertex = v, triangle = t, barycentric = bary, sign = sign, under = under };
                    found = pr;
                }
                if (found == null) return null;
                var f2 = found.Value;
                f2.rest = f2.Height(p, still, tris, out _);
                return f2;
            }
            var cloth = new List<Pair>();
            var behind = new List<Pair>();
            for (int v = 0; v < n; v++) {
                if (loose[v]) continue;
                if (weld[v] != v || normal[v] == Vector3.zero) continue;
                var d = normal[v].normalized;
                if (Vector3.Dot(d, outward[v]) < 0) d = -d;
                var over = Cast(v, d, true, true);       // the layer over it: d is outward
                if (over != null) cloth.Add(over.Value);
                var below = Cast(v, -d, false, true);    // the layer under it: outward is -(-d) = d
                if (below != null) {
                    var b = below.Value;
                    b.sign = -b.sign;                     // the ray went along -d: flip so the normal is outward
                    b.rest = b.Height(p, still, tris, out _);
                    cloth.Add(b);
                }
            }
            for (int v = 0; v < n; v++) {
                if (!loose[v]) continue;
                var d = outward[v];
                if (d == Vector3.zero) continue;
                var b = Cast(v, -d.normalized, false, false);
                if (b == null) continue;
                var pb = b.Value;
                pb.sign = -pb.sign;
                pb.rest = pb.Height(p, still, tris, out _);
                behind.Add(pb);
            }
            return (cloth.ToArray(), behind.ToArray());
        }

        // Keeps the cloth pairs in order: where a vertex has come within Layer (or its rest gap, if less) of the layer
        // over it, or gone through it, the outer one is moved out. move holds the cloth's movement and is changed.
        private static int Keep(Vector3[] p, Vector3[] move, int[] tris, Pair[] pairs) {
            var weld = Welds(p);
            int fixedCount = 0;
            for (int round = 0; round < 10; round++) {
                bool any = false;
                foreach (var pr in pairs) {
                    float h = pr.Height(p, move, tris, out var nrm);
                    float keep = Mathf.Min(Mathf.Abs(pr.rest), Layer);
                    if (pr.under) {
                        float deficit = h + keep;   // wants h <= -keep
                        if (deficit <= 1e-6f) continue;
                        for (int k = 0; k < 3; k++) move[weld[tris[pr.triangle + k]]] += nrm * deficit;
                    } else {
                        float deficit = keep - h;   // wants h >= keep
                        if (deficit <= 1e-6f) continue;
                        move[weld[pr.vertex]] += nrm * deficit;
                    }
                    any = true;
                    fixedCount++;
                }
                for (int v = 0; v < p.Length; v++)
                    if (weld[v] != v) move[v] = move[weld[v]];
                if (!any) break;
            }
            return fixedCount;
        }

        // A triangle's orientation: facing along its normal, its first edge up.
        private static Quaternion Frame(Vector3 a, Vector3 b, Vector3 c) {
            var n = Vector3.Cross(b - a, c - a);
            var up = b - a;
            if (n.sqrMagnitude < 1e-20f || up.sqrMagnitude < 1e-20f) return Quaternion.identity;
            return Quaternion.LookRotation(n, up);
        }

        // How far each garment vertex has to move so the body stays Clearance below the cloth: a ray from every body
        // vertex along its normal finds the cloth over it; a face too close, or under the skin, is moved out along that
        // normal (twice over, as moving one face changes what's over its neighbours). Loose parts are left at zero: see Rigid.
        private static Vector3[] Clear(Vector3[] cloth, int[] tris, bool[] loose, Vector3[] body, int[] bodyTris, out float most, out int moved,
            float behind = ClearBehind, float maxPush = MaxPush, int passes = 2) {
            int n = cloth.Length;
            var weld = Welds(cloth);
            // neighbours through the cloth's edges, seams welded: for softening the push into the faces around it
            var neighbours = new HashSet<int>[n];
            for (int v = 0; v < n; v++) neighbours[weld[v]] ??= new HashSet<int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int k = 0; k < 3; k++) {
                    int a = weld[tris[t + k]], b = weld[tris[t + (k + 1) % 3]];
                    if (a == b) continue;
                    neighbours[a].Add(b);
                    neighbours[b].Add(a);
                }
            var total = new Vector3[n];
            var current = (Vector3[])cloth.Clone();
            var normals = new Vector3[body.Length];
            for (int t = 0; t < bodyTris.Length; t += 3) {
                int a = bodyTris[t], b = bodyTris[t + 1], c = bodyTris[t + 2];
                var f = Vector3.Cross(body[b] - body[a], body[c] - body[a]);
                normals[a] += f; normals[b] += f; normals[c] += f;
            }
            for (int v = 0; v < normals.Length; v++) normals[v] = normals[v].normalized;

            var clothTris = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
                if (!loose[tris[t]]) clothTris.Add(t);
            for (int pass = 0; pass < passes; pass++) {
                var grid = new Dictionary<Vector3Int, List<int>>();
                foreach (int t in clothTris) {
                    Vector3 a = current[tris[t]], b = current[tris[t + 1]], c = current[tris[t + 2]];
                    var min = ClearCellOf(Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * ClearAhead);
                    var max = ClearCellOf(Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * ClearAhead);
                    for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++) {
                        var key = new Vector3Int(x, y, z);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(t);
                    }
                }
                var need = new float[n];
                var dir = new Vector3[n];
                for (int v = 0; v < body.Length; v++) {
                    var o = body[v];
                    var d = normals[v];
                    if (d == Vector3.zero || !grid.TryGetValue(ClearCellOf(o), out var list)) continue;
                    float bestT = float.MaxValue;
                    int bestTri = -1;
                    foreach (int t in list) {
                        if (!Ray(o, d, current[tris[t]], current[tris[t + 1]], current[tris[t + 2]], out float hit)) continue;
                        if (hit < -behind || hit > ClearAhead || Mathf.Abs(hit) >= Mathf.Abs(bestT)) continue;
                        bestT = hit;
                        bestTri = t;
                    }
                    if (bestTri < 0 || bestT >= Clearance) continue;
                    float want = Clearance - bestT;
                    for (int k = 0; k < 3; k++) {
                        int i = weld[tris[bestTri + k]];
                        if (want > need[i]) (need[i], dir[i]) = (want, d);
                    }
                }
                // soften: a vertex takes at least half its neighbours' average push, twice over, so a pushed face
                // eases into the cloth around it instead of standing out as a step
                for (int round = 0; round < 2; round++) {
                    var soft = (float[])need.Clone();
                    var softDir = (Vector3[])dir.Clone();
                    for (int i = 0; i < n; i++) {
                        if (weld[i] != i || neighbours[i] == null || neighbours[i].Count == 0) continue;
                        float sum = 0;
                        var sumDir = Vector3.zero;
                        foreach (int j in neighbours[i]) {
                            sum += need[j];
                            sumDir += dir[j] * need[j];
                        }
                        float half = sum / neighbours[i].Count * 0.5f;
                        if (half > soft[i]) (soft[i], softDir[i]) = (half, sumDir.normalized);
                    }
                    need = soft;
                    dir = softDir;
                }
                // every copy of a welded vertex moves as one
                for (int i = 0; i < n; i++)
                    if (weld[i] != i) (need[i], dir[i]) = (need[weld[i]], dir[weld[i]]);
                bool any = false;
                for (int i = 0; i < n; i++)
                    if (need[i] > 0) {
                        var step = Vector3.ClampMagnitude(total[i] + dir[i] * need[i], maxPush) - total[i];
                        if (step.sqrMagnitude < 1e-12f) continue;
                        total[i] += step;
                        current[i] += step;
                        any = true;
                    }
                if (!any) break;
            }

            // loose parts aren't pushed here: Rigid carries them with the cloth under them
            for (int v = 0; v < n; v++)
                if (loose[v]) total[v] = Vector3.zero;

            most = 0;
            moved = 0;
            for (int i = 0; i < n; i++) {
                float m = total[i].magnitude;
                if (m <= 1e-6f) continue;
                moved++;
                most = Mathf.Max(most, m);
            }
            return total;
        }

        // Each vertex's first vertex at the same position (to 0.05 mm): the copies a seam splits a point of the cloth into.
        private static int[] Welds(Vector3[] cloth) {
            var first = new Dictionary<(int, int, int), int>();
            var weld = new int[cloth.Length];
            for (int v = 0; v < cloth.Length; v++) {
                var p = cloth[v];
                var key = (Mathf.RoundToInt(p.x * 20000), Mathf.RoundToInt(p.y * 20000), Mathf.RoundToInt(p.z * 20000));
                if (!first.TryGetValue(key, out weld[v])) first[key] = weld[v] = v;
            }
            return weld;
        }

        private static Vector3Int ClearCellOf(Vector3 p) =>
            new Vector3Int(Mathf.FloorToInt(p.x / ClearCell), Mathf.FloorToInt(p.y / ClearCell), Mathf.FloorToInt(p.z / ClearCell));

        // Where the line o + t*d crosses triangle abc (t may be negative). Moller-Trumbore.
        private static bool Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t) {
            t = 0;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1 / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0 || u + w > 1) return false;
            t = Vector3.Dot(e2, q) * inv;
            return true;
        }

        private static Vector3[] FaceNormals(Vector3[] vertices, int[] triangles) {
            var normals = new Vector3[triangles.Length / 3];
            for (int t = 0; t < normals.Length; t++) {
                Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
                var n = Vector3.Cross(b - a, c - a);
                normals[t] = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
            }
            return normals;
        }

        // Distance from p to a triangle, and the closest point on it (and its barycentric coordinates). Ericson, Real-Time Collision Detection 5.1.5.
        private static float Closest(Vector3 p, Vector3[] vertices, int[] triangles, int t, out Vector3 bary, out Vector3 point) {
            Vector3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) bary = new Vector3(1, 0, 0);
            else {
                Vector3 bp = p - b;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                Vector3 cp = p - c;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                float vc = d1 * d4 - d3 * d2, vb = d5 * d2 - d1 * d6, va = d3 * d6 - d5 * d4;
                if (d3 >= 0 && d4 <= d3) bary = new Vector3(0, 1, 0);
                else if (d6 >= 0 && d5 <= d6) bary = new Vector3(0, 0, 1);
                else if (vc <= 0 && d1 >= 0 && d3 <= 0) {
                    float w = Ratio(d1, d1 - d3);
                    bary = new Vector3(1 - w, w, 0);
                } else if (vb <= 0 && d2 >= 0 && d6 <= 0) {
                    float w = Ratio(d2, d2 - d6);
                    bary = new Vector3(1 - w, 0, w);
                } else if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) {
                    float w = Ratio(d4 - d3, d4 - d3 + (d5 - d6));
                    bary = new Vector3(0, 1 - w, w);
                } else {
                    float bv = Ratio(vb, va + vb + vc), cw = Ratio(vc, va + vb + vc);
                    bary = new Vector3(1 - bv - cw, bv, cw);
                }
            }
            point = a * bary.x + b * bary.y + c * bary.z;
            return Vector3.Distance(p, point);
        }

        // a / b, 0 for a degenerate (zero-area) triangle
        private static float Ratio(float a, float b) => Mathf.Abs(b) > 1e-20f ? a / b : 0;

        // ---------------------------------------------------------------- helpers

        private const float SimMatch = 0.2f;   // metres a sim's vertex may be from the source's it's matched to

        // A sim's cloth in world space, vertex for vertex the source's (garment: the source's, world space). Unity splits
        // the exports' vertices where their normals differ, so a sim's mesh can have a few more or fewer than the source's
        // and in another order: each cloth vertex is matched to the sim's with the same UV, the nearest of those. The loose
        // parts (stitching, buttons: many share UVs) aren't matched; they ride on the sim's cloth (Rigid). Null (and why, in
        // the report) when it isn't the source's garment moved.
        private static Vector3[] SimWorld(WardrobeSim sim, Mesh source, Vector3[] garment, bool[] loose, string label, Report report) {
            var skin = sim.source.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh != null);
            if (skin == null) {
                report.skipped.Add((label, "has no skinned mesh: export it rigged to the avatar's skeleton"));
                return null;
            }
            var mesh = skin.sharedMesh;
            var bones = skin.bones.Select((b, i) => (b != null ? b.localToWorldMatrix : skin.transform.localToWorldMatrix) * mesh.bindposes[i]).ToArray();
            var per = PerVertex(mesh.boneWeights, bones, mesh.vertexCount, skin.transform.localToWorldMatrix);
            var vertices = mesh.vertices;
            var simWorld = new Vector3[vertices.Length];
            for (int v = 0; v < simWorld.Length; v++) simWorld[v] = per[v].MultiplyPoint3x4(vertices[v]);

            var uv = mesh.uv;
            var sourceUv = source.uv;
            if (uv.Length != vertices.Length || sourceUv.Length != source.vertexCount) {
                report.skipped.Add((label, "it or the source has no UVs to match their vertices by"));
                return null;
            }
            // the same vertices in the same order: as it is
            if (vertices.Length == source.vertexCount) {
                bool same = true;
                for (int v = 0; v < uv.Length && same; v++) same = (uv[v] - sourceUv[v]).sqrMagnitude <= 1e-10f;
                if (same) return simWorld;
            }

            long Key(Vector2 t) => (long)Mathf.Round(t.x * 65536f) * 1000003L + (long)Mathf.Round(t.y * 65536f);
            var byUv = new Dictionary<long, List<int>>();
            for (int v = 0; v < uv.Length; v++) {
                long k = Key(uv[v]);
                if (!byUv.TryGetValue(k, out var list)) byUv[k] = list = new List<int>(1);
                list.Add(v);
            }
            var world = new Vector3[garment.Length];
            int cloth = 0, unmatched = 0;
            for (int v = 0; v < garment.Length; v++) {
                world[v] = garment[v];
                if (loose[v]) continue;
                cloth++;
                int best = -1;
                float bestDistance = SimMatch;
                if (byUv.TryGetValue(Key(sourceUv[v]), out var list))
                    foreach (int s in list) {
                        float d = Vector3.Distance(simWorld[s], garment[v]);
                        if (d < bestDistance) (best, bestDistance) = (s, d);
                    }
                if (best >= 0) world[v] = simWorld[best];
                else unmatched++;
            }
            if (unmatched > cloth / 100) {
                report.skipped.Add((label, $"{unmatched} of the cloth's {cloth} vertices have no vertex in it with their UV within " +
                                           $"{SimMatch * 100:0} cm: it isn't the source's garment simulated again (same pattern, mesh and UVs)"));
                return null;
            }
            if (unmatched > 0) report.notes.Add($"{label}: {unmatched} cloth vertices without a match stay as they are in the source.");
            return world;
        }

        private static SkinnedMeshRenderer SourceSkin(WardrobeGarment g) =>
            g.source == null ? null : g.source.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh != null);

        // Puts the fit on the prefab's renderers that wear the source's mesh (or an older fit). True when the prefab changed.
        private static bool Wear(GameObject prefab, Mesh fit, Mesh source, Mesh old) {
            if (prefab == null) return false;
            string path = AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try {
                bool changed = false;
                foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    if (skin.sharedMesh == fit || skin.sharedMesh != source && (old == null || skin.sharedMesh != old)) continue;
                    skin.sharedMesh = fit;
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                return changed;
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // What a fit depends on: the source, the avatar's model and .ravt, the shape keys and the garment's body hides.
        private static string Key(WardrobeGarment g) {
            var avatar = WardrobeAvatars.Get(g.avatarId);
            var hash = new Hash128();
            hash.Append(Build);
            foreach (string p in new[] { AssetDatabase.GetAssetPath(g.source), avatar?.path, AssetDatabase.GetAssetPath(avatar?.Model) }) {
                if (string.IsNullOrEmpty(p)) continue;
                hash.Append(p);
                hash.Append(AssetDatabase.GetAssetDependencyHash(p).ToString());
            }
            foreach (string s in Shapes(g)) hash.Append(s);
            foreach (var sim in g.sims ?? new List<WardrobeSim>()) {
                if (sim?.source == null) continue;
                string p = AssetDatabase.GetAssetPath(sim.source);
                hash.Append(p);
                hash.Append(AssetDatabase.GetAssetDependencyHash(p).ToString());
                foreach (var s in sim.shapes) hash.Append($"{s.shape}@{s.weight}");
            }
            foreach (string h in g.bodyHides ?? new List<string>()) hash.Append("hide:" + h);
            return hash.ToString();
        }
    }
}
