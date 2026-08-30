using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>A grabbable test tube. Carry it to the flask and let go inside the trigger zone to pour it.</summary>
    public class TestTube : MonoBehaviour
    {
        private int _slot;
        private ChemicalReactionLabGame _lab;
        private XRGrabInteractable _grab;
        private bool _consumed;

        public void Configure(ChemicalReactionLabGame lab, int slot)
        {
            _lab = lab;
            _slot = slot;
            _grab = GetComponent<XRGrabInteractable>();
        }

        public void NotifyInFlask(ChemicalReactionLabGame lab)
        {
            if (_consumed) return;
            if (_grab != null && _grab.isSelected) return;

            _consumed = true;
            lab.OnTubePoured(_slot);
            Destroy(gameObject);
        }
    }
}
