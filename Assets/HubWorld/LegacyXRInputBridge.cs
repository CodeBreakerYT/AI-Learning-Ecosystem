using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Drives hand tracking, grab/ray select+activate, and thumbstick locomotion
    /// straight from the legacy UnityEngine.XR.InputDevices API instead of through
    /// Input System / Input Actions. On this project's Unity 6000.3.13f1 +
    /// Input System 1.19 + OpenXR 1.16.1 combination, confirmed live: the legacy
    /// API reports real, moving controller data every frame, while every
    /// InputSystem-side XR control (TrackedPoseDriver, XRInputButtonReader bound
    /// via InputActionReference, thumbstick readers) permanently reads zero - the
    /// Input System<->OpenXR bridge itself never delivers device state here. This
    /// bypasses that broken bridge by feeding XRI's own "Manual" input mode, so the
    /// existing interactor/locomotion components keep working unmodified.
    /// </summary>
    public class LegacyXRInputBridge : MonoBehaviour
    {
        public Transform leftHand;
        public Transform rightHand;
        public XRBaseInputInteractor leftDirectInteractor;
        public XRBaseInputInteractor rightDirectInteractor;
        public XRRayInteractor rayInteractor;
        public ContinuousMoveProvider moveProvider;
        public ContinuousTurnProvider turnProvider;
        public JumpProvider jumpProvider;

        private bool _wasJumpPressed;

        private void Awake()
        {
            // The Jump child GameObject ships disabled by default on the source
            // prefab, and several scenes additionally carry a per-instance
            // override pinning it disabled (confirmed live: activeSelf false
            // on the scene instance even after enabling it on the prefab
            // asset itself, since instance overrides win). Force it active here
            // instead of chasing down every scene's override - this also
            // guarantees JumpProvider.Awake()/OnEnable() run (they're what
            // auto-discover the sibling GravityProvider via
            // ComponentLocatorUtility; without this they never fire and
            // CanJump()/Jump() NRE on a null gravityProvider).
            if (jumpProvider != null)
            {
                jumpProvider.gameObject.SetActive(true);

                // JumpProvider ships with its own jumpInput reader, pre-bound
                // via XRI's default Input Actions (commonly the A/primary
                // button) - only activating the component left that binding
                // live alongside DriveJump()'s own B-button read below, so
                // both A and B independently triggered Jump(). Force it to
                // Manual and never feed it, so this bridge is the only path
                // that can ever call Jump().
                if (jumpProvider.jumpInput != null)
                    jumpProvider.jumpInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            }

            DisableTrackedPoseDriver(leftHand);
            DisableTrackedPoseDriver(rightHand);

            SetManual(leftDirectInteractor);
            SetManual(rightDirectInteractor);
            SetManual(rayInteractor);
            if (rayInteractor != null && rayInteractor.uiPressInput != null)
                rayInteractor.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;

            if (moveProvider != null)
            {
                SetManual(moveProvider.leftHandMoveInput);
                SetManual(moveProvider.rightHandMoveInput);
            }

            if (turnProvider != null)
            {
                SetManual(turnProvider.leftHandTurnInput);
                SetManual(turnProvider.rightHandTurnInput);
            }
        }

        private static void DisableTrackedPoseDriver(Transform hand)
        {
            var tpd = hand != null ? hand.GetComponent<TrackedPoseDriver>() : null;
            if (tpd != null) tpd.enabled = false;
        }

        private static void SetManual(XRBaseInputInteractor interactor)
        {
            if (interactor == null) return;
            if (interactor.selectInput != null)
                interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            if (interactor.activateInput != null)
                interactor.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        }

        private static void SetManual(XRInputValueReader<Vector2> reader)
        {
            if (reader != null)
                reader.inputSourceMode = XRInputValueReader<Vector2>.InputSourceMode.ManualValue;
        }

        private void Update()
        {
            Drive(XRNode.LeftHand, leftHand, leftDirectInteractor,
                moveProvider != null ? moveProvider.leftHandMoveInput : null,
                turnProvider != null ? turnProvider.leftHandTurnInput : null);

            Drive(XRNode.RightHand, rightHand, rightDirectInteractor,
                moveProvider != null ? moveProvider.rightHandMoveInput : null,
                turnProvider != null ? turnProvider.rightHandTurnInput : null);

            if (rayInteractor != null)
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                float trigger = 0f;
                if (device.isValid)
                    device.TryGetFeatureValue(CommonUsages.trigger, out trigger);
                bool pressed = trigger > 0.1f;
                rayInteractor.selectInput?.QueueManualState(pressed, trigger);
                rayInteractor.activateInput?.QueueManualState(pressed, trigger);
                rayInteractor.uiPressInput?.QueueManualState(pressed, trigger);
            }

            DriveJump();
        }

        private void DriveJump()
        {
            if (jumpProvider == null) return;

            // CommonUsages.secondaryButton is the B button on the right controller
            // (A is primaryButton) - same legacy XR read path as every other
            // button in this bridge, since the Input System<->OpenXR path never
            // delivers real state here (see class doc comment).
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool pressed = device.isValid && device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b) && b;

            if (pressed && !_wasJumpPressed && jumpProvider.CanJump())
                jumpProvider.Jump();

            _wasJumpPressed = pressed;
        }

        private static void Drive(XRNode node, Transform hand, XRBaseInputInteractor directInteractor,
            XRInputValueReader<Vector2> moveInput, XRInputValueReader<Vector2> turnInput)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            if (hand != null)
            {
                if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                    hand.localPosition = pos;
                if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                    hand.localRotation = rot;
            }

            device.TryGetFeatureValue(CommonUsages.grip, out float grip);
            device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
            device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);

            if (directInteractor != null)
            {
                directInteractor.selectInput?.QueueManualState(grip > 0.1f, grip);
                directInteractor.activateInput?.QueueManualState(trigger > 0.1f, trigger);
            }

            if (moveInput != null) moveInput.manualValue = axis;
            if (turnInput != null) turnInput.manualValue = axis;
        }
    }
}
