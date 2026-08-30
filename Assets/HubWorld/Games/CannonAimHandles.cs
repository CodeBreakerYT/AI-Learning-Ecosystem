using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Two-handle cannon aiming, ported from the user's own reference project
    /// (ref/VR-Mathipia/VR Mathepia/Assets/Main/scripts/obj/rayShooter/
    /// RayShooterController.cs + HandleHandRestrictor.cs). The source rig
    /// tracks full pitch+yaw for a free-aim ray gun; MathCannon only teaches
    /// one angle (elevation, matching StandardAngles), so this keeps just the
    /// pitch half of that math - the live hand-height-above-pivot calculation
    /// - and drops yaw entirely, leaving the cannon aimed along its fixed
    /// firing lane.
    ///
    /// Both handles must be built with their XRGrabInteractable's
    /// trackPosition/trackRotation OFF (same as the reference) so grabbing
    /// them doesn't rip them off the gun body - they're aim handles, not
    /// separate objects the player carries away.
    /// </summary>
    public class CannonAimHandles : MonoBehaviour
    {
        public Transform pivot;
        public XRGrabInteractable leftHandle;
        public XRGrabInteractable rightHandle;
        public float rotationSpeed = 10f;
        // How many degrees of elevation one meter of real vertical hand
        // travel is worth - tuned so a comfortable ~0.5-0.6m raise from the
        // handles' resting height sweeps close to the full 90 degrees.
        public float degreesPerMeter = 150f;

        /// <summary>Continuous 0-90 elevation angle, updated every frame both handles are held - what MathCannonGame reads for the live readout and for grading Fire().</summary>
        public float CurrentAngleDegrees { get; private set; }

        public bool BothHandlesHeld => leftHandle != null && rightHandle != null && leftHandle.isSelected && rightHandle.isSelected;

        /// <summary>Live world-space barrel direction at the current elevation - for the angle wedge gizmo.</summary>
        public Vector3 CurrentAimDirection => pivot != null ? pivot.forward : Vector3.forward;

        private bool _wasBothHeld;
        private float _baselineHeight;
        private float _baselineBarrelAngle;

        private void OnEnable()
        {
            if (leftHandle != null) leftHandle.selectEntered.AddListener(OnLeftGrabbed);
            if (rightHandle != null) rightHandle.selectEntered.AddListener(OnRightGrabbed);
        }

        private void OnDisable()
        {
            if (leftHandle != null) leftHandle.selectEntered.RemoveListener(OnLeftGrabbed);
            if (rightHandle != null) rightHandle.selectEntered.RemoveListener(OnRightGrabbed);
        }

        // Same "force-release on wrong hand" trick as HandleHandRestrictor.cs -
        // toggling enabled off/on cancels the just-started select cleanly.
        private void OnLeftGrabbed(SelectEnterEventArgs args) => EnforceHand(args, leftHandle, isLeftHandle: true);
        private void OnRightGrabbed(SelectEnterEventArgs args) => EnforceHand(args, rightHandle, isLeftHandle: false);

        private static void EnforceHand(SelectEnterEventArgs args, XRGrabInteractable grab, bool isLeftHandle)
        {
            var interactorTransform = (args.interactorObject as Component)?.transform;
            if (interactorTransform == null) return;
            if (IsFromHand(interactorTransform, isLeftHandle)) return;

            grab.enabled = false;
            grab.enabled = true;
        }

        // This project's rig has real "LeftHand"/"RightHand" GameObjects in
        // every scene (confirmed this session while fixing left-controller
        // teleport) - walking up to find one is simpler than porting the
        // reference's separate XRHandTag component.
        private static bool IsFromHand(Transform interactor, bool wantLeft)
        {
            for (var t = interactor; t != null; t = t.parent)
            {
                if (t.name == "LeftHand") return wantLeft;
                if (t.name == "RightHand") return !wantLeft;
            }
            return false;
        }

        private void Update()
        {
            if (pivot == null || !BothHandlesHeld) { _wasBothHeld = false; return; }

            var leftPos = leftHandle.interactorsSelecting[0].transform.position;
            var rightPos = rightHandle.interactorsSelecting[0].transform.position;
            var mid = (leftPos + rightPos) * 0.5f;

            var dir = mid - pivot.position;
            var localDir = pivot.parent != null ? pivot.parent.InverseTransformDirection(dir) : dir;

            // The handles are mounted on a FIXED pedestal, not carried in the
            // player's own hand like the reference project's hand-held
            // raygun - an absolute "atan2(height, distance-from-pivot)"
            // reading (the original approach here) means the achievable
            // angle range is dictated by exactly how far forward/back your
            // hands happen to be from the pivot. With the handles at a
            // fairly fixed ~0.6m horizontal offset, raising your hands
            // straight up barely moves that ratio - confirmed
            // live/reported: no matter how high the hands went, the angle
            // plateaued around 30 degrees, since reaching much higher
            // requires the hands almost directly above the pivot, which is
            // not a real arm motion.
            //
            // Replaced with a plain LINEAR control: track how far your hand
            // height has moved (in meters) since the moment you grabbed
            // both handles, and turn that directly into degrees via
            // degreesPerMeter, added on top of wherever the barrel already
            // was. This has no dependence on horizontal distance from the
            // pivot at all, so it can't plateau early regardless of exactly
            // where the handles sit - a normal ~0.5-0.6m raise now sweeps
            // close to the full 0-90 range. Re-baselining on every fresh
            // grab (instead of once ever) means letting go and re-gripping
            // continues smoothly from the current angle rather than
            // snapping back to an absolute reading.
            if (!_wasBothHeld)
            {
                _baselineHeight = localDir.y;
                _baselineBarrelAngle = CurrentAngleDegrees;
                _wasBothHeld = true;
            }

            var heightDelta = localDir.y - _baselineHeight;
            CurrentAngleDegrees = Mathf.Clamp(_baselineBarrelAngle + heightDelta * degreesPerMeter, 0f, 90f);

            var targetRot = Quaternion.Euler(-CurrentAngleDegrees, 0f, 0f);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, targetRot, Time.deltaTime * rotationSpeed);
        }
    }
}
