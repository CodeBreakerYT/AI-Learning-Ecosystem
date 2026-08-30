using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Chemistry Minigame 3 - "Periodic Table Hunt" (design doc section 5.3),
    /// scoped to a curated 24-element subset (the first 20 plus a few notable
    /// heavier elements) rather than the full 118 - enough for real group/
    /// period/metal-vs-nonmetal retrieval practice without hand-authoring a
    /// full periodic dataset. Elements float in a loose period/group grid the
    /// player has to physically search and walk through; grab the one the
    /// challenge names and drop it in the confirmation ring.
    /// </summary>
    // The HUD, confirm socket and full element grid build in Awake() and
    // persist as real scene content - [ExecuteAlways] means it also builds
    // in the Editor so the grid layout is visible in Scene view without
    // Play mode. Only the current challenge/target is runtime state.
    [ExecuteAlways]
    public class PeriodicTableHuntGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "PeriodicTableHunt";
        public string Subject => "Chemistry";

        public struct ElementData
        {
            public string Symbol;
            public string Name;
            public int AtomicNumber;
            public int Group;
            public int Period;
            public string Category; // metal, nonmetal, metalloid, noble gas
        }

        private static readonly ElementData[] Elements =
        {
            new ElementData { Symbol = "H", Name = "Hydrogen", AtomicNumber = 1, Group = 1, Period = 1, Category = "nonmetal" },
            new ElementData { Symbol = "He", Name = "Helium", AtomicNumber = 2, Group = 18, Period = 1, Category = "noble gas" },
            new ElementData { Symbol = "Li", Name = "Lithium", AtomicNumber = 3, Group = 1, Period = 2, Category = "metal" },
            new ElementData { Symbol = "Be", Name = "Beryllium", AtomicNumber = 4, Group = 2, Period = 2, Category = "metal" },
            new ElementData { Symbol = "B", Name = "Boron", AtomicNumber = 5, Group = 13, Period = 2, Category = "metalloid" },
            new ElementData { Symbol = "C", Name = "Carbon", AtomicNumber = 6, Group = 14, Period = 2, Category = "nonmetal" },
            new ElementData { Symbol = "N", Name = "Nitrogen", AtomicNumber = 7, Group = 15, Period = 2, Category = "nonmetal" },
            new ElementData { Symbol = "O", Name = "Oxygen", AtomicNumber = 8, Group = 16, Period = 2, Category = "nonmetal" },
            new ElementData { Symbol = "F", Name = "Fluorine", AtomicNumber = 9, Group = 17, Period = 2, Category = "nonmetal" },
            new ElementData { Symbol = "Ne", Name = "Neon", AtomicNumber = 10, Group = 18, Period = 2, Category = "noble gas" },
            new ElementData { Symbol = "Na", Name = "Sodium", AtomicNumber = 11, Group = 1, Period = 3, Category = "metal" },
            new ElementData { Symbol = "Mg", Name = "Magnesium", AtomicNumber = 12, Group = 2, Period = 3, Category = "metal" },
            new ElementData { Symbol = "Al", Name = "Aluminium", AtomicNumber = 13, Group = 13, Period = 3, Category = "metal" },
            new ElementData { Symbol = "Si", Name = "Silicon", AtomicNumber = 14, Group = 14, Period = 3, Category = "metalloid" },
            new ElementData { Symbol = "P", Name = "Phosphorus", AtomicNumber = 15, Group = 15, Period = 3, Category = "nonmetal" },
            new ElementData { Symbol = "S", Name = "Sulfur", AtomicNumber = 16, Group = 16, Period = 3, Category = "nonmetal" },
            new ElementData { Symbol = "Cl", Name = "Chlorine", AtomicNumber = 17, Group = 17, Period = 3, Category = "nonmetal" },
            new ElementData { Symbol = "Ar", Name = "Argon", AtomicNumber = 18, Group = 18, Period = 3, Category = "noble gas" },
            new ElementData { Symbol = "K", Name = "Potassium", AtomicNumber = 19, Group = 1, Period = 4, Category = "metal" },
            new ElementData { Symbol = "Ca", Name = "Calcium", AtomicNumber = 20, Group = 2, Period = 4, Category = "metal" },
            new ElementData { Symbol = "Fe", Name = "Iron", AtomicNumber = 26, Group = 8, Period = 4, Category = "metal" },
            new ElementData { Symbol = "Cu", Name = "Copper", AtomicNumber = 29, Group = 11, Period = 4, Category = "metal" },
            new ElementData { Symbol = "Ag", Name = "Silver", AtomicNumber = 47, Group = 11, Period = 5, Category = "metal" },
            new ElementData { Symbol = "Au", Name = "Gold", AtomicNumber = 79, Group = 11, Period = 6, Category = "metal" }
        };

        private const int TotalRounds = 6;
        private const float GridZOffset = 1.5f;
        private const float ConfirmZOffset = 3.2f;

        private static readonly Color ElementColor = new Color(0.357f, 0.549f, 1f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        public System.Action<int, int> onComplete;
        public GameObject elementTilePrefab;

        private TMP_Text _challengeText;
        private TMP_Text _feedbackText;
        private ElementSocket _confirmSocket;
        private Renderer _confirmRenderer;
        private readonly List<ElementBlock> _blocks = new List<ElementBlock>();

        private int _round;
        private int _score;
        private ElementData _target;
        private string _concept;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;

        private void Awake()
        {
            if (transform.Find("Periodic Hunt Canvas") == null)
                BuildStatic();
            else
                RediscoverReferences();
        }

        public void InitializeGame(int startingLevel) { }

        public void StartWith()
        {
            _round = 0;
            _score = 0;
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
        }

        public void StartGame() => NextChallenge();

        // ---- Build (edit-mode safe, persists in the scene) ----

        public void Rebuild()
        {
            ClearBuilt();
            BuildStatic();
        }

        private void ClearBuilt()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            _blocks.Clear();
        }

        private void BuildStatic()
        {
            BuildHud();
            BuildConfirmSocket();
            BuildElementGrid();
        }

        private void RediscoverReferences()
        {
            var panel = transform.Find("Periodic Hunt Canvas/Panel");
            _challengeText = panel != null ? panel.Find("Challenge")?.GetComponent<TMP_Text>() : null;
            _feedbackText = panel != null ? panel.Find("Feedback")?.GetComponent<TMP_Text>() : null;

            var ring = transform.Find("Confirm Ring");
            _confirmRenderer = ring != null ? ring.GetComponent<Renderer>() : null;
            _confirmSocket = ring != null ? ring.GetComponentInChildren<ElementSocket>() : null;
            if (_confirmSocket != null) _confirmSocket.onElementPlaced = HandleElementPlaced;

            _blocks.Clear();
            foreach (Transform child in transform)
            {
                var block = child.GetComponent<ElementBlock>();
                if (block != null) _blocks.Add(block);
            }
        }

        private void BuildHud()
        {
            var canvasGO = new GameObject("Periodic Hunt Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.3f, GridZOffset - 1f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 180);
            canvasGO.transform.localScale = Vector3.one * 0.003f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(560, 180), PanelColor);
            _challengeText = CreateText(panel.transform, "Search the grid for the challenge element.", 26, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 30), new Vector2(520, 90), "Challenge");
            _feedbackText = CreateText(panel.transform, "Search the grid, grab it, drop it in the ring.", 18, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, -50), new Vector2(520, 50), "Feedback");
        }

        private void BuildConfirmSocket()
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Confirm Ring";
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 1f, ConfirmZOffset);
            ring.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
            _confirmRenderer = ring.GetComponent<Renderer>();
            _confirmRenderer.material.color = ElementColor * 0.6f;
            var ringCol = ring.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(ringCol); else DestroyImmediate(ringCol);

            var triggerGO = new GameObject("Confirm Trigger");
            triggerGO.transform.SetParent(ring.transform, false);
            triggerGO.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var col = triggerGO.AddComponent<SphereCollider>();
            col.radius = 0.55f;
            col.isTrigger = true;
            var socketInteractor = triggerGO.AddComponent<XRSocketInteractor>();
            socketInteractor.interactionLayers = -1;
            _confirmSocket = triggerGO.AddComponent<ElementSocket>();
            _confirmSocket.onElementPlaced = HandleElementPlaced;
        }

        private void BuildElementGrid()
        {
            const int columns = 6;
            for (var i = 0; i < Elements.Length; i++)
            {
                // A real crystal prop stands in for each element tile, tinted
                // by category, instead of a bare cylinder/cube.
                var e = Elements[i];
                GameObject blockGO;
                if (elementTilePrefab != null)
                {
                    blockGO = Instantiate(elementTilePrefab, transform);
                    blockGO.name = $"Element {e.Symbol}";
                    blockGO.transform.localScale = Vector3.one * 0.18f;
                    // The crystal's own mesh collider is concave - a dynamic
                    // Rigidbody (added below) requires a convex one.
                    foreach (var mc in blockGO.GetComponentsInChildren<MeshCollider>())
                        mc.convex = true;
                }
                else
                {
                    blockGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    blockGO.name = $"Element {e.Symbol}";
                    blockGO.transform.SetParent(transform, false);
                    blockGO.transform.localScale = new Vector3(0.32f, 0.08f, 0.32f);
                }

                var col = i % columns;
                var row = i / columns;
                var x = (col - (columns - 1) / 2f) * 0.8f;
                var z = GridZOffset - row * 0.9f;
                blockGO.transform.localPosition = new Vector3(x, 1.2f, z);

                var rb = blockGO.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                blockGO.AddComponent<XRGrabInteractable>();

                var renderer = blockGO.GetComponentInChildren<Renderer>();
                renderer.material.color = CategoryColor(e.Category);

                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(blockGO.transform, false);
                labelGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                labelGO.transform.localScale = Vector3.one * 0.4f;
                var label = labelGO.AddComponent<TextMeshPro>();
                label.text = e.Symbol;
                label.fontSize = 6;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;

                var block = blockGO.AddComponent<ElementBlock>();
                block.Data = e;
                _blocks.Add(block);
            }
        }

        private static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "metal": return new Color(0.75f, 0.75f, 0.8f);
                case "nonmetal": return new Color(0.4f, 0.75f, 0.95f);
                case "metalloid": return new Color(0.6f, 0.85f, 0.5f);
                default: return new Color(0.85f, 0.6f, 0.95f); // noble gas
            }
        }

        private void NextChallenge()
        {
            _round++;
            if (_round > TotalRounds)
            {
                _challengeText.text = "Complete!";
                _feedbackText.text = $"Score: {_score} / {TotalRounds}";
                MinigameEnvironment.PlayRoundCompleteVfx(_confirmSocket.transform.position);
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke(_score, TotalRounds);
                return;
            }

            _target = Elements[Random.Range(0, Elements.Length)];
            _mistakesThisTask = 0;
            _taskStartTime = Time.time;
            _confirmSocket.TargetSymbol = _target.Symbol;
            _confirmRenderer.material.color = ElementColor * 0.6f;

            int challengeType = Random.Range(0, 3);
            switch (challengeType)
            {
                case 0:
                    _concept = "atomic number";
                    _challengeText.text = $"Find the element with {_target.AtomicNumber} protons.";
                    break;
                case 1:
                    _concept = "element categories";
                    _challengeText.text = $"Find a {_target.Category} in period {_target.Period}.";
                    break;
                default:
                    _concept = "groups and periods";
                    _challengeText.text = $"Find the element in group {_target.Group}, period {_target.Period}.";
                    break;
            }

            _feedbackText.text = "Search the grid, grab it, drop it in the ring.";
            _roundActive = true;
        }

        private void HandleElementPlaced(ElementBlock block, bool correct)
        {
            if (!_roundActive) return;

            _confirmRenderer.material.color = correct ? CorrectColor : WrongColor;
            if (correct)
            {
                _roundActive = false;
                _score++;
                _feedbackText.text = $"Correct - that's {block.Data.Name}!";
                HandleSuccess();
                Invoke(nameof(NextChallenge), 1.2f);
            }
            else
            {
                _mistakesThisTask++;
                _feedbackText.text = $"{block.Data.Name} isn't it - try again.";
                HandleFailure();
            }
        }

        // ---- IMinigame ----

        public void SubmitAnswer(string playerAnswer) { }

        public void HandleSuccess()
        {
            var data = GetLearningData();
            data.wasCorrect = true;
            GameManager.Instance?.ReportAnswer(data);
        }

        public void HandleFailure()
        {
            var data = GetLearningData();
            data.wasCorrect = false;
            GameManager.Instance?.ReportAnswer(data);
        }

        public void EndGame() => GameManager.Instance?.EndMinigameSession();

        public LearningTaskData GetLearningData()
        {
            return new LearningTaskData
            {
                subject = Subject,
                minigameId = MinigameId,
                level = 1,
                concept = _concept,
                taskDescription = _challengeText != null ? _challengeText.text : "",
                playerAnswer = "",
                correctAnswer = $"{_target.Symbol} ({_target.Name})",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
