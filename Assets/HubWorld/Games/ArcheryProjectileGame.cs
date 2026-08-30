using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using AILearningEcosystem.Learning;
using static AILearningEcosystem.Hub.CanvasUIHelpers;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Physics Minigame 1 - "Archery Range", replacing the old button-cannon
    /// Projectile Launcher (design doc section 6.1) with a real bow the
    /// player draws and releases by hand. Ported from the reference Archery
    /// project in ref/Archery_Game_Unity3D: its bow/arrow/target art and its
    /// core physics idea (a real Rigidbody arrow under gravity, scored by
    /// distance from the arrow's actual contact point to the target's
    /// center) - not its old gamepad-driven input scheme, which this
    /// project's XR hands replace with a real grab-and-draw (see ArcheryBow).
    ///
    /// The lesson is the two things projectile motion is actually made of:
    /// horizontal velocity stays constant (nothing pushes the arrow
    /// sideways) while vertical velocity is constantly changed by gravity
    /// (decelerating on the way up, accelerating on the way down). Rather
    /// than just say that, every shot leaves a "motion diagram" of dots at
    /// equal time intervals (ArcheryArrow) - equal horizontal gaps, uneven
    /// vertical gaps - and the live readout during flight prints the actual
    /// vx/vy numbers so the two halves of the motion are visibly decoupled.
    ///
    /// Class kept named ArcheryProjectileGame is the new occupant of the
    /// "Archery Game" GameObject that used to hold ElectricityGame in this
    /// same scene (Assets/PlatformScenes/Physics/ProjectileLauncher.unity).
    /// Built in Awake() and persists as real scene content - [ExecuteAlways]
    /// means it also builds in the Editor so the range is visible without
    /// Play mode. Only round/flight state is runtime-only.
    /// </summary>
    [ExecuteAlways]
    public class ArcheryProjectileGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "ProjectileLauncher";
        public string Subject => "Physics";

        [Header("Archery art (ported from ref/Archery_Game_Unity3D)")]
        public GameObject bowModel;
        // The complete, correctly-assembled Arrow.prefab (arrowhead+stick
        // pre-combined by the asset's own author, zero rotation correction
        // needed) - preferred over the old separately-instantiated
        // arrowheadModel+arrowStickModel pair, which relied on a guessed
        // rotation fix-up that left the head looking visibly broken/mis-
        // angled relative to the shaft.
        public GameObject arrowPrefab;
        public GameObject arrowheadModel;
        public GameObject arrowStickModel;
        // The real "Diana" target (Assets/Prefabs/FullTarget.prefab) -
        // preferred over the procedural cylinder-on-legs stand built in
        // BuildTarget() when assigned.
        public GameObject fullTargetPrefab;
        public Material bowMaterial;
        public Material arrowMaterial;
        public Material targetMaterial;
        public Material woodMaterial;
        public AudioClip shootClip;
        public AudioClip arrowHitClip;
        public AudioClip bullseyeClip;
        public AudioClip missClip;

        [Header("Real GreenForest terrain (Assets/GreenForest/Forest.asset, copied wholesale from ref/Archery_Game_Unity3D) - preferred over the procedural clearing below when assigned")]
        public TerrainData greenForestTerrainData;
        // Measured live (SampleHeight across the reference scene's own play
        // area, before this project copied the terrain out on its own): the
        // real terrain's height near its own local (0,0) corner is ~20.7,
        // not 0. Every other position in this script (BowZOffset, target
        // height, etc.) assumes ground = Y 0, so the terrain GameObject gets
        // shifted down by this amount rather than rewriting every height
        // constant in the file.
        private const float GreenForestGroundHeight = 20.70439f;

        [Header("Forest clearing (ported from ref/Crimson-Valor's own Old Forest pack) - fallback only, used when greenForestTerrainData is unassigned")]
        public GameObject treeModel;
        public GameObject saplingModel;
        public GameObject rockModel;
        public GameObject stoneModel;
        public GameObject forestPlantModel;
        public Material treeBarkMaterial;
        public Material treeLeavesMaterial;
        public Material rockMaterial;
        public Material stoneMaterial;
        public Material forestPlantMaterial;
        public Color groundColor = new Color(0.278f, 0.361f, 0.176f);

        [Header("Real GreenForest scene mode - this scene's own Terrain/Diana targets are already placed in the scene (copied wholesale, not built by this script); skip building an environment/target and shoot at the real pre-placed Diana instances instead")]
        public bool skipEnvironmentBuild;
        public bool useSceneTargets;
        public string[] sceneTargetNames = { "Diana", "Diana2", "Diana3", "Diana4", "Diana5", "Diana6" };
        private readonly System.Collections.Generic.List<Transform> _sceneTargetFaces = new System.Collections.Generic.List<Transform>();

        private int _totalRounds = 9;
        // "Progress" is grouped into stages of 3 correct hits each, not one
        // continuous smooth ramp - every 3rd hit jumps the target a much
        // bigger distance and gets its own "Stage cleared!" callout, so
        // clearing a stage actually feels like reaching a checkpoint instead
        // of the target just quietly creeping outward every single shot.
        private const int RoundsPerStage = 3;
        private const float BowZOffset = 0.5f;
        // Matches the real FullTarget prefab's "Target" child (1m-diameter
        // disc, radius 0.5) - see BuildTarget().
        private const float TargetRadius = 0.5f;
        private const float ClearingHalfWidth = 6f;
        private const float ClearingLength = 16f;

        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        private TMP_Text _questionText;
        private TMP_Text _readoutText;
        private TMP_Text _feedbackText;
        private TMP_Text _drawPredictionText;
        private SciFiProgressBar _progressBar;
        private AngleWedgeIndicator _angleWedge;
        private ArcheryReplayCamera _replayCam;
        private RawImage _replayScreenImage;

        private const float Gravity = 9.81f;
        // A shot always used to be silently guaranteed a hit at whichever
        // angle happened to be "correct" (an internal, invisible-to-the-
        // player speed was solved for that guaranteed it) - draw strength
        // didn't actually do anything except gate a fire/no-fire threshold.
        // Now speed genuinely comes from how far you draw (see
        // ArcheryBow.CurrentDrawSpeed/OnRelease) and there's no secret
        // correct answer - hitting the target means actually solving
        // R = U^2 sin(2*theta) / g for a combination of angle and pull that
        // reaches the given distance, same as a real archer would.
        private bool _shotResolved;

        private const int QuiverSize = 6;

        private Transform _bowRoot;
        private Transform _nockAnchor;
        private ArcheryBow _bow;
        private AudioSource _audio;

        private Transform _targetRoot;
        private Transform _targetFace;
        private Renderer _targetFaceRenderer;

        private ArcheryArrow _liveArrow;

        private int _round;
        private int _score;
        private float _targetDistance;
        private int _mistakesThisTask;
        private float _taskStartTime;
        private bool _roundActive;
        private bool _hasFiredThisRound;
        private bool _playSessionStarted;

        private void Awake()
        {
            if (transform.Find("Range Canvas") == null)
                BuildStatic();
            else
                RediscoverReferences();

            if (!Application.isPlaying || _playSessionStarted) return;
            _playSessionStarted = true;

            // The quiver holds live, consumable gameplay objects (arrows get
            // grabbed, nocked, fired, and replaced) - it doesn't belong in
            // the persisted static scene content built above, so it's always
            // populated fresh here at the start of a real Play session
            // regardless of whether this scene hit BuildStatic() (first-ever
            // build) or RediscoverReferences() (already built and saved).
            BuildQuiver();

            EnsureEventSystem();
            NavTabBar.Build(transform);
            GameManager.Instance?.StartMinigameSession(this);
            // The tutorial (formula breakdown + a scripted demo shot) runs
            // once before Round 1 - StartGame() itself is now called at the
            // END of that sequence instead of immediately here.
            StartCoroutine(TutorialSequence());

            // A small delay lets the XR rig's own first-frame tracking setup
            // settle before measuring/correcting height - both the minimum-
            // height fix and the bow/quiver placement (which reads whatever
            // height results) need that settled value, so they run together
            // here in that order.
            Invoke(nameof(FixHeightAndReposition), 0.3f);
        }

        private void FixHeightAndReposition()
        {
            var player = GameObject.Find("PlayerPhysics");
            if (player != null) MinimumEyeHeightEnforcer.Apply(player.transform, 1.6f);
            AdjustToPlayerHeight();
        }

        private void AdjustToPlayerHeight()
        {
            var cam = Camera.main;
            if (cam == null || _bowRoot == null) return;

            var eyeHeight = cam.transform.position.y - transform.position.y;
            if (eyeHeight < 0.5f || eyeHeight > 3f) return; // sanity guard against a not-yet-settled reading

            // Bow held roughly sternum-height - comfortably reachable and
            // easy to draw back toward the face - about 55% of eye height
            // above the ground, matching real archery form.
            var bowY = eyeHeight * 0.55f;
            var pos = _bowRoot.localPosition;
            _bowRoot.localPosition = new Vector3(pos.x, bowY, pos.z);

            var quiver = GameObject.Find("Quiver");
            if (quiver != null)
            {
                var qPos = quiver.transform.localPosition;
                quiver.transform.localPosition = new Vector3(qPos.x, eyeHeight * 0.85f, qPos.z);
            }
        }

        // Demo-only angle - a fixed, nice-looking shot chosen purely to
        // teach the formula clearly (mid-range, comfortably clears the demo
        // distance), not tied to any real round's target. Speed comes from
        // _bow.launchSpeed - the SAME fixed speed every real shot uses now
        // (see ArcheryBow.launchSpeed), so the demo is a genuine worked
        // example of the real thing, not a separately-tuned illustration.
        private const float DemoAngleDeg = 45f;
        private float DemoSpeed => _bow != null ? _bow.launchSpeed : 18f;

        // Runs once before Round 1: explains the two halves of projectile
        // motion in words, then actually SHOWS it with a real scripted shot
        // (same ArcheryArrow physics/motion-diagram dots every real shot
        // uses - not a fake animation) while the live Ux/Uy/Range breakdown
        // prints next to it, before finally handing control to the player.
        // "have a tutorial on how the formula works with animations from bow
        // arrow" - this is that: a worked example the player watches once,
        // using the bow/arrow itself as the animation.
        private IEnumerator TutorialSequence()
        {
            // The teacher's ConvaiNPC only finishes setting up its gRPC
            // client a frame after her own Start() runs - speaking this same
            // frame (confirmed live elsewhere in this project) silently
            // drops with no audio/caption at all.
            yield return new WaitForSeconds(1.5f);

            _questionText.text = "Tutorial: how projectile motion works";
            _feedbackText.text = "Watch the demo shot below, then it's your turn.";
            ConvaiGuide.Speak("Before your first shot, let's see how this actually works. Every arrow's velocity splits into two independent parts the moment it leaves the bow: a horizontal part and a vertical part.");
            yield return new WaitForSeconds(4f);

            if (_bowRoot == null || _nockAnchor == null) { StartGame(); yield break; }

            var flatForward = new Vector3(_bowRoot.forward.x, 0f, _bowRoot.forward.z);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = transform.forward;
            flatForward.Normalize();
            var right = Vector3.Cross(Vector3.up, flatForward);
            var demoDir = Quaternion.AngleAxis(-DemoAngleDeg, right) * flatForward;
            var demoVelocity = demoDir * DemoSpeed;

            var ux = DemoSpeed * Mathf.Cos(DemoAngleDeg * Mathf.Deg2Rad);
            var uy = DemoSpeed * Mathf.Sin(DemoAngleDeg * Mathf.Deg2Rad);
            var demoRange = (DemoSpeed * DemoSpeed * Mathf.Sin(2f * DemoAngleDeg * Mathf.Deg2Rad)) / Gravity;

            if (_drawPredictionText != null)
                _drawPredictionText.text =
                    $"DEMO SHOT: U = {DemoSpeed:0.0} m/s at θ = {DemoAngleDeg:0}°\n" +
                    $"Ux = U cos θ = {ux:0.0} m/s (stays constant the whole flight)\n" +
                    $"Uy = U sin θ = {uy:0.0} m/s (gravity changes this every instant)\n" +
                    $"Range = U² sin(2θ) / g ≈ {demoRange:0.0} m";

            ConvaiGuide.Speak($"Watch the glowing dots the arrow leaves behind - they're dropped at equal time steps. The horizontal gaps between them stay perfectly even, because Ux, {ux:0.0} meters per second, never changes. The vertical gaps shrink going up, then grow coming down - that's gravity acting on Uy the entire time.");

            var demoArrowGO = BuildArrowVisual(null);
            demoArrowGO.name = "Tutorial Demo Arrow";
            demoArrowGO.transform.position = _nockAnchor.position;
            demoArrowGO.transform.rotation = Quaternion.LookRotation(demoVelocity);
            var demoCol = demoArrowGO.AddComponent<CapsuleCollider>();
            demoCol.radius = 0.02f;
            demoCol.height = 0.9f;
            demoCol.direction = 2; // Z axis
            demoCol.center = new Vector3(0f, 0f, 0.35f);
            foreach (var bowCollider in _bowRoot.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(demoCol, bowCollider, true);

            var demoArrow = demoArrowGO.AddComponent<ArcheryArrow>();
            var demoLanded = false;
            demoArrow.OnHit += (_, __) => demoLanded = true;
            demoArrow.Launch(demoVelocity);

            var timeout = 6f;
            while (!demoLanded && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(1.5f);

            demoArrow.ClearMarkers();
            if (demoArrowGO != null) Destroy(demoArrowGO);
            if (_drawPredictionText != null) _drawPredictionText.text = "";

            _questionText.text = "Your turn!";
            ConvaiGuide.Speak("Now you try it. Draw the bow, pick your own angle and pull strength, and let go - the same breakdown will show live while you aim.");
            yield return new WaitForSeconds(1f);

            StartGame();
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (_liveArrow != null && _liveArrow.IsFlying)
            {
                var v = _liveArrow.CurrentVelocity;
                var vx = new Vector2(v.x, v.z).magnitude;
                _readoutText.text = $"Horizontal: {vx:0.0} m/s (constant)   Vertical: {v.y:+0.0;-0.0} m/s (gravity changes this)";
            }

            // The actual "figure out the angle from the formula" teaching
            // tool - live as you draw, not just described in the lesson
            // text. Same breakdown as the classic Ux=Ucos(theta)/
            // Uy=Usin(theta) projectile diagram: plug in the CURRENT draw's
            // speed/angle and show what range that would actually reach,
            // against the target distance you're actually trying to hit.
            if (_bow != null && _bow.IsDrawn && _drawPredictionText != null)
            {
                var u = _bow.CurrentDrawSpeed;
                var theta = Mathf.Clamp(Vector3.Angle(_bow.FlatForward, _bow.CurrentAimDirection), 0f, 89.9f);
                var ux = u * Mathf.Cos(theta * Mathf.Deg2Rad);
                var uy = u * Mathf.Sin(theta * Mathf.Deg2Rad);
                var predictedRange = (u * u * Mathf.Sin(2f * theta * Mathf.Deg2Rad)) / Gravity;
                _drawPredictionText.text =
                    $"U = {u:0.0} m/s   θ = {theta:0}°\n" +
                    $"Ux = U cos θ = {ux:0.0}     Uy = U sin θ = {uy:0.0}\n" +
                    $"Range = U² sin(2θ) / g ≈ {predictedRange:0.0} m   (target: {_targetDistance:0.0} m)";
            }
            else if (_drawPredictionText != null && _drawPredictionText.text != "")
            {
                _drawPredictionText.text = "";
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
            if (!skipEnvironmentBuild) BuildForestClearing();
            BuildHud();
            BuildBow();
            BuildTarget();
            BuildMusic();
        }

        // "no music nothing" - this range had no ambient audio at all.
        // Reuses World.unity's own WorldMusicDirector (looped Exploration
        // Cue + occasional forest one-shots, same Complete Mysterious Forest
        // Game Music Pack this scene's terrain already comes from) instead
        // of a second bespoke music player - same forest setting, same
        // component, just wired to whichever clips are assigned here.
        private void BuildMusic()
        {
            if (GetComponent<WorldMusicDirector>() != null) return;
            gameObject.AddComponent<WorldMusicDirector>();
        }

        private void RediscoverReferences()
        {
            var panel = transform.Find("Range Canvas/Panel");
            _questionText = panel.Find("Question")?.GetComponent<TMP_Text>();
            _readoutText = panel.Find("Readout")?.GetComponent<TMP_Text>();
            _feedbackText = panel.Find("Feedback")?.GetComponent<TMP_Text>();
            _progressBar = panel.Find("SciFi Progress Bar")?.GetComponent<SciFiProgressBar>();
            _drawPredictionText = transform.Find("Draw Prediction Readout")?.GetComponent<TMP_Text>();
            _angleWedge = transform.Find("Draw Angle Wedge")?.GetComponent<AngleWedgeIndicator>();

            var replayScreen = transform.Find("Replay Screen/Panel/Feed");
            _replayScreenImage = replayScreen != null ? replayScreen.GetComponent<RawImage>() : null;
            var replayCamGO = transform.Find("Replay Camera");
            _replayCam = replayCamGO != null ? replayCamGO.GetComponent<ArcheryReplayCamera>() : null;
            if (_replayCam != null) AttachReplayTexture(_replayCam.cam);

            _bowRoot = transform.Find("Bow Stand");
            _nockAnchor = _bowRoot != null ? _bowRoot.Find("Nock Anchor") : null;
            _bow = _bowRoot != null ? _bowRoot.GetComponent<ArcheryBow>() : null;
            _audio = _bowRoot != null ? _bowRoot.GetComponent<AudioSource>() : null;
            if (_bow != null)
            {
                _bow.OnRelease += HandleRelease;
                _bow.OnDrawAngleChanged += HandleDrawAngleChanged;
                _bow.OnDrawStateChanged += HandleDrawStateChanged;
            }
            if (_angleWedge != null && _nockAnchor != null && _bowRoot != null)
            {
                var flatForward = new Vector3(_bowRoot.forward.x, 0f, _bowRoot.forward.z);
                if (flatForward.sqrMagnitude < 0.0001f) flatForward = transform.forward;
                _angleWedge.Init(_nockAnchor, flatForward.normalized, 0.6f, new Color(1f, 0.85f, 0.2f));
            }

            // A normal Play-mode entry hits this path (the scene was already
            // built once in the Editor and saved), NOT BuildStatic() - it
            // must populate _sceneTargetFaces itself or NextChallenge()'s
            // _sceneTargetFaces[_round - 1] throws on an empty list
            // (confirmed live: IndexOutOfRange on the very first round).
            if (useSceneTargets)
            {
                RediscoverSceneTargets();
                return;
            }

            _targetRoot = transform.Find("Target Stand");
            _targetFace = _targetRoot != null ? (_targetRoot.Find("Target Face") ?? _targetRoot.Find("Target")) : null;
            _targetFaceRenderer = _targetFace != null ? _targetFace.GetComponent<Renderer>() : null;
        }

        private void RediscoverSceneTargets()
        {
            _sceneTargetFaces.Clear();
            foreach (var targetName in sceneTargetNames)
            {
                var go = GameObject.Find(targetName);
                var targetFaceChild = go != null ? go.transform.Find("Target") : null;
                if (targetFaceChild != null) _sceneTargetFaces.Add(targetFaceChild);
            }
            if (_sceneTargetFaces.Count == 0) return;

            _totalRounds = _sceneTargetFaces.Count;
            _targetFace = _sceneTargetFaces[0];
            _targetFaceRenderer = _targetFace.GetComponent<Renderer>();
            _targetRoot = _targetFace.parent;
        }

        private void BuildHud()
        {
            var canvasGO = new GameObject("Range Canvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvasGO.transform.localPosition = new Vector3(0f, 2.5f, BowZOffset + 1.2f);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = canvasGO.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(600, 340);
            canvasGO.transform.localScale = Vector3.one * 0.003f;

            var panel = CreatePanel(canvasGO.transform, Vector2.zero, new Vector2(600, 340), PanelColor);
            // Permanent, never-overwritten lesson framing - the round status
            // and hints below explain WHAT'S happening this round, but never
            // said WHY, or what the exercise is actually teaching. This
            // stays on screen the whole time, doesn't depend on Convai's
            // (currently quota-limited) spoken welcome line ever playing.
            //
            // Every text here uses CreateFitText, not the plain CreateText -
            // a fixed font size on a full sentence (this Lesson line
            // especially) could overflow past its own box's edges into the
            // text above/below it depending on how it happened to wrap.
            // Auto-sizing shrinks each one down until it actually fits its
            // assigned box instead of trusting a guessed font size.
            CreateFitText(panel.transform, "LESSON: Projectile Motion - horizontal speed stays constant, gravity changes vertical speed. Pick an angle + draw the string fully, then release to land on target.",
                14, new Color(0.65f, 0.85f, 1f), TextAlignmentOptions.Center, new Vector2(0, 130), new Vector2(560, 60), "Lesson");
            _questionText = CreateFitText(panel.transform, "Draw the bow and aim.", 24, TextColor, TextAlignmentOptions.Center,
                new Vector2(0, 60), new Vector2(560, 60), "Question");
            _readoutText = CreateFitText(panel.transform, "", 18, CorrectColor, TextAlignmentOptions.Center,
                new Vector2(0, 0), new Vector2(560, 50), "Readout");
            _feedbackText = CreateFitText(panel.transform, "Grab the arrow, pull back, let go to fire.", 18, TextDimColor, TextAlignmentOptions.Center,
                new Vector2(0, -70), new Vector2(560, 60), "Feedback");

            // Stage/round progress - one lit-up node per round (not just a
            // smooth bar) so "3 of 9 done, on the 4th" reads at a glance,
            // plus a big animated percentage.
            _progressBar = SciFiProgressBar.Build(panel.transform, new Vector2(0, -128), new Vector2(500, 44),
                _totalRounds, RoundsPerStage);

            BuildReplayScreen();
        }

        // A small "TV monitor" beside the main panel, fed by a dedicated
        // camera that watches the fired arrow - not the player's own view,
        // which would need to move on its own and cause real VR discomfort.
        // "Preview of where the arrow landed... like golf games" - a
        // broadcast-style replay screen is the VR-safe version of that.
        private void BuildReplayScreen()
        {
            var screenGO = new GameObject("Replay Screen", typeof(RectTransform));
            screenGO.transform.SetParent(transform, false);
            screenGO.transform.localPosition = new Vector3(1.6f, 2.5f, BowZOffset + 1.2f);
            screenGO.transform.localRotation = Quaternion.identity;
            var screenCanvas = screenGO.AddComponent<Canvas>();
            screenCanvas.renderMode = RenderMode.WorldSpace;
            screenGO.AddComponent<CanvasScaler>();
            var screenRect = screenGO.GetComponent<RectTransform>();
            screenRect.sizeDelta = new Vector2(400, 300);
            screenGO.transform.localScale = Vector3.one * 0.003f;

            var frame = CreatePanel(screenGO.transform, Vector2.zero, new Vector2(400, 300), PanelColor);
            var imageGO = new GameObject("Feed", typeof(RectTransform));
            imageGO.transform.SetParent(frame.transform, false);
            var imgRect = imageGO.GetComponent<RectTransform>();
            imgRect.anchorMin = Vector2.zero;
            imgRect.anchorMax = Vector2.one;
            imgRect.offsetMin = new Vector2(10, 10);
            imgRect.offsetMax = new Vector2(-10, -10);
            _replayScreenImage = imageGO.AddComponent<RawImage>();

            var camGO = new GameObject("Replay Camera");
            camGO.transform.SetParent(transform, false);
            camGO.transform.localPosition = new Vector3(0f, 2f, BowZOffset - 2f);
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            _replayCam = camGO.AddComponent<ArcheryReplayCamera>();
            _replayCam.cam = cam;
            AttachReplayTexture(cam);
        }

        // The RenderTexture is a runtime-only resource, same as the tone
        // synth clips elsewhere in this project's minigames - it can't
        // survive a domain reload as an actual asset, so this gets called
        // again in RediscoverReferences() rather than only at first build.
        private void AttachReplayTexture(Camera cam)
        {
            var tex = new RenderTexture(512, 384, 16) { name = "Archery Replay RT" };
            cam.targetTexture = tex;
            if (_replayScreenImage != null) _replayScreenImage.texture = tex;
        }

        private void BuildBow()
        {
            var standGO = new GameObject("Bow Stand");
            standGO.transform.SetParent(transform, false);
            standGO.transform.localPosition = new Vector3(0f, 1.1f, BowZOffset);
            _bowRoot = standGO.transform;

            if (bowModel != null)
            {
                var bowVisual = Instantiate(bowModel, standGO.transform);
                bowVisual.name = "Bow Visual";
                bowVisual.transform.localPosition = Vector3.zero;
                // CRT Studio's "Bow 001" (measured live at true identity:
                // bounds 0.81 x 0.18 x 2.45 - Z is the dominant/limb-to-limb
                // axis, same convention as the previous bow) is ~1.75x too
                // large for a real bow at scale 1 (2.45m tip-to-tip vs a real
                // ~1.1m bow), hence the scale-down. The -90-around-X maps
                // that Z-axis length to vertical; the extra -90-around-Z on
                // top is the explicit "rotate the bow 90 clockwise" ask
                // (first attempt went the wrong way round - anticlockwise -
                // and was corrected) - purely cosmetic (this is the VISUAL
                // mesh only, a child of the bow's aim/grip transform - none
                // of the actual draw or shot-direction math below reads this
                // rotation).
                bowVisual.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
                bowVisual.transform.localScale = Vector3.one * 0.45f;
                ApplyMaterial(bowVisual, bowMaterial);
                StripColliders(bowVisual);
            }

            // Bowstring anchor points - rotated the same 90 as the visual
            // mesh above so the string still runs through the real limb
            // tips instead of alongside them. Approximate, not exact to the
            // mesh's real curve, but close enough that a taut string reads
            // correctly instead of not existing at all.
            var topTipGO = new GameObject("String Top");
            topTipGO.transform.SetParent(standGO.transform, false);
            topTipGO.transform.localPosition = new Vector3(0.55f, 0f, 0.03f);
            var bottomTipGO = new GameObject("String Bottom");
            bottomTipGO.transform.SetParent(standGO.transform, false);
            bottomTipGO.transform.localPosition = new Vector3(-0.55f, 0f, 0.03f);

            // The bow itself had NO collider anywhere in its hierarchy -
            // confirmed live, nothing to grab at all ("i cant even grab the
            // bow"). A grip box on the riser (bow's own middle, where a real
            // hand actually holds one) makes the bow itself a real held
            // object: trackPosition/trackRotation ON, kinematic, so it moves
            // rigidly with whichever hand grabs it instead of staying behind
            // while only the tiny draw-grip bead moves (the "floating" arrow
            // disconnected from a static bow that was previously reported).
            var gripCollider = standGO.AddComponent<BoxCollider>();
            gripCollider.size = new Vector3(0.08f, 0.32f, 0.08f);
            gripCollider.center = Vector3.zero;

            var bowRb = standGO.AddComponent<Rigidbody>();
            bowRb.useGravity = false;
            bowRb.isKinematic = true;

            var bowGrab = standGO.AddComponent<XRGrabInteractable>();
            bowGrab.movementType = XRBaseInteractable.MovementType.Kinematic;
            bowGrab.trackPosition = true;
            // Rotation is driven manually in ArcheryBow.LateUpdate instead -
            // see its comment for why (raw controller roll made the bow face
            // sideways once actually held).
            bowGrab.trackRotation = false;

            var nockGO = new GameObject("Nock Anchor");
            nockGO.transform.SetParent(standGO.transform, false);
            nockGO.transform.localPosition = new Vector3(0f, 0f, -0.15f);
            _nockAnchor = nockGO.transform;

            // No permanently-attached arrow here anymore - the arrow is a
            // real separate object the player draws from the quiver on their
            // back and nocks onto the string themselves (see BuildQuiver/
            // QuiverArrow). ArcheryBow just exposes nockAnchor for a held
            // arrow to snap onto.
            _bow = standGO.AddComponent<ArcheryBow>();
            _bow.nockAnchor = _nockAnchor;
            _bow.aimSource = standGO.transform;
            _bow.stringTop = topTipGO.transform;
            _bow.stringBottom = bottomTipGO.transform;

            _audio = standGO.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;

            // Live physics-breakdown readout while drawing - the actual
            // "figure out the angle from the formula" teaching tool, not
            // just a bare degree number floating above the bow (dropped per
            // an explicit "dont show the angle above, just the [angle]
            // between bow and arrow is fine" - the wedge below already
            // covers that; this text is the Ux/Uy/range breakdown instead).
            var predictionGO = new GameObject("Draw Prediction Readout");
            predictionGO.transform.SetParent(transform, true);
            predictionGO.transform.position = standGO.transform.position + Vector3.up * 0.55f + transform.forward * -0.15f;
            predictionGO.transform.rotation = transform.rotation;
            predictionGO.transform.localScale = Vector3.one * 0.16f;
            var predictionTmp = predictionGO.AddComponent<TextMeshPro>();
            predictionTmp.fontSize = 6f;
            predictionTmp.fontStyle = FontStyles.Bold;
            predictionTmp.alignment = TextAlignmentOptions.Center;
            predictionTmp.color = new Color(1f, 0.85f, 0.2f);
            predictionTmp.text = "";
            _drawPredictionText = predictionTmp;

            // A ground-level reference line right under the archery station,
            // in the same firing plane as the shot - "the angle with respect
            // to the ground" reads as an actual measurement against something
            // physical instead of just a floating number.
            var groundLineGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundLineGO.name = "Angle Ground Reference";
            groundLineGO.transform.SetParent(transform, false);
            groundLineGO.transform.localPosition = new Vector3(0f, 0.02f, BowZOffset);
            groundLineGO.transform.localScale = new Vector3(0.02f, 0.02f, 3f);
            groundLineGO.GetComponent<Renderer>().material.color = new Color(0.9f, 0.85f, 0.3f);
            SafeDestroy(groundLineGO.GetComponent<Collider>());

            // The actual "<" angle gizmo requested - a horizontal reference
            // ray, a live ray that tracks exactly where the arrow will fly
            // (same direction math as the real release, see
            // ArcheryBow.CurrentAimDirection), an arc between them, and a
            // degree label - not just a bare number with nothing to measure
            // it against.
            var wedgeGO = new GameObject("Draw Angle Wedge");
            wedgeGO.transform.SetParent(transform, false);
            _angleWedge = wedgeGO.AddComponent<AngleWedgeIndicator>();
            var flatForward = new Vector3(standGO.transform.forward.x, 0f, standGO.transform.forward.z);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = transform.forward;
            _angleWedge.Init(_nockAnchor, flatForward.normalized, 0.6f, new Color(1f, 0.85f, 0.2f));

            if (Application.isPlaying)
            {
                _bow.OnRelease += HandleRelease;
                _bow.OnDrawAngleChanged += HandleDrawAngleChanged;
                _bow.OnDrawStateChanged += HandleDrawStateChanged;
            }
        }

        private void HandleDrawAngleChanged(float angleDeg)
        {
            if (_angleWedge != null && _bow != null) _angleWedge.UpdateAngle(angleDeg, _bow.CurrentAimDirection);
        }

        private void HandleDrawStateChanged(bool isDrawing)
        {
            if (_angleWedge != null && !isDrawing) _angleWedge.SetVisible(false);
            if (!isDrawing && _drawPredictionText != null) _drawPredictionText.text = "";
        }

        // arrowhead sits 0.7 forward of Arrow_stick's origin. Neither gets an
        // extra scale multiplier - measured live (Instantiate at each
        // prefab's own natural/default scale, no override): the stick is
        // already a correct ~0.70m shaft at its default scale of 1, and the
        // arrowhead is already a correct ~2.8x9.5x1.3cm tip at ITS default
        // scale of 100 (baked into that FBX's own import). An earlier version
        // multiplied both by an extra 100x on top of that, on the mistaken
        // assumption neither prefab's default scale was already correct -
        // that's what produced a 70-meter-long arrow (confirmed via
        // screenshot - an arrow dwarfing a full-grown tree). Instantiate()
        // already preserves each prefab's own root scale, so simply not
        // touching localScale here is the fix.
        //
        // Both meshes' own long axis runs along local Y (measured live: the
        // stick's raw bounds are 0.04 x 0.70 x 0.04 - the 0.70m shaft length
        // is the Y component), not Z. Left at identity rotation, the whole
        // arrow assembly pointed straight up regardless of aim direction
        // ("the arrow stays vertical, cant even align it") since nothing
        // ever re-oriented it to match the wrapper's own +Z-forward
        // convention (the same axis nockAnchor/aimSource/the draw rail all
        // assume). Rotating each mesh -90 around X maps its local +Y to the
        // wrapper's +Z, without touching the wrapper's own position offsets
        // (0.7 forward for the head) which are already defined in the
        // wrapper's un-rotated frame.
        private static readonly Quaternion ArrowMeshForwardCorrection = Quaternion.Euler(90f, 0f, 0f);

        private GameObject BuildArrowVisual(Transform parent)
        {
            // arrowPrefab (Assets/Free medieval weapons/Prefabs/Arrow.prefab)
            // is the asset author's own pre-assembled head+shaft, already at
            // the correct relative rotation - preferred whenever assigned.
            if (arrowPrefab != null)
            {
                var arrowInst = parent != null ? Instantiate(arrowPrefab, parent) : Instantiate(arrowPrefab);
                arrowInst.name = "Arrow";
                arrowInst.transform.localPosition = Vector3.zero;
                arrowInst.transform.localRotation = Quaternion.identity;
                ApplyMaterial(arrowInst, arrowMaterial);
                StripColliders(arrowInst);
                return arrowInst;
            }

            var arrowGO = new GameObject("Arrow");
            arrowGO.transform.SetParent(parent, false);
            arrowGO.transform.localPosition = Vector3.zero;
            arrowGO.transform.localRotation = Quaternion.identity;
            arrowGO.transform.localScale = Vector3.one;

            if (arrowheadModel != null)
            {
                var head = Instantiate(arrowheadModel, arrowGO.transform);
                head.name = "arrowhead";
                head.transform.localPosition = new Vector3(0f, 0f, 0.7f);
                head.transform.localRotation = ArrowMeshForwardCorrection;
                ApplyMaterial(head, arrowMaterial);
                StripColliders(head);
            }

            if (arrowStickModel != null)
            {
                var stick = Instantiate(arrowStickModel, arrowGO.transform);
                stick.name = "Arrow_stick";
                stick.transform.localPosition = Vector3.zero;
                stick.transform.localRotation = ArrowMeshForwardCorrection;
                ApplyMaterial(stick, arrowMaterial);
                StripColliders(stick);
            }

            return arrowGO;
        }

        // ---- Quiver ----

        // Real, separate arrow objects the player draws from their own back
        // and nocks onto the bow themselves - not a bead permanently welded
        // to the string. Attached to the player rig (not this stationary
        // range setup) so it moves and turns with them like a real quiver
        // strap. Each slot refills itself the instant its arrow is taken.
        private void BuildQuiver()
        {
            var player = GameObject.Find("PlayerPhysics");
            if (player == null) return;

            var quiverGO = new GameObject("Quiver");
            quiverGO.transform.SetParent(player.transform, false);
            // Behind the right hip/shoulder, angled up and slightly out -
            // approximate without real avatar bones, but reads as "on my back."
            quiverGO.transform.localPosition = new Vector3(0.18f, 0.9f, -0.22f);
            quiverGO.transform.localRotation = Quaternion.Euler(-25f, 20f, 0f);

            for (var i = 0; i < QuiverSize; i++)
            {
                var slotOffset = new Vector3(0f, 0f, i * 0.045f);
                SpawnQuiverArrow(quiverGO.transform, slotOffset);
            }
        }

        private void SpawnQuiverArrow(Transform quiverRoot, Vector3 localOffset)
        {
            var arrowGO = BuildArrowVisual(quiverRoot);
            arrowGO.transform.localPosition = localOffset;
            arrowGO.transform.localRotation = Quaternion.identity;

            var col = arrowGO.AddComponent<CapsuleCollider>();
            col.radius = 0.02f;
            col.height = 0.9f;
            col.direction = 2; // Z axis
            col.center = new Vector3(0f, 0f, 0.35f);

            arrowGO.AddComponent<Rigidbody>();
            var arrowGrab = arrowGO.AddComponent<XRGrabInteractable>();
            // XRGrabInteractable.OnSelectExiting calls the public selectExited
            // event FIRST (which is where QuiverArrow->ArcheryBow->HandleRelease
            // ->ArcheryArrow.Launch() sets our real, physics-correct velocity)
            // and only THEN calls its own Detach(), which - with the default
            // throwOnDetach true - overwrites the rigidbody's velocity with a
            // throw velocity computed from the hand's recent motion. That silent
            // overwrite is what "the arrow doesn't move forward" actually was:
            // Launch() DID fire and set the right velocity, XRI just stomped it
            // a moment later with a near-zero/backward release-hand velocity.
            // Confirmed live - a direct call into HandleRelease (bypassing real
            // XRI select/deselect entirely) flew the same arrow correctly across
            // 30+ metres, while every real in-VR shot landed within centimetres
            // of the bow. Disabling XRI's own throw physics here leaves Launch()
            // as the only thing that ever sets this arrow's velocity.
            arrowGrab.throwOnDetach = false;

            var quiverArrow = arrowGO.AddComponent<QuiverArrow>();
            quiverArrow.bow = _bow;
            quiverArrow.onTakenFromQuiver = _ => SpawnQuiverArrow(quiverRoot, localOffset);
        }

        // The real GreenForest terrain (copied wholesale from
        // ref/Archery_Game_Unity3D per the user's explicit "copy everything"
        // instruction) replaces the procedural clearing below - real Unity
        // Terrain with 4323 baked tree instances, 13 grass detail layers and
        // 6 ground texture layers already authored, instead of a flat
        // primitive Plane with a few dozen scattered Forest Pack props.
        // MinigameEnvironment itself is deactivated below so its walls/floor
        // don't render underneath this either way.
        private void BuildForestClearing()
        {
            var env = FindFirstObjectByType<MinigameEnvironment>();
            if (env != null) env.gameObject.SetActive(false);

            if (greenForestTerrainData != null)
            {
                var terrainGO = Terrain.CreateTerrainGameObject(greenForestTerrainData);
                terrainGO.name = "GreenForest Terrain";
                terrainGO.transform.SetParent(transform, false);
                terrainGO.transform.localPosition = new Vector3(0f, -GreenForestGroundHeight, 0f);
                return;
            }

            var groundGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGO.name = "Forest Ground";
            groundGO.transform.SetParent(transform, false);
            groundGO.transform.localPosition = Vector3.zero;
            groundGO.transform.localScale = new Vector3(ClearingHalfWidth * 2f / 10f, 1f, ClearingLength * 2f / 10f);
            groundGO.GetComponent<Renderer>().material.color = groundColor;

            var rng = new System.Random(1); // fixed seed - stable layout across rebuilds, not a new forest every time
            PlaceRing(rockModel, rockMaterial, rng, 10, 0.35f, 0.7f);
            PlaceRing(stoneModel, stoneMaterial, rng, 8, 0.25f, 0.5f);
            PlaceRing(forestPlantModel, forestPlantMaterial, rng, 14, 0.6f, 1f);
            PlaceRing(saplingModel, treeBarkMaterial, rng, 8, 0.7f, 1.1f);
            PlaceTrees(rng, 10);
        }

        // Scatters instances of one prop just outside the clear shooting
        // lane (|x| > half-width, or beyond the target) so nothing blocks
        // the bow-to-target sightline the physics lesson depends on.
        private void PlaceRing(GameObject model, Material material, System.Random rng, int count, float minScale, float maxScale)
        {
            if (model == null) return;
            for (var i = 0; i < count; i++)
            {
                var pos = RandomClearingEdgePosition(rng);
                var prop = Instantiate(model, transform);
                prop.transform.localPosition = pos;
                prop.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                var scale = Mathf.Lerp(minScale, maxScale, (float)rng.NextDouble());
                prop.transform.localScale = Vector3.one * scale;
                if (material != null) ApplyMaterial(prop, material);
                StripColliders(prop);
                KeepOnlyHighestLod(prop);
            }
        }

        private void PlaceTrees(System.Random rng, int count)
        {
            if (treeModel == null) return;
            for (var i = 0; i < count; i++)
            {
                var pos = RandomClearingEdgePosition(rng);
                var tree = Instantiate(treeModel, transform);
                tree.transform.localPosition = pos;
                tree.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
                var scale = Mathf.Lerp(0.35f, 0.55f, (float)rng.NextDouble());
                tree.transform.localScale = Vector3.one * scale;
                KeepOnlyHighestLod(tree);
                // The trunk+canopy is one mesh with 2 submeshes (bark, then
                // leaves) - both slots must be set on the SAME renderer, not
                // split across separate renderer components.
                foreach (var renderer in tree.GetComponentsInChildren<Renderer>())
                {
                    var slots = renderer.sharedMaterials.Length;
                    var mats = new Material[slots];
                    for (var s = 0; s < slots; s++)
                        mats[s] = s == 0 ? treeBarkMaterial : treeLeavesMaterial;
                    renderer.sharedMaterials = mats;
                }
                StripColliders(tree);
            }
        }

        private Vector3 RandomClearingEdgePosition(System.Random rng)
        {
            // Keep clear of the shooting lane down the middle - only place
            // props off to either side or behind the bow/target line.
            var side = rng.Next(0, 2) == 0 ? -1f : 1f;
            var x = side * Mathf.Lerp(ClearingHalfWidth * 0.5f, ClearingHalfWidth * 0.95f, (float)rng.NextDouble());
            var z = Mathf.Lerp(-1f, ClearingLength - 2f, (float)rng.NextDouble());
            return new Vector3(x, 0f, z);
        }

        private void BuildTarget()
        {
            // This scene (the real GreenForest scene, copied wholesale) came
            // with SIX real Diana/Diana2-6 targets already placed across the
            // terrain at their own authored positions - find them instead of
            // building anything. _totalRounds becomes however many resolve
            // successfully; NextChallenge() switches _targetFace/_targetFaceRenderer
            // to each in turn rather than moving one stand.
            if (useSceneTargets)
            {
                RediscoverSceneTargets();
                return;
            }

            // The real "Diana" target (Assets/Prefabs/FullTarget.prefab,
            // copied wholesale from ref/Archery_Game_Unity3D - this is the
            // exact prefab the reference scene's Diana/Diana2-6 instances all
            // use) replaces the primitive cylinder-on-capsule-legs stand
            // below. Its own child named "Target" (tag "Target", a 1m-diameter
            // disc with a real MeshCollider and the actual Diana_target
            // bullseye texture) is exactly what HandleArrowHit already
            // expects from _targetFace - same collision-and-distance-to-
            // center logic, just against the real asset instead of a
            // procedural stand-in.
            if (fullTargetPrefab != null)
            {
                var real = Instantiate(fullTargetPrefab, transform);
                real.name = "Target Stand";
                // Left at the source asset's own identity rotation - the
                // "Target" disc's local Y is already its face normal,
                // pointing straight up. A prior pass rotated the whole
                // assembly upright to look like a real vertical dartboard,
                // but this lesson is about where an ARC actually LANDS, not
                // a wall the archer shoots straight into - "flat since it's
                // projectile motion" - so the disc stays flat on the ground,
                // the same orientation the source asset already used.
                //
                // "Pata1/2/3" (Spanish "leg") are the tripod stand that held
                // the disc up at ~waist height for a wall-mounted target -
                // meaningless (and visually distracting sticks) for a flat
                // ground target, so they're stripped, leaving just the disc.
                foreach (var legName in new[] { "Pata1", "Pata2", "Pata3" })
                {
                    var leg = real.transform.Find(legName);
                    if (leg != null) SafeDestroy(leg.gameObject);
                }
                _targetRoot = real.transform;
                _targetFace = real.transform.Find("Target");
                _targetFaceRenderer = _targetFace.GetComponent<Renderer>();
                return;
            }

            var standGO = new GameObject("Target Stand");
            standGO.transform.SetParent(transform, false);
            _targetRoot = standGO.transform;

            var face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "Target Face";
            face.transform.SetParent(standGO.transform, false);
            face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            face.transform.localScale = new Vector3(TargetRadius * 2f, 0.05f, TargetRadius * 2f);
            if (targetMaterial != null) face.GetComponent<Renderer>().sharedMaterial = targetMaterial;
            _targetFace = face.transform;
            _targetFaceRenderer = face.GetComponent<Renderer>();

            BuildStrut(standGO.transform, new Vector3(-0.3f, -0.5f, -0.15f), 20f);
            BuildStrut(standGO.transform, new Vector3(0.3f, -0.5f, -0.15f), -20f);
            BuildStrut(standGO.transform, new Vector3(0f, -0.5f, 0.2f), 0f);
        }

        private void BuildStrut(Transform parent, Vector3 localPos, float tiltZ)
        {
            var strut = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            strut.name = "Stand Leg";
            strut.transform.SetParent(parent, false);
            strut.transform.localPosition = localPos;
            strut.transform.localRotation = Quaternion.Euler(65f, 0f, tiltZ);
            strut.transform.localScale = new Vector3(0.04f, 0.5f, 0.04f);
            if (woodMaterial != null) strut.GetComponent<Renderer>().sharedMaterial = woodMaterial;
            SafeDestroy(strut.GetComponent<Collider>());
        }

        // Same as CanvasUIHelpers.CreateText, but auto-sized so a long
        // sentence at a fixed font size can never overflow the box it was
        // given (and spill into the text above/below it) - it shrinks down
        // until it actually fits instead.
        private static TMP_Text CreateFitText(Transform parent, string content, int maxFontSize, Color color,
            TextAlignmentOptions align, Vector2 anchoredPos, Vector2 size, string name = "Text")
        {
            var t = CreateText(parent, content, maxFontSize, color, align, anchoredPos, size, name);
            t.enableAutoSizing = true;
            // Floor raised from 8 - at this canvas's small world-space
            // scale, anything much below ~12pt read as genuinely blurry
            // (too few on-screen pixels per glyph), not just small. Left
            // below maxFontSize for every current caller (lowest is the
            // 14pt Lesson sentence) so autosize still has real shrink room
            // instead of min >= max disabling it entirely.
            t.fontSizeMin = Mathf.Min(12, maxFontSize);
            t.fontSizeMax = maxFontSize;
            return t;
        }

        // Assigning Renderer.sharedMaterial (singular) collapses a multi-
        // submesh renderer's material array down to length 1, leaving any
        // further submesh with no material at all - which URP renders as
        // flat white, not an error. Filling an array sized to the existing
        // submesh count keeps every submesh covered.
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

        // The Old Forest pack's meshes ship 4 LOD variants as sibling
        // GameObjects (_LOD0.._LOD3) with no working LODGroup carried over
        // through the raw FBX import, so all four rendered at once - heavy
        // Z-fighting that looked like a missing-texture wash-out, not
        // actually a material problem. Keeping only the nearest/highest
        // detail LOD0 fixes it and is also the right call visually at the
        // close range these props sit at in a small clearing.
        private static void KeepOnlyHighestLod(GameObject root)
        {
            foreach (Transform child in root.transform)
            {
                if (child.name.Contains("_LOD") && !child.name.Contains("_LOD0"))
                    SafeDestroy(child.gameObject);
            }
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ---- IMinigame ----

        public void InitializeGame(int startingLevel) { }
        public void StartGame() => NextChallenge();

        // ---- Rounds ----

        // The actual answer to "how do I figure out the angle" - solves
        // R = U^2 sin(2θ)/g for θ given the fixed launch speed and this
        // round's real distance, and shows the working, not just the
        // result. sin(2θ) = Rg/U^2 has two solutions in 0-90 (θ and 90-θ,
        // the classic "low flat arc vs high lobbed arc" pair a real archer
        // picks between) - showing both is honest about there being two
        // right answers, not one.
        private string BuildAngleHint(float distance)
        {
            var u = _bow != null ? _bow.launchSpeed : 18f;
            var x = distance * Gravity / (u * u);
            if (x > 1f)
            {
                // Distance is out of range even at the ideal 45 degrees -
                // shouldn't happen at the tuned distances/speed, but fails
                // honestly instead of printing garbage from asin() if it did.
                return $"HINT: even a perfect 45° only reaches {(u * u / Gravity):0.0}m at this draw strength - this target may be out of range.";
            }

            var twoTheta = Mathf.Asin(Mathf.Clamp(x, -1f, 1f)) * Mathf.Rad2Deg;
            var theta1 = twoTheta / 2f;
            var theta2 = 90f - theta1;
            // One decimal place, not a rounded whole degree - how far a shot
            // lands is far more sensitive to angle error near the low/high
            // ends of the achievable range than near 45°, so a whole-degree
            // hint could be off by more than the target's own radius even
            // when followed exactly. The distance curve above keeps both
            // solutions in a forgiving band, but the extra hint precision
            // costs nothing and only helps.
            return $"HINT: sin(2θ) = Rg/U² = ({distance:0.0}×9.81)/{u:0.0}² = {x:0.00}\n" +
                   $"2θ = {twoTheta:0.0}° (or {180f - twoTheta:0.0}°)  →  θ ≈ {theta1:0.0}° or {theta2:0.0}°";
        }

        private void NextChallenge()
        {
            _round++;
            if (_round > _totalRounds)
            {
                _questionText.text = "Complete!";
                _readoutText.text = "";
                _feedbackText.text = $"Score: {_score} / {_totalRounds}";
                ConvaiGuide.Speak($"You landed {_score} out of {_totalRounds} shots. Remember: horizontal speed never changed, vertical speed changed the whole time - that's projectile motion.");
                _progressBar?.SetProgress(_totalRounds, _totalRounds, "COMPLETE");
                QuestLog.MarkComplete(SceneManager.GetActiveScene().name);
                MinigameEnvironment.PlayRoundCompleteVfx(_targetFace.position);
                GameManager.Instance?.EndMinigameSession();
                return;
            }

            if (useSceneTargets)
            {
                // Switch to this round's real, pre-placed Diana instead of
                // moving a stand - each one sits at its own authored spot
                // scattered across the terrain, not staged at a fake
                // incrementing distance.
                _targetFace = _sceneTargetFaces[_round - 1];
                _targetFaceRenderer = _targetFace.GetComponent<Renderer>();
                _targetRoot = _targetFace.parent;
                _targetDistance = Vector3.Distance(_bowRoot.position, _targetFace.position);
                _targetFaceRenderer.material.color = Color.white;
            }
            else
            {
                var stage = (_round - 1) / RoundsPerStage;
                var roundInStage = (_round - 1) % RoundsPerStage;
                // With a FIXED launch speed, R = U^2 sin(2θ)/g has two valid
                // angles for any given distance, but how far apart the two
                // solutions are - and how sensitive landing spot is to a
                // small aiming error - depends entirely on how close the
                // distance is to the fixed speed's maximum range (U^2/g,
                // ~33m at 18 m/s): near that ceiling both solutions converge
                // toward a forgiving ~45°, but a SHORT distance forces the
                // low-angle solution down toward an unforgivably shallow,
                // razor-sensitive shot (confirmed live: round 1's old 9m
                // distance solved to 8°/82° - even the exact hinted angle
                // missed, since 2*(U^2/g)*cos(2θ) blows up that close to 0°/
                // 90°). Keeping every round's distance in the upper ~75-93%
                // of max range keeps both solution angles in a genuinely
                // learnable, hand-precision-tolerant 20-45° band throughout,
                // instead of only the last stage being reachable at all.
                _targetDistance = 24f + stage * 2.5f + roundInStage * 0.8f;
                // Y dropped from 1.3 (chest height, right for a wall-mounted
                // dartboard) to just above the ground - a flat landing
                // target has to actually sit where the arc comes down, not
                // float at head height.
                _targetRoot.localPosition = new Vector3(0f, 0.05f, BowZOffset + _targetDistance);
                // targetMaterial only exists to tint the crude procedural
                // cylinder fallback (BuildTarget's last-resort branch, which
                // never sets a material of its own). When fullTargetPrefab
                // is assigned, the real "Target" child already ships its own
                // correct Diana_target material - overwriting sharedMaterial
                // here with the (usually unassigned) targetMaterial field
                // wiped it out, and the very next line's `.material` getter
                // then auto-instantiated a blank, untextured material to
                // replace it. Confirmed live: the target rendered as a flat
                // grey disc instead of the real bullseye texture the exact
                // same prefab shows everywhere else it's placed in the scene.
                if (fullTargetPrefab == null) _targetFaceRenderer.sharedMaterial = targetMaterial;
                _targetFaceRenderer.material.color = Color.white;
            }

            _mistakesThisTask = 0;
            _taskStartTime = Time.time;
            _hasFiredThisRound = false;

            // Speed is fixed now (ArcheryBow.launchSpeed) - the only thing
            // left to solve for is the angle, exactly one equation:
            // R = U^2 sin(2θ)/g. Solving it for the player and showing the
            // work (not just the final number) is the actual "how do I
            // figure out the angle" answer - a live worked hint instead of
            // a bare instruction to guess.
            _questionText.text = $"Target {_round}/{_totalRounds}: {_targetDistance:0.0}m away. Find the angle that reaches it.";
            _readoutText.text = BuildAngleHint(_targetDistance);
            _feedbackText.text = "Grab the bow (left hand), grab the arrow (right hand), pull back and aim.";

            // 3 correct hits = 1 stage. Every stage boundary (round 4, 7, ...)
            // gets its own callout instead of quietly blending into the next
            // shot, so "clearing" actually reads as reaching a checkpoint.
            if (!useSceneTargets && _round > 1 && (_round - 1) % RoundsPerStage == 0)
            {
                var stageJustCleared = (_round - 1) / RoundsPerStage;
                _feedbackText.text = $"Stage {stageJustCleared} cleared! Targets just moved further out.";
                ConvaiGuide.Speak("Nice work clearing that stage - three in a row. The target's further away now, so you'll need a different angle to reach it.");
            }

            if (_progressBar != null)
            {
                var completed = _round - 1;
                if (useSceneTargets)
                {
                    _progressBar.SetProgress(completed, _totalRounds, $"ROUND {_round}/{_totalRounds}");
                }
                else
                {
                    var totalStages = Mathf.Max(1, _totalRounds / RoundsPerStage);
                    var stage = Mathf.Min(totalStages, (_round - 1) / RoundsPerStage + 1);
                    _progressBar.SetProgress(completed, _totalRounds, $"STAGE {stage}/{totalStages}");
                }
            }
            _roundActive = true;
        }

        private void HandleRelease(GameObject arrowGO, Vector3 direction, Vector3 origin, float angleDeg, float speed)
        {
            if (!_roundActive || _hasFiredThisRound) return;
            _hasFiredThisRound = true;
            _shotResolved = false;
            if (_drawPredictionText != null) _drawPredictionText.text = "";

            if (_liveArrow != null) _liveArrow.ClearMarkers();

            // Speed genuinely comes from how far this shot was actually
            // drawn (ArcheryBow.OnRelease) - not a secretly-solved-for value
            // that guaranteed a hit regardless of what the player did.
            var velocity = direction * speed;

            // This IS the real arrow the player drew from their quiver and
            // nocked themselves - no more building a fresh disconnected
            // visual at fire time.
            arrowGO.name = "Fired Arrow";
            arrowGO.transform.SetParent(null, true);
            arrowGO.transform.position = origin;
            arrowGO.transform.rotation = Quaternion.LookRotation(velocity);

            // This arrow has graduated out of the quiver system - it's a
            // live projectile now, not something waiting to be nocked.
            // QuiverArrow's own release handling never runs a second time
            // (its "already nocked" flag is permanent), so leaving the
            // component attached was exactly why grabbing an already-fired
            // arrow later and letting go left it frozen in mid-air -
            // ArcheryArrow's own grab listener (added below) is what
            // actually handles a spent arrow being picked back up.
            var staleQuiverArrow = arrowGO.GetComponent<QuiverArrow>();
            if (staleQuiverArrow != null) Destroy(staleQuiverArrow);

            var col = arrowGO.GetComponent<CapsuleCollider>();
            if (col == null)
            {
                col = arrowGO.AddComponent<CapsuleCollider>();
                col.radius = 0.015f;
                col.height = 0.9f;
                col.direction = 2; // Z axis
                col.center = new Vector3(0f, 0f, 0.35f);
            }

            // The arrow releases right at the nock, inside the bow's own
            // grip collider - without this, Unity's very first physics step
            // sees them already overlapping and fires an immediate self-
            // collision, freezing the arrow right at the string instead of
            // ever flying ("doesn't even shoot towards target" was this, not
            // a bad launch direction).
            foreach (var bowCollider in _bowRoot.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(col, bowCollider, true);

            var arrow = arrowGO.GetComponent<ArcheryArrow>();
            if (arrow == null) arrow = arrowGO.AddComponent<ArcheryArrow>();
            arrow.OnHit += HandleArrowHit;
            arrow.Launch(velocity);
            _liveArrow = arrow;

            // Real-time flight now (no more global Time.timeScale slow-mo -
            // that was a blunt, global hack that risked exactly the kind of
            // "everything looks frozen" symptom this was meant to fix, and
            // fighting XR's own frame timing besides). "Preview of where it
            // landed" is handled by the dedicated replay camera instead,
            // which doesn't touch time or the player's own view at all.
            _replayCam?.Follow(arrow.transform);
            StartCoroutine(ForceResolveIfUnresolved(6f));

            if (_audio != null && shootClip != null) _audio.PlayOneShot(shootClip);
            _feedbackText.text = "In flight - watch the replay screen, and the dots: even gaps sideways, uneven gaps up/down.";
        }

        // The arrow flew clean out of the range without ever colliding with
        // anything (sailed over the backstop, out of the world bounds,
        // whatever) - OnCollisionEnter never fires, so without this the
        // round would just hang forever with no feedback and no way to
        // retry ("i cant see if it hit or not" applies doubly to a shot
        // that never resolves at all). Plain WaitForSeconds now - there's no
        // more slow-mo to be realtime-independent of.
        // A spent arrow used to just lie wherever it landed forever - after
        // enough shots the range fills with old arrows, and one landing
        // directly along the (mostly-reused) firing lane could physically
        // block the NEXT arrow's flight the instant it launched, reading as
        // "unable to shoot arrows after certain shots" even though release/
        // launch itself was working fine. Destroying it a few seconds after
        // it resolves (real enough time to see where it landed, via the
        // replay screen or in person) keeps the range clear.
        private const float SpentArrowLifetime = 7f;

        private IEnumerator ForceResolveIfUnresolved(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (_roundActive && _hasFiredThisRound && !_shotResolved)
            {
                _shotResolved = true;
                _replayCam?.Idle();
                _mistakesThisTask++;
                _feedbackText.text = "Missed - flew past everything. Try again.";
                HandleFailure();
                if (_liveArrow != null) Destroy(_liveArrow.gameObject, SpentArrowLifetime);
                Invoke(nameof(RetryChallenge), 1.2f);
            }
        }

        private void HandleArrowHit(ArcheryArrow arrow, Collision collision)
        {
            if (_shotResolved) return;
            _shotResolved = true;
            var hitPoint = collision.GetContact(0).point;
            _replayCam?.HoldOnLanding(hitPoint);
            Destroy(arrow.gameObject, SpentArrowLifetime);

            var isTarget = collision.gameObject == _targetFace.gameObject;

            if (isTarget)
            {
                var local = _targetFace.InverseTransformPoint(hitPoint);
                var distanceToCenter = new Vector2(local.x, local.z).magnitude * _targetFace.localScale.x;
                var radius = TargetRadius;
                var hit = distanceToCenter < radius;

                if (hit)
                {
                    var points = Mathf.CeilToInt(10f * (1f - distanceToCenter / radius));
                    _score += Mathf.Max(1, points);
                    _targetFaceRenderer.material.color = CorrectColor;
                    if (_audio != null && bullseyeClip != null) _audio.PlayOneShot(bullseyeClip);
                    _feedbackText.text = distanceToCenter < radius * 0.25f ? "Bullseye!" : "Hit! Nice shot.";
                    _roundActive = false;
                    HandleSuccess();
                    Invoke(nameof(NextChallenge), 1.8f);
                    return;
                }
            }

            if (isTarget) _targetFaceRenderer.material.color = WrongColor;
            if (_audio != null && arrowHitClip != null) _audio.PlayOneShot(arrowHitClip);
            if (_audio != null && missClip != null) _audio.PlayOneShot(missClip);
            _mistakesThisTask++;
            _feedbackText.text = isTarget ? "Edge of the target - try again." : "Missed - try again.";
            HandleFailure();
            Invoke(nameof(RetryChallenge), 1.8f);
        }

        private void RetryChallenge()
        {
            _hasFiredThisRound = false;
            _feedbackText.text = "Grab the arrow, pull back, let go to fire.";
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
                concept = "projectile motion (constant horizontal velocity, gravity-accelerated vertical velocity)",
                taskDescription = $"hit a target {_targetDistance:0.0}m away",
                playerAnswer = "",
                correctAnswer = "land the arrow within the target ring",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }
}
