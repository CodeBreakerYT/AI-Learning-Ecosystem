using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AILearningEcosystem.Learning;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Math Adventure I - "The Sealed Dungeon" (see the Hollow Spire design
    /// bible). No floating equation UI anywhere - the door is the Adjudicator,
    /// a real balance scale mechanically bolted to it. The target is a carved
    /// number on the scale's own calibration mark (diegetic, like a real
    /// scale needs a target load), never a "3 + 5 = ?" quiz panel. Feedback
    /// ("too heavy", "balanced") comes from Six, the AI companion, reacting
    /// to the mechanism - not a UI string. The player piles grabbable weight
    /// stones (1/2/5/10, scattered through the room) onto the left pan and
    /// watches the beam tilt live until it settles.
    /// </summary>
    public class EquationEscapeRoomGame : MonoBehaviour, IMinigame
    {
        public enum Track { Addition, Subtraction, Multiplication }

        public string MinigameId => "EquationEscapeRoom";
        public string Subject => "Mathematics";

        private const int TotalRounds = 5;
        private const float ShelfWidth = 4.5f;
        private const float ShelfZOffset = -3.2f;
        private const float DoorZOffset = 3.2f;
        private const float MaxTiltDegrees = 20f;
        private const float TiltLerpSpeed = 4f;
        private const float NarrationCooldown = 4f;
        private static readonly int[] Denominations = { 1, 2, 5, 10 };

        private static readonly Color StoneColor = new Color(0.357f, 0.549f, 1f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color NeutralColor = new Color(0.2f, 0.22f, 0.3f);

        public Action<int, int> onComplete;

        private Track _track;
        private int _level = 1;
        private TMP_Text _gauge;
        private TextMeshPro _targetLabel;
        private Renderer _doorRenderer;
        private Transform _lever;
        private Quaternion _leverClosed;
        private Transform _beamPivot;
        private WeightPan _leftPan;
        private float _currentTilt;
        private float _targetTilt;
        private DungeonRoomConfig _dungeon;
        private bool _doorSolved;
        private bool _awaitingExit;
        private readonly List<WeightStone> _stones = new List<WeightStone>();

        // Tri-state so narration only fires on a real transition, not every
        // frame a stone shifts slightly - Six comments when the situation
        // changes, not continuously.
        private enum LoadState { TooLight, Balanced, TooHeavy }
        private LoadState _lastNarratedState = LoadState.TooLight;
        private float _lastNarrationTime;

        private int _round;
        private int _score;
        private int _correctAnswer;
        private int _mistakesThisTask;
        private int _lastAttemptSum;
        private float _taskStartTime;
        private string _concept;
        private string _taskDescription;
        private bool _roundActive;

        public void InitializeGame(int startingLevel)
        {
            _level = Mathf.Max(1, startingLevel);
            _dungeon = FindFirstObjectByType<DungeonRoomConfig>();
            BuildBalanceScale();
        }

        public void StartWith(Track track)
        {
            _track = track;
            _round = 0;
            _score = 0;
            var startingLevel = GameManager.Instance != null ? GameManager.Instance.Difficulty.CurrentLevel(Subject, MinigameId) : 1;
            InitializeGame(startingLevel);
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
        }

        public void StartGame() => NextEquation();

        public static string TrackLabel(Track track)
        {
            switch (track)
            {
                case Track.Addition: return "Addition Chambers";
                case Track.Subtraction: return "Subtraction Chambers";
                default: return "Multiplication Chambers";
            }
        }

        private void Update()
        {
            _currentTilt = Mathf.Lerp(_currentTilt, _targetTilt, Time.deltaTime * TiltLerpSpeed);
            if (_beamPivot != null) _beamPivot.localRotation = Quaternion.Euler(0f, 0f, _currentTilt);

            if (_awaitingExit && _dungeon != null && Camera.main != null &&
                _dungeon.IsPositionInExitRoom(Camera.main.transform.position))
            {
                _awaitingExit = false;
                GameManager.Instance?.EndMinigameSession();
                onComplete?.Invoke(_score, TotalRounds);
            }
        }

        // ---- Build ----

        // A real balance scale: a fulcrum, a tilting beam, a left pan (a
        // WeightPan trigger the player loads stones into) and a right pan
        // whose calibration mark already shows the target - a mechanical
        // gauge, not a quiz answer.
        private void BuildBalanceScale()
        {
            Vector3 doorPos;
            if (_dungeon != null && _dungeon.DoorObject != null)
                doorPos = _dungeon.DoorObject.transform.position;
            else
            {
                var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
                door.name = "Escape Door";
                door.transform.SetParent(transform, false);
                door.transform.localPosition = new Vector3(0f, 1.2f, DoorZOffset + 0.3f);
                door.transform.localScale = new Vector3(2.2f, 2.6f, 0.15f);
                door.GetComponent<Renderer>().material.color = NeutralColor;
                doorPos = door.transform.position;
            }

            var scaleBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            scaleBase.name = "Scale Fulcrum";
            scaleBase.transform.position = doorPos + new Vector3(0f, -0.4f, -0.9f);
            scaleBase.transform.localScale = new Vector3(0.1f, 0.4f, 0.1f);
            scaleBase.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.35f);
            Destroy(scaleBase.GetComponent<Collider>());

            var pivotGO = new GameObject("Beam Pivot");
            pivotGO.transform.position = scaleBase.transform.position + new Vector3(0f, 0.5f, 0f);
            _beamPivot = pivotGO.transform;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            beam.name = "Beam";
            beam.transform.SetParent(_beamPivot, false);
            beam.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            beam.transform.localScale = new Vector3(0.06f, 0.85f, 0.06f);
            beam.GetComponent<Renderer>().material.color = new Color(0.55f, 0.4f, 0.25f);
            Destroy(beam.GetComponent<Collider>());

            // The gauge - a small mechanical readout riveted to the beam
            // itself (world-space TMP on the physical object), not a
            // floating Canvas panel. Shows the current load only, nothing
            // that reads as a quiz.
            var gaugeGO = new GameObject("Load Gauge");
            gaugeGO.transform.SetParent(_beamPivot, false);
            gaugeGO.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            gaugeGO.transform.localScale = Vector3.one * 0.15f;
            var gaugeTmp = gaugeGO.AddComponent<TextMeshPro>();
            gaugeTmp.fontSize = 4f;
            gaugeTmp.alignment = TextAlignmentOptions.Center;
            gaugeTmp.color = StoneColor;
            gaugeTmp.text = "0";
            _gauge = gaugeTmp;

            _leftPan = BuildPan("Left Pan (load stones here)", new Vector3(-0.7f, -0.15f, 0f));
            _leftPan.onSumChanged = HandleSumChanged;
            BuildPanVisual(new Vector3(-0.7f, -0.15f, 0f));

            var rightPanVisual = BuildPanVisual(new Vector3(0.7f, -0.15f, 0f));
            var targetLabelGO = new GameObject("Calibration Mark");
            targetLabelGO.transform.SetParent(transform, true);
            targetLabelGO.transform.position = rightPanVisual.TransformPoint(new Vector3(0f, 0.3f, 0f));
            targetLabelGO.transform.rotation = rightPanVisual.rotation;
            targetLabelGO.transform.localScale = Vector3.one * 0.15f;
            _targetLabel = targetLabelGO.AddComponent<TextMeshPro>();
            _targetLabel.fontSize = 5;
            _targetLabel.alignment = TextAlignmentOptions.Center;
            _targetLabel.color = Color.white;

            _doorRenderer = rightPanVisual.GetComponent<Renderer>();

            var leverBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leverBase.name = "Lever Base";
            leverBase.transform.position = doorPos + new Vector3(1.6f, -0.3f, 0f);
            leverBase.transform.localScale = new Vector3(0.08f, 0.05f, 0.08f);
            leverBase.GetComponent<Renderer>().material.color = new Color(0.15f, 0.16f, 0.2f);

            var leverArm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            leverArm.name = "Lever Arm";
            leverArm.transform.SetParent(leverBase.transform, false);
            leverArm.transform.localPosition = new Vector3(0f, 3f, 0f);
            leverArm.transform.localScale = new Vector3(0.6f, 3f, 0.6f);
            leverArm.GetComponent<Renderer>().material.color = new Color(0.9f, 0.35f, 0.25f);
            Destroy(leverArm.GetComponent<Collider>());

            var grabCollider = leverBase.AddComponent<BoxCollider>();
            grabCollider.center = new Vector3(0f, 3f, 0f);
            grabCollider.size = new Vector3(1.2f, 4f, 1.2f);
            var grabInteractable = leverBase.AddComponent<XRSimpleInteractable>();
            grabInteractable.selectEntered.AddListener(_ => TryPullLever());

            _lever = leverArm.transform;
            _leverClosed = _lever.localRotation;
        }

        private WeightPan BuildPan(string name, Vector3 localOffset)
        {
            var panGO = new GameObject(name);
            panGO.transform.SetParent(_beamPivot, false);
            panGO.transform.localPosition = localOffset;
            var col = panGO.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.5f, 0.6f, 0.5f);
            col.center = new Vector3(0f, 0.2f, 0f);
            return panGO.AddComponent<WeightPan>();
        }

        private Transform BuildPanVisual(Vector3 localOffset)
        {
            var dish = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dish.name = "Pan Dish";
            dish.transform.SetParent(_beamPivot, false);
            dish.transform.localPosition = localOffset - new Vector3(0f, 0.05f, 0f);
            dish.transform.localScale = new Vector3(0.35f, 0.02f, 0.35f);
            dish.GetComponent<Renderer>().material.color = NeutralColor;
            Destroy(dish.GetComponent<Collider>());
            return dish.transform;
        }

        // ---- Rounds ----

        private void NextEquation()
        {
            _round++;
            ClearStones();

            if (_round > TotalRounds)
            {
                // Reaching the exit room is the actual win condition, not
                // this timer - onComplete only fires once the player has
                // physically walked through the open door (checked in
                // Update()). Confirmed live before this fix: the level
                // "cleared" itself 2 seconds after the last correct pull
                // regardless of where the player was standing.
                ConvaiGuide.Speak($"The Adjudicator has nothing left to test - {_score} of {TotalRounds} mechanisms, all correctly balanced. The way out is open.");
                MinigameEnvironment.PlayRoundCompleteVfx(_beamPivot.position);
                _awaitingExit = true;
                return;
            }

            _mistakesThisTask = 0;
            _lastAttemptSum = 0;
            _lastNarratedState = LoadState.TooLight;
            _lastNarrationTime = 0f;
            _taskStartTime = Time.time;

            int a, b;
            string op;
            switch (_track)
            {
                case Track.Subtraction:
                    _concept = "isolating an unknown by reversing an operation";
                    a = UnityEngine.Random.Range(6, 8 + _level * 2);
                    b = UnityEngine.Random.Range(1, a);
                    op = "-";
                    _correctAnswer = a - b;
                    break;
                case Track.Multiplication:
                    _concept = "scaling a known relationship";
                    a = UnityEngine.Random.Range(2, Mathf.Min(6 + _level, 12));
                    b = UnityEngine.Random.Range(2, Mathf.Min(6 + _level, 12));
                    op = "x";
                    _correctAnswer = a * b;
                    break;
                default:
                    _concept = "combining known quantities to match a target";
                    a = UnityEngine.Random.Range(1, 6 + _level * 3);
                    b = UnityEngine.Random.Range(1, 6 + _level * 3);
                    op = "+";
                    _correctAnswer = a + b;
                    break;
            }

            // Kept internally for hint/learning-data purposes only - never
            // rendered anywhere. The player experiences this as a number
            // carved into a calibration mark, not an equation.
            _taskDescription = $"{a} {op} {b} = {_correctAnswer}";

            _targetLabel.text = _correctAnswer.ToString();
            _targetTilt = 0f;
            _gauge.text = "0";
            SetDoorGlow(NeutralColor);
            ConvaiGuide.Speak(RoundIntroLine());
            _roundActive = true;

            SpawnStones();
        }

        // Story framing instead of algebra notation - what Six actually says
        // when a new mechanism activates.
        private string RoundIntroLine()
        {
            switch (_round)
            {
                case 1:
                    return $"The Adjudicator's calibration mark reads {_correctAnswer}. Find what the left pan needs to match it.";
                case 2:
                    return "It's reset itself for another test. Same trick - watch the beam, not the number.";
                default:
                    return "Another lock in the chain. The mark's changed - the mechanism hasn't.";
            }
        }

        // Scattered denomination stones (1/2/5/10), several of each - the
        // player has to COMBINE them to reach the target, not find one
        // labeled object. Guarantees enough of each denomination that every
        // achievable target in this level range can actually be built.
        private void SpawnStones()
        {
            var counts = new Dictionary<int, int> { { 1, 6 }, { 2, 5 }, { 5, 3 }, { 10, 2 } };
            var spawnList = new List<int>();
            foreach (var d in Denominations)
                for (var i = 0; i < counts[d]; i++)
                    spawnList.Add(d);

            for (var i = 0; i < spawnList.Count; i++)
            {
                var denomination = spawnList[i];
                var prefab = _dungeon != null ? _dungeon.GetWeightStonePrefab(denomination) : null;

                // Real rock meshes, sized by denomination - never a primitive
                // cube. Fallback only fires if a scene's DungeonRoomConfig
                // hasn't been wired with rock prefabs yet, and uses a sphere
                // (not a cube) so it still doesn't read as a matching-block game.
                var stoneGO = prefab != null
                    ? Instantiate(prefab, transform)
                    : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stoneGO.name = $"Weight Stone {i}";
                if (prefab == null) stoneGO.transform.SetParent(transform, false);

                // Rock prefabs range wildly in authored scale (rock_tiny is
                // ~0.19m, rock_big_round is a ~2.5m outdoor boulder prop, not
                // a hand prop) - measuring each one's actual mesh size and
                // normalizing to a graspable band, instead of trusting the
                // prefab's native scale, is what keeps a "10" stone from
                // spawning as a wall-clipping boulder while still reading as
                // bigger than a "1" stone.
                var rawRenderer = stoneGO.GetComponentInChildren<Renderer>();
                var rawSize = rawRenderer != null ? rawRenderer.bounds.size : Vector3.one * 0.3f;
                var rawDiameter = Mathf.Max(rawSize.x, Mathf.Max(rawSize.y, rawSize.z));
                if (rawDiameter < 0.01f) rawDiameter = 0.3f;
                var targetDiameter = 0.16f + denomination * 0.02f;
                var scaleFactor = targetDiameter / rawDiameter;
                stoneGO.transform.localScale = Vector3.one * scaleFactor;
                stoneGO.transform.localRotation = Quaternion.Euler(
                    UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(0f, 360f));

                var col = (i - (spawnList.Count - 1) / 2f) / spawnList.Count;
                var x = col * ShelfWidth + UnityEngine.Random.Range(-0.15f, 0.15f);
                var z = ShelfZOffset + UnityEngine.Random.Range(-0.5f, 0.5f);
                stoneGO.transform.localPosition = new Vector3(x, 1f, z);

                // Strip whatever collider(s) the rock mesh brings (imported
                // MeshColliders are often non-convex, which Unity rejects on a
                // non-kinematic Rigidbody) and use one predictable
                // SphereCollider instead. Local radius is derived from the
                // pre-normalization raw mesh diameter (not a fixed 0.5), so
                // the collider scales down with the transform and actually
                // matches the now-normalized visible mesh instead of
                // extending far past small stones or falling short of big ones.
                foreach (var existingCol in stoneGO.GetComponentsInChildren<Collider>())
                    Destroy(existingCol);
                var stoneCol = stoneGO.AddComponent<SphereCollider>();
                stoneCol.radius = rawDiameter / 2f;

                var rb = stoneGO.AddComponent<Rigidbody>();
                rb.mass = 0.3f;
                stoneGO.AddComponent<XRGrabInteractable>();

                var stone = stoneGO.AddComponent<WeightStone>();
                stone.Init(denomination, StoneColor);
                _stones.Add(stone);
            }
        }

        private void ClearStones()
        {
            foreach (var stone in _stones)
                if (stone != null) Destroy(stone.gameObject);
            _stones.Clear();
        }

        private void HandleSumChanged(int sum)
        {
            if (!_roundActive) return;

            _lastAttemptSum = sum;
            _gauge.text = sum.ToString();

            var diff = sum - _correctAnswer;
            _targetTilt = Mathf.Clamp((diff / (float)Mathf.Max(5, _correctAnswer)) * MaxTiltDegrees, -MaxTiltDegrees, MaxTiltDegrees);

            var state = diff == 0 ? LoadState.Balanced : (diff > 0 ? LoadState.TooHeavy : LoadState.TooLight);
            SetDoorGlow(state == LoadState.Balanced ? CorrectColor : NeutralColor);
            NarrateStateChange(state);
        }

        // Six comments like a companion watching the mechanism, not a UI
        // label - only on a real state transition, and never more than once
        // every few seconds, so it reads as reacting rather than narrating
        // every single stone.
        private void NarrateStateChange(LoadState state)
        {
            if (state == _lastNarratedState) return;
            if (Time.time - _lastNarrationTime < NarrationCooldown) return;

            _lastNarratedState = state;
            _lastNarrationTime = Time.time;

            switch (state)
            {
                case LoadState.Balanced:
                    ConvaiGuide.Speak("There - it's settling. Pull the lever before it shifts.");
                    break;
                case LoadState.TooHeavy:
                    ConvaiGuide.Speak("Too much - the beam's straining the wrong way. Take some weight back off.");
                    break;
                default:
                    ConvaiGuide.Speak("Still light. It needs more on that side.");
                    break;
            }
        }

        private void SetDoorGlow(Color color)
        {
            if (_doorRenderer != null) _doorRenderer.material.color = color;
        }

        private void TryPullLever()
        {
            if (!_roundActive) return;

            _lever.localRotation = _leverClosed * Quaternion.Euler(-45f, 0f, 0f);
            Invoke(nameof(ResetLever), 0.3f);

            if (_lastAttemptSum != _correctAnswer)
            {
                _mistakesThisTask++;
                ConvaiGuide.Speak("The lever won't turn - it's not balanced yet.");
                HandleFailure();
                return;
            }

            SubmitAnswer(_correctAnswer.ToString());
        }

        private void ResetLever() => _lever.localRotation = _leverClosed;

        // ---- IMinigame ----

        public void SubmitAnswer(string playerAnswer)
        {
            if (!_roundActive) return;
            _roundActive = false;
            HandleSuccess();
        }

        public void HandleSuccess()
        {
            _score++;
            var data = GetLearningData();
            data.wasCorrect = true;
            GameManager.Instance?.ReportAnswer(data);
            MinigameEnvironment.PlayRoundCompleteVfx(_beamPivot.position);
            ConvaiGuide.Speak("That's it - the door's opening. You didn't guess the number, you isolated it: whatever you added to one side, you matched on the other.");

            if (_round >= TotalRounds && !_doorSolved)
            {
                _doorSolved = true;
                _dungeon?.PlayDoorOpen();
            }

            Invoke(nameof(NextEquation), 2f);
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
                concept = _concept,
                taskDescription = _taskDescription,
                playerAnswer = _lastAttemptSum.ToString(),
                correctAnswer = _correctAnswer.ToString(),
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
