using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Shared runtime Canvas/TextMeshPro UI builders, used by both StartAuthBridge
    /// (StartScene's login/register screen) and HubBootstrap (World's guide panel).
    /// Approximates EcoLearn's CSS look (rounded corners via a procedurally-generated
    /// sprite, a blue-to-purple gradient for primary buttons/headings) since Canvas/TMP
    /// can't render real CSS - see the project README for the fidelity trade-offs and
    /// where to drop in real Space Grotesk/Inter TMP font assets for closer typography.
    /// </summary>
    public static class CanvasUIHelpers
    {
        public static readonly Color PanelColor = new Color(0.071f, 0.086f, 0.129f, 0.85f);
        public static readonly Color BgColor = new Color(0.043f, 0.055f, 0.086f, 0.95f);
        public static readonly Color AccentColor = new Color(0.357f, 0.549f, 1f, 1f);
        public static readonly Color Accent2Color = new Color(0.545f, 0.361f, 0.965f, 1f);
        public static readonly Color TextColor = new Color(0.945f, 0.953f, 0.973f, 1f);
        public static readonly Color TextDimColor = new Color(0.643f, 0.671f, 0.753f, 1f);
        public static readonly Color GhostButtonColor = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color PrimaryTextColor = new Color(0.043f, 0.055f, 0.086f, 1f);

        private static Sprite _roundedSprite;
        private static Sprite _roundedGradientSprite;

        public static Sprite RoundedSprite
        {
            get
            {
                if (_roundedSprite == null) _roundedSprite = CreateRoundedSprite(64, 16, null);
                return _roundedSprite;
            }
        }

        public static Sprite RoundedGradientSprite
        {
            get
            {
                if (_roundedGradientSprite == null) _roundedGradientSprite = CreateRoundedSprite(64, 16, (AccentColor, Accent2Color));
                return _roundedGradientSprite;
            }
        }

        private static Sprite CreateRoundedSprite(int size, int radius, (Color left, Color right)? gradient)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var inside = true;
                    if (x < radius && y < radius)
                        inside = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) <= radius;
                    else if (x >= size - radius && y < radius)
                        inside = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, radius)) <= radius;
                    else if (x < radius && y >= size - radius)
                        inside = Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius)) <= radius;
                    else if (x >= size - radius && y >= size - radius)
                        inside = Vector2.Distance(new Vector2(x, y), new Vector2(size - radius, size - radius)) <= radius;

                    Color32 rgb;
                    if (gradient.HasValue)
                    {
                        var t = x / (float)(size - 1);
                        rgb = Color.Lerp(gradient.Value.left, gradient.Value.right, t);
                    }
                    else
                    {
                        rgb = new Color32(255, 255, 255, 255);
                    }

                    pixels[y * size + x] = inside ? new Color32(rgb.r, rgb.g, rgb.b, 255) : new Color32(rgb.r, rgb.g, rgb.b, 0);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        /// <summary>
        /// Makes sure a scene has an EventSystem with an InputSystemUIInputModule, so
        /// TrackedDeviceGraphicRaycaster-based Canvas UI (buttons, input fields) can
        /// actually receive clicks/selects. The minigame scenes under
        /// Assets/PlatformScenes/{Math,Physics,Chemistry}/ use Lumora's own
        /// PlayerPhysics rig, which doesn't bundle an EventSystem of its own (unlike
        /// the VR-template rig in World.unity), so every scene's bootstrap script
        /// calls this once at Start().
        ///
        /// The PlayerPhysics rig's own XRI ray interactors independently spawn a
        /// second EventSystem (with XRUIInputModule) slightly later, once they
        /// detect UI-capable canvases in the scene - a plain existence check here
        /// can't see that one coming, so the created EventSystem also carries an
        /// EventSystemDeduper to clean up whichever one shows up second.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<InputSystemUIInputModule>();
            esGO.AddComponent<EventSystemDeduper>();
        }

        public static Image CreatePanel(Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;
            var image = go.AddComponent<Image>();
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        public static TMP_Text CreateText(Transform parent, string content, int fontSize, Color color,
            TextAlignmentOptions align, Vector2 anchoredPos, Vector2 size, string name = "Text")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            return text;
        }

        public static Button CreateButton(Transform parent, string label, Color bgColor, Color textColor,
            Vector2 anchoredPos, Vector2 size, UnityAction onClick, int fontSize = 26)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.sprite = RoundedSprite;
            image.type = Image.Type.Sliced;
            image.color = bgColor;

            var button = go.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            CreateText(go.transform, label, fontSize, textColor, TextAlignmentOptions.Center, Vector2.zero, size);

            return button;
        }

        // ---- Sci-fi skin ----
        //
        // A second, deliberately different visual language alongside the
        // EcoLearn-matching one above: angular chamfered-corner panels
        // (cut top-left/bottom-right corners, not rounded) with a glowing
        // cyan HUD palette, for the in-VR "instrument panel" screens (World's
        // subject picker/My Progress) rather than the login page - which
        // stays matched to the real website and is untouched by any of this.
        // Ported from SciFiProgressBar's own chamfer-sprite generator so
        // every sci-fi-skinned screen in the project shares one definition
        // instead of each screen growing its own copy.
        public static readonly Color SciFiBackingColor = new Color(0.02f, 0.05f, 0.06f, 0.92f);
        public static readonly Color SciFiFrameColor = new Color(0.25f, 0.85f, 0.95f, 0.9f);
        public static readonly Color SciFiGlowDim = new Color(0.25f, 0.85f, 0.95f, 0.22f);
        public static readonly Color SciFiGlowCore = new Color(0.55f, 1f, 1f, 1f);
        public static readonly Color SciFiTextDim = new Color(0.6f, 0.85f, 0.9f, 0.85f);

        private static Sprite _sciFiChamferSprite;
        private static Sprite _sciFiChamferOutlineSprite;

        // Deliberately NOT `_x ??= Build()` - Unity Objects overload `==` to
        // treat a destroyed-but-still-referenced object as "null" (the
        // classic "fake null"), but `??`/`??=` bypass that overload and use
        // the raw C# reference check instead. A scene transition can destroy
        // this cached Sprite/Texture2D via Resources.UnloadUnusedAssets()
        // (nothing in a loaded scene references a purely-static, code-
        // generated asset) while the static field itself still "points" at
        // the now-destroyed object - `??=` then sees a non-null field and
        // skips rebuilding, handing every caller a dead sprite. Confirmed
        // live: a fresh Play in StartScene rendered the whole panel as a
        // single solid-color block with no visible border/corners at all -
        // `frame.sprite == null` was true via Unity's own check, but the
        // `??=` cache had already "skipped" rebuilding it. An explicit
        // `if (x == null)` uses the correct (overloaded) check.
        public static Sprite SciFiChamferSprite
        {
            get
            {
                if (_sciFiChamferSprite == null) _sciFiChamferSprite = BuildSciFiChamferSprite(filled: true);
                return _sciFiChamferSprite;
            }
        }

        public static Sprite SciFiChamferOutlineSprite
        {
            get
            {
                if (_sciFiChamferOutlineSprite == null) _sciFiChamferOutlineSprite = BuildSciFiChamferSprite(filled: false);
                return _sciFiChamferOutlineSprite;
            }
        }

        private static Sprite BuildSciFiChamferSprite(bool filled)
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

        /// <summary>A chamfered-corner sci-fi HUD panel: dark backing + a glowing cyan frame, optionally with real sci-fi HUD art (a texture, not a sprite - use an imported .png) dimmed behind it as ambient texture. Children of the returned Image render on top of both.</summary>
        public static Image CreateSciFiPanel(Transform parent, Vector2 anchoredPos, Vector2 size, Texture2D backgroundArt = null)
        {
            var go = new GameObject("SciFi Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;
            var backing = go.AddComponent<Image>();
            backing.sprite = SciFiChamferSprite;
            backing.type = Image.Type.Sliced;
            backing.color = SciFiBackingColor;

            if (backgroundArt != null)
            {
                var artClipGO = new GameObject("Background Art", typeof(RectTransform));
                artClipGO.transform.SetParent(go.transform, false);
                StretchFull(artClipGO.GetComponent<RectTransform>());
                artClipGO.AddComponent<RectMask2D>();
                var artImgGO = new GameObject("Art Image", typeof(RectTransform));
                artImgGO.transform.SetParent(artClipGO.transform, false);
                StretchFull(artImgGO.GetComponent<RectTransform>());
                var artImg = artImgGO.AddComponent<RawImage>();
                artImg.texture = backgroundArt;
                artImg.color = new Color(SciFiGlowCore.r, SciFiGlowCore.g, SciFiGlowCore.b, 0.16f);
            }

            var frameGO = new GameObject("Frame", typeof(RectTransform));
            frameGO.transform.SetParent(go.transform, false);
            StretchFull(frameGO.GetComponent<RectTransform>());
            var frame = frameGO.AddComponent<Image>();
            frame.sprite = SciFiChamferOutlineSprite;
            frame.type = Image.Type.Sliced;
            frame.color = SciFiFrameColor;

            return backing;
        }

        public static Button CreateSciFiButton(Transform parent, string label, Color accent, Vector2 anchoredPos, Vector2 size,
            UnityAction onClick, int fontSize = 22)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.sprite = SciFiChamferSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(accent.r, accent.g, accent.b, 0.22f);

            var frameGO = new GameObject("Frame", typeof(RectTransform));
            frameGO.transform.SetParent(go.transform, false);
            StretchFull(frameGO.GetComponent<RectTransform>());
            var frame = frameGO.AddComponent<Image>();
            frame.sprite = SciFiChamferOutlineSprite;
            frame.type = Image.Type.Sliced;
            frame.color = accent;

            var button = go.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            var text = CreateText(go.transform, label, fontSize, SciFiGlowCore, TextAlignmentOptions.Center, Vector2.zero, size);
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 1.5f;

            return button;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static Button CreateGradientButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
            UnityAction onClick, int fontSize = 26)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.sprite = RoundedGradientSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = go.AddComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            CreateText(go.transform, label, fontSize, PrimaryTextColor, TextAlignmentOptions.Center, Vector2.zero, size);

            return button;
        }

        public static TMP_InputField CreateInputField(Transform parent, string placeholder, bool isPassword,
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("InputField_" + placeholder, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;

            var bgImage = go.AddComponent<Image>();
            bgImage.sprite = RoundedSprite;
            bgImage.type = Image.Type.Sliced;
            bgImage.color = PanelColor;
            var inputField = go.AddComponent<TMP_InputField>();

            var textArea = new GameObject("Text Area", typeof(RectTransform));
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(16, 6);
            textAreaRect.offsetMax = new Vector2(-16, -6);
            textArea.AddComponent<RectMask2D>();

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGO.transform.SetParent(textArea.transform, false);
            var placeholderRect = placeholderGO.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 26;
            placeholderText.color = TextDimColor;
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(textArea.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 26;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            inputField.fontAsset = text.font;
            if (isPassword) inputField.contentType = TMP_InputField.ContentType.Password;
            inputField.ForceLabelUpdate();

            return inputField;
        }
    }

    /// <summary>
    /// Waits a beat, then collapses down to a single EventSystem if more than
    /// one exists - covers the case where PlayerPhysics's own XRI ray
    /// interactors spawn their own (XRUIInputModule) EventSystem shortly
    /// after EnsureEventSystem() already created one. Keeps the XRUIInputModule
    /// one when both are present, since that's the one built for XR ray/poke
    /// UI interaction rather than mouse/touch.
    /// </summary>
    internal class EventSystemDeduper : MonoBehaviour
    {
        private void Start() => StartCoroutine(DedupeAsSoonAsSeen());

        // Polls every frame instead of waiting a flat 0.5s - the rig's XRI-spawned
        // EventSystem can appear anywhere in that window, and every frame both
        // exist Unity logs a red "2 event systems" error. Catching it the frame
        // it appears (rather than after a fixed delay) closes that window down
        // to worst-case a single frame instead of dozens.
        private IEnumerator DedupeAsSoonAsSeen()
        {
            for (int frame = 0; frame < 300; frame++)
            {
                var all = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
                if (all.Length > 1)
                {
                    EventSystem keep = null;
                    foreach (var es in all)
                    {
                        if (es.GetComponent<XRUIInputModule>() != null) { keep = es; break; }
                    }
                    if (keep == null) keep = all[0];

                    foreach (var es in all)
                    {
                        if (es == keep) continue;
                        es.enabled = false;
                        Destroy(es.gameObject);
                    }
                    yield break;
                }
                yield return null;
            }
        }
    }
}
