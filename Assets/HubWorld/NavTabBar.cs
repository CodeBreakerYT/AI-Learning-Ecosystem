using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Persistent in-VR pause menu - the headset-side equivalent of the
    /// browser's flat-website nav bar (a headset never renders the
    /// surrounding webpage, so this is the only way to reach that
    /// navigation once you're actually wearing one). Present in every
    /// minigame scene, not in Hub.unity itself (there's nowhere else to go
    /// from there). A small branded panel - "AI LEARNING ECOSYSTEM" title
    /// plus clearly-labeled buttons - rather than bare buttons, so it reads
    /// as a real piece of the app's UI instead of a debug overlay.
    /// Interactive via the XR rig's ray interactor, same as every other
    /// Canvas UI here.
    ///
    /// Opening it actually PAUSES the game (Time.timeScale = 0, restored by
    /// Resume or by NavTabBarToggle closing it again) - a real pause menu,
    /// not just a navigation shortcut, per an explicit ask for one. UI
    /// interaction (ray-cast/EventSystem) keeps working at timeScale 0 since
    /// it's driven by real input, not scaled time.
    ///
    /// Parented to the camera (not a fixed point in scene space) so it's
    /// always in the same spot low in your view no matter where you're
    /// looking or standing - a first attempt pinned it to a fixed world
    /// position off to one side, which worked in a screenshot framed to show
    /// it but was genuinely easy to miss/lose track of during normal play.
    /// </summary>
    public static class NavTabBar
    {
        private static readonly Vector3 LocalOffset = new Vector3(0f, -0.2f, 0.55f);

        public static void Build(Transform fallbackParent)
        {
            var parent = Camera.main != null ? Camera.main.transform : fallbackParent;

            var canvasGO = new GameObject("Nav Tab Bar Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(parent, false);
            canvasGO.transform.localPosition = LocalOffset;
            // Unlike a world-anchored canvas (StartSceneNav.cs, HubBootstrap.cs),
            // a canvas PARENTED TO THE CAMERA already reads correctly at identity
            // rotation - confirmed live (a 180 flip here mirrors the text
            // instead of fixing it, the opposite of the world-anchored case).
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            var panelSize = new Vector2(340, 300);
            rect.sizeDelta = panelSize;
            canvasGO.transform.localScale = Vector3.one * 0.0011f;

            var panel = CreateSciFiPanel(canvasGO.transform, Vector2.zero, panelSize);

            var titleText = CreateText(panel.transform, "PAUSED", 22, SciFiGlowCore, TextAlignmentOptions.Center,
                new Vector2(0, 118), new Vector2(320, 34));
            titleText.fontStyle = FontStyles.Bold;
            titleText.characterSpacing = 2f;
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 12;
            titleText.fontSizeMax = 22;
            titleText.textWrappingMode = TextWrappingModes.NoWrap;

            // NavTabBarToggle pauses on open / unpauses on close (whether
            // closed via this button, the thumbstick-click toggle again, or
            // one of the scene-change buttons below) - see its own comment.
            var toggle = canvasGO.AddComponent<NavTabBarToggle>();
            CreateSciFiButton(panel.transform, "RESUME", SciFiGlowCore,
                new Vector2(0, 50), new Vector2(280, 56), () => toggle.SetOpen(false), 22);
            CreateSciFiButton(panel.transform, "< BACK TO SUBJECTS", SciFiTextDim,
                new Vector2(0, -20), new Vector2(280, 56), HubNavigation.GoHome, 20);
            CreateSciFiButton(panel.transform, "< BACK TO START SCENE", SciFiTextDim,
                new Vector2(0, -90), new Vector2(280, 56), HubNavigation.GoToStartScene, 20);

            // Hidden by default - NavTabBarToggle (on the always-active canvas
            // root) shows/hides this panel on a thumbstick-click press.
            panel.gameObject.SetActive(false);
            toggle.target = panel.gameObject;
        }
    }
}
