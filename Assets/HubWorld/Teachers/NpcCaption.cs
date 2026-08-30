using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Guaranteed-visible floating subtitle for whatever NPC it's attached to.
    /// ConvaiGuide.Speak drives this independently of whether Convai's own
    /// voice pipeline actually produces audio (wrong/expired API key, network
    /// hiccup, TriggerSpeech routed through the LLM instead of spoken
    /// verbatim, etc.) - narration text should never depend on that pipeline
    /// working correctly to be readable.
    ///
    /// [ExecuteAlways] so BuildBubble() runs the instant AddComponent adds
    /// this in the Editor, not only once Play mode starts. Without it, a
    /// caller that configures the bubble right after adding it in edit mode
    /// (e.g. PhoboNewtonsGuide's [ExecuteAlways] build) was calling
    /// SetPanelSprite/SetSize/SetHeightOffset/SetBillboard against a
    /// not-yet-built canvas - every one of them silently no-opped, and the
    /// caption only ever got BuildBubble()'s bare defaults once Play mode
    /// finally triggered Awake() for the first time (confirmed live: the
    /// custom sprite, size, and facing were all missing, replaced by the
    /// generic small dark panel).
    /// </summary>
    [ExecuteAlways]
    public class NpcCaption : MonoBehaviour
    {
        private GameObject _bubbleRoot;
        private TMP_Text _bubbleText;
        private Transform _canvasTransform;
        private Coroutine _hideRoutine;
        private bool _billboard = true;

        private void Awake()
        {
            // Build once, rediscover after - same dual-path every other
            // [ExecuteAlways] builder in this project uses. Awake() fires
            // again when Play mode starts even for a component that was
            // already built in edit mode (a fresh initialization pass, not
            // a no-op) - calling BuildBubble() unconditionally there created
            // a SECOND "Caption Canvas" with bare defaults and silently
            // re-pointed every private field at it, orphaning the first,
            // already-styled one. Confirmed live: the custom sprite/size/
            // facing all reverted to defaults specifically on entering Play
            // mode, even though rebuilding in the Editor beforehand showed
            // them correctly applied.
            var existing = transform.Find("Caption Canvas");
            if (existing == null) BuildBubble();
            else RediscoverBubble(existing);
        }

        private void RediscoverBubble(Transform canvasTransform)
        {
            _canvasTransform = canvasTransform;
            _bubbleRoot = canvasTransform.Find("Panel")?.gameObject;
            _bubbleText = _bubbleRoot != null ? _bubbleRoot.GetComponentInChildren<TMP_Text>(true) : null;
        }

        // The canvas only ever inherited the NPC's own facing, which reads
        // backwards/mirrored to the player whenever the NPC isn't pointed
        // straight at them (confirmed live - "the dialogue box above him is
        // inverted"). Billboard it toward the camera every frame instead, so
        // it's always readable regardless of which way the NPC is facing.
        // A caller can opt out via SetBillboard(false) - e.g. Phobo, whose
        // box is meant to turn WITH him and face the same direction he does
        // (matching VR-Mathipia's own dialogueCanvas, a plain child of the
        // robot with no per-frame rotation code at all) rather than always
        // spinning to face the player regardless of which way he's looking.
        private void LateUpdate()
        {
            if (!_billboard || _canvasTransform == null || !_bubbleRoot.activeSelf) return;
            var cam = Camera.main;
            if (cam == null) return;

            // Unity's WorldSpace Canvas renders its content readable to a
            // viewer standing on the canvas's local -Z side looking toward
            // +Z (confirmed against HubBootstrap's own canvas-flip fix - the
            // Hub canvas read correctly once its forward matched "pointing
            // away from the viewer standing behind it," not "pointing at the
            // viewer"). Pointing local +Z AT the camera - what the previous
            // fix attempt here did - puts the camera on the +Z side instead,
            // which is exactly backwards and renders the text mirrored.
            var toCam = cam.transform.position - _canvasTransform.position;
            if (toCam.sqrMagnitude < 0.0001f) return;
            _canvasTransform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
        }

        // duration <= 0 means "stay up until HideNow() is called" - for
        // callers that let the player pace their own reading (press a
        // button to continue) instead of a fixed timer.
        public void Show(string text, float duration = -1f)
        {
            if (string.IsNullOrEmpty(text)) return;

            // A fixed 6s duration cut off longer instructional lines before
            // they could be read (confirmed live - "no instructions nothing").
            // Scale with message length instead: roughly reading pace, with a
            // floor so short lines still stay up long enough to notice.
            if (duration < 0f)
                duration = Mathf.Clamp(text.Length * 0.07f, 5f, 14f);

            _bubbleText.text = text;
            _bubbleRoot.SetActive(true);
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            if (duration > 0f) _hideRoutine = StartCoroutine(HideAfter(duration));
        }

        /// <summary>Dismisses the bubble immediately - for a caller driving its own "press a button to continue" pacing instead of the automatic timer.</summary>
        public void HideNow()
        {
            if (_hideRoutine != null) { StopCoroutine(_hideRoutine); _hideRoutine = null; }
            _bubbleRoot.SetActive(false);
        }

        /// <summary>Overrides the default hard-coded height (tuned for a roughly human, feet-pivoted NPC) - a caller whose model is scaled or pivoted differently (e.g. Phobo, spawned at half scale with a center-ish pivot) can position the bubble to actually clear the top of ITS OWN model instead of ending up level with its chest.</summary>
        public void SetHeightOffset(float localY)
        {
            if (_canvasTransform != null) _canvasTransform.localPosition = new Vector3(0f, localY, 0f);
        }

        /// <summary>Turns off the per-frame camera billboard so the box instead rigidly follows its parent NPC's own rotation - matching VR-Mathipia's own dialogueCanvas, which is just a plain child of the robot transform with a fixed local rotation, turning with him instead of always facing the player. Defaults to a 180-degree yaw, not identity: a WorldSpace canvas reads correctly to a viewer standing on its local -Z side, and the NPC's own forward (local +Z, identity) points TOWARD whoever it's facing/addressing - the opposite direction from where that viewer actually is. Confirmed live with identity: the text rendered mirrored/backwards to a player Phobo was facing head-on.</summary>
        public void SetBillboard(bool billboard, Quaternion? localRotation = null)
        {
            _billboard = billboard;
            if (!billboard && _canvasTransform != null) _canvasTransform.localRotation = localRotation ?? Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>Swaps the plain colored panel for a real dialogue-box sprite (e.g. VR-Mathipia's own dialogueBox.png, a plain white rounded-rect outline) - Simple, not sliced, so the sprite's own shape isn't distorted. VR-Mathipia's own dialogueCanvas doesn't use the sprite at plain white either - it tints it with a warm orange (RawImage color (0.906, 0.511, 0.362) in its source scene), which `tint` defaults to here so callers get the same look without having to know that value themselves.</summary>
        public void SetPanelSprite(Sprite sprite, Color? tint = null)
        {
            if (sprite == null || _bubbleRoot == null) return;
            var image = _bubbleRoot.GetComponent<Image>();
            if (image == null) return;
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = tint ?? new Color(0.9056604f, 0.51077175f, 0.36169446f, 1f);
        }

        /// <summary>Overrides the default compact size (tuned as a small subtitle) - a caller reading full sentence-length dialogue, not a short caption, needs the box big enough to actually read comfortably (confirmed live - "dialogue box too small"). worldScale is the canvas's own transform scale; sizeDelta is in that canvas's local units, same convention Unity's own RectTransform uses.</summary>
        public void SetSize(Vector2 sizeDelta, float worldScale, int fontSize)
        {
            if (_canvasTransform == null || _bubbleRoot == null) return;
            _canvasTransform.GetComponent<RectTransform>().sizeDelta = sizeDelta;
            _canvasTransform.localScale = Vector3.one * worldScale;

            var panelRect = _bubbleRoot.GetComponent<RectTransform>();
            panelRect.sizeDelta = sizeDelta;

            var textRect = _bubbleText.GetComponent<RectTransform>();
            textRect.sizeDelta = sizeDelta - new Vector2(40f, 20f);
            _bubbleText.fontSize = fontSize;
        }

        private IEnumerator HideAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            _bubbleRoot.SetActive(false);
        }

        private void BuildBubble()
        {
            var canvasGO = new GameObject("Caption Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            _canvasTransform = canvasGO.transform;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360, 120);
            canvasGO.transform.localScale = Vector3.one * 0.0018f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(360, 120), PanelColor);
            _bubbleText = CreateText(panel.transform, "", 18, TextColor, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(320, 100));

            _bubbleRoot = panel.gameObject;
            _bubbleRoot.SetActive(false);
        }
    }
}
