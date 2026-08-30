using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AILearningEcosystem.Learning;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Chemistry Minigame 2 - "Chemical Reaction Lab". The equation's
    /// coefficients are never shown as numbers - the player only sees the
    /// reactant/product names carved on a lab tablet. Two racks of real
    /// glass test tubes (one per reactant) sit beside a real flask; the
    /// riddle is figuring out, by physically pouring tubes in one at a
    /// time and watching the tablet update, exactly how many of each
    /// reactant balances the reaction. Pour one too many of either and the
    /// mixture fizzles out and the station resets for another attempt.
    ///
    /// The tablet, flask and a sample rack are built in Awake() and persist
    /// as real scene content - [ExecuteAlways] means it also builds in the
    /// Editor so the layout is visible and tweakable in Scene view without
    /// Play mode. Only the pour tally/round state is runtime-only.
    /// </summary>
    [ExecuteAlways]
    public class ChemicalReactionLabGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "ChemicalReactionLab";
        public string Subject => "Chemistry";

        public GameObject testTubePrefab;
        public GameObject flaskPrefab;
        public GameObject tablePrefab;

        private class Reaction
        {
            public string Reactant1;
            public int Coeff1;
            public string Reactant2;
            public int Coeff2;
            public string ProductName;
            public Color EffectColor;
            public string Fact;
        }

        private static readonly Reaction[] Reactions =
        {
            new Reaction { Reactant1 = "H2", Coeff1 = 2, Reactant2 = "O2", Coeff2 = 1, ProductName = "Water",
                EffectColor = new Color(0.4f, 0.7f, 1f), Fact = "2H2 + O2 -> 2H2O - a burst of energy as bonds reform." },
            new Reaction { Reactant1 = "N2", Coeff1 = 1, Reactant2 = "H2", Coeff2 = 3, ProductName = "Ammonia",
                EffectColor = new Color(0.6f, 0.9f, 0.6f), Fact = "N2 + 3H2 -> 2NH3 - the Haber process feeds the planet." },
            new Reaction { Reactant1 = "Na", Coeff1 = 2, Reactant2 = "Cl2", Coeff2 = 1, ProductName = "Table Salt",
                EffectColor = new Color(0.95f, 0.95f, 0.6f), Fact = "2Na + Cl2 -> 2NaCl - a violent reaction settles into ordinary salt." }
        };

        private static readonly Dictionary<string, Color> ElementColors = new Dictionary<string, Color>
        {
            { "H2", new Color(0.7f, 0.85f, 1f) },
            { "O2", new Color(1f, 0.55f, 0.5f) },
            { "N2", new Color(0.6f, 0.7f, 1f) },
            { "Na", new Color(1f, 0.9f, 0.5f) },
            { "Cl2", new Color(0.75f, 0.95f, 0.6f) }
        };

        private const float FlaskZOffset = 2f;
        private const float TableTopY = 1.072f;
        private const int TubesPerRack = 4;

        public System.Action onComplete;

        private TextMeshPro _tabletText;
        private Renderer _flaskRenderer;
        private Transform _flaskTransform;

        private readonly List<GameObject> _spawnedTubes = new List<GameObject>();

        private Reaction _current;
        private int _pouredCount1;
        private int _pouredCount2;
        private int _round;
        private int _score;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;

        private void Awake()
        {
            if (transform.Find("Lab Tablet") == null)
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

        public void StartGame() => NextReaction();

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
            _spawnedTubes.Clear();
        }

        private void BuildStatic()
        {
            BuildTablet();
            BuildTable();
            BuildFlask();
            // Edit-mode/initial preview - a sample rack pair so the bench
            // layout is visible before any round actually starts.
            SpawnRack(1, Reactions[0].Reactant1, -0.6f);
            SpawnRack(2, Reactions[0].Reactant2, 0.6f);
            _tabletText.text = $"{Reactions[0].Reactant1} + {Reactions[0].Reactant2} -> {Reactions[0].ProductName}\nPour test tubes into the flask to find the balance.";
        }

        private void RediscoverReferences()
        {
            // "Tablet Text" is a sibling of "Lab Tablet" (parented to the
            // room transform, not the slab) - see BuildTablet's comment.
            _tabletText = transform.Find("Tablet Text")?.GetComponent<TextMeshPro>();

            var flaskGO = transform.Find("Reaction Flask");
            _flaskRenderer = flaskGO != null ? flaskGO.GetComponentInChildren<Renderer>() : null;
            _flaskTransform = flaskGO != null ? flaskGO.transform : null;

            _spawnedTubes.Clear();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("Test Tube") || child.name.StartsWith("Rack Sign"))
                    _spawnedTubes.Add(child.gameObject);
            }
        }

        // The unbalanced equation and the running pour tally are carved
        // into a physical lab tablet - never a floating coefficient HUD -
        // so the player has to read the room, not a UI, to solve it.
        private void BuildTablet()
        {
            var slabGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            slabGO.name = "Lab Tablet";
            slabGO.transform.SetParent(transform, false);
            slabGO.transform.localPosition = new Vector3(0f, 1.7f, FlaskZOffset - 1.4f);
            slabGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            slabGO.transform.localScale = new Vector3(0.8f, 0.03f, 0.55f);
            slabGO.GetComponent<Renderer>().material.color = new Color(0.3f, 0.28f, 0.25f);
            SafeDestroy(slabGO.GetComponent<Collider>());

            // Parented to the room transform (always scale 1) at the slab's
            // world pose, not to the slab itself - a child of a non-uniformly
            // scaled slab (0.8, 0.03, 0.55) would inherit that squash and a
            // world-space TextMeshPro's fontSize is near-literal world units,
            // so inheriting scale 1 here (not the slab's) is what keeps the
            // text a readable sign instead of blowing up to room-filling size.
            var textGO = new GameObject("Tablet Text");
            textGO.transform.SetParent(transform, true);
            textGO.transform.position = slabGO.transform.TransformPoint(new Vector3(0f, 0.04f, 0f));
            textGO.transform.rotation = slabGO.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            textGO.transform.localScale = Vector3.one * 0.18f;
            _tabletText = textGO.AddComponent<TextMeshPro>();
            _tabletText.fontSize = 3f;
            _tabletText.alignment = TextAlignmentOptions.Center;
            _tabletText.color = new Color(0.85f, 0.75f, 0.4f);
        }

        // A real lab bench - everything else (flask, test tubes) sits on its
        // top surface, instead of floating unanchored in the middle of the room.
        private void BuildTable()
        {
            GameObject table;
            if (tablePrefab != null)
            {
                table = Instantiate(tablePrefab, transform);
                table.name = "Lab Bench";
            }
            else
            {
                table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                table.name = "Lab Bench";
                table.transform.SetParent(transform, false);
                table.transform.localScale = new Vector3(1.8f, TableTopY, 1.4f);
                table.transform.localPosition += Vector3.up * (TableTopY / 2f);
            }
            table.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            table.transform.localPosition = new Vector3(0f, 0f, FlaskZOffset);
        }

        private void BuildFlask()
        {
            GameObject flask;
            if (flaskPrefab != null)
            {
                flask = Instantiate(flaskPrefab, transform);
                flask.name = "Reaction Flask";
                flask.transform.localScale = Vector3.one * 1.3f;
            }
            else
            {
                flask = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                flask.name = "Reaction Flask";
                flask.transform.SetParent(transform, false);
                flask.transform.localScale = new Vector3(0.3f, 0.35f, 0.3f);
            }
            flask.transform.localPosition = new Vector3(0f, TableTopY, FlaskZOffset + 0.3f);
            _flaskRenderer = flask.GetComponentInChildren<Renderer>();
            _flaskTransform = flask.transform;

            var triggerGO = new GameObject("Flask Trigger");
            triggerGO.transform.SetParent(flask.transform, false);
            var col = triggerGO.AddComponent<SphereCollider>();
            col.radius = 0.4f;
            col.isTrigger = true;
            triggerGO.AddComponent<Rigidbody>().isKinematic = true;
            triggerGO.AddComponent<FlaskZone>().owner = this;
        }

        // ---- Rounds ----

        private void NextReaction()
        {
            _round++;
            if (_round > Reactions.Length)
            {
                ClearTubes();
                _tabletText.text = $"Complete!\nScore: {_score} / {Reactions.Length}";
                MinigameEnvironment.PlayRoundCompleteVfx(_flaskTransform.position);
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke();
                return;
            }

            _current = Reactions[_round - 1];
            _pouredCount1 = 0;
            _pouredCount2 = 0;
            _mistakesThisTask = 0;
            _taskStartTime = Time.time;
            _flaskRenderer.material.color = new Color(0.7f, 0.7f, 0.75f, 0.5f);
            _roundActive = true;

            ClearTubes();
            SpawnRack(1, _current.Reactant1, -0.6f);
            SpawnRack(2, _current.Reactant2, 0.6f);
            UpdateTablet("Pour test tubes into the flask to find the balance.");
        }

        private void SpawnRack(int slot, string element, float baseX)
        {
            var color = ElementColors.TryGetValue(element, out var c) ? c : new Color(0.6f, 0.8f, 0.9f);
            var rackZ = FlaskZOffset - 0.35f;

            var signGO = new GameObject($"Rack Sign {element}");
            signGO.transform.SetParent(transform, false);
            signGO.transform.localPosition = new Vector3(baseX, TableTopY + 0.3f, rackZ - 0.14f);
            signGO.transform.localScale = Vector3.one * 0.15f;
            var sign = signGO.AddComponent<TextMeshPro>();
            sign.text = element;
            sign.fontSize = 4f;
            sign.alignment = TextAlignmentOptions.Center;
            sign.color = Color.white;
            _spawnedTubes.Add(signGO);

            for (var i = 0; i < TubesPerRack; i++)
            {
                GameObject tube;
                if (testTubePrefab != null)
                {
                    tube = Instantiate(testTubePrefab, transform);
                    tube.name = $"Test Tube {element}";
                    tube.transform.localScale = Vector3.one;
                }
                else
                {
                    tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    tube.name = $"Test Tube {element}";
                    tube.transform.SetParent(transform, false);
                    tube.transform.localScale = new Vector3(0.05f, 0.12f, 0.05f);
                }
                tube.transform.localPosition = new Vector3(baseX + (i - 1.5f) * 0.09f, TableTopY, rackZ);
                var renderer = tube.GetComponentInChildren<Renderer>();
                if (renderer != null) renderer.material.color = color;

                // The real "Pipette" asset ships with NO collider anywhere in
                // its hierarchy - confirmed live (same class of bug as the
                // Archery bow before its own fix). Without one XRGrabInteractable
                // has nothing to hover/select, so the tubes were never actually
                // grabbable, and FlaskZone's OnTriggerStay had nothing to detect
                // pouring one in either way.
                if (tube.GetComponentInChildren<Collider>() == null)
                {
                    var box = tube.AddComponent<BoxCollider>();
                    var bounds = renderer != null ? renderer.bounds : new Bounds(tube.transform.position, Vector3.one * 0.1f);
                    box.center = tube.transform.InverseTransformPoint(bounds.center);
                    box.size = Vector3.Scale(bounds.size, new Vector3(
                        1f / Mathf.Max(0.0001f, tube.transform.lossyScale.x),
                        1f / Mathf.Max(0.0001f, tube.transform.lossyScale.y),
                        1f / Mathf.Max(0.0001f, tube.transform.lossyScale.z)));
                }

                var rb = tube.AddComponent<Rigidbody>();
                rb.useGravity = true;
                var grab = tube.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                var comp = tube.AddComponent<TestTube>();
                comp.Configure(this, slot);

                _spawnedTubes.Add(tube);
            }
        }

        private void ClearTubes()
        {
            foreach (var go in _spawnedTubes)
                if (go != null) SafeDestroy(go);
            _spawnedTubes.Clear();
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        private void UpdateTablet(string status)
        {
            _tabletText.text = $"{_current.Reactant1} + {_current.Reactant2} -> {_current.ProductName}\n" +
                                $"Poured: {_pouredCount1} {_current.Reactant1}, {_pouredCount2} {_current.Reactant2}\n" +
                                status;
        }

        public void OnTubePoured(int slot)
        {
            if (!_roundActive) return;

            if (slot == 1) _pouredCount1++; else _pouredCount2++;

            if (_pouredCount1 == _current.Coeff1 && _pouredCount2 == _current.Coeff2)
            {
                CompleteReaction();
            }
            else if (_pouredCount1 > _current.Coeff1 || _pouredCount2 > _current.Coeff2)
            {
                _mistakesThisTask++;
                HandleFailure();
                UpdateTablet("Too much reagent - the mixture fizzles out. Resetting...");
                Invoke(nameof(ResetRoundTubes), 1.2f);
            }
            else
            {
                UpdateTablet("Find the right balance.");
            }
        }

        private void ResetRoundTubes()
        {
            if (!_roundActive) return;
            _pouredCount1 = 0;
            _pouredCount2 = 0;
            ClearTubes();
            SpawnRack(1, _current.Reactant1, -0.6f);
            SpawnRack(2, _current.Reactant2, 0.6f);
            UpdateTablet("Pour test tubes into the flask to find the balance.");
        }

        private void CompleteReaction()
        {
            _roundActive = false;
            _score++;
            _flaskRenderer.material.color = _current.EffectColor;
            ClearTubes();
            _tabletText.text = _current.Fact;
            HandleSuccess();
            Invoke(nameof(NextReaction), 2f);
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
                concept = "balancing chemical equations",
                taskDescription = _current != null ? $"{_current.Reactant1} + {_current.Reactant2} -> {_current.ProductName}" : "",
                playerAnswer = $"{_pouredCount1}{_current?.Reactant1}, {_pouredCount2}{_current?.Reactant2}",
                correctAnswer = _current != null ? $"{_current.Coeff1}{_current.Reactant1}, {_current.Coeff2}{_current.Reactant2}" : "",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }

    }
}
