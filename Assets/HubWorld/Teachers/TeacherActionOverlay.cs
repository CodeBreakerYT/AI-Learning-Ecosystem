using System.Collections.Generic;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Visual flourish for a teacher's Convai actions: a floating text overlay
    /// describing what the teacher is doing ("Let's look at the see-saw...") and
    /// a gold sparkle burst, both driven purely by ConvaiActionsHandler's
    /// ActionStarted/ActionEnded events - no changes needed to Convai's own
    /// action logic, this just reacts to it.
    /// </summary>
    [RequireComponent(typeof(ConvaiActionsHandler))]
    public class TeacherActionOverlay : MonoBehaviour
    {
        private static readonly Dictionary<string, string> Flavor = new()
        {
            { "moveto", "Let's take a look over here..." },
            { "pickup", "Let me pick this up..." },
            { "drop", "And there we go!" },
            { "point", "See this?" },
            { "dance", "Watch this!" },
            { "jump", "Whoa!" },
            { "crouch", "Let me get a closer look..." }
        };

        private ConvaiActionsHandler _actionsHandler;
        private GameObject _bubbleRoot;
        private TMP_Text _bubbleText;
        private ParticleSystem _sparkles;

        private void Awake()
        {
            _actionsHandler = GetComponent<ConvaiActionsHandler>();
            BuildBubble();
            BuildSparkles();
        }

        private void Start()
        {
            _actionsHandler.RegisterForActionEvents(OnActionStarted, OnActionEnded);
        }

        private void OnDestroy()
        {
            _actionsHandler.UnregisterForActionEvents(OnActionStarted, OnActionEnded);
        }

        private void OnActionStarted(string action, GameObject target)
        {
            var key = action.ToLowerInvariant().Replace(" ", "");
            var line = Flavor.TryGetValue(key, out var text) ? text : "Let me show you...";
            if (target != null && (key == "moveto" || key == "pickup"))
                line += "\n(" + target.name.Replace("(Clone)", "").Trim() + ")";

            _bubbleText.text = line;
            _bubbleRoot.SetActive(true);

            _sparkles.transform.position = target != null ? target.transform.position + Vector3.up * 0.3f : transform.position + Vector3.up * 1.6f;
            _sparkles.Play();
        }

        private void OnActionEnded(string action, GameObject target)
        {
            _bubbleRoot.SetActive(false);
        }

        private void BuildBubble()
        {
            var canvasGO = new GameObject("Action Overlay Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320, 100);
            canvasGO.transform.localScale = Vector3.one * 0.0016f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(320, 100), AccentColor);
            _bubbleText = CreateText(panel.transform, "", 18, PrimaryTextColor, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(280, 80));

            _bubbleRoot = panel.gameObject;
            _bubbleRoot.SetActive(false);
        }

        private void BuildSparkles()
        {
            var go = new GameObject("Sparkle Effect");
            go.transform.SetParent(transform, false);
            _sparkles = go.AddComponent<ParticleSystem>();
            _sparkles.Stop();

            var main = _sparkles.main;
            main.duration = 1f;
            main.loop = false;
            main.startLifetime = 0.8f;
            main.startSpeed = 0.6f;
            main.startSize = 0.05f;
            main.startColor = new Color(1f, 0.85f, 0.3f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _sparkles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = _sparkles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var colorOverLifetime = _sparkles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.7f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = _sparkles.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetColor("_BaseColor", Color.white);
            renderer.material = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }
    }
}
