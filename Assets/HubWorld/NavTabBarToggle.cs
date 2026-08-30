using UnityEngine;
using UnityEngine.XR;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Shows/hides the pause menu on a thumbstick-click press (either hand),
    /// instead of leaving it permanently on screen. Lives on the
    /// always-active canvas root so Update keeps running even while the
    /// visual panel itself is toggled off. Reads the legacy
    /// UnityEngine.XR.InputDevices API directly, consistent with
    /// LegacyXRInputBridge - Input System-side XR controls don't deliver
    /// state on this project's Unity/Input System/OpenXR combination.
    ///
    /// Opening the panel actually pauses the game (Time.timeScale = 0) -
    /// a real pause, not just a navigation shortcut. UI ray-cast/click
    /// still works at timeScale 0 (EventSystem/Canvas input is driven by
    /// real input events, not scaled time), so Resume/the nav buttons stay
    /// clickable while paused.
    /// </summary>
    public class NavTabBarToggle : MonoBehaviour
    {
        public GameObject target;

        private bool _lastPressed;

        private void Update()
        {
            if (target == null) return;

            bool pressed = IsClicked(XRNode.LeftHand) || IsClicked(XRNode.RightHand);
            if (pressed && !_lastPressed)
                SetOpen(!target.activeSelf);
            _lastPressed = pressed;
        }

        public void SetOpen(bool open)
        {
            target.SetActive(open);
            Time.timeScale = open ? 0f : 1f;
        }

        // A scene unload/reload (nav-back, going to a whole different scene)
        // must never leave the NEXT scene permanently paused - Resume and
        // the nav buttons already reset timeScale themselves before loading,
        // but this covers being destroyed any other way (e.g. this object's
        // own scene tearing down) while still paused.
        private void OnDestroy()
        {
            if (target != null && target.activeSelf) Time.timeScale = 1f;
        }

        private static bool IsClicked(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;
            device.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool click);
            return click;
        }
    }
}
