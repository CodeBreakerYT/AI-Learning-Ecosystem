using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A selectable prop used by the World minigames (answer blocks, atoms, power
    /// buttons): picks the first Renderer it finds for color feedback, and fires
    /// onSelected via an XR ray/poke select - the same select-only interaction
    /// this project's XR rig is already proven to support, rather than a
    /// hand-tracked grab/throw.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class AnswerTarget : MonoBehaviour
    {
        public Action onSelected;

        private Renderer _renderer;
        private XRSimpleInteractable _interactable;
        private Color _baseColor;
        private Coroutine _flashRoutine;

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null) _baseColor = _renderer.material.color;

            _interactable = GetComponent<XRSimpleInteractable>();
            _interactable.selectEntered.AddListener(_ => onSelected?.Invoke());
        }

        public void Flash(Color color, float duration = 0.4f)
        {
            if (_renderer == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(color, duration));
        }

        public void Flash(Color color, bool correct, float duration = 0.4f)
        {
            Flash(color, duration);
            PlayFeedbackVfx(correct);
        }

        // Looked up per-call rather than cached - MinigameEnvironment.correctVfxPrefab/
        // wrongVfxPrefab (Hovl Studio Magic Effects pack) are the only VFX source this
        // needs, and doing this once per selection is cheap.
        public void PlayFeedbackVfx(bool correct)
        {
            var env = FindFirstObjectByType<MinigameEnvironment>();
            if (env == null) return;
            var prefab = correct ? env.correctVfxPrefab : env.wrongVfxPrefab;
            if (prefab == null) return;
            var vfx = Instantiate(prefab, transform.position + Vector3.up * 0.35f, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * env.feedbackVfxScale;
            Destroy(vfx, 4f);
        }

        public void SetColor(Color color)
        {
            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            if (_renderer != null) _renderer.material.color = color;
        }

        public void SetInteractable(bool value)
        {
            if (_interactable != null) _interactable.enabled = value;
        }

        private IEnumerator FlashRoutine(Color color, float duration)
        {
            _renderer.material.color = color;
            yield return new WaitForSeconds(duration);
            _renderer.material.color = _baseColor;
            _flashRoutine = null;
        }
    }
}
