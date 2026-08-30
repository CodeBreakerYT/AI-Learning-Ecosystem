using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AILearningEcosystem.Learning;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Math Adventure II - "The Rune Pillars". No floating quiz panel showing
    /// a literal "construct a triangle with angles X, Y, Z" instruction -
    /// the target is a carved rune tablet mounted on the chamber wall (a
    /// physical object, the same diegetic trick as EquationEscapeRoomGame's
    /// calibration mark), and the player drags three real stone rune-pillars
    /// to reshape the triangle between them until it matches. A physical
    /// lock lever checks the shape; the door out of the chamber only opens
    /// once every rune-lock in the sequence is solved, and reaching the real
    /// exit room beyond it - not a timer - is what actually clears the level.
    /// </summary>
    public class GeometryBuilderGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "GeometryBuilder";
        public string Subject => "Mathematics";

        private const int TotalRounds = 5;
        private const float BenchZOffset = -1.5f;
        private const float AngleToleranceDegrees = 6f;

        private static readonly Color PillarColor = new Color(0.357f, 0.549f, 1f);
        private static readonly Color EdgeColor = new Color(0.8f, 0.8f, 0.85f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        public System.Action<int, int> onComplete;

        private DungeonRoomConfig _dungeon;
        private TextMeshPro _tabletText;
        private Transform[] _vertices = new Transform[3];
        private GameObject[] _edges = new GameObject[3];
        private Transform _leverArm;
        private Quaternion _leverClosed;

        private int _round;
        private int _score;
        private int[] _targetAngles;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;
        private bool _doorSolved;
        private bool _awaitingExit;
        private float _lastFeedbackTime;

        public void InitializeGame(int startingLevel)
        {
            _dungeon = FindFirstObjectByType<DungeonRoomConfig>();
            BuildTablet();
            BuildVertices();
            BuildLockLever();
        }

        public void StartWith()
        {
            _round = 0;
            _score = 0;
            var startingLevel = GameManager.Instance != null ? GameManager.Instance.Difficulty.CurrentLevel(Subject, MinigameId) : 1;
            InitializeGame(startingLevel);
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
        }

        public void StartGame() => NextChallenge();

        private void Update()
        {
            if (_vertices[0] != null) RedrawEdges();

            if (_awaitingExit && _dungeon != null && Camera.main != null &&
                _dungeon.IsPositionInExitRoom(Camera.main.transform.position))
            {
                _awaitingExit = false;
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke(_score, TotalRounds);
            }
        }

        // ---- Build ----

        // The target is carved into a physical tablet mounted on the wall,
        // not a floating Canvas - the player reads it the same way they'd
        // read a real ruin's inscription, not a UI prompt.
        private void BuildTablet()
        {
            var slabGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            slabGO.name = "Rune Tablet";
            slabGO.transform.SetParent(transform, false);
            slabGO.transform.localPosition = new Vector3(0f, 1.7f, BenchZOffset - 1.1f);
            slabGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            slabGO.transform.localScale = new Vector3(0.7f, 0.03f, 0.5f);
            slabGO.GetComponent<Renderer>().material.color = new Color(0.3f, 0.28f, 0.25f);
            Destroy(slabGO.GetComponent<Collider>());

            // Parented to the room transform (scale 1), not the slab (0.7,
            // 0.03, 0.5) - inheriting that squash would either flatten the
            // text or, combined with fontSize being near-literal world units
            // for a non-Canvas TextMeshPro, blow it up to room-filling size.
            var textGO = new GameObject("Tablet Runes");
            textGO.transform.SetParent(transform, true);
            textGO.transform.position = slabGO.transform.TransformPoint(new Vector3(0f, 0.04f, 0f));
            textGO.transform.rotation = slabGO.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            textGO.transform.localScale = Vector3.one * 0.18f;
            _tabletText = textGO.AddComponent<TextMeshPro>();
            _tabletText.fontSize = 3.5f;
            _tabletText.alignment = TextAlignmentOptions.Center;
            _tabletText.color = new Color(0.85f, 0.75f, 0.4f);
        }

        private void BuildVertices()
        {
            Vector3[] starts =
            {
                new Vector3(-0.6f, 1f, BenchZOffset),
                new Vector3(0.6f, 1f, BenchZOffset),
                new Vector3(0f, 1.6f, BenchZOffset)
            };

            for (var i = 0; i < 3; i++)
            {
                var prefab = _dungeon != null ? _dungeon.GetWeightStonePrefab(2) : null;
                var vGO = prefab != null ? Instantiate(prefab, transform) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                vGO.name = $"Rune Pillar {i}";
                if (prefab == null) vGO.transform.SetParent(transform, false);

                // Same fix as EquationEscapeRoomGame's weight stones: a fixed
                // 0.5 collider radius doesn't track the mesh's actual size
                // after scaling, leaving an invisible grab zone far bigger
                // than the visible rock - measure it and match instead.
                var rawRenderer = vGO.GetComponentInChildren<Renderer>();
                var rawSize = rawRenderer != null ? rawRenderer.bounds.size : Vector3.one * 0.3f;
                var rawDiameter = Mathf.Max(rawSize.x, Mathf.Max(rawSize.y, rawSize.z));
                if (rawDiameter < 0.01f) rawDiameter = 0.3f;
                vGO.transform.localScale *= prefab != null ? 0.6f : 0.12f;
                vGO.transform.localPosition = starts[i];

                foreach (var existingCol in vGO.GetComponentsInChildren<Collider>())
                    Destroy(existingCol);
                var sphereCol = vGO.AddComponent<SphereCollider>();
                sphereCol.radius = rawDiameter / 2f;

                var rb = vGO.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.linearDamping = 5f;
                var grab = vGO.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                _vertices[i] = vGO.transform;
            }

            for (var i = 0; i < 3; i++)
            {
                var edge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                edge.name = $"Edge {i}";
                edge.transform.SetParent(transform, false);
                Destroy(edge.GetComponent<Collider>());
                edge.GetComponent<Renderer>().material.color = EdgeColor;
                _edges[i] = edge;
            }
        }

        private void BuildLockLever()
        {
            var leverBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leverBase.name = "Lock Lever Base";
            leverBase.transform.SetParent(transform, false);
            leverBase.transform.localPosition = new Vector3(1.3f, 0.8f, BenchZOffset);
            leverBase.transform.localScale = new Vector3(0.08f, 0.05f, 0.08f);
            leverBase.GetComponent<Renderer>().material.color = new Color(0.15f, 0.16f, 0.2f);

            var leverArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leverArm.name = "Lock Lever Arm";
            leverArm.transform.SetParent(leverBase.transform, false);
            leverArm.transform.localPosition = new Vector3(0f, 3f, 0f);
            leverArm.transform.localScale = new Vector3(0.6f, 3f, 0.6f);
            leverArm.GetComponent<Renderer>().material.color = new Color(0.9f, 0.35f, 0.25f);
            Destroy(leverArm.GetComponent<Collider>());

            var grabCollider = leverBase.AddComponent<BoxCollider>();
            grabCollider.center = new Vector3(0f, 3f, 0f);
            grabCollider.size = new Vector3(1.2f, 4f, 1.2f);
            var grabInteractable = leverBase.AddComponent<XRSimpleInteractable>();
            grabInteractable.selectEntered.AddListener(_ => TryLockShape());

            _leverArm = leverArm.transform;
            _leverClosed = _leverArm.localRotation;
        }

        // ---- Rounds ----

        private void NextChallenge()
        {
            _round++;
            if (_round > TotalRounds)
            {
                ConvaiGuide.Speak($"Every rune pillar has locked true - {_score} of {TotalRounds}. The way out is open.");
                MinigameEnvironment.PlayRoundCompleteVfx(_vertices[0].position);
                if (!_doorSolved)
                {
                    _doorSolved = true;
                    _dungeon?.PlayDoorOpen();
                }
                _awaitingExit = true;
                return;
            }

            _mistakesThisTask = 0;
            _taskStartTime = Time.time;

            // A short curated set of valid triangle-angle triples (must sum to 180).
            int[][] presets =
            {
                new[] { 60, 60, 60 },
                new[] { 90, 45, 45 },
                new[] { 90, 60, 30 },
                new[] { 100, 50, 30 },
                new[] { 70, 70, 40 }
            };
            _targetAngles = presets[(_round - 1) % presets.Length];

            _tabletText.text = $"{_targetAngles[0]}\n{_targetAngles[1]}\n{_targetAngles[2]}";
            ConvaiGuide.Speak(_round == 1
                ? "The tablet's carved with three numbers. Drag the pillars until the shape between them matches - then pull the lever."
                : "The tablet's changed. Same trick - shape the pillars to it.");
            _roundActive = true;
        }

        private void RedrawEdges()
        {
            DrawEdge(_edges[0], _vertices[0].position, _vertices[1].position);
            DrawEdge(_edges[1], _vertices[1].position, _vertices[2].position);
            DrawEdge(_edges[2], _vertices[2].position, _vertices[0].position);
        }

        private static void DrawEdge(GameObject edge, Vector3 a, Vector3 b)
        {
            var mid = (a + b) / 2f;
            edge.transform.position = mid;
            edge.transform.up = (b - a).normalized;
            var length = Vector3.Distance(a, b);
            edge.transform.localScale = new Vector3(0.015f, length / 2f, 0.015f);
        }

        private float[] ComputeAngles()
        {
            var a = _vertices[0].position;
            var b = _vertices[1].position;
            var c = _vertices[2].position;

            var angleA = Vector3.Angle(b - a, c - a);
            var angleB = Vector3.Angle(a - b, c - b);
            var angleC = 180f - angleA - angleB;
            return new[] { angleA, angleB, angleC };
        }

        private void TryLockShape()
        {
            if (!_roundActive) return;

            _leverArm.localRotation = _leverClosed * Quaternion.Euler(-45f, 0f, 0f);
            Invoke(nameof(ResetLever), 0.3f);

            var current = ComputeAngles();
            var sortedCurrent = new[] { current[0], current[1], current[2] };
            System.Array.Sort(sortedCurrent);
            var sortedTarget = new float[] { _targetAngles[0], _targetAngles[1], _targetAngles[2] };
            System.Array.Sort(sortedTarget);

            bool matches = true;
            for (var i = 0; i < 3; i++)
                if (Mathf.Abs(sortedCurrent[i] - sortedTarget[i]) > AngleToleranceDegrees)
                    matches = false;

            if (matches)
            {
                _roundActive = false;
                _score++;
                foreach (var edge in _edges) edge.GetComponent<Renderer>().material.color = CorrectColor;
                HandleSuccess();
                Invoke(nameof(NextChallengeAndResetColor), 1.2f);
            }
            else
            {
                _mistakesThisTask++;
                foreach (var edge in _edges) edge.GetComponent<Renderer>().material.color = WrongColor;
                Invoke(nameof(ResetEdgeColor), 0.4f);
                if (Time.time - _lastFeedbackTime > 3f)
                {
                    _lastFeedbackTime = Time.time;
                    ConvaiGuide.Speak("The lever won't turn - the shape's still off. Keep adjusting the pillars.");
                }
                HandleFailure();
            }
        }

        private void ResetLever() => _leverArm.localRotation = _leverClosed;
        private void ResetEdgeColor() { foreach (var edge in _edges) edge.GetComponent<Renderer>().material.color = EdgeColor; }
        private void NextChallengeAndResetColor() { ResetEdgeColor(); NextChallenge(); }

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
            var angles = ComputeAngles();
            return new LearningTaskData
            {
                subject = Subject,
                minigameId = MinigameId,
                level = 1,
                concept = "triangle angle sum",
                taskDescription = _targetAngles != null ? $"triangle with angles {_targetAngles[0]}, {_targetAngles[1]}, {_targetAngles[2]}" : "",
                playerAnswer = $"{angles[0]:0}, {angles[1]:0}, {angles[2]:0}",
                correctAnswer = _targetAngles != null ? $"{_targetAngles[0]}, {_targetAngles[1]}, {_targetAngles[2]}" : "",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
