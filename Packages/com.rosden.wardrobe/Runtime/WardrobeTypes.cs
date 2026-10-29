using System;
using System.Collections.Generic;
using UnityEngine;

namespace RosDen.Wardrobe {
    public enum RegionMode {
        Keep,       // leave the original texture
        Solid,      // one colour, fabric detail kept
        Gradient,   // colour from a gradient along the garment's gradient map
    }

    /// <summary>A named group of parts (ID map colours) that is recoloured together, e.g. "Body" or "Collar".</summary>
    [Serializable]
    public class WardrobeRegion {
        public string name = "Region";
        public List<Color32> ids = new List<Color32>();
    }

    /// <summary>How one region looks.</summary>
    [Serializable]
    public class RegionStyle {
        public string region;
        public RegionMode mode = RegionMode.Keep;
        public Color color = Color.white;
        public Gradient gradient = DefaultGradient();

        public static Gradient DefaultGradient() {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.35f, 0.35f, 0.35f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        public RegionStyle Clone() {
            var g = new Gradient { mode = gradient.mode };
            g.SetKeys(gradient.colorKeys, gradient.alphaKeys);
            return new RegionStyle { region = region, mode = mode, color = color, gradient = g };
        }
    }

    /// <summary>A full recolour: one style per region (regions without a style keep the original texture).</summary>
    [Serializable]
    public class WardrobeLook {
        public List<RegionStyle> styles = new List<RegionStyle>();

        public RegionStyle Get(string region) => styles.Find(s => s.region == region);

        public RegionStyle GetOrAdd(string region) {
            var s = Get(region);
            if (s == null) {
                s = new RegionStyle { region = region };
                styles.Add(s);
            }
            return s;
        }

        public WardrobeLook Clone() {
            var l = new WardrobeLook();
            foreach (var s in styles) l.styles.Add(s.Clone());
            return l;
        }
    }
}
