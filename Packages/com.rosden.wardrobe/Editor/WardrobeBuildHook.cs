#if ROSDEN_VRCSDK
using VRC.SDKBase.Editor.BuildPipeline;

namespace RosDen.Wardrobe.Editor {
    /// <summary>
    /// Before VRChat builds the avatar: bakes looks still only shown as a live preview, and runs the clearance pass on
    /// what's worn (in case the body changed since); stops the build if a look can't be baked.
    /// </summary>
    internal class WardrobeBuildHook : IVRCSDKBuildRequestedCallback {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType) {
            if (!WardrobeClearance.ApplyAll())
                UnityEngine.Debug.LogWarning("[Ros' Wardrobe] The clearance pass failed on an avatar (see the error above): its garments may clip.");
            if (WardrobeLookPanel.FlushAll()) return true;
            UnityEngine.Debug.LogError("[Ros' Wardrobe] A garment's look couldn't be baked (see the error above), so it wouldn't upload. " +
                                       "Pick the look again, then build.");
            return false;
        }
    }
}
#endif
