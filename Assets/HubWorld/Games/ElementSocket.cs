using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AILearningEcosystem.Hub
{
    /// <summary>Confirmation socket - accepts an ElementBlock and checks its symbol against the current target.</summary>
    public class ElementSocket : MonoBehaviour
    {
        public string TargetSymbol;
        public System.Action<ElementBlock, bool> onElementPlaced;

        private XRSocketInteractor _socket;

        private void Awake()
        {
            _socket = GetComponent<XRSocketInteractor>();
            _socket.selectEntered.AddListener(OnEntered);
        }

        private void OnEntered(SelectEnterEventArgs args)
        {
            var block = args.interactableObject.transform.GetComponent<ElementBlock>();
            if (block == null) return;

            string symbol = block.Data.Symbol;
            bool correct = symbol == TargetSymbol;
            onElementPlaced?.Invoke(block, correct);

            if (!correct)
            {
                var rb = block.GetComponent<Rigidbody>();
                _socket.interactionManager.SelectExit(_socket, args.interactableObject);
                if (rb != null) rb.AddForce((block.transform.position - transform.position + Vector3.up * 0.3f).normalized * 1.5f, ForceMode.Impulse);
            }
        }
    }
}
