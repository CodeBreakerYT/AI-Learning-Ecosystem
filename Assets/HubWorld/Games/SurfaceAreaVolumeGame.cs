using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Math Topic 2 - "Surface Area &amp; Volume", built on the same idea as
    /// ref/VR-Mathipia's 3D Scan Scene (ScannedData.cs): raycast-voxelize a
    /// shape into a grid of small cubes, then sum each cube's own volume/
    /// surface area to approximate the whole object's - except here the
    /// "scanned" object is a real, always-available primitive (cube, sphere,
    /// cylinder, even the Suzanne monkey mesh ref/VR-Mathipia ships for its
    /// hologram demo) instead of something loaded from a file picker, and the
    /// blocks fly in from a spawn point and animate into place one at a time
    /// instead of appearing instantly - the "animated way place the blocks
    /// and recreate the object" the player asked for.
    ///
    /// Same lesson every voxel-based CAD/3D-printing slicer teaches: a curved
    /// shape's true volume/surface area can be approximated by chopping it
    /// into simple cubes and summing - the finer the cubes, the closer the
    /// approximation gets to the real calculus answer, shown here by also
    /// printing each shape's exact formula result to compare against.
    /// </summary>
    public class SurfaceAreaVolumeGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "SurfaceAreaVolume";
        public string Subject => "Mathematics";

        [Header("Shape references (ported from ref/VR-Mathipia's Holograms demo)")]
        public GameObject suzanneModel; // Assets/VRMathipia/Main/assets/Holograms/Models/Suzzane/Suzzane.fbx
        public Material shapeMaterial;
        public Material blockMaterial;

        private const float SpawnZOffset = 4.5f; // player spawns at local Z~2.2 in this scene - keep the shape comfortably ahead, not overlapping
        private const float CubeSize = 0.08f;
        private const float BlockFlightSeconds = 0.35f;
        private const float DelayBetweenBlocks = 0.015f;
        private const int MaxBlocksPerShape = 400; // keeps the animated build-up watchable, not a multi-minute wait

        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        private enum ShapeKind { Cube, Sphere, Cylinder, Suzanne }

        private readonly ShapeKind[] _sequence = { ShapeKind.Cube, ShapeKind.Sphere, ShapeKind.Cylinder, ShapeKind.Suzanne };

        public System.Action<int, int> onComplete;

        private TMP_Text _questionText;
        private TMP_Text _monitorText;
        private Transform _spawnPoint;
        private Transform _blockSpawnPoint;
        private GameObject _currentShape;
        private GameObject _blockContainer;
        private GameObject _answerButtonsHolder;

        private int _round;
        private int _score;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;
        private bool _awaitingAnswer;
        private float _correctVolume;
        private float _measuredVolume;
        private string _concept;

        public void InitializeGame(int startingLevel)
        {
            BuildHud();
            BuildSpawnPoints();
        }

        public void StartWith()
        {
            _round = 0;
            _score = 0;
            InitializeGame(1);
            GameManager.Instance?.StartMinigameSession(this);
            ConvaiGuide.Speak("Any shape's volume and surface area can be estimated by chopping it into lots of little cubes and adding them up - the same trick a 3D printer slicer uses. Watch each shape get scanned into blocks, then tell me if my block-count estimate for its volume looks right.");
            Invoke(nameof(StartGame), 6f);
        }

        public void StartGame() => NextShape();

        // ---- Build ----

        private void BuildHud()
        {
            var canvasGO = new GameObject("Scan Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.2f, SpawnZOffset + 1.4f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(560, 420);
            canvasGO.transform.localScale = Vector3.one * 0.0028f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(560, 420), PanelColor);
            _questionText = CreateText(panel.transform, "Scanning...", 22, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 175), new Vector2(520, 50), "Question");
            _monitorText = CreateText(panel.transform, "", 16, CorrectColor, TextAlignmentOptions.TopLeft,
                new Vector2(0, -20), new Vector2(500, 320), "Monitor");
        }

        private void BuildSpawnPoints()
        {
            var spawnGO = new GameObject("Shape Spawn Point");
            spawnGO.transform.SetParent(transform, false);
            spawnGO.transform.localPosition = new Vector3(0f, 1.3f, SpawnZOffset);
            _spawnPoint = spawnGO.transform;

            var blockSpawnGO = new GameObject("Block Spawn Point");
            blockSpawnGO.transform.SetParent(transform, false);
            blockSpawnGO.transform.localPosition = new Vector3(1.6f, 2.6f, SpawnZOffset);
            _blockSpawnPoint = blockSpawnGO.transform;
        }

        // ---- Rounds ----

        private void NextShape()
        {
            _round++;
            if (_round > _sequence.Length)
            {
                _questionText.text = "Complete!";
                _monitorText.text = $"Final score: {_score} / {_sequence.Length - 1}\n(the monkey head has no simple formula - shown for fun, not graded.)";
                ConvaiGuide.Speak($"Nice work - you got {_score} out of {_sequence.Length - 1} volume estimates right. Curvier shapes need smaller cubes to approximate well - that's why a real 3D scanner uses thousands of tiny voxels, not four.");
                QuestLog.MarkComplete(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                MinigameEnvironment.PlayRoundCompleteVfx(_spawnPoint.position);
                GameManager.Instance?.EndMinigameSession();
                return;
            }

            _mistakesThisTask = 0;
            _taskStartTime = Time.time;
            _roundActive = true;
            _awaitingAnswer = false;

            var kind = _sequence[_round - 1];
            _concept = $"volume and surface area by voxel decomposition ({kind})";
            _questionText.text = $"Shape {_round}/{_sequence.Length}: {kind}";
            _monitorText.text = "";

            if (_currentShape != null) Destroy(_currentShape);
            if (_blockContainer != null) Destroy(_blockContainer);
            // The previous round's Yes/No buttons were never cleaned up here -
            // they piled up round over round, leaving stale (but still
            // interactable) buttons from earlier shapes floating around
            // alongside the current ones.
            if (_answerButtonsHolder != null) Destroy(_answerButtonsHolder);

            _currentShape = BuildShape(kind);
            StartCoroutine(ScanSequence(kind));
        }

        private GameObject BuildShape(ShapeKind kind)
        {
            GameObject go;
            switch (kind)
            {
                case ShapeKind.Sphere:
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.transform.localScale = Vector3.one * 0.7f;
                    _correctVolume = 4f / 3f * Mathf.PI * Mathf.Pow(0.35f, 3);
                    break;
                case ShapeKind.Cylinder:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                    _correctVolume = Mathf.PI * Mathf.Pow(0.25f, 2) * 1f;
                    break;
                case ShapeKind.Suzanne when suzanneModel != null:
                    go = Instantiate(suzanneModel);
                    go.transform.localScale = Vector3.one * 0.9f;
                    _correctVolume = -1f; // no simple closed-form - ungraded
                    break;
                default:
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.transform.localScale = Vector3.one * 0.6f;
                    _correctVolume = Mathf.Pow(0.6f, 3);
                    break;
            }

            go.name = "Scan Target";
            go.transform.position = _spawnPoint.position;
            go.transform.rotation = Quaternion.identity;
            if (shapeMaterial != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = shapeMaterial;

            // Raycast-voxelization needs real colliders and an exclusive layer
            // so the block scan doesn't also pick up the room/floor/target
            // stand underneath it - same trick ScannedData.cs used.
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.GetComponent<Collider>() == null) filter.gameObject.AddComponent<MeshCollider>();
                filter.gameObject.layer = ScanLayer;
            }
            return go;
        }

        private const int ScanLayer = 30;

        private IEnumerator ScanSequence(ShapeKind kind)
        {
            yield return TypeText("--- INITIALIZING SCAN ---\n");
            yield return new WaitForSeconds(0.4f);

            var renderers = _currentShape.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            yield return TypeText($"Extents: {bounds.size.x:F2} x {bounds.size.y:F2} x {bounds.size.z:F2} m\n\n");
            yield return TypeText("--- BLOCK-OUT: SCANNING VOXELS ---\n");

            _blockContainer = new GameObject("Block Container");
            _blockContainer.transform.SetParent(transform, false);

            var positions = VoxelizePositions(bounds);
            if (positions.Count > MaxBlocksPerShape)
            {
                // Evenly thin out rather than just truncating, so the animated
                // build-up still reads as "the whole shape," not a corner of it.
                var stride = Mathf.CeilToInt(positions.Count / (float)MaxBlocksPerShape);
                var thinned = new List<Vector3>();
                for (var i = 0; i < positions.Count; i += stride) thinned.Add(positions[i]);
                positions = thinned;
            }

            foreach (var pos in positions)
            {
                SpawnBlockAnimated(pos);
                yield return new WaitForSeconds(DelayBetweenBlocks);
            }

            yield return new WaitForSeconds(BlockFlightSeconds);

            var blockCount = positions.Count;
            _measuredVolume = blockCount * CubeSize * CubeSize * CubeSize;
            var surfaceArea = blockCount * 6f * CubeSize * CubeSize; // outer-surface overcount is the same simplification ScannedData.cs made - good enough for "which is bigger/smaller" intuition, not exact

            yield return TypeText($"\nBlocks placed: {blockCount}\n\n");
            yield return TypeText("--- CALCULATIONS ---\n");
            yield return TypeText($"Volume  ~= {blockCount} x {CubeSize:F2}^3 = {_measuredVolume:F3} m^3\n");
            yield return TypeText($"Surface ~= {blockCount} x 6 x {CubeSize:F2}^2 = {surfaceArea:F3} m^2\n\n");

            if (_correctVolume > 0f)
            {
                yield return TypeText($"Exact formula volume: {_correctVolume:F3} m^3\n\n");
                yield return TypeText("Does the block estimate look about right for this shape? Point and select: Yes / No\n");
                _awaitingAnswer = true;
                BuildYesNoButtons();
            }
            else
            {
                yield return TypeText("(No simple formula for this one - just enjoy the monkey.)\n\n");
                yield return new WaitForSeconds(2f);
                NextShape();
            }
        }

        // Same axis-aligned top/bottom raycast approach as ScannedData.cs's
        // VoxelizeMesh - simple, and fine for the convex-ish primitives here.
        private List<Vector3> VoxelizePositions(Bounds b)
        {
            var result = new List<Vector3>();
            var resX = Mathf.Clamp(Mathf.CeilToInt(b.size.x / CubeSize), 1, 60);
            var resZ = Mathf.Clamp(Mathf.CeilToInt(b.size.z / CubeSize), 1, 60);
            var resY = Mathf.Clamp(Mathf.CeilToInt(b.size.y / CubeSize), 1, 60);
            var layerMask = 1 << ScanLayer;

            for (var x = 0; x < resX; x++)
            {
                for (var z = 0; z < resZ; z++)
                {
                    var worldX = b.min.x + x * CubeSize + CubeSize / 2f;
                    var worldZ = b.min.z + z * CubeSize + CubeSize / 2f;
                    var rayTop = new Vector3(worldX, b.max.y + 1f, worldZ);
                    var rayBot = new Vector3(worldX, b.min.y - 1f, worldZ);

                    if (Physics.Raycast(rayTop, Vector3.down, out var topHit, 100f, layerMask) &&
                        Physics.Raycast(rayBot, Vector3.up, out var botHit, 100f, layerMask))
                    {
                        for (var y = 0; y < resY; y++)
                        {
                            var worldY = b.min.y + y * CubeSize + CubeSize / 2f;
                            if (worldY >= botHit.point.y && worldY <= topHit.point.y)
                                result.Add(new Vector3(worldX, worldY, worldZ));
                        }
                    }
                }
            }
            return result;
        }

        private void SpawnBlockAnimated(Vector3 targetWorldPos)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Voxel";
            block.transform.SetParent(_blockContainer.transform, true);
            block.transform.position = _blockSpawnPoint.position;
            block.transform.localScale = Vector3.one * CubeSize;
            Destroy(block.GetComponent<Collider>());
            if (blockMaterial != null) block.GetComponent<Renderer>().sharedMaterial = blockMaterial;
            StartCoroutine(FlyBlock(block.transform, targetWorldPos));
        }

        private IEnumerator FlyBlock(Transform block, Vector3 targetPos)
        {
            var start = block.position;
            var t = 0f;
            while (t < 1f)
            {
                if (block == null) yield break;
                t += Time.deltaTime / BlockFlightSeconds;
                block.position = Vector3.Lerp(start, targetPos, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            if (block != null) block.position = targetPos;
        }

        private void BuildYesNoButtons()
        {
            var holder = new GameObject("Answer Buttons");
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = new Vector3(0f, 1.1f, SpawnZOffset - 1.2f);
            _answerButtonsHolder = holder;

            BuildAnswerButton(holder.transform, new Vector3(-0.9f, 0f, 0f), "Yes", true);
            BuildAnswerButton(holder.transform, new Vector3(0.9f, 0f, 0f), "No", false);
        }

        private void BuildAnswerButton(Transform parent, Vector3 localPos, string label, bool answerIsYes)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Answer " + label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(0.5f, 0.2f, 0.05f);
            go.GetComponent<Renderer>().material.color = new Color(0.3f, 0.35f, 0.45f);

            var textGO = new GameObject("Label");
            textGO.transform.SetParent(go.transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.55f);
            textGO.transform.localScale = new Vector3(2f, 5f, 1f);
            var tmp = textGO.AddComponent<TextMeshPro>();
            tmp.text = label;
            tmp.fontSize = 3f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var target = go.AddComponent<AnswerTarget>();
            target.onSelected = () => HandleAnswer(answerIsYes, target);
        }

        private void HandleAnswer(bool answeredYes, AnswerTarget target)
        {
            if (!_awaitingAnswer) return;
            _awaitingAnswer = false;

            // "About right" means within a generous 20% relative error -
            // matches the intuition question being asked, not a precise check.
            var relError = Mathf.Abs(_measuredVolume - _correctVolume) / _correctVolume;
            var estimateWasGood = relError <= 0.2f;
            var correct = answeredYes == estimateWasGood;

            target.Flash(correct ? CorrectColor : WrongColor, correct);
            _monitorText.text += correct
                ? "\nCorrect! "
                : "\nNot quite. ";
            _monitorText.text += estimateWasGood
                ? $"The block estimate was within {relError * 100f:0}% of the real volume - close enough."
                : $"The blocks were off by {relError * 100f:0}% - this shape's curves need smaller cubes for a fair estimate.";

            if (correct) { _score++; HandleSuccess(); }
            else { _mistakesThisTask++; HandleFailure(); }

            Invoke(nameof(NextShape), 2.5f);
        }

        private IEnumerator TypeText(string text)
        {
            foreach (var c in text)
            {
                _monitorText.text += c;
                yield return new WaitForSeconds(0.012f);
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
                taskDescription = "estimate a shape's volume from its voxel block-out",
                playerAnswer = "",
                correctAnswer = $"{_correctVolume:F2} m^3",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
