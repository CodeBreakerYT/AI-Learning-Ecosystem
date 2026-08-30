using System.Collections.Generic;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A trigger zone that sums the value of every WeightStone currently
    /// resting inside it - the "pan" half of the Equation Escape Room's
    /// balance scale. Fires onSumChanged live as stones are dropped in or
    /// picked back out, so the beam can tilt continuously as real-time
    /// physical feedback instead of a single accept/reject check.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class WeightPan : MonoBehaviour
    {
        public System.Action<int> onSumChanged;
        public int CurrentSum { get; private set; }

        private readonly HashSet<WeightStone> _stones = new HashSet<WeightStone>();

        private void OnTriggerEnter(Collider other)
        {
            var stone = other.GetComponentInParent<WeightStone>();
            if (stone == null || !_stones.Add(stone)) return;
            Recalculate();
        }

        private void OnTriggerExit(Collider other)
        {
            var stone = other.GetComponentInParent<WeightStone>();
            if (stone == null || !_stones.Remove(stone)) return;
            Recalculate();
        }

        private void Recalculate()
        {
            CurrentSum = 0;
            foreach (var stone in _stones)
                if (stone != null) CurrentSum += stone.Value;
            onSumChanged?.Invoke(CurrentSum);
        }
    }
}
