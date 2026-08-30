using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Chemistry Topic 2 - "Chemistry in the Real World". Molecule Builder
    /// (Topic 1) already covers what a chemical reaction/bond IS; this one
    /// is deliberately about WHERE chemistry actually shows up outside a
    /// lab - a real object/scenario is presented on a lab tablet, and the
    /// player points at the chemical process actually responsible for it,
    /// out of a few plausible-sounding options. Replaces the old Chemical
    /// Reaction Lab (equation-balancing, the same core idea as Molecule
    /// Builder's bonding) in this scene.
    /// </summary>
    [ExecuteAlways]
    public class ChemistryRealWorldGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "ChemistryRealWorld";
        public string Subject => "Chemistry";

        public GameObject tablePrefab;

        private class Scenario
        {
            public string ObjectLabel;      // what the physical prop represents
            public PrimitiveType PropShape;
            public Color PropColor;
            public string Description;      // the real-world situation, read off the tablet
            public string CorrectAnswer;
            public string[] Distractors;
            public string Fact;
        }

        private static readonly Scenario[] Scenarios =
        {
            new Scenario
            {
                ObjectLabel = "Bar of Soap", PropShape = PrimitiveType.Cube, PropColor = new Color(0.95f, 0.9f, 0.75f),
                Description = "Soap is made by boiling animal fat or vegetable oil with a strong base.",
                CorrectAnswer = "Saponification", Distractors = new[] { "Electrolysis", "Fermentation" },
                Fact = "Saponification: fat + sodium hydroxide breaks the fat into soap molecules and glycerol - the same NaOH from Molecule Builder, put to work."
            },
            new Scenario
            {
                ObjectLabel = "Fertilizer Bag", PropShape = PrimitiveType.Cube, PropColor = new Color(0.5f, 0.75f, 0.4f),
                Description = "Farms spread this on fields every season to make crops grow faster and bigger.",
                CorrectAnswer = "Nitrogen Fixation", Distractors = new[] { "Combustion", "Distillation" },
                Fact = "The Haber process fixes nitrogen gas from the air into ammonia (NH3), which becomes the ammonium nitrate in most fertilizers - it feeds roughly half the world's population."
            },
            new Scenario
            {
                ObjectLabel = "Battery", PropShape = PrimitiveType.Cylinder, PropColor = new Color(0.3f, 0.35f, 0.4f),
                Description = "This stores energy chemically and releases it as electricity when you connect it in a circuit.",
                CorrectAnswer = "Redox Reaction", Distractors = new[] { "Saponification", "Neutralization" },
                Fact = "A battery is a controlled reduction-oxidation (redox) reaction - electrons flow from one electrode to the other through your device instead of jumping directly between the chemicals."
            },
            new Scenario
            {
                ObjectLabel = "Rusty Nail", PropShape = PrimitiveType.Capsule, PropColor = new Color(0.6f, 0.35f, 0.2f),
                Description = "Left outside in the rain, this iron nail slowly turns reddish-brown and flaky.",
                CorrectAnswer = "Oxidation", Distractors = new[] { "Fermentation", "Nitrogen Fixation" },
                Fact = "Rust is iron oxide - iron slowly reacting with oxygen and water. Paint, galvanizing (a zinc coating) and stainless steel's chromium layer all work by keeping oxygen away from the iron."
            },
            new Scenario
            {
                ObjectLabel = "Antacid Tablet", PropShape = PrimitiveType.Sphere, PropColor = new Color(0.95f, 0.95f, 0.98f),
                Description = "Swallowed after a heavy meal, this fizzes and calms an upset, acidic stomach.",
                CorrectAnswer = "Neutralization", Distractors = new[] { "Redox Reaction", "Oxidation" },
                Fact = "Antacids are mild bases - they neutralize excess stomach acid (HCl) the same way an acid and a base cancel out in Molecule Builder's NaOH round, just gentler."
            },
            new Scenario
            {
                ObjectLabel = "Tap Water", PropShape = PrimitiveType.Cylinder, PropColor = new Color(0.5f, 0.75f, 0.95f, 0.7f),
                Description = "Cities treat drinking water with a chemical that kills bacteria before it reaches your tap.",
                CorrectAnswer = "Chlorination", Distractors = new[] { "Fermentation", "Saponification" },
                Fact = "Chlorine added in small, safe amounts disinfects municipal water - one of the biggest public-health wins in history, alongside vaccines."
            }
        };

        private const float TabletZOffset = 1.8f;
        private const int TotalRounds = 6;

        public System.Action onComplete;

        private TextMeshPro _tabletText;
        private TextMeshPro _descriptionText;
        private GameObject _propHolder;
        private GameObject _answerButtonsHolder;

        private readonly List<int> _order = new List<int>();
        private Scenario _current;
        private int _round;
        private int _score;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;

        private void Awake()
        {
            if (transform.Find("RealWorld Tablet") == null)
                BuildStatic();
            else
                RediscoverReferences();
        }

        public void InitializeGame(int startingLevel) { }

        public void StartWith()
        {
            _round = 0;
            _score = 0;
            _order.Clear();
            for (var i = 0; i < Scenarios.Length; i++) _order.Add(i);
            // Fisher-Yates - a different order each playthrough.
            for (var i = _order.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
        }

        public void StartGame() => NextScenario();

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
        }

        private void BuildStatic()
        {
            BuildTable();
            BuildTablet();
            var preview = Scenarios[0];
            _descriptionText.text = preview.Description;
            _tabletText.text = "Point at the real chemistry behind it.";
            BuildProp(preview);
        }

        private void RediscoverReferences()
        {
            _tabletText = transform.Find("RealWorld Tablet Text")?.GetComponent<TextMeshPro>();
            _descriptionText = transform.Find("RealWorld Description")?.GetComponent<TextMeshPro>();
            _propHolder = transform.Find("Scenario Prop")?.gameObject;
            _answerButtonsHolder = transform.Find("Answer Buttons")?.gameObject;
        }

        private void BuildTable()
        {
            GameObject table;
            if (tablePrefab != null)
            {
                table = Instantiate(tablePrefab, transform);
                table.name = "Display Table";
            }
            else
            {
                table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                table.name = "Display Table";
                table.transform.SetParent(transform, false);
                table.transform.localScale = new Vector3(1.2f, 1.0f, 0.8f);
                table.transform.localPosition = new Vector3(0f, 0.5f, TabletZOffset + 0.3f);
                return;
            }
            table.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            table.transform.localPosition = new Vector3(0f, 0f, TabletZOffset + 0.3f);
        }

        private void BuildTablet()
        {
            var slabGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            slabGO.name = "RealWorld Tablet";
            slabGO.transform.SetParent(transform, false);
            slabGO.transform.localPosition = new Vector3(0f, 1.8f, TabletZOffset - 1.2f);
            slabGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            slabGO.transform.localScale = new Vector3(1f, 0.03f, 0.6f);
            slabGO.GetComponent<Renderer>().material.color = new Color(0.3f, 0.28f, 0.25f);
            SafeDestroy(slabGO.GetComponent<Collider>());

            var descGO = new GameObject("RealWorld Description");
            descGO.transform.SetParent(transform, true);
            descGO.transform.position = slabGO.transform.TransformPoint(new Vector3(0f, 0.04f, 0.1f));
            descGO.transform.rotation = slabGO.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            descGO.transform.localScale = Vector3.one * 0.16f;
            _descriptionText = descGO.AddComponent<TextMeshPro>();
            _descriptionText.fontSize = 3.2f;
            _descriptionText.alignment = TextAlignmentOptions.Center;
            _descriptionText.color = new Color(0.9f, 0.85f, 0.6f);

            var promptGO = new GameObject("RealWorld Tablet Text");
            promptGO.transform.SetParent(transform, true);
            promptGO.transform.position = slabGO.transform.TransformPoint(new Vector3(0f, 0.04f, -0.18f));
            promptGO.transform.rotation = slabGO.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            promptGO.transform.localScale = Vector3.one * 0.16f;
            _tabletText = promptGO.AddComponent<TextMeshPro>();
            _tabletText.fontSize = 2.4f;
            _tabletText.alignment = TextAlignmentOptions.Center;
            _tabletText.color = new Color(0.65f, 0.7f, 0.75f);
        }

        // A simple labelled primitive stands in for the real-world object -
        // the point is the reasoning, not the prop art.
        private void BuildProp(Scenario scenario)
        {
            if (_propHolder != null) SafeDestroy(_propHolder);

            var prop = GameObject.CreatePrimitive(scenario.PropShape);
            prop.name = "Scenario Prop";
            prop.transform.SetParent(transform, false);
            prop.transform.localPosition = new Vector3(0f, 1.1f, TabletZOffset + 0.3f);
            prop.transform.localScale = Vector3.one * 0.35f;
            prop.GetComponent<Renderer>().material.color = scenario.PropColor;
            SafeDestroy(prop.GetComponent<Collider>());
            _propHolder = prop;

            var labelGO = new GameObject("Prop Label");
            labelGO.transform.SetParent(prop.transform, false);
            labelGO.transform.localPosition = new Vector3(0f, 1f, 0f);
            labelGO.transform.localScale = Vector3.one * 0.4f;
            var label = labelGO.AddComponent<TextMeshPro>();
            label.text = scenario.ObjectLabel;
            label.fontSize = 4f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
        }

        // ---- Rounds ----

        private void NextScenario()
        {
            _round++;
            if (_round > TotalRounds || _round > _order.Count)
            {
                if (_propHolder != null) SafeDestroy(_propHolder);
                if (_answerButtonsHolder != null) SafeDestroy(_answerButtonsHolder);
                _descriptionText.text = "Complete!";
                _tabletText.text = $"Score: {_score} / {Mathf.Min(TotalRounds, _order.Count)}";
                MinigameEnvironment.PlayRoundCompleteVfx(_propHolder != null ? _propHolder.transform.position : transform.position);
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke();
                return;
            }

            _current = Scenarios[_order[_round - 1]];
            _mistakesThisTask = 0;
            _taskStartTime = Time.time;
            _roundActive = true;

            _descriptionText.text = _current.Description;
            _tabletText.text = "Point at the real chemistry behind it.";

            BuildProp(_current);
            BuildAnswerButtons(_current);
        }

        private void BuildAnswerButtons(Scenario scenario)
        {
            if (_answerButtonsHolder != null) SafeDestroy(_answerButtonsHolder);

            var choices = new List<string>(scenario.Distractors) { scenario.CorrectAnswer };
            for (var i = choices.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (choices[i], choices[j]) = (choices[j], choices[i]);
            }

            var holder = new GameObject("Answer Buttons");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = new Vector3(0f, 1.1f, TabletZOffset - 2.2f);
            _answerButtonsHolder = holder;

            var spacing = 0.9f;
            var startX = -(choices.Count - 1) * spacing / 2f;
            for (var i = 0; i < choices.Count; i++)
            {
                var isCorrect = choices[i] == scenario.CorrectAnswer;
                BuildAnswerButton(holder.transform, new Vector3(startX + i * spacing, 0f, 0f), choices[i], isCorrect);
            }
        }

        private void BuildAnswerButton(Transform parent, Vector3 localPos, string label, bool isCorrect)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Answer " + label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(0.8f, 0.25f, 0.05f);
            go.GetComponent<Renderer>().material.color = new Color(0.3f, 0.35f, 0.45f);

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.55f);
            textGO.transform.localScale = new Vector3(1.1f, 3.5f, 1f);
            var tmp = textGO.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = 3f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1.5f;
            tmp.fontSizeMax = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            go.AddComponent<XRSimpleInteractable>();
            var target = go.AddComponent<AnswerTarget>();
            target.onSelected = () => HandleAnswer(isCorrect, target);
        }

        private void HandleAnswer(bool isCorrect, AnswerTarget target)
        {
            if (!_roundActive) return;
            _roundActive = false;

            target.Flash(isCorrect ? CorrectColor : WrongColor, isCorrect);
            _tabletText.text = _current.Fact;

            if (isCorrect) { _score++; HandleSuccess(); }
            else { _mistakesThisTask++; HandleFailure(); }

            Invoke(nameof(NextScenario), 3f);
        }

        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
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
                concept = "chemistry in everyday life",
                taskDescription = _current != null ? $"{_current.ObjectLabel}: {_current.Description}" : "",
                playerAnswer = "",
                correctAnswer = _current != null ? _current.CorrectAnswer : "",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
