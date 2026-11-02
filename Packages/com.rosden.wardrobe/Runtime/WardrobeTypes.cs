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
