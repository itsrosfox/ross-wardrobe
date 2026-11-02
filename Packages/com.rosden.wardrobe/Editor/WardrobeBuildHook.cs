#if ROSDEN_VRCSDK
using VRC.SDKBase.Editor.BuildPipeline;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Bakes looks still only shown as a live preview before VRChat builds the avatar.</summary>
    internal class WardrobeBuildHook : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            WardrobeLookPanel.FlushAll();
            return true;
        }
    }
}
#endif
