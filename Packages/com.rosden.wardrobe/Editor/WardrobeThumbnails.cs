using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Small front-view renders of a garment wearing a look, for the browser cards and preset buttons. Rendered in a
    /// material type of the garment (so they look like the real thing), outside of GUI drawing, a few per editor tick,
    /// and kept on disk in Library/Ros' Wardrobe/Cache (by everything that changes the picture), so switching garments or
    /// reopening the project shows them right away.
    /// </summary>
    public static class WardrobeThumbnails {
        public const int Size = 192;
        private const int MaxCached = 512;
        private const int PerBatch = 4;
        private const int Version = 2;   // bump when the pictures change, so the disk cache isn't used

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Queue<string> Order = new Queue<string>();
        private static readonly Dictionary<string, (WardrobeGarment garment, WardrobeLook look, string type)> Pending =
            new Dictionary<string, (WardrobeGarment, WardrobeLook, string)>();
        private static readonly List<Action> Waiting = new List<Action>();
        private static bool _scheduled;
        private static PreviewRenderUtility _utility;
        private static RenderTexture _texture;

        /// <summary>Where cached pictures go (Library is never shared or uploaded).</summary>
        public static string CacheFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "Ros' Wardrobe", "Cache");

        [InitializeOnLoadMethod]
        private static void Hook() => AssemblyReloadEvents.beforeAssemblyReload += Release;

        /// <summary>The thumbnail (in that material type, null = default) if it's ready; otherwise null, and <paramref name="repaint"/> is called once it is.</summary>
        public static Texture2D Get(WardrobeGarment g, WardrobeLook look, string materialType, Action repaint) {
            if (!CanRender(g, materialType)) return null;
            string key = Key(g, look, materialType);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var stored = FromDisk(key);
            if (stored != null) return Remember(key, stored);
            if (!Pending.ContainsKey(key)) Pending[key] = (g, look?.Clone(), materialType);
            if (repaint != null && !Waiting.Contains(repaint)) Waiting.Add(repaint);
            Schedule();
            return null;
        }

        /// <summary>Renders right away, uncached (the caller owns the texture).</summary>
        public static Texture2D RenderNow(WardrobeGarment g, WardrobeLook look, string materialType = null) =>
            CanRender(g, materialType) ? Render(g, look, materialType) : null;

        /// <summary>Forgets the pictures in memory (the disk cache stays; <see cref="ClearDisk"/> empties it).</summary>
        public static void Clear() {
            foreach (var texture in Cache.Values) if (texture != null) Object.DestroyImmediate(texture);
            Cache.Clear();
            Order.Clear();
        }

        public static void ClearDisk() {
            Clear();
            try {
                if (Directory.Exists(CacheFolder)) Directory.Delete(CacheFolder, true);
            } catch (IOException e) {
                Debug.LogWarning($"[Ros' Wardrobe] Couldn't empty {CacheFolder}: {e.Message}");
            }
        }

        private static bool CanRender(WardrobeGarment g, string materialType) =>
            g != null && g.mesh != null && WardrobeBaker.Problem(g) == null && WardrobeMaterialTypes.For(g, materialType) != null;

        private static Texture2D Remember(string key, Texture2D texture) {
            if (Cache.Count >= MaxCached)
                while (Order.Count > MaxCached / 2) {
                    string old = Order.Dequeue();
                    if (Cache.TryGetValue(old, out var t) && t != null) Object.DestroyImmediate(t);
                    Cache.Remove(old);
                }
            Cache[key] = texture;
            Order.Enqueue(key);
            return texture;
        }

        private static void Schedule() {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += Process;
        }

        // A few per editor tick, so a window full of new thumbnails doesn't freeze the editor.
        private static void Process() {
            _scheduled = false;
            foreach (string key in Pending.Keys.Take(PerBatch).ToList()) {
                var (g, look, type) = Pending[key];
                Pending.Remove(key);
                if (g == null) continue;
                try {
                    var texture = Render(g, look, type);
                    Remember(key, texture);
                    ToDisk(key, texture);
                } catch (Exception e) {
                    Debug.LogException(e);
                    Cache[key] = null;
                }
            }
            if (Pending.Count > 0) Schedule();
            var waiting = Waiting.ToList();
            if (Pending.Count == 0) Waiting.Clear();
            foreach (var repaint in waiting) repaint();
        }

        // ---------------------------------------------------------------- disk

        private static string FileOf(string key) => Path.Combine(CacheFolder, "Thumbnails", Hash128.Compute(key) + ".png");

        private static Texture2D FromDisk(string key) {
            string file = FileOf(key);
            if (!File.Exists(file)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try {
                if (texture.LoadImage(File.ReadAllBytes(file))) return texture;
            } catch (IOException) { }
            Object.DestroyImmediate(texture);
            return null;
        }

        private static void ToDisk(string key, Texture2D texture) {
            try {
                string file = FileOf(key);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, texture.EncodeToPNG());
            } catch (Exception e) {
                Debug.LogWarning($"[Ros' Wardrobe] Couldn't cache a thumbnail: {e.Message}");
            }
        }

        // Everything the picture depends on: the garment's file and maps, the material type, the look and its patterns' files.
        private static string Key(WardrobeGarment g, WardrobeLook look, string materialType) {
            var type = WardrobeMaterialTypes.For(g, materialType);
            var t = WardrobeSettings.Current.thumbnails;
            var sb = new StringBuilder().Append(Version).Append('/').Append(Dependency(WardrobeGarments.PathOf(g)))
                .Append('/').Append(Dependency(type.path)).Append('/').Append(Dependency(AssetDatabase.GetAssetPath(type.template)))
                .Append('/').Append(t.background).Append(t.ambient);
            foreach (var o in new Object[] { g.baseColor, g.idMap, g.gradientMap, g.mesh, g.normalMap })
                sb.Append('/').Append(o != null ? Dependency(AssetDatabase.GetAssetPath(o)) : "-");
            for (int r = 0; r < g.regions.Count && r < WardrobeBaker.MaxRegions; r++) {
                var s = WardrobeBaker.StyleOf(g, look, r);
                var pattern = WardrobePatterns.Get(s.pattern);
                sb.Append('|').Append(s.pattern).Append('#').Append(pattern != null ? Dependency(pattern.jsonPath) : "-")
                    .Append('x').Append(s.scale.ToString("F2")).Append(s.rotate ? 'r' : '-').Append(':');
                for (int i = 0; i < Mathf.Max(1, pattern?.slots.Count ?? 1); i++) {
                    var slot = WardrobeBaker.SlotOf(s, pattern, i);
                    if (!slot.useGradient || slot.gradient == null) sb.Append(ColorUtility.ToHtmlStringRGB(slot.color));
                    else foreach (var k in slot.gradient.colorKeys) sb.Append(ColorUtility.ToHtmlStringRGB(k.color)).Append('@').Append(k.time.ToString("F3"));
                    sb.Append(',');
                }
            }
            return sb.ToString();
        }

        private static readonly Dictionary<string, string> Dependencies = new Dictionary<string, string>();

        internal static string Dependency(string path) {
            if (string.IsNullOrEmpty(path)) return "-";
            if (!Dependencies.TryGetValue(path, out string hash)) Dependencies[path] = hash = AssetDatabase.GetAssetDependencyHash(path).ToString();
            return hash;
        }

        // Anything imported may change what a picture depends on.
        internal static void ForgetDependencies() => Dependencies.Clear();

        // ---------------------------------------------------------------- rendering

        private static Texture2D Render(WardrobeGarment g, WardrobeLook look, string materialType) {
            if (_utility == null) {
                _utility = new PreviewRenderUtility();
                var c = _utility.camera;
                c.fieldOfView = 30f;
                c.clearFlags = CameraClearFlags.SolidColor;
                var t = WardrobeSettings.Current.thumbnails;
                c.backgroundColor = WardrobeSettings.Color(t.background, Color.gray);
                _utility.ambientColor = WardrobeSettings.Color(t.ambient, Color.gray);
                _utility.lights[0].intensity = 1.1f;
                _utility.lights[0].transform.rotation = Quaternion.LookRotation(new Vector3(0.3f, -0.5f, -1f));
                _utility.lights[1].intensity = 0.4f;
                _utility.lights[1].transform.rotation = Quaternion.LookRotation(new Vector3(-0.4f, 0.2f, 1f));
            }

            _texture = WardrobeBaker.RenderPreview(g, look, _texture);
            var type = WardrobeMaterialTypes.For(g, materialType);
            var material = WardrobeMaterialTypes.PreviewMaterial(g, type);
            foreach (var output in type.outputs.Where(o => o.albedo))
            foreach (string property in output.Properties)
                if (material.HasProperty(property)) material.SetTexture(property, _texture);
            var matrix = WardrobeGenerators.ModelMatrix(g.mesh);
            var bounds = Transform(g.mesh.bounds, matrix);

            // Front view (garments face +Z), framed on the garment's width/height.
            var cam = _utility.camera;
            float half = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.08f;
            float distance = half / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + bounds.extents.z;
            cam.transform.position = bounds.center + Vector3.forward * distance;
            cam.transform.LookAt(bounds.center);
            cam.nearClipPlane = distance * 0.05f;
            cam.farClipPlane = distance * 4f;
            try {
                _utility.BeginStaticPreview(new Rect(0, 0, Size, Size));
                for (int i = 0; i < g.mesh.subMeshCount; i++) _utility.DrawMesh(g.mesh, matrix, material, i);
                cam.Render();
                var result = _utility.EndStaticPreview();
                result.hideFlags = HideFlags.HideAndDontSave;
                return result;
            } finally {
                Object.DestroyImmediate(material);
            }
        }

        private static Bounds Transform(Bounds b, Matrix4x4 m) {
            var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++) {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return result;
        }

        private static void Release() {
            Clear();
            Pending.Clear();
            Waiting.Clear();
            _utility?.Cleanup();
            _utility = null;
            if (_texture != null) Object.DestroyImmediate(_texture);
            _texture = null;
        }
    }
}
