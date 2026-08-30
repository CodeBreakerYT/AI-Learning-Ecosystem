using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>Trigger volume around the flask - forwards a dropped-in test tube to the owning game.</summary>
    public class FlaskZone : MonoBehaviour
    {
        public ChemicalReactionLabGame owner;

        private void OnTriggerStay(Collider other)
        {
            var tube = other.GetComponentInParent<TestTube>();
            if (tube != null) tube.NotifyInFlask(owner);
        }
    }
}
