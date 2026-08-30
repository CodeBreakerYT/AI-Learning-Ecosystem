using Convai.Scripts.Runtime.Core;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// VR-native "hold trigger to talk" for a teacher's ConvaiNPC. Two things
    /// this project never wired up otherwise:
    /// 1. Input: Convai's own path (ConvaiPlayerInteractionManager ->
    ///    ConvaiInputManager) is keyboard-driven ("hold T") with no VR binding
    ///    and no ConvaiInputManager singleton placed in any scene here - reads
    ///    the legacy UnityEngine.XR.InputDevices API directly instead
    ///    (consistent with LegacyXRInputBridge - the Input System-side XR
    ///    controls don't deliver state on this project's Unity/Input
    ///    System/OpenXR combination).
    /// 2. Focus: ConvaiNPC.isCharacterActive only ever becomes true when
    ///    ConvaiNPCManager.SetActiveConvaiNPC(npc) is called - normally done by
    ///    a gaze/crosshair raycast system (ConvaiCrosshairHandler, part of the
    ///    "Convai Essentials - XR" prefab this project deliberately never
    ///    includes). Nothing else in this project calls it, so isCharacterActive
    ///    stayed permanently false and every NPC was silently unreachable -
    ///    claim focus here instead, in-range and holding trigger.
    /// </summary>
    public class TeacherPushToTalk : MonoBehaviour
    {
        public ConvaiNPC npc;
        public float talkRadius = 3f;

        private Transform _player;
        private bool _wasPressed;

        private void Awake()
        {
            if (npc == null) npc = GetComponent<ConvaiNPC>();
        }

        private void Start()
        {
            _player = Camera.main != null ? Camera.main.transform : null;
        }

        private void Update()
        {
            if (npc == null) return;

            bool inRange = _player == null || Vector3.Distance(_player.position, transform.position) <= talkRadius;

            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool vrTriggerHeld = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool t) && t;

            // Desktop/Editor fallback - the VR trigger path above only ever
            // reads a real value from an actual headset controller (device.isValid
            // is false with nothing physically connected), so testing at a
            // keyboard without a headset could never trigger voice input at all,
            // no matter how correct the Convai backend was. Hold T to talk,
            // matching Convai's own original (non-VR) input convention.
            bool keyboardHeld = Keyboard.current != null && Keyboard.current.tKey.isPressed;

            bool pressed = inRange && (vrTriggerHeld || keyboardHeld);

            if (pressed && !_wasPressed)
            {
                // ConvaiNPC.Start() sets its private gRPC client asynchronously
                // relative to activation (Unity defers Start() to the next frame
                // after an object's first SetActive(true), which is exactly how
                // MinigameTeacher builds this NPC). Pressing grip inside that
                // window used to NRE deep inside StartRecordAudio because the
                // client was still null - GetClient() is Convai's own readiness
                // check, so bail out silently and let the next press succeed.
                if (npc.GetClient() == null) { _wasPressed = pressed; return; }

                // ConvaiGRPCAPI has been observed getting destroyed unexpectedly
                // during this project's Editor testing (root cause not pinned down -
                // possibly stale state from a mid-session Editor hang/domain-reload
                // recovery). Recreate defensively right before every interaction
                // instead of relying on the one-time Start() call.
                ConvaiSingletons.EnsureGRPCAPI();

                // ConvaiNPCManager.SetActiveConvaiNPC only runs its body (which is what
                // recreates ConvaiGRPCAPI's cancellation token) when the active NPC
                // actually CHANGES - a no-op if this NPC is already marked active, even
                // though Convai's own internals dispose that token after every completed
                // call. Force a real change through null first so the token always gets
                // rebuilt, or every press after the first silently NREs.
                if (ConvaiNPCManager.Instance != null)
                {
                    ConvaiNPCManager.Instance.SetActiveConvaiNPC(null);
                    ConvaiNPCManager.Instance.SetActiveConvaiNPC(npc);
                }
                npc.StartListening();
            }
            else if (!pressed && _wasPressed)
            {
                npc.StopListening();
            }
            _wasPressed = pressed;
        }
    }
}
