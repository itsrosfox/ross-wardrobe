#if ROSDEN_VRCSDK
using VRC.SDKBase.Editor.BuildPipeline;

namespace RosDen.Wardrobe.Editor {
    /// <summary>Bakes looks still only shown as a live preview before VRChat builds the avatar; stops the build if one can't be.</summary>
    internal class WardrobeBuildHook : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            if (WardrobeLookPanel.FlushAll()) return true;
            UnityEngine.Debug.LogError("[Ros' Wardrobe] A garment's look couldn't be baked (see the error above), so it wouldn't upload. " +
                                       "Pick the look again, then build.");
            return false;
        }
    }
}
#endif
