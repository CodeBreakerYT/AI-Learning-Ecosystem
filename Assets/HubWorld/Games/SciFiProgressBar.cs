using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A HUD-style progress readout: a track of individual diamond "nodes"
    /// (one per round) that light up as each is cleared, a glowing fill bar
    /// running underneath them, a big animated count-up percentage, and an
    /// angular chamfered-corner frame instead of a plain rounded bar. Nodes
    /// read at a glance ("3 of 9 done, working on the 4th") in a way a bare
    /// fill bar can't, which is the actual point of a discrete round-by-
    /// round lesson rather than a continuous loading-style progress value.
    /// Built as real Canvas/Image/TMP content (same as every other
    /// world-space HUD in this project) so it renders in VR, unlike a
    /// Screen Space Overlay canvas.
    /// </summary>
    public class SciFiProgressBar : MonoBehaviour
    {
        private static readonly Color BackingColor = new Color(0.02f, 0.05f, 0.06f, 0.92f);
        private static readonly Color FrameColor = new Color(0.25f, 0.85f, 0.95f, 0.9f);
        private static readonly Color GlowDim = new Color(0.25f, 0.85f, 0.95f, 0.35f);
        private static readonly Color GlowCore = new Color(0.55f, 1f, 1f, 1f);
        private static readonly Color TickColor = new Color(0.6f, 0.95f, 1f, 0.55f);
        private static readonly Color ScanColor = new Color(1f, 1f, 1f, 0.25f);
        private static readonly Color NodeLockedColor = new Color(0.4f, 0.55f, 0.6f, 0.35f);
        private static readonly Color NodeDoneColor = new Color(0.55f, 1f, 1f, 1f);
        private static readonly Color NodeCurrentColor = new Color(1f, 0.85f, 0.3f, 1f);

        private RectTransform _fillDimRect;
        private RectTransform _fillCoreRect;
        private RectTransform _scanRect;
        private TMP_Text _label;
        private TMP_Text _percentText;
        private RectTransform[] _nodeCores;
        private RectTransform[] _nodeHalos;
        private float _barWidth;
        private float _targetFraction;
        private float _currentFraction;
        private float _scanT;
        private int _totalUnits = 1;
        private int _completedUnits;
        private float _displayedPercent;

        // Build() only ever runs once, from ArcheryProjectileGame's own
        // BuildHud() - but this whole project's [ExecuteAlways] scripts get
        // rebuilt in the Editor, then hit a domain reload the moment real
        // Play mode starts. A domain reload resets every plain (non-
        // serialized) private field on every component back to its default,
        // even though the GameObject hierarchy itself survives - so without
        // this, every cached reference here was silently null the instant
        // Play began, and SetProgress()'s own `if (x != null)` guards just
        // as silently no-opped forever after (confirmed live on the previous,
        // simpler version of this bar - label/fill never updated, no console
        // error to point at why). Awake() re-finds the same named children
        // Build() created, the same rediscover pattern every other
        // [ExecuteAlways] builder in this project already uses.
        private void Awake()
        {
            if (_label != null) return; // already wired (mid-session AddComponent, not a reload)
            _fillDimRect = transform.Find("Fill Glow") as RectTransform;
            _fillCoreRect = transform.Find("Fill Core") as RectTransform;
            _scanRect = _fillCoreRect != null ? _fillCoreRect.Find("Scanline") as RectTransform : null;
            var labelT = transform.Find("Label");
            _label = labelT != null ? labelT.GetComponent<TMP_Text>() : null;
            var percentT = transform.Find("Percent");
            _percentText = percentT != null ? percentT.GetComponent<TMP_Text>() : null;
            var rect = transform as RectTransform;
            _barWidth = rect != null ? rect.sizeDelta.x - 12f : _barWidth;

            var nodesRoot = transform.Find("Nodes");
            if (nodesRoot != null)
            {
                var count = nodesRoot.childCount;
                _nodeCores = new RectTransform[count];
                _nodeHalos = new RectTransform[count];
                for (var i = 0; i < count; i++)
                {
                    var node = nodesRoot.GetChild(i);
                    _nodeHalos[i] = node.Find("Halo") as RectTransform;
                    _nodeCores[i] = node.Find("Core") as RectTransform;
                }
            }
        }

        /// <summary>Builds the bar as a child of parent at anchoredPos, sized barSize, with one lit-up node per unit (round) and a slightly wider gap every groupSize nodes (stage boundaries). backgroundArt, when given, is drawn full-bleed behind the frame as ambient sci-fi texture (a real imported HUD sprite sheet, not a hand-rolled shape) instead of a flat procedural rectangle.</summary>
        public static SciFiProgressBar Build(Transform parent, Vector2 anchoredPos, Vector2 barSize, int totalUnits, int groupSize, Texture2D backgroundArt = null)
        {
            totalUnits = Mathf.Max(1, totalUnits);
            groupSize = Mathf.Max(1, groupSize);

            var root = new GameObject("SciFi Progress Bar", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = barSize;
            rootRect.anchoredPosition = anchoredPos;

            var bar = root.AddComponent<SciFiProgressBar>();
            bar._barWidth = barSize.x - 12f;
            bar._totalUnits = totalUnits;

            var backingGO = new GameObject("Backing", typeof(RectTransform));
            backingGO.transform.SetParent(root.transform, false);
            Stretch(backingGO.GetComponent<RectTransform>());
            var backingImg = backingGO.AddComponent<Image>();
            backingImg.sprite = ChamferSprite;
            backingImg.type = Image.Type.Sliced;
            backingImg.color = BackingColor;

            if (backgroundArt != null)
            {
                // Real sci-fi HUD art (a dense sheet of circular gauges, bar
                // charts, hex-panel framing) laid full-bleed behind the
                // functional overlay, dimmed and tinted cyan so it reads as
                // ambient greebling rather than competing, mismatched
                // instrumentation - the actual progress is still told by our
                // own fill/nodes/percentage on top, this is texture, not data.
                // RectMask2D (a plain axis-aligned clip) keeps it inside the
                // bar's own bounds - it won't match the Backing/Frame's
                // angular cut corners exactly, but at this low an alpha
                // behind an opaque frame border, that's not worth the extra
                // complexity of clipping to the angular shape itself.
                var artClipGO = new GameObject("Background Art", typeof(RectTransform));
                artClipGO.transform.SetParent(root.transform, false);
                Stretch(artClipGO.GetComponent<RectTransform>());
                artClipGO.AddComponent<RectMask2D>();
                var artImgGO = new GameObject("Art Image", typeof(RectTransform));
                artImgGO.transform.SetParent(artClipGO.transform, false);
                Stretch(artImgGO.GetComponent<RectTransform>());
                var artImg = artImgGO.AddComponent<RawImage>();
                artImg.texture = backgroundArt;
                artImg.color = new Color(GlowCore.r, GlowCore.g, GlowCore.b, 0.22f);
            }

            var frameGO = new GameObject("Frame", typeof(RectTransform));
            frameGO.transform.SetParent(root.transform, false);
            Stretch(frameGO.GetComponent<RectTransform>());
            var frameImg = frameGO.AddComponent<Image>();
            frameImg.sprite = ChamferOutlineSprite;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = FrameColor;

            // The track/fill bar sits in the LOWER half of the frame - the
            // upper half is reserved for the node row, so nodes read as
            // sitting "on" the energy track rather than overlapping the text.
            const float trackHeight = 12f;
            const float trackYOffset = -10f;

            var fillDimGO = new GameObject("Fill Glow", typeof(RectTransform));
            fillDimGO.transform.SetParent(root.transform, false);
            bar._fillDimRect = fillDimGO.GetComponent<RectTransform>();
            bar._fillDimRect.anchorMin = new Vector2(0f, 0.5f);
            bar._fillDimRect.anchorMax = new Vector2(0f, 0.5f);
            bar._fillDimRect.pivot = new Vector2(0f, 0.5f);
            bar._fillDimRect.anchoredPosition = new Vector2(6f, trackYOffset);
            bar._fillDimRect.sizeDelta = new Vector2(0f, trackHeight + 6f);
            var fillDimImg = fillDimGO.AddComponent<Image>();
            fillDimImg.color = GlowDim;

            var fillCoreGO = new GameObject("Fill Core", typeof(RectTransform));
            fillCoreGO.transform.SetParent(root.transform, false);
            bar._fillCoreRect = fillCoreGO.GetComponent<RectTransform>();
            bar._fillCoreRect.anchorMin = new Vector2(0f, 0.5f);
            bar._fillCoreRect.anchorMax = new Vector2(0f, 0.5f);
            bar._fillCoreRect.pivot = new Vector2(0f, 0.5f);
            bar._fillCoreRect.anchoredPosition = new Vector2(6f, trackYOffset);
            bar._fillCoreRect.sizeDelta = new Vector2(0f, trackHeight - 4f);
            var fillCoreImg = fillCoreGO.AddComponent<Image>();
            fillCoreImg.color = GlowCore;

            var scanGO = new GameObject("Scanline", typeof(RectTransform));
            scanGO.transform.SetParent(fillCoreGO.transform, false);
            bar._scanRect = scanGO.GetComponent<RectTransform>();
            bar._scanRect.anchorMin = new Vector2(0f, 0f);
            bar._scanRect.anchorMax = new Vector2(0f, 1f);
            bar._scanRect.pivot = new Vector2(0.5f, 0.5f);
            bar._scanRect.sizeDelta = new Vector2(trackHeight * 0.7f, 0f);
            var scanImg = scanGO.AddComponent<Image>();
            scanImg.color = ScanColor;

            // Group-boundary ticks - taller/brighter than the node row so
            // stage boundaries still read even with nodes on top of them.
            for (var i = groupSize; i < totalUnits; i += groupSize)
            {
                var t = i / (float)totalUnits;
                var tickGO = new GameObject("Tick", typeof(RectTransform));
                tickGO.transform.SetParent(root.transform, false);
                var tickRect = tickGO.GetComponent<RectTransform>();
                tickRect.anchorMin = new Vector2(t, 0f);
                tickRect.anchorMax = new Vector2(t, 1f);
                tickRect.pivot = new Vector2(0.5f, 0.5f);
                tickRect.anchoredPosition = Vector2.zero;
                tickRect.sizeDelta = new Vector2(2f, barSize.y + 4f);
                var tickImg = tickGO.AddComponent<Image>();
                tickImg.color = TickColor;
            }

            // One diamond node per round, evenly spaced along the track,
            // each a dim halo behind a smaller bright core - lit up in
            // SetProgress as rounds are cleared, pulsing on whichever one is
            // currently in progress.
            var nodesRoot = new GameObject("Nodes", typeof(RectTransform));
            nodesRoot.transform.SetParent(root.transform, false);
            Stretch(nodesRoot.GetComponent<RectTransform>());
            bar._nodeCores = new RectTransform[totalUnits];
            bar._nodeHalos = new RectTransform[totalUnits];
            for (var i = 0; i < totalUnits; i++)
            {
                var t = (i + 0.5f) / totalUnits;
                var nodeGO = new GameObject($"Node {i}", typeof(RectTransform));
                nodeGO.transform.SetParent(nodesRoot.transform, false);
                var nodeRect = nodeGO.GetComponent<RectTransform>();
                nodeRect.anchorMin = new Vector2(t, 0.5f);
                nodeRect.anchorMax = new Vector2(t, 0.5f);
                nodeRect.pivot = new Vector2(0.5f, 0.5f);
                nodeRect.anchoredPosition = new Vector2(0f, trackYOffset);
                nodeRect.sizeDelta = Vector2.zero;

                var haloGO = new GameObject("Halo", typeof(RectTransform));
                haloGO.transform.SetParent(nodeGO.transform, false);
                var haloRect = haloGO.GetComponent<RectTransform>();
                haloRect.sizeDelta = new Vector2(20f, 20f);
                haloRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var haloImg = haloGO.AddComponent<Image>();
                haloImg.color = NodeLockedColor;

                var coreGO = new GameObject("Core", typeof(RectTransform));
                coreGO.transform.SetParent(nodeGO.transform, false);
                var coreRect = coreGO.GetComponent<RectTransform>();
                coreRect.sizeDelta = new Vector2(11f, 11f);
                coreRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var coreImg = coreGO.AddComponent<Image>();
                coreImg.color = new Color(NodeLockedColor.r, NodeLockedColor.g, NodeLockedColor.b, 0.7f);

                bar._nodeHalos[i] = haloRect;
                bar._nodeCores[i] = coreRect;
            }

            // Stage/round text, upper-left.
            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(root.transform, false);
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(0.7f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = new Vector2(10f, -4f);
            labelRect.sizeDelta = new Vector2(0f, 20f);
            bar._label = labelGO.AddComponent<TextMeshProUGUI>();
            bar._label.fontStyle = FontStyles.Bold;
            bar._label.color = GlowCore;
            bar._label.alignment = TextAlignmentOptions.Left;
            bar._label.characterSpacing = 2f;
            bar._label.text = "";
            // Auto-sized, not a fixed 15pt - "STAGE 3/3" and "COMPLETE" are
            // different lengths in the same fixed-width box, and a fixed
            // size that fit one could clip or overflow the other.
            // Floor raised from 8 - confirmed live, text that small on a
            // canvas this world-scale reads as genuinely blurry rather than
            // just compact.
            bar._label.enableAutoSizing = true;
            bar._label.fontSizeMin = 12;
            bar._label.fontSizeMax = 15;

            // Big animated count-up percentage, upper-right - the
            // "hero number" every sci-fi HUD readout leans on.
            var percentGO = new GameObject("Percent", typeof(RectTransform));
            percentGO.transform.SetParent(root.transform, false);
            var percentRect = percentGO.GetComponent<RectTransform>();
            percentRect.anchorMin = new Vector2(0.7f, 1f);
            percentRect.anchorMax = new Vector2(1f, 1f);
            percentRect.pivot = new Vector2(1f, 1f);
            percentRect.anchoredPosition = new Vector2(-8f, -2f);
            percentRect.sizeDelta = new Vector2(0f, 24f);
            bar._percentText = percentGO.AddComponent<TextMeshProUGUI>();
            bar._percentText.fontStyle = FontStyles.Bold;
            bar._percentText.color = NodeCurrentColor;
            bar._percentText.alignment = TextAlignmentOptions.Right;
            bar._percentText.text = "0%";
            bar._percentText.enableAutoSizing = true;
            bar._percentText.fontSizeMin = 12;
            bar._percentText.fontSizeMax = 20;

            return bar;
        }

        /// <summary>completed/total drive both the node lighting and the animated fill/percentage; label is the free-text readout shown top-left (e.g. "STAGE 2/3").</summary>
        public void SetProgress(int completed, int total, string label)
        {
            _totalUnits = Mathf.Max(1, total);
            _completedUnits = Mathf.Clamp(completed, 0, _totalUnits);
            _targetFraction = (float)_completedUnits / _totalUnits;
            if (_label != null) _label.text = label;
            RefreshNodes();
        }

        private void RefreshNodes()
        {
            if (_nodeCores == null) return;
            for (var i = 0; i < _nodeCores.Length; i++)
            {
                if (_nodeCores[i] == null || _nodeHalos[i] == null) continue;
                var coreImg = _nodeCores[i].GetComponent<Image>();
                var haloImg = _nodeHalos[i].GetComponent<Image>();
                if (i < _completedUnits)
                {
                    coreImg.color = NodeDoneColor;
                    haloImg.color = GlowDim;
                }
                else if (i == _completedUnits)
                {
                    coreImg.color = NodeCurrentColor;
                    haloImg.color = new Color(NodeCurrentColor.r, NodeCurrentColor.g, NodeCurrentColor.b, 0.3f);
                }
                else
                {
                    coreImg.color = new Color(NodeLockedColor.r, NodeLockedColor.g, NodeLockedColor.b, 0.7f);
                    haloImg.color = NodeLockedColor;
                }
            }
        }

        private void Update()
        {
            _currentFraction = Mathf.MoveTowards(_currentFraction, _targetFraction, Time.deltaTime * 0.8f);
            RefreshFillWidth();

            // Count-up percentage, not a snap - matches the fill bar's own
            // gradual animation instead of jumping straight to the new number.
            var targetPercent = _targetFraction * 100f;
            _displayedPercent = Mathf.MoveTowards(_displayedPercent, targetPercent, Time.deltaTime * 80f);
            if (_percentText != null) _percentText.text = $"{Mathf.RoundToInt(_displayedPercent)}%";

            _scanT += Time.deltaTime / 2.2f;
            if (_scanT > 1f) _scanT -= 1f;
            if (_scanRect != null && _fillCoreRect.sizeDelta.x > 1f)
            {
                var x = Mathf.Lerp(0f, _fillCoreRect.sizeDelta.x, _scanT);
                _scanRect.anchoredPosition = new Vector2(x, 0f);
            }

            var dimImg = _fillDimRect != null ? _fillDimRect.GetComponent<Image>() : null;
            if (dimImg != null)
            {
                var pulse = 0.28f + Mathf.Sin(Time.time * 2.2f) * 0.08f;
                var c = GlowDim;
                c.a = pulse;
                dimImg.color = c;
            }

            // The "current" node breathes (scale pulse) so it's obvious at a
            // glance which round is actually in progress, not just which
            // ones are already done.
            if (_nodeHalos != null && _completedUnits < _nodeHalos.Length && _nodeHalos[_completedUnits] != null)
            {
                var scale = 1f + Mathf.Sin(Time.time * 4f) * 0.18f;
                _nodeHalos[_completedUnits].localScale = new Vector3(scale, scale, 1f);
            }
        }

        private void RefreshFillWidth()
        {
            var w = _barWidth * _currentFraction;
            if (_fillDimRect != null) _fillDimRect.sizeDelta = new Vector2(w, _fillDimRect.sizeDelta.y);
            if (_fillCoreRect != null) _fillCoreRect.sizeDelta = new Vector2(Mathf.Max(0f, w - 4f), _fillCoreRect.sizeDelta.y);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ---- Procedural angular ("chamfered corner") sprites ----

        private static Sprite _chamferSprite;
        private static Sprite _chamferOutlineSprite;

        // Not `_x ??= Build()` - see CanvasUIHelpers.SciFiChamferSprite's
        // comment for why: `??=` bypasses Unity's overloaded "is this
        // destroyed" null check, so a sprite killed by a scene-transition's
        // Resources.UnloadUnusedAssets() (nothing in a loaded scene
        // references a purely code-generated asset) never gets rebuilt.
        private static Sprite ChamferSprite
        {
            get
            {
                if (_chamferSprite == null) _chamferSprite = BuildChamferSprite(filled: true);
                return _chamferSprite;
            }
        }

        private static Sprite ChamferOutlineSprite
        {
            get
            {
                if (_chamferOutlineSprite == null) _chamferOutlineSprite = BuildChamferSprite(filled: false);
                return _chamferOutlineSprite;
            }
        }

        private static Sprite BuildChamferSprite(bool filled)
        {
            const int size = 64;
            const int cut = 14;
            const int border = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var insideChamfer = true;
                    // Diagonal cut on the top-left and bottom-right corners only -
                    // an asymmetric angular cut reads more "tech panel" than a
                    // uniform cut on all four corners would.
                    if (x + y < cut) insideChamfer = false;
                    if ((size - 1 - x) + (size - 1 - y) < cut) insideChamfer = false;

                    var alpha = insideChamfer ? 255 : 0;
                    if (insideChamfer && !filled)
                    {
                        var nearEdge = x < border || y < border || x >= size - border || y >= size - border;
                        var nearCutTL = x + y < cut + border;
                        var nearCutBR = (size - 1 - x) + (size - 1 - y) < cut + border;
                        if (!(nearEdge || nearCutTL || nearCutBR)) alpha = 0;
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)alpha);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(cut, cut, cut, cut));
        }
    }
}
