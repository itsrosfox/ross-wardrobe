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
        public string lookName = "Original";
        public WardrobeLook look = new WardrobeLook();

        [HideInInspector] public Texture2D bakedTexture;
        [HideInInspector] public Material bakedMaterial;

        private void Reset() {
            target = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (target == null) target = GetComponentInChildren<Renderer>(true);
        }
    }
}
