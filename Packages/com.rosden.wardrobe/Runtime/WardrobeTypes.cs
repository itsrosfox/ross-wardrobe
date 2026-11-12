using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>Real-world size of one ID map part's UVs, so patterns tile at the same size on every part.</summary>
    [Serializable]
    public class WardrobePartScale {
        public Color32 id;
        [Tooltip("Metres of garment per 1 UV unit on this part")]
        public float metresPerUv;
    }

    public enum TextureChannel { None = -1, R, G, B, A }

    /// <summary>
    /// One of a garment's surface maps (metalness, roughness, occlusion, sheen, opacity): a channel of a texture, or a
    /// constant when there's no texture.
    /// </summary>
    [Serializable]
    public class MapChannel {
        public Texture2D texture;
        public TextureChannel channel = TextureChannel.R;
        [Tooltip("The texture holds the opposite (e.g. smoothness for roughness)")]
        public bool invert;
        [Range(0, 1), Tooltip("Used when there's no texture")]
        public float value;

        public MapChannel() { }

        public MapChannel(float value) => this.value = value;
    }

    /// <summary>One body shape key at a weight (0..100).</summary>
    [Serializable]
    public class WardrobeShapeWeight {
        public string shape;
        [Range(0, 100)] public float weight = 100;
    }

    /// <summary>
    /// The garment simulated again, with the body in another shape: the body shape keys at the weights listed. The cloth
    /// tool's export of it must be the garment's source mesh moved (the same vertices, in the same order). Fit to Avatar
    /// makes the garment's shape keys from these instead of following the body:
    /// - one shape key: a frame of the garment's shape key of that name at that weight (several: in-betweens, a 1D blend);
    /// - several: a corrective, what the one-key shapes at those weights miss, on a garment shape key named by
    ///   <see cref="ComboName"/> and set to the product of the keys' weights over these (2D and more: bilinear).
    /// </summary>
    [Serializable]
    public class WardrobeSim {
        [Tooltip("The cloth tool's export of the garment, simulated on the body in this shape")]
        public GameObject source;
        [Tooltip("The body shape keys it was simulated at, and their weights")]
        public List<WardrobeShapeWeight> shapes = new List<WardrobeShapeWeight>();

        /// <summary>A corrective's shape key name: "breasts+Buff" (every key at 100) or "breasts@100+Buff@50".</summary>
        public static string ComboName(IEnumerable<WardrobeShapeWeight> shapes) {
            var list = shapes.ToList();
            bool full = list.All(s => Mathf.Approximately(s.weight, 100));
            return string.Join("+", list.Select(s => full ? s.shape : $"{s.shape}@{s.weight:0.##}"));
        }

        /// <summary>The keys and weights a corrective's name stands for, or null when it isn't one.</summary>
        public static List<WardrobeShapeWeight> ParseCombo(string name) {
            if (string.IsNullOrEmpty(name) || !name.Contains("+")) return null;
            var result = new List<WardrobeShapeWeight>();
            foreach (string part in name.Split('+')) {
                int at = part.LastIndexOf('@');
                float w = 100;
                if (at >= 0 && !float.TryParse(part.Substring(at + 1), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out w)) return null;
                string shape = at >= 0 ? part.Substring(0, at) : part;
                if (shape.Length == 0 || w <= 0) return null;
                result.Add(new WardrobeShapeWeight { shape = shape, weight = w });
            }
            return result.Count >= 2 ? result : null;
        }

        /// <summary>A corrective's weight (0..100) for the body's weights of its keys: the product of each over its sim's.</summary>
        public static float ComboWeight(List<WardrobeShapeWeight> combo, Func<string, float> bodyWeight) {
            float w = 1;
            foreach (var s in combo) w *= Mathf.Clamp01(bodyWeight(s.shape) / s.weight);
            return w * 100;
        }
    }

    /// <summary>A named group of parts (ID map colours) that is recoloured together, e.g. "Body" or "Collar".</summary>
    [Serializable]
    public class WardrobeRegion {
        public string name = "Region";
        public List<Color32> ids = new List<Color32>();
    }

    /// <summary>One colour slot of a pattern: a flat colour, or a gradient along the garment's gradient map.</summary>
    [Serializable]
    public class SlotColor {
        public Color color = Color.white;
        public bool useGradient;
        public Gradient gradient;

        /// <summary>The colour at a position along the garment's gradient map (0..1).</summary>
        public Color Evaluate(float t) => useGradient && gradient != null ? gradient.Evaluate(t) : color;

        public SlotColor Clone() => new SlotColor { color = color, useGradient = useGradient, gradient = gradient != null ? Copy(gradient) : null };

        public static Gradient Copy(Gradient source) {
            var g = new Gradient { mode = source.mode };
            g.SetKeys(source.colorKeys, source.alphaKeys);
            return g;
        }

        /// <summary>A gradient of one colour (the start for a slot switched to a gradient).</summary>
        public static Gradient Constant(Color c) {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }
    }

    /// <summary>
    /// How one region looks: a pattern (by its .rpat id; empty for the default pattern) in colours of the look's
    /// choosing. Slots the look hasn't set use the pattern's own colours.
    /// </summary>
    [Serializable]
    public class RegionStyle {
        public string region;
        [Tooltip("Id of the region's pattern (from its .rpat); empty = the default pattern")]
        public string pattern = "";
        [Tooltip("The pattern's colour slots, in its order")]
        public List<SlotColor> colors = new List<SlotColor>();
        [Tooltip("Bigger = bigger pattern")]
        public float scale = 1f;
        [Tooltip("Turn the pattern 90°")]
        public bool rotate;

        /// <summary>Slot <paramref name="i"/> as the look set it, or null to use the pattern's own.</summary>
        public SlotColor Slot(int i) => colors != null && i >= 0 && i < colors.Count ? colors[i] : null;

        public RegionStyle Clone() => new RegionStyle {
            region = region, pattern = pattern, scale = scale, rotate = rotate,
            colors = (colors ?? new List<SlotColor>()).Select(c => c?.Clone()).ToList(),
        };
    }

    /// <summary>A full recolour: one style per region (regions without one are the default pattern in the garment's own colour).</summary>
    [Serializable]
    public class WardrobeLook {
        public List<RegionStyle> styles = new List<RegionStyle>();

        public RegionStyle Get(string region) => styles.Find(s => s.region == region);

        public WardrobeLook Clone() {
            var l = new WardrobeLook();
            foreach (var s in styles) l.styles.Add(s.Clone());
            return l;
        }
    }
}
