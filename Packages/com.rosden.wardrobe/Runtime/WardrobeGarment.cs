using System.Collections.Generic;
using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>
    /// Everything needed to recolour one garment. Authored once per garment and shipped with it.
    /// </summary>
    [CreateAssetMenu(menuName = "Ros' den/Wardrobe Garment", fileName = "Wardrobe Garment")]
    public class WardrobeGarment : ScriptableObject {
        [Tooltip("Mesh the ID and gradient maps are generated from")]
        public Mesh mesh;

        [Tooltip("Base colour texture the recolour is built on; its fabric/shading detail is kept")]
        public Texture2D baseColor;

        [Tooltip("Material the recoloured materials are copied from (e.g. the garment's locked Poiyomi material)")]
        public Material material;

        [Tooltip("Texture property of that material that receives the recoloured texture")]
        public string textureProperty = "_MainTex";

        [Range(0, 1), Tooltip("How much of the base texture's fabric/shading detail shows on recoloured parts")]
        public float detail = 1f;

        [Tooltip("One flat colour per part; regions group these colours")]
        public Texture2D idMap;

        [Tooltip("16-bit grayscale (R16): where each pixel sits along a gradient, 0 = start, 1 = end")]
        public Texture2D gradientMap;

        public List<WardrobeRegion> regions = new List<WardrobeRegion>();

        [Tooltip("Looks shipped with the garment, shown first in the recolour editor")]
        public List<WardrobePreset> presets = new List<WardrobePreset>();
    }
}
