using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Physics Minigame 2 - "Newton's Force Arena" (design doc section 6.2).
    /// Class kept named LeversGame so this scene's existing serialized
    /// MonoBehaviour reference (Assets/PlatformScenes/Physics/Levers.unity)
    /// keeps resolving without a scene edit - the old torque-balance click
    /// game has been fully replaced by this open push-physics sandbox.
    /// A challenge card names a target time; the player physically shoves a
    /// mass-labeled box (via PushableBox - no grab, real momentum) to a
    /// glowing target ring before the timer runs out, with a live force/
    /// acceleration readout so F = ma is visible while it's happening.
    ///
    /// The arena (HUD, box, target ring) is built in Awake() and persists as
    /// real scene content - [ExecuteAlways] means it also builds in the Editor
    /// so the layout is visible and tweakable in Scene view without entering
    /// Play mode. Only round state (mass/timer/score) is runtime-only.
    /// </summary>
    [ExecuteAlways]
    public class LeversGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "NewtonsForceArena";
        public string Subject => "Physics";

        public GameObject barrelPrefab;

        private const int TotalRounds = 5;
        private const float BoxStartZOffset = 1.2f;
        private const float CaptureRadius = 0.5f;

        private static readonly Color BoxColor = new Color(0.133f, 0.827f, 0.933f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        private TMP_Text _questionText;
        private TMP_Text _feedbackText;
        private TMP_Text _readoutText;

        private Rigidbody _boxRb;
        private PushableBox _pushable;
        private TextMeshPro _massLabel;
        private Transform _boxStart;
        private Transform _target;
        private Renderer _targetRenderer;

        private int _round;
        private int _score;
        private int _level = 1;
        private float _boxMass;
        private float _timeLimit;
        private float _timeRemaining;
        private float _taskStartTime;
        private int _attemptsThisChallenge;
        private bool _roundActive;
        private bool _playSessionStarted;

        private void Awake()
        {
            if (transform.Find("Force Arena Canvas") == null)
                BuildStatic();
            else
                RediscoverReferences();

            if (!Application.isPlaying || _playSessionStarted) return;
            _playSessionStarted = true;

            EnsureEventSystem();
            NavTabBar.Build(transform);
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
            // The teacher's ConvaiNPC only finishes setting up its gRPC client
            // a frame after her own Start() runs - speaking this same frame
            // (confirmed live) silently drops with no audio/caption at all.
            Invoke(nameof(SpeakWelcome), 1.5f);
        }

        private void SpeakWelcome() =>
            ConvaiGuide.Speak("Newton's Force Arena. Shove the box to the target ring before time runs out - the heavier it is, the harder you'll need to push.");

        private void Update()
        {
            if (!Application.isPlaying || !_roundActive) return;

            _timeRemaining -= Time.deltaTime;

            var accel = _boxMass > 0f ? _pushable.LastAppliedForceMagnitude / _boxMass : 0f;
            _readoutText.text = $"Force: {_pushable.LastAppliedForceMagnitude:0.0} N   Accel: {accel:0.0} m/s²   Time: {Mathf.Max(0, _timeRemaining):0.0}s";

            if (Vector3.Distance(_boxRb.position, _target.position) < CaptureRadius)
            {
                CompleteChallenge(true);
            }
            else if (_timeRemaining <= 0f)
            {
                CompleteChallenge(false);
            }
        }

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
            BuildHud();
            BuildArena();
        }

        private void RediscoverReferences()
        {
            var panel = transform.Find("Force Arena Canvas/Panel");
            _questionText = panel.Find("Question")?.GetComponent<TMP_Text>();
            _readoutText = panel.Find("Readout")?.GetComponent<TMP_Text>();
            _feedbackText = panel.Find("Feedback")?.GetComponent<TMP_Text>();

            var boxGO = transform.Find("Pushable Barrel");
            _boxRb = boxGO.GetComponent<Rigidbody>();
            _pushable = boxGO.GetComponent<PushableBox>();
            _massLabel = boxGO.Find("Mass Label")?.GetComponent<TextMeshPro>();

            _boxStart = transform.Find("Box Start");
            _target = transform.Find("Target Ring");
            _targetRenderer = _target != null ? _target.GetComponent<Renderer>() : null;
        }

        private void BuildHud()
        {
            var canvasGO = new GameObject("Force Arena Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2f, BoxStartZOffset - 0.9f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 260);
            canvasGO.transform.localScale = Vector3.one * 0.003f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(560, 260), PanelColor);
            _questionText = CreateText(panel.transform, "Push the box to the ring.", 26, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 70), new Vector2(520, 90), "Question");
            _readoutText = CreateText(panel.transform, "", 20, BoxColor, TextAlignmentOptions.Center,
                new Vector2(0, 0), new Vector2(520, 40), "Readout");
            _feedbackText = CreateText(panel.transform, "Grip the box and shove it toward the glowing ring.", 18, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, -60), new Vector2(520, 50), "Feedback");
        }

        private void BuildArena()
        {
            // A real pushable barrel prop, not a primitive - the F=ma demo
            // still works identically since PushableBox just needs a
            // Rigidbody+Collider, whatever the mesh is.
            GameObject boxGO;
            if (barrelPrefab != null)
            {
                boxGO = Instantiate(barrelPrefab, transform);
                boxGO.name = "Pushable Barrel";
                boxGO.transform.localScale = Vector3.one * 0.45f;
                boxGO.transform.localPosition = new Vector3(0f, 0.33f, BoxStartZOffset);
                var col = boxGO.AddComponent<CapsuleCollider>();
                col.height = 1.4f;
                col.radius = 0.5f;
            }
            else
            {
                boxGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                boxGO.name = "Pushable Barrel";
                boxGO.transform.SetParent(transform, false);
                boxGO.transform.localScale = new Vector3(0.35f, 0.3f, 0.35f);
                boxGO.transform.localPosition = new Vector3(0f, 0.3f, BoxStartZOffset);
                boxGO.GetComponent<Renderer>().material.color = BoxColor;
            }
            _boxRb = boxGO.AddComponent<Rigidbody>();
            _boxRb.linearDamping = 0.4f;
            _pushable = boxGO.AddComponent<PushableBox>();

            var labelGO = new GameObject("Mass Label");
            labelGO.transform.SetParent(boxGO.transform, false);
            labelGO.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            labelGO.transform.localScale = Vector3.one * 0.3f;
            var label = labelGO.AddComponent<TextMeshPro>();
            label.text = "10kg";
            label.fontSize = 4f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            _massLabel = label;

            var startGO = new GameObject("Box Start");
            startGO.transform.SetParent(transform, false);
            startGO.transform.localPosition = new Vector3(0f, 0.33f, BoxStartZOffset);
            _boxStart = startGO.transform;

            var targetGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            targetGO.name = "Target Ring";
            targetGO.transform.SetParent(transform, false);
            targetGO.transform.localScale = new Vector3(CaptureRadius * 2f, 0.02f, CaptureRadius * 2f);
            targetGO.transform.localPosition = new Vector3(0f, 0.02f, BoxStartZOffset + 2.5f);
            if (Application.isPlaying) Destroy(targetGO.GetComponent<Collider>()); else DestroyImmediate(targetGO.GetComponent<Collider>());
            _targetRenderer = targetGO.GetComponent<Renderer>();
            _targetRenderer.material.color = CorrectColor * 0.6f;
            _target = targetGO.transform;
        }

        // ---- Rounds ----

        public void InitializeGame(int startingLevel) => _level = Mathf.Max(1, startingLevel);

        public void StartGame() => NextChallenge();

        private void NextChallenge()
        {
            _round++;
            if (_round > TotalRounds)
            {
                _questionText.text = "Complete!";
                _feedbackText.text = $"Score: {_score} / {TotalRounds}";
                _readoutText.text = "";
                ConvaiGuide.Speak($"You cleared {_score} out of {TotalRounds} challenges in the arena - solid work.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
                MinigameEnvironment.PlayRoundCompleteVfx(_target.position);
                GameManager.Instance?.EndMinigameSession();
                return;
            }

            _level = GameManager.Instance != null ? GameManager.Instance.Difficulty.CurrentLevel(Subject, MinigameId) : _level;
            _boxMass = Mathf.Clamp(4f + _level * 2.5f, 4f, 20f);
            _boxRb.mass = _boxMass;
            var distance = Mathf.Clamp(1.5f + _level * 0.6f, 1.5f, 4.5f);
            _timeLimit = Mathf.Clamp(6f - _level * 0.5f, 2.5f, 6f);
            _timeRemaining = _timeLimit;
            _attemptsThisChallenge = 0;
            _taskStartTime = Time.time;

            _boxRb.position = _boxStart.position;
            _boxRb.linearVelocity = Vector3.zero;
            _boxRb.angularVelocity = Vector3.zero;
            _target.localPosition = new Vector3(0f, 0.02f, BoxStartZOffset + distance);
            _massLabel.text = $"{_boxMass:0}kg";
            _targetRenderer.material.color = CorrectColor * 0.6f;

            _questionText.text = $"Push the {_boxMass:0}kg box to the ring in {_timeLimit:0.0}s.";
            _feedbackText.text = "Grip the box and shove it toward the glowing ring.";
            _roundActive = true;
        }

        private void CompleteChallenge(bool success)
        {
            if (!_roundActive) return;
            _roundActive = false;
            _attemptsThisChallenge++;

            if (success)
            {
                _score++;
                _targetRenderer.material.color = CorrectColor;
                _feedbackText.text = "Target reached!";
                HandleSuccess();
                Invoke(nameof(NextChallenge), 1.2f);
            }
            else
            {
                _targetRenderer.material.color = WrongColor;
                _feedbackText.text = "Out of time - one more try at this challenge.";
                HandleFailure();
                Invoke(nameof(RetryChallenge), 1.2f);
            }
        }

        private void RetryChallenge()
        {
            _round--; // re-run the same challenge slot rather than skipping ahead
            NextChallenge();
        }

        // ---- IMinigame ----

        public void SubmitAnswer(string playerAnswer) { /* the physical capture check in Update() is the submit action */ }

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
                level = _level,
                concept = "Newton's second law (F = ma)",
                taskDescription = $"push a {_boxMass:0}kg box to the target in {_timeLimit:0.0}s",
                playerAnswer = "",
                correctAnswer = "reach the target ring before time runs out",
                mistakeCount = _attemptsThisChallenge,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
