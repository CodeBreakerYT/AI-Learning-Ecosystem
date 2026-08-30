using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Guarantees a comfortable minimum standing eye height, applied ONCE by
    /// restructuring the hierarchy rather than fighting it every frame.
    ///
    /// Confirmed live that neither editing XROrigin.CameraYOffset/the
    /// "Camera Offset" transform directly, nor correcting the Main Camera's
    /// own position every LateUpdate, actually sticks - something in Unity's
    /// XR tracking pipeline applies the (with no real headset attached,
    /// effectively floor-level/zero) tracked pose to the camera even later
    /// than LateUpdate, right before rendering, silently undoing any
    /// per-frame correction applied earlier in the same frame ("I am
    /// smaller" persisted even with an active LateUpdate fix in place).
    ///
    /// The fix that actually survives that: insert a NEW static parent
    /// between the camera's existing parent and the camera itself, and put
    /// the height correction on THAT node instead. Whatever the XR system
    /// resets the camera's own local transform to each frame, this
    /// ancestor's offset still composes on top of it every frame - it's
    /// never the thing being reset.
    /// </summary>
    public static class MinimumEyeHeightEnforcer
    {
        public static void Apply(Transform playerRoot, float minimumEyeHeight = 1.6f)
        {
            var cam = Camera.main;
            if (cam == null || playerRoot == null) return;
            if (cam.transform.parent == null) return;
            if (cam.transform.parent.name == "Height Fix") return; // already applied

            var currentHeight = cam.transform.position.y - playerRoot.position.y;
            var shortfall = minimumEyeHeight - currentHeight;
            if (shortfall <= 0f) return; // already tall enough - real/good tracking, leave it alone

            var oldParent = cam.transform.parent;
            var fixGO = new GameObject("Height Fix");
            fixGO.transform.SetParent(oldParent, false);
            fixGO.transform.localPosition = new Vector3(0f, shortfall, 0f);

            cam.transform.SetParent(fixGO.transform, true); // keep world pose - only the extra offset changes
        }
    }
}
