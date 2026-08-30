using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A box you physically shove, not grab-and-carry: while a "Hand" collider
    /// (the trigger spheres on PlayerPhysics's Direct Interactors - see
    /// LegacyXRInputBridge/the "Hand" tag fix) sits inside this box's trigger
    /// AND that hand's grip is held, continuous force is applied proportional
    /// to how fast the hand is moving. No XRGrabInteractable at all - the box
    /// obeys real F = ma the whole time, which is the point for Newton's
    /// Force Arena (design doc 6.2): mass actually matters, momentum carries
    /// after you let go, and shoving harder is a real input, not a metaphor.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableBox : MonoBehaviour
    {
        public float pushForceMultiplier = 45f;
        public float LastAppliedForceMagnitude { get; private set; }

        private Rigidbody _rb;
        private readonly Dictionary<Collider, Vector3> _lastHandPos = new Dictionary<Collider, Vector3>();

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void OnTriggerStay(Collider other)
        {
            if (!other.CompareTag("Hand")) return;

            if (!IsGripHeld(other))
            {
                _lastHandPos.Remove(other);
                return;
            }

            if (_lastHandPos.TryGetValue(other, out var lastPos) && Time.deltaTime > 0f)
            {
                var handVelocity = (other.transform.position - lastPos) / Time.deltaTime;
                var force = handVelocity * pushForceMultiplier;
                _rb.AddForce(force, ForceMode.Force);
                LastAppliedForceMagnitude = force.magnitude;
            }
            _lastHandPos[other] = other.transform.position;
        }

        private void OnTriggerExit(Collider other) => _lastHandPos.Remove(other);

        private static bool IsGripHeld(Collider handCollider)
        {
            var node = handCollider.name.Contains("Left") ? XRNode.LeftHand : XRNode.RightHand;
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float g) && g > 0.15f;
        }
    }
}
