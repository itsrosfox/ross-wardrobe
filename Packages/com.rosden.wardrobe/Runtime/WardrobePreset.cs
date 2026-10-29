using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>A saved look for a garment. Shipped presets live in the garment; buyers can save their own.</summary>
    [CreateAssetMenu(menuName = "Ros' den/Wardrobe Preset", fileName = "Wardrobe Preset")]
    public class WardrobePreset : ScriptableObject {
        public WardrobeGarment garment;
        public WardrobeLook look = new WardrobeLook();
    }
}
