using UnityEngine;

namespace RosDen.Wardrobe {
    /// <summary>
    /// Put on a garment to recolour it in the editor. The result is baked into a normal texture + material,
    /// so nothing of this component is needed at runtime (VRChat strips it on upload).
    /// </summary>
    [AddComponentMenu("Ros' den/Wardrobe Recolor")]
    [DisallowMultipleComponent]
    public class WardrobeRecolor : MonoBehaviour
#if ROSDEN_VRCSDK
        , VRC.SDKBase.IEditorOnly
#endif
    {
        public WardrobeGarment garment;
        public Renderer target;
        public int materialSlot;
        [Tooltip("Which material type (.rmat id) to use (empty = the garment's default)")]
        public string materialType;
        public string lookName = "Custom";
        public const string CustomPreset = "custom";

        [Tooltip("Id of the preset the look comes from (\"custom\" = Custom, empty = none picked yet)")]
        public string preset = "";
        [Tooltip("Which of the preset's looks: 0 = its own, 1.. = its colourways, -1 = its colours changed")]
        public int variation;
        public WardrobeLook look = new WardrobeLook();

        [HideInInspector] public Texture2D bakedTexture;
        [HideInInspector] public Material bakedMaterial;

        private void Reset() {
            target = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (target == null) target = GetComponentInChildren<Renderer>(true);
        }
    }
}
