using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// StartScene's always-visible, always-clickable way forward: a floating
    /// "AI Learning Ecosystem" heading plus one pressable 3D tab - Subjects
    /// (loads Hub.unity) - positioned in front of the spawn point, plus a
    /// looping background music track. World was removed - "no need for
    /// that, only subjects" - Hub.unity's own picker is the only destination.
    ///
    /// Why this exists: the HTML login overlay
    /// (Assets/WebGLTemplates/EcoLearn/) only ever renders inside an actual
    /// WebGL build - browsers don't run inside the Editor's Game view, and a
    /// VR headset never renders the surrounding webpage either - which made
    /// this scene a dead end everywhere except a real deployed build: no
    /// heading, nothing clickable, nothing audible, and (in the Editor) only
    /// an undiscoverable Space-key debug bypass. This panel works
    /// unconditionally in every context (Editor Play Mode, a native build, a
    /// WebGL build, with or without login configured), same Canvas +
    /// TrackedDeviceGraphicRaycaster + XR ray interactor pattern already
    /// proven throughout this project. The HTML login flow is untouched and
    /// still the primary path in a real deployed build; this is the
    /// always-available supplement so the app is never a dead end.
    /// </summary>
    public class StartSceneNav : MonoBehaviour
    {
        private void Start()
        {
            BuildPanel();
        }

        private void BuildPanel()
        {
            var canvasGO = new GameObject("Start Nav Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.position = new Vector3(0f, 1.7f, 0.5f);
            canvasGO.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            var size = new Vector2(720, 320);
            rect.sizeDelta = size;
            canvasGO.transform.localScale = Vector3.one * 0.0022f;

            var panel = CreateSciFiPanel(canvasGO.transform, Vector2.zero, size);

            var heading = CreateText(panel.transform, "AI LEARNING ECOSYSTEM", 40, SciFiGlowCore, TextAlignmentOptions.Center,
                new Vector2(0, 100), new Vector2(680, 70));
            heading.fontStyle = FontStyles.Bold;
            heading.characterSpacing = 2f;

            CreateText(panel.transform, "Choose where to go", 20, SciFiTextDim, TextAlignmentOptions.Center,
                new Vector2(0, 40), new Vector2(680, 40));

            CreateSciFiButton(panel.transform, "SUBJECTS", SciFiGlowCore,
                new Vector2(0, -60), new Vector2(300, 90), () => SceneManager.LoadScene("Hub"), 26);
        }
    }
}
