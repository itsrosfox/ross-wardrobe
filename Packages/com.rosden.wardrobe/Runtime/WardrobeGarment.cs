using System.Collections.Generic;
using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>
    /// One garment fitted to one avatar base, read from its .rgarm file (the file is the source; this is what Ros'
    /// Wardrobe imports it as). Avatars (.ravt), material types (.rmat), patterns (.rpat) and presets (.rpre) are
    /// separate files that refer to garments by id, so anyone can add to a garment without touching it.
    /// </summary>
    public class WardrobeGarment : ScriptableObject {
        [Tooltip("Stable id patterns and presets refer to the garment by (e.g. rosden.turtleneck); the same for every avatar " +
                 "it's fitted to. Never change it once shipped")]
        public string id;

        [Tooltip("Name shown in the Ros' Wardrobe window")]
        public string displayName;

        [Tooltip("Browser category, e.g. Tops, Bottoms, Shoes, Accessories")]
        public string category;

        [Tooltip("Where it's worn, e.g. Top. Putting a garment on takes off the one in the same slot; empty = never swapped")]
        public string slot;

        [Tooltip("Shape keys on the avatar's body that hide what this garment covers (e.g. TShirt). Suggestions only: " +
                 "Ros' Wardrobe lists them in its Avatar section while the garment is worn, and only you turn them on")]
        public List<string> bodyHides = new List<string>();

        public string version = "1.0.0";
        public string author;

        [Tooltip("Id of the avatar base (.ravt) this fit is for")]
        public string avatarId;

        [Tooltip("Which versions of the avatar: \"1\" = 1.x.x, empty = any")]
        public string avatarVersion;

        [Tooltip("Prefab the Ros' Wardrobe window puts on the avatar")]
        public GameObject prefab;

        [Tooltip("Where Make Prefab puts the garment's toggle in the avatar's menu, e.g. Clothing/T-Shirt; empty = Clothing/<name>")]
        public string menuPath;

        [Tooltip("Picture in the Ros' Wardrobe window; rendered from the garment when empty")]
        public Texture2D thumbnail;

        [Tooltip("The garment as your cloth tool exports it (e.g. Marvelous Designer's FBX), rigged to the avatar's skeleton. " +
                 "Fit to Avatar makes the mesh from it")]
        public GameObject source;

        [Tooltip("Mesh the prefab wears (Fit to Avatar makes it from the source, with the body's shape keys), the ID and " +
                 "gradient maps are made from, and thumbnails are rendered with")]
        public Mesh mesh;

        [Tooltip("The garment simulated again with the body in other shapes (body shape keys at weights). Fit to Avatar makes " +
                 "the garment's shape keys from these: in-betweens for one key, correctives for several. Keys without one " +
                 "follow the body")]
        public List<WardrobeSim> sims = new List<WardrobeSim>();

        [Tooltip("Material type (.rmat id) the garment starts in; empty = the first that fits")]
        public string defaultMaterial;

        // ---------------------------------------------------------------- the garment's own maps

        [Tooltip("Colour texture: looks are baked at its size and keep its alpha")]
        public Texture2D baseColor;

        [Tooltip("Normal map; patterns' relief is added to it")]
        public Texture2D normalMap;

        public MapChannel metalness = new MapChannel(0);
        public MapChannel roughness = new MapChannel(0.8f);
        public MapChannel occlusion = new MapChannel(1);
        public MapChannel sheen = new MapChannel(0.2f);
        [Tooltip("Without a texture: the base colour's alpha")]
        public MapChannel opacity = new MapChannel(1);

        [Tooltip("Largest size of the textures Make Material packs for the shipped material; 0 = the base colour's size")]
        public int mapSize;

        // ---------------------------------------------------------------- recolouring

        [Tooltip("One flat colour per part; regions group these colours")]
        public Texture2D idMap;

        [Tooltip("16-bit grayscale (R16): where each pixel sits along a gradient, 0 = start, 1 = end")]
        public Texture2D gradientMap;

        public List<WardrobeRegion> regions = new List<WardrobeRegion>();

        [Tooltip("Real-world size of each ID map part's UVs, so patterns tile at real size (filled in by Generate from UV islands)")]
        public List<WardrobePartScale> partScales = new List<WardrobePartScale>();

        [Tooltip("Metres of garment per 1 UV unit, for parts without their own scale")]
        public float metresPerUv = 1f;
    }
}
