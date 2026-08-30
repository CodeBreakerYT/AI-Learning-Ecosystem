using UnityEngine;
using UnityEngine.XR;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Drives a hand model's Animator ("Grip"/"Trigger" float params, blending
    /// between open/point/fist poses) from live controller input. Reads the
    /// legacy UnityEngine.XR.InputDevices API directly rather than through an
    /// Input Action: on this project's Unity/Input System/OpenXR combination the
    /// Input System side of the OpenXR bridge never delivers device state (confirmed
    /// live - legacy InputDevices reports real, moving values while every
    /// InputSystem-side XR control reads a permanent 0), so anything routed through
    /// an InputActionReference for XR controller data is silently dead here.
    /// </summary>
    public class HandPoseAnimator : MonoBehaviour
    {
        public XRNode node = XRNode.RightHand;

        private Animator _animator;
        private static readonly int GripParam = Animator.StringToHash("Grip");
        private static readonly int TriggerParam = Animator.StringToHash("Trigger");

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void Update()
        {
            if (_animator == null) return;

            var device = InputDevices.GetDeviceAtXRNode(node);
            float grip = 0f, trigger = 0f;
            if (device.isValid)
            {
                device.TryGetFeatureValue(CommonUsages.grip, out grip);
                device.TryGetFeatureValue(CommonUsages.trigger, out trigger);
            }

            _animator.SetFloat(GripParam, grip);
            _animator.SetFloat(TriggerParam, trigger);
        }
    }
}
