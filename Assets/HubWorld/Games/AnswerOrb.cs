using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A grabbable, number-labeled sphere used by MathBlockTossGame's
    /// "grab and sort" mechanic - the physical alternative to just pointing a
    /// ray at a static block and clicking.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    public class AnswerOrb : MonoBehaviour
    {
        public int Value { get; private set; }

        public void Init(int value, Color color, TMP_FontAsset font = null)
        {
            Value = value;

            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.material.color = color;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(transform, false);
            labelGO.transform.localPosition = Vector3.zero;
            labelGO.transform.localScale = Vector3.one * 3f;
            var label = labelGO.AddComponent<TextMeshPro>();
            label.text = value.ToString();
            label.fontSize = 8;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            if (font != null) label.font = font;
        }
    }
}
