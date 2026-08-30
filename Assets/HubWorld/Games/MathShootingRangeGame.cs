using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Math Minigame 4 - "Shooting Range". Ported from ref/VR-math-game: its
    /// core idea (grab a real pistol, shoot the floating block that has the
    /// right answer, wrong blocks nudge you, the right one shatters) and its
    /// actual Pistol model - not its keyboard/desktop input or its bare
    /// local quiz loop, which had no tutor at all. Every question here is
    /// spoken and explained by the Convai teacher: what the operation means
    /// before the first round of its kind, a real hint (not just "wrong")
    /// when a shot misses, and a worked-out confirmation when it lands - the
    /// "guided, learns the topic" workflow the ported prototype didn't have.
    ///
    /// A brand-new scene, not a reskin of MathCannon - the user asked for
    /// this to stay separate rather than merge with that existing minigame.
    /// Built in Awake() and persists as real scene content - [ExecuteAlways]
    /// means it also builds in the Editor so the range is visible without
    /// Play mode. Only round/question state is runtime-only.
    /// </summary>
    [ExecuteAlways]
    public class MathShootingRangeGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "MathShootingRange";
        public string Subject => "Mathematics";

        [Header("Pistol art (ported from ref/VR-math-game)")]
        public GameObject pistolModel;
        public Material pistolMaterial;

        private const int TotalRounds = 8;
        private const float StandZOffset = 0.6f;
        private const float BlockDistance = 4f;
        private const float BulletSpeed = 14f;

        // A real firing bay (Valorant-range style: an open entrance behind
        // the player, side walls flanking the lane, a solid backstop wall
        // behind the targets) instead of relying on MinigameEnvironment's
        // generic fully-enclosed 12x12 room - that room has no doorway at
        // all, and this scene's player/teacher spawn points sit outside it
        // entirely (same "spawns outside a sealed box" bug documented for
        // MathCannon/NewtonsLaws), which is why the room was unreachable.
        private const float LaneHalfWidth = 2.75f;
        private const float EntranceZ = -2f;
        private const float BackWallZ = 6.5f;
        private const float RangeWallHeight = 4f;

        private static readonly Color BlockColor = new Color(0.357f, 0.549f, 1f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);
        private static readonly Color RangeWallColor = new Color(0.14f, 0.15f, 0.18f);
        private static readonly Color RangeFloorColor = new Color(0.22f, 0.23f, 0.27f);
        private static readonly Color RangeAccentColor = new Color(0.357f, 0.549f, 1f);

        private TMP_Text _questionText;
        private TMP_Text _feedbackText;
        private TMP_Text _scoreText;
        private Transform _muzzle;
        private XRGrabInteractable _pistolGrab;
        private AudioSource _audio;

        private readonly List<MathAnswerBlock> _blocks = new List<MathAnswerBlock>();

        private int _round;
        private int _score;
        private int _level = 1;
        private int _correctAnswer;
        private string _concept;
        private string _op;
        private string _taskDescription;
        private float _taskStartTime;
        private int _mistakesThisTask;
        private bool _roundActive;
        private bool _playSessionStarted;
        private readonly HashSet<string> _conceptsIntroduced = new HashSet<string>();

        private void Awake()
        {
            if (transform.Find("Range Canvas") == null)
                BuildStatic();
            else
                RediscoverReferences();

            if (!Application.isPlaying || _playSessionStarted) return;
            _playSessionStarted = true;

            EnsureEventSystem();
            NavTabBar.Build(transform);
            GameManager.Instance?.StartMinigameSession(this);
            StartGame();
            Invoke(nameof(SpeakWelcome), 1.5f);
        }

        private void SpeakWelcome() =>
            ConvaiGuide.Speak("Welcome to the Shooting Range. I'll show you a problem - grab the pistol, aim at the block with the right answer, and pull the trigger.");

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
            BuildRange();
            BuildHud();
            BuildPistol();
        }

        // Open-fronted firing bay: side walls + a backstop wall behind the
        // targets, no wall at the entrance (south end) so the player can
        // just walk straight in from their spawn point instead of hitting a
        // sealed box.
        private void BuildRange()
        {
            var laneWidth = LaneHalfWidth * 2f;
            var laneDepth = BackWallZ - EntranceZ;
            var laneCenterZ = (EntranceZ + BackWallZ) / 2f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Range Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, laneCenterZ);
            floor.transform.localScale = new Vector3(laneWidth, 0.2f, laneDepth);
            floor.GetComponent<Renderer>().material.color = RangeFloorColor;

            BuildRangeWall(new Vector3(0f, RangeWallHeight / 2f, BackWallZ), new Vector3(laneWidth, RangeWallHeight, 0.3f));
            BuildRangeWall(new Vector3(-LaneHalfWidth, RangeWallHeight / 2f, laneCenterZ), new Vector3(0.3f, RangeWallHeight, laneDepth));
            BuildRangeWall(new Vector3(LaneHalfWidth, RangeWallHeight / 2f, laneCenterZ), new Vector3(0.3f, RangeWallHeight, laneDepth));

            // Emissive target backdrop stripe - reads as "this is the range's
            // business end" the way a real gun range's backstop is marked.
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "Backstop Accent Stripe";
            stripe.transform.SetParent(transform, false);
            stripe.transform.localPosition = new Vector3(0f, 1.9f, BackWallZ - 0.05f);
            stripe.transform.localScale = new Vector3(laneWidth * 0.9f, 0.5f, 0.05f);
            var stripeMat = stripe.GetComponent<Renderer>().material;
            stripeMat.color = RangeAccentColor;
            stripeMat.EnableKeyword("_EMISSION");
            stripeMat.SetColor("_EmissionColor", RangeAccentColor * 0.6f);
            SafeDestroy(stripe.GetComponent<Collider>());

            // Firing-line marker on the floor at the player's stand point.
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "Firing Line";
            line.transform.SetParent(transform, false);
            line.transform.localPosition = new Vector3(0f, 0.001f, StandZOffset - 0.5f);
            line.transform.localScale = new Vector3(laneWidth - 0.4f, 0.01f, 0.08f);
            var lineMat = line.GetComponent<Renderer>().material;
            lineMat.color = RangeAccentColor;
            lineMat.EnableKeyword("_EMISSION");
            lineMat.SetColor("_EmissionColor", RangeAccentColor * 0.5f);
            SafeDestroy(line.GetComponent<Collider>());
        }

        private void BuildRangeWall(Vector3 localPos, Vector3 scale)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Range Wall";
            wall.transform.SetParent(transform, false);
            wall.transform.localPosition = localPos;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().material.color = RangeWallColor;
        }

        private void RediscoverReferences()
        {
            var panel = transform.Find("Range Canvas/Panel");
            _questionText = panel.Find("Question")?.GetComponent<TMP_Text>();
            _feedbackText = panel.Find("Feedback")?.GetComponent<TMP_Text>();
            _scoreText = panel.Find("Score")?.GetComponent<TMP_Text>();

            var stand = transform.Find("Pistol Stand");
            _muzzle = stand != null ? stand.Find("Muzzle") : null;
            _pistolGrab = stand != null ? stand.GetComponentInChildren<XRGrabInteractable>() : null;
            _audio = stand != null ? stand.GetComponent<AudioSource>() : null;
            if (_pistolGrab != null) _pistolGrab.activated.AddListener(_ => Fire());
        }

        private void BuildHud()
        {
            var canvasGO = new GameObject("Range Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.3f, StandZOffset + 1.4f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(600, 260);
            canvasGO.transform.localScale = Vector3.one * 0.003f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(600, 260), PanelColor);
            _questionText = CreateText(panel.transform, "Get ready...", 30, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 70), new Vector2(560, 80), "Question");
            _scoreText = CreateText(panel.transform, "Score: 0 / " + TotalRounds, 18, BlockColor, TextAlignmentOptions.Center,
                new Vector2(0, 10), new Vector2(560, 40), "Score");
            _feedbackText = CreateText(panel.transform, "Grab the pistol and aim at the right answer.", 16, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, -55), new Vector2(560, 60), "Feedback");
        }

        private void BuildPistol()
        {
            var standGO = new GameObject("Pistol Stand");
            standGO.transform.SetParent(transform, false);
            standGO.transform.localPosition = new Vector3(0f, 1.1f, StandZOffset);
            standGO.transform.localRotation = Quaternion.identity;

            GameObject pistolVisual;
            if (pistolModel != null)
            {
                pistolVisual = Instantiate(pistolModel, standGO.transform);
                pistolVisual.name = "Pistol Visual";
                pistolVisual.transform.localPosition = Vector3.zero;
                pistolVisual.transform.localRotation = Quaternion.identity;
                ApplyMaterial(pistolVisual, pistolMaterial);
            }
            else
            {
                pistolVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pistolVisual.transform.SetParent(standGO.transform, false);
                pistolVisual.transform.localScale = new Vector3(0.05f, 0.12f, 0.2f);
            }
            StripColliders(pistolVisual);

            var grip = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            grip.name = "Grip Handle";
            grip.transform.SetParent(standGO.transform, false);
            grip.transform.localScale = new Vector3(0.06f, 0.09f, 0.06f);
            grip.GetComponent<Renderer>().enabled = false;
            var rb = grip.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            _pistolGrab = standGO.AddComponent<XRGrabInteractable>();
            _pistolGrab.throwOnDetach = false;
            // XRGrabInteractable requires a Rigidbody and Unity auto-adds one
            // with gravity on - left alone, the whole stand free-falls from
            // the moment Play starts (confirmed live: ended up thousands of
            // meters underground within a minute).
            var standRb = standGO.GetComponent<Rigidbody>();
            if (standRb != null) standRb.useGravity = false;
            if (Application.isPlaying) _pistolGrab.activated.AddListener(_ => Fire());

            // A real holster, not just a fixed-in-place prop: an
            // XRSocketInteractor (the same standard XRI pattern this
            // project's own earlier prototype scene used for a cleaner
            // grab/return feel than a bare stand) so letting go of the
            // pistol near its stand snaps it cleanly back into place
            // instead of leaving it floating wherever it was released -
            // "there is a smoother way to do it, use sockets". A SIBLING of
            // the pistol, not a child of it - the socket has to stay fixed
            // at the stand's own position in the world, not follow the
            // pistol's transform once it's picked up and carried away.
            var holsterGO = new GameObject("Pistol Holster");
            holsterGO.transform.SetParent(transform, false);
            holsterGO.transform.localPosition = standGO.transform.localPosition;
            holsterGO.transform.localRotation = standGO.transform.localRotation;
            var holsterCollider = holsterGO.AddComponent<SphereCollider>();
            holsterCollider.isTrigger = true;
            holsterCollider.radius = 0.12f;
            var socket = holsterGO.AddComponent<XRSocketInteractor>();
            socket.startingSelectedInteractable = _pistolGrab;

            var muzzleGO = new GameObject("Muzzle");
            muzzleGO.transform.SetParent(standGO.transform, false);
            // Local Y matches the answer blocks' own height (1.3 world, ~0.2
            // above the stand) rather than the pistol's own grip height - a
            // muzzle at grip height (0.02) points at each block's bottom
            // edge, and even a dead-level shot then falls under it well
            // before reaching a block 4m away (confirmed: a level shot from
            // the old 0.02 offset drops ~0.4m over that distance once
            // gravity is applied, landing nowhere near a block spanning
            // 1.1-1.5). Aiming level with the blocks' own center fixes both
            // at once, independent of the gravity fix below.
            muzzleGO.transform.localPosition = new Vector3(0f, 0.2f, 0.15f);
            _muzzle = muzzleGO.transform;

            _audio = standGO.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
        }

        private static void ApplyMaterial(GameObject root, Material mat)
        {
            if (mat == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var count = Mathf.Max(1, renderer.sharedMaterials.Length);
                var mats = new Material[count];
                for (var i = 0; i < count; i++) mats[i] = mat;
                renderer.sharedMaterials = mats;
            }
        }

        private static void StripColliders(GameObject root)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>())
                SafeDestroy(col);
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ---- IMinigame ----

        public void InitializeGame(int startingLevel) => _level = Mathf.Max(1, startingLevel);
        public void StartGame() => NextProblem();

        // ---- Rounds ----

        private void NextProblem()
        {
            _round++;
            ClearBlocks();

            if (_round > TotalRounds)
            {
                _questionText.text = "Complete!";
                _feedbackText.text = "";
                ConvaiGuide.Speak($"You solved {_score} out of {TotalRounds} - nice shooting, and nice math.");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
                MinigameEnvironment.PlayRoundCompleteVfx(_muzzle.position);
                GameManager.Instance?.EndMinigameSession();
                return;
            }

            _level = GameManager.Instance != null ? GameManager.Instance.Difficulty.CurrentLevel(Subject, MinigameId) : _level;
            _mistakesThisTask = 0;
            _taskStartTime = Time.time;

            int a, b;
            if (_level <= 2)
            {
                _concept = "addition and subtraction";
                _op = Random.value < 0.5f ? "+" : "-";
                a = Random.Range(2, 10 + _level * 3);
                b = Random.Range(1, a);
                _correctAnswer = _op == "+" ? a + b : a - b;
            }
            else
            {
                _concept = "multiplication";
                _op = "x";
                a = Random.Range(2, Mathf.Min(6 + _level, 12));
                b = Random.Range(2, Mathf.Min(6 + _level, 12));
                _correctAnswer = a * b;
            }

            _taskDescription = $"{a} {_op} {b}";
            _questionText.text = $"What is {a} {_op} {b}?";
            _scoreText.text = $"Score: {_score} / {TotalRounds}";
            _feedbackText.text = "Aim at the right answer and pull the trigger.";

            var intro = !_conceptsIntroduced.Contains(_concept);
            _conceptsIntroduced.Add(_concept);
            ConvaiGuide.Speak(intro ? IntroLineFor(_concept, _taskDescription) : $"What is {a} {_op} {b}?");

            SpawnBlocks();
            _roundActive = true;
        }

        private static string IntroLineFor(string concept, string task) => concept switch
        {
            "addition and subtraction" => $"Let's practice addition and subtraction. {task} means combining or taking away amounts - work it out, then shoot the block with that answer.",
            "multiplication" => $"Now multiplication - think of {task} as repeated addition, adding one number that many times. Shoot the block with the right total.",
            _ => task
        };

        private void SpawnBlocks()
        {
            var values = new HashSet<int> { _correctAnswer };
            var spread = Mathf.Max(4, _correctAnswer);
            var safety = 0;
            while (values.Count < 3 && safety < 1000)
            {
                var c = _correctAnswer + Random.Range(-spread, spread + 1);
                if (c >= 0 && c != _correctAnswer) values.Add(c);
                safety++;
            }

            var shuffled = new List<int>(values);
            for (var i = 0; i < shuffled.Count; i++)
            {
                var j = Random.Range(i, shuffled.Count);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            for (var i = 0; i < shuffled.Count; i++)
            {
                var blockGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blockGO.name = $"Answer Block {i}";
                blockGO.transform.SetParent(transform, false);
                blockGO.transform.localScale = Vector3.one * 0.4f;
                var x = (i - (shuffled.Count - 1) / 2f) * 1.2f;
                blockGO.transform.localPosition = new Vector3(x, 1.3f, StandZOffset + BlockDistance);
                blockGO.GetComponent<Renderer>().material.color = BlockColor;
                blockGO.AddComponent<Rigidbody>().isKinematic = true;

                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(blockGO.transform, false);
                labelGO.transform.localPosition = new Vector3(0f, 0f, -0.55f);
                labelGO.transform.localScale = Vector3.one * 2.2f;
                var label = labelGO.AddComponent<TextMeshPro>();
                label.text = shuffled[i].ToString();
                label.fontSize = 8;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;

                var block = blockGO.AddComponent<MathAnswerBlock>();
                block.Init(shuffled[i], shuffled[i] == _correctAnswer, HandleBlockHit);
                _blocks.Add(block);
            }
        }

        private void ClearBlocks()
        {
            foreach (var b in _blocks) if (b != null) Destroy(b.gameObject);
            _blocks.Clear();
        }

        private void Fire()
        {
            if (!_roundActive || _muzzle == null) return;

            var bulletGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletGO.name = "Bullet";
            bulletGO.transform.position = _muzzle.position;
            bulletGO.transform.localScale = Vector3.one * 0.05f;
            bulletGO.GetComponent<Renderer>().material.color = Color.yellow;
            var rb = bulletGO.AddComponent<Rigidbody>();
            // A dropped-by-gravity bullet is a real ballistic weapon
            // (correct for ArcheryBow's arrow), but this is a flat,
            // point-and-click shooting-gallery target, not a ballistics
            // lesson - a shot that reads as dead-on to the player must not
            // still fall short. Rigidbody's own default (gravity on) was
            // silently dropping every bullet ~0.4m over the 4m flight to a
            // block, missing it entirely regardless of aim.
            rb.useGravity = false;
            // ContinuousSpeculative, not ContinuousDynamic - still safe
            // against tunneling through a thin block at 14 m/s, but far
            // cheaper per physics step (no full sweep test), and it only
            // matters for however long the bullet is actually alive - which
            // is the real fix below.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = _muzzle.forward * BulletSpeed;
            bulletGO.AddComponent<MathBullet>();
            // A MISSED bullet used to keep flying and doing continuous
            // collision checks against the entire scene for the full 5
            // seconds regardless of hitting the back wall almost
            // immediately (BackWallZ is only ~6m away at 14 m/s - under
            // half a second) - "very laggy" matched exactly this: several
            // live physics bodies stacking up per missed volley, each
            // paying full collision-detection cost for 4+ seconds after
            // they'd already left the playable area. MathBullet now
            // destroys itself on ANY collision, not just a correct hit, so
            // a miss's actual physics lifetime is under a second instead of
            // a flat 5 - the 5s timer stays only as a safety net for a shot
            // that somehow never collides with anything at all.
            Destroy(bulletGO, 5f);

            if (_audio != null) _audio.Play();
        }

        private void HandleBlockHit(MathAnswerBlock block, bool correct)
        {
            if (!_roundActive) return;

            if (correct)
            {
                _roundActive = false;
                _score++;
                _blocks.Remove(block);
                ShatterBlock(block.gameObject);
                _feedbackText.text = $"Correct - {_taskDescription} is {_correctAnswer}!";
                ConvaiGuide.Speak($"That's it - {_taskDescription} is {_correctAnswer}.");
                HandleSuccess();
                Invoke(nameof(NextProblem), 1.6f);
            }
            else
            {
                _mistakesThisTask++;
                block.FlashWrong(WrongColor, BlockColor);
                _feedbackText.text = "Not that one - try again.";
                ConvaiGuide.Speak(HintFor(_concept));
                HandleFailure();
            }
        }

        private static string HintFor(string concept) => concept switch
        {
            "addition and subtraction" => "Try counting up from the bigger number, or counting back if it's subtraction.",
            "multiplication" => "Break it into smaller groups - add the number to itself a few times and see where you land.",
            _ => "Take another look and try again."
        };

        // A small physics-cube burst on the correct hit - the payoff idea
        // from ref/VR-math-game's Explode.cs, rebuilt with this project's own
        // primitive+Rigidbody conventions rather than porting that script.
        private void ShatterBlock(GameObject block)
        {
            var pos = block.transform.position;
            var color = CorrectColor;
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
            for (var z = 0; z < 2; z++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = pos + new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * 0.2f;
                cube.transform.localScale = Vector3.one * 0.2f;
                cube.GetComponent<Renderer>().material.color = color;
                var rb = cube.AddComponent<Rigidbody>();
                rb.AddExplosionForce(6f, pos, 1f);
                Destroy(cube, 2f);
            }
            Destroy(block);
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
                level = _level,
                concept = _concept,
                taskDescription = _taskDescription,
                playerAnswer = "",
                correctAnswer = _correctAnswer.ToString(),
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }

    /// <summary>One floating answer block - tracks its value/correctness and reports a bullet hit back to the game.</summary>
    public class MathAnswerBlock : MonoBehaviour
    {
        private int _value;
        private bool _isCorrect;
        private System.Action<MathAnswerBlock, bool> _onHit;
        private Renderer _renderer;
        private Color _normalColor;

        public void Init(int value, bool isCorrect, System.Action<MathAnswerBlock, bool> onHit)
        {
            _value = value;
            _isCorrect = isCorrect;
            _onHit = onHit;
            _renderer = GetComponent<Renderer>();
        }

        public void FlashWrong(Color wrong, Color normal)
        {
            _normalColor = normal;
            if (_renderer != null) _renderer.material.color = wrong;
            Invoke(nameof(ResetColor), 0.4f);
        }

        private void ResetColor()
        {
            if (_renderer != null) _renderer.material.color = _normalColor;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.GetComponent<MathBullet>() == null) return;
            _onHit?.Invoke(this, _isCorrect);
            Destroy(collision.gameObject);
        }
    }

    /// <summary>Marker so blocks can tell a real fired shot apart from anything else they might touch.</summary>
    public class MathBullet : MonoBehaviour
    {
        // A hit on the correct block destroys the bullet itself (see
        // MathAnswerBlock.OnCollisionEnter), but a WRONG block only flashes
        // and a MISS hits a wall/floor/nothing at all - none of those used
        // to stop the bullet, so it kept flying (and paying full continuous-
        // collision cost) for the rest of its flat 5-second lifetime even
        // after leaving the playable range entirely. Ending it on the first
        // collision, period, is what actually fixes the "very laggy" range -
        // a missed shot now lives well under a second instead of 5.
        private void OnCollisionEnter(Collision collision) => Destroy(gameObject);
    }
}
