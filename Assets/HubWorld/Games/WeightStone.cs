using TMPro;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A single grabbable weight denomination for the Equation Escape Room's
    /// balance scale (see WeightPan.cs). Deliberately just a labeled mass -
    /// no "correct/wrong" concept lives on the stone itself, only on the
    /// combined sum a pan reads. Several stones of a few denominations
    /// (1/2/5/10) are scattered around the room so reaching a target number
    /// is a real combination problem, not a single-object lookup.
    /// </summary>
    public class WeightStone : MonoBehaviour
    {
        public int Value { get; private set; }

        public void Init(int value, Color color)
        {
            Value = value;

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", color * 0.5f);
            }

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            labelGO.transform.localScale = Vector3.one * 0.15f;
            var label = labelGO.AddComponent<TextMeshPro>();
            label.text = value.ToString();
            label.fontSize = 5;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
        }
    }
}
