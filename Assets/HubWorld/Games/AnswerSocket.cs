using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A socket the player drops AnswerOrb spheres into. Wrong orbs are
    /// rejected (kicked back out) rather than accepted, so guessing wrong
    /// doesn't end the round - matches MathBlockTossGame's old "try again"
    /// forgiving-retry feel, just with a grab-and-place mechanic instead of
    /// a point-and-click one.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class AnswerSocket : MonoBehaviour
    {
        public int TargetValue { get; set; }
        public Action<AnswerOrb, bool> onOrbPlaced;

        private XRSocketInteractor _socket;

        private void Awake()
        {
            _socket = GetComponent<XRSocketInteractor>();
            _socket.selectEntered.AddListener(OnOrbEntered);
        }

        private void OnOrbEntered(SelectEnterEventArgs args)
        {
            var orb = args.interactableObject.transform.GetComponent<AnswerOrb>();
            if (orb == null) return;

            bool correct = orb.Value == TargetValue;
            onOrbPlaced?.Invoke(orb, correct);

            if (!correct)
            {
                // Kick it back out so the player can try a different orb -
                // XRSocketInteractor doesn't reject on its own.
                var rb = orb.GetComponent<Rigidbody>();
                _socket.interactionManager.SelectExit(_socket, args.interactableObject);
                if (rb != null) rb.AddForce((orb.transform.position - transform.position + Vector3.up * 0.3f).normalized * 1.5f, ForceMode.Impulse);
            }
        }
    }
}
