using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AILearningEcosystem.Learning;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Math Adventure III - "The Sentry Vault", retaught as trigonometry.
    /// The vault tablet no longer asks for a raw arithmetic answer - it asks
    /// for an ANGLE, and the player answers by aiming the real hand-held
    /// cannon (now a RayGun model ported from ref/VR-Mathipia) to that angle
    /// and firing. A persistent reference table and a labelled right-triangle
    /// diagram (SOH-CAH-TOA) stay in view the whole time so the player can
    /// look up the answer while they aim - the lesson is "read the table,
    /// then act on it," not memorization under pressure.
    ///
    /// Aiming is a real two-handed grip (CannonAimHandles.cs), ported from
    /// the user's own ref/VR-Mathipia RayShooterController.cs rather than the
    /// old floating +/- dial - grab both handles and physically raise or
    /// lower them to change elevation, with the angle read out live. The
    /// scene environment is an open forest clearing (WorldEnvironment.cs,
    /// singleClearing mode) instead of the cramped dungeon room the other
    /// two Math Adventures still use.
    /// </summary>
    [ExecuteAlways]
    public class MathCannonGame : MonoBehaviour, IMinigame
    {
        public string MinigameId => "MathCannon";
        public string Subject => "Mathematics";

        [Header("Cannon art (ported from ref/VR-Mathipia's RayGun)")]
        public GameObject rayGunModel;
        public Material rayGunMaterial;

        private const int TotalRounds = 6;
        private const float TargetDistance = 11f; // pushed further out - was 4m, too close to feel like a real shot
        private const float TargetHeight = 1.4f;
        private const float LaunchSpeed = 8f;
        private const float AngleToleranceDegrees = 6f;
        // A real ground-mounted siege cannon reads as much bigger than a
        // hand-held prop - bumped well past the previous 1.8x, then again
        // per an explicit "make cannon bigger" ask.
        private const float CannonScale = 5.2f;
        private const float CannonBaseHeight = 1.0f; // pedestal the barrel sits on, visibly touching the ground - raised to match the bigger cannon

        // The five angles the reference table covers - what the questions
        // ask for, graded against the live continuous aim angle within
        // AngleToleranceDegrees (the cannon itself is no longer restricted
        // to only these five positions).
        private static readonly int[] StandardAngles = { 0, 30, 45, 60, 90 };
        private static readonly float[] SinTable = { 0f, 0.50f, 0.71f, 0.87f, 1.00f };
        private static readonly float[] CosTable = { 1.00f, 0.87f, 0.71f, 0.50f, 0f };
        private static readonly float[] TanTable = { 0f, 0.58f, 1.00f, 1.73f, float.NaN }; // tan(90) undefined - excluded from questions

        private static readonly Color TargetColor = new Color(0.357f, 0.549f, 1f);
        private static readonly Color CorrectColor = new Color(0.2f, 0.85f, 0.6f);
        private static readonly Color WrongColor = new Color(0.95f, 0.4f, 0.4f);

        public System.Action<int, int> onComplete;

        private TextMeshPro _tabletText;
        private TextMeshPro _angleReadout;
        private TextMeshPro _baseAngleReadout;
        private AngleWedgeIndicator _angleWedge;
        private Transform _cannonMuzzle;
        private Transform _cannonPivot;
        private CannonAimHandles _aimHandles;
        private Transform _targetRing;
        private Renderer _targetRingRenderer;
        private AudioSource _audio;
        private AudioClip _correctClip;
        private AudioClip _incorrectClip;

        private int _round;
        private int _score;
        private int _level = 1;
        private string _correctFuncName;
        private int _correctAngleIndex;
        private float _correctValue;
        private string _concept;
        private string _taskDescription;
        private float _taskStartTime;
        private int _mistakesThisTask;
        private bool _roundActive;
        private bool _triggerHeldLastFrame;
        private float _lastFeedbackTime;

        // All the standing set-dressing (trig table, triangle diagram,
        // objective panel, tablet, cannon, target) used to be rebuilt from
        // scratch by script every single time this scene was entered,
        // living nowhere the Editor could see or edit - "make it so i can
        // edit their position in scene view... not spawn them via script."
        // Same [ExecuteAlways] build-once-and-persist pattern already used
        // by ArcheryProjectileGame and the other minigames: Awake() below
        // only calls BuildStatic() the FIRST time this component ever runs
        // (nothing built yet) and just re-links references on every
        // subsequent load, so anything you move in the Scene view stays
        // moved. Use the "Rebuild" button in this component's Inspector
        // (see MinigameRebuildEditors.cs) to intentionally regenerate
        // everything after a script/config change.
        private void Awake()
        {
            if (transform.Find("Cannon Base") == null)
                BuildStatic();
            else
                RediscoverReferences();
        }

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
            BuildTrigReferenceTable();
            BuildTriangleDiagram();
            BuildObjectivePanel();
            BuildTablet();
            BuildCannon();
        }

        private void RediscoverReferences()
        {
            _tabletText = transform.Find("Tablet Runes")?.GetComponent<TextMeshPro>();
            _angleReadout = transform.Find("Angle Readout")?.GetComponent<TextMeshPro>();
            _baseAngleReadout = transform.Find("Base Angle Readout")?.GetComponent<TextMeshPro>();

            var pivot = transform.Find("Cannon Pivot");
            _cannonPivot = pivot;
            _aimHandles = pivot != null ? pivot.GetComponent<CannonAimHandles>() : null;
            _cannonMuzzle = pivot != null ? pivot.Find("Cannon Body/Muzzle") : null;

            var wedge = transform.Find("Aim Angle Wedge");
            _angleWedge = wedge != null ? wedge.GetComponent<AngleWedgeIndicator>() : null;
            if (_angleWedge != null && _cannonPivot != null)
                _angleWedge.Init(_cannonPivot, transform.forward, 1.3f, new Color(1f, 0.85f, 0.2f));

            var target = transform.Find("Landing Target");
            _targetRing = target;
            _targetRingRenderer = target != null ? target.GetComponent<Renderer>() : null;

            // The two feedback tones are synthesized in memory (AudioClip.Create),
            // not real asset files, so they never survive a scene reload/domain
            // reload - regenerated fresh every time rather than persisted.
            _audio = transform.Find("Cannon Base")?.GetComponent<AudioSource>();
            _correctClip = BuildToneClip(new[] { 660f, 990f }, 0.09f);
            _incorrectClip = BuildToneClip(new[] { 220f, 165f }, 0.14f);
        }

        public void InitializeGame(int startingLevel)
        {
            _level = Mathf.Max(1, startingLevel);
        }

        // The "how do I actually play this" instructions used to live only
        // in a spoken ConvaiGuide.Speak line - which currently never plays
        // at all (Convai's API is over its usage quota, confirmed live via
        // console errors), leaving nothing on screen explaining the task.
        // This is the same information, permanently on a sign instead of
        // dependent on a voice line that may or may not fire.
        private void BuildObjectivePanel()
        {
            var panelGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelGO.name = "Objective Panel";
            panelGO.transform.SetParent(transform, false);
            // Sits well ABOVE the trig table (which tops out around y=2.95)
            // and the triangle diagram - it used to sit at y=2.6, almost
            // exactly overlapping the trig table's own band, which is why
            // "OBJECTIVE" and "Angle sin cos tan" rendered on top of each
            // other from a distance. Multi-line TextMeshPro text isn't
            // clipped to this backing cube's nominal height, so even after
            // the first fix its last line still dipped low enough to clip
            // the table's header row - pushed further up again, with a
            // shorter two-line message so its real rendered height (not
            // just the panel mesh) actually clears the gap.
            panelGO.transform.localPosition = new Vector3(0f, 4.5f, TargetDistance + 1.8f);
            panelGO.transform.localRotation = Quaternion.identity;
            panelGO.transform.localScale = new Vector3(2.4f, 0.5f, 0.05f);
            panelGO.GetComponent<Renderer>().material.color = new Color(0.13f, 0.14f, 0.19f);
            SafeDestroy(panelGO.GetComponent<Collider>());

            var textGO = new GameObject("Objective Text");
            textGO.transform.SetParent(panelGO.transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            textGO.transform.localRotation = Quaternion.identity;
            textGO.transform.localScale = new Vector3(0.42f, 1.67f, 1f);
            var text = textGO.AddComponent<TextMeshPro>();
            text.fontSize = 2.6f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.85f, 0.9f, 1f);
            text.text = "<b>OBJECTIVE</b>  Read the tablet, look up the angle on the trig table below.\n" +
                "Grip both handles, match the < wedge to that angle, then fire.";
        }

        public void StartWith()
        {
            _round = 0;
            _score = 0;
            var startingLevel = GameManager.Instance != null ? GameManager.Instance.Difficulty.CurrentLevel(Subject, MinigameId) : 1;
            InitializeGame(startingLevel);
            GameManager.Instance?.StartMinigameSession(this);
            ConvaiGuide.Speak("Before you shoot, look at the table on the wall. It shows sine, cosine and tangent for five angles. " +
                "Sine is the opposite side over the hypotenuse, cosine is the adjacent side over the hypotenuse, tangent is opposite over adjacent - remember it as SOH, CAH, TOA. " +
                "I'll ask for one of those values - find the matching angle on the table, set the cannon to it, and fire.");
            Invoke(nameof(StartGame), 8f);
        }

        public void StartGame() => NextProblem();

        private void Update()
        {
            if (!_roundActive || _cannonMuzzle == null) return;

            // Live readout - the whole point of two-handed aiming over the old
            // dial is seeing the angle change as you move your hands, not just
            // after you commit to a value.
            if (_angleReadout != null && _aimHandles != null)
                _angleReadout.text = _aimHandles.BothHandlesHeld ? $"{_aimHandles.CurrentAngleDegrees:0}°" : "Grip both handles";
            if (_baseAngleReadout != null && _aimHandles != null)
                _baseAngleReadout.text = _aimHandles.BothHandlesHeld ? $"θ = {_aimHandles.CurrentAngleDegrees:0}°" : "θ = --";

            // The actual "<" angle gizmo, same as the archery range - only
            // while both handles are actually gripped (that's when the angle
            // means anything), matching the barrel's real live direction.
            if (_angleWedge != null && _aimHandles != null)
            {
                if (_aimHandles.BothHandlesHeld)
                    _angleWedge.UpdateAngle(_aimHandles.CurrentAngleDegrees, _aimHandles.CurrentAimDirection);
                else
                    _angleWedge.SetVisible(false);
            }

            bool triggerHeld = false;
            if (_aimHandles != null && _aimHandles.BothHandlesHeld)
            {
                var left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
                var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
                triggerHeld = (left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool lt) && lt)
                    || (right.isValid && right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool rt) && rt);
            }

            if (triggerHeld && !_triggerHeldLastFrame)
                Fire();
            _triggerHeldLastFrame = triggerHeld;
        }

        // ---- Build ----

        // The vault tablet still poses the current problem, physically
        // carved into the wall - same diegetic trick as the other two Math
        // adventures.
        private void BuildTablet()
        {
            var slabGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            slabGO.name = "Vault Tablet";
            slabGO.transform.SetParent(transform, false);
            slabGO.transform.localPosition = new Vector3(0f, 2f, TargetDistance + 1.2f);
            slabGO.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            slabGO.transform.localScale = new Vector3(0.6f, 0.03f, 0.4f);
            slabGO.GetComponent<Renderer>().material.color = new Color(0.3f, 0.28f, 0.25f);
            SafeDestroy(slabGO.GetComponent<Collider>());

            var textGO = new GameObject("Tablet Runes");
            textGO.transform.SetParent(transform, true);
            textGO.transform.position = slabGO.transform.TransformPoint(new Vector3(0f, 0.04f, 0f));
            textGO.transform.rotation = slabGO.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            textGO.transform.localScale = Vector3.one * 0.16f;
            _tabletText = textGO.AddComponent<TextMeshPro>();
            _tabletText.fontSize = 5f;
            _tabletText.alignment = TextAlignmentOptions.Center;
            _tabletText.color = new Color(0.85f, 0.75f, 0.4f);
        }

        // A standing panel, listing sin/cos/tan for the five angles the
        // cannon can fire at - the answer key the player is meant to
        // actually use, not hide. Used to stand all the way out at the
        // target (11m away) - moved beside the cannon itself, inclined like
        // a reading lectern, so it's actually checkable while aiming
        // instead of requiring a look away across the whole range.
        private void BuildTrigReferenceTable()
        {
            var panelGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panelGO.name = "Trig Reference Table";
            panelGO.transform.SetParent(transform, false);
            panelGO.transform.localPosition = new Vector3(1.35f, 2.1f, 0.75f);
            // Tilted back (X only) - a lectern angle instead of a flat
            // vertical sign, per an explicit "inclined" ask. Deliberately NOT
            // yawed: the child text's own identity rotation is what makes it
            // read right-side-up (confirmed live - adding a yaw here without
            // also re-deriving the text's own rotation flipped it into
            // mirrored/upside-down nonsense), and a pure X tilt keeps that
            // relationship intact while still reading as "inclined."
            panelGO.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            panelGO.transform.localScale = new Vector3(1.5f, 1.1f, 0.05f) * 0.8f; // resized smaller by 0.2 (20%)
            panelGO.GetComponent<Renderer>().material.color = new Color(0.15f, 0.16f, 0.2f);
            SafeDestroy(panelGO.GetComponent<Collider>());

            var textGO = new GameObject("Table Text");
            textGO.transform.SetParent(panelGO.transform, false);
            textGO.transform.localPosition = new Vector3(0f, 0f, -0.6f);
            textGO.transform.localRotation = Quaternion.identity;
            textGO.transform.localScale = new Vector3(0.67f, 0.91f, 1f);
            var text = textGO.AddComponent<TextMeshPro>();
            text.fontSize = 3.2f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.text = "<b>Angle   sin    cos    tan</b>\n" +
                         "  0°     0.00   1.00   0.00\n" +
                         " 30°     0.50   0.87   0.58\n" +
                         " 45°     0.71   0.71   1.00\n" +
                         " 60°     0.87   0.50   1.73\n" +
                         " 90°     1.00   0.00    --";
        }

        // A literal 3D right triangle - three thin stretched cubes as edges,
        // labelled Opposite / Adjacent / Hypotenuse, so "what sin/cos/tan
        // actually is" is something to look at, not just a formula.
        private void BuildTriangleDiagram()
        {
            var root = new GameObject("Triangle Diagram");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(-1.9f, 1.3f, TargetDistance + 1.8f);
            root.transform.localRotation = Quaternion.identity;

            // Right angle at origin: Adjacent runs along local X, Opposite
            // runs up along local Y, Hypotenuse connects their far ends.
            var adjacentLen = 0.6f;
            var oppositeLen = 0.4f;
            BuildEdge(root.transform, new Vector3(adjacentLen / 2f, 0f, 0f), adjacentLen, 0f, new Color(0.357f, 0.549f, 1f));
            BuildEdge(root.transform, new Vector3(0f, oppositeLen / 2f, 0f), oppositeLen, 90f, new Color(0.95f, 0.6f, 0.2f));
            var hypLen = Mathf.Sqrt(adjacentLen * adjacentLen + oppositeLen * oppositeLen);
            var hypAngle = Mathf.Atan2(oppositeLen, -adjacentLen) * Mathf.Rad2Deg;
            BuildEdge(root.transform, new Vector3(adjacentLen / 2f, oppositeLen / 2f, 0f), hypLen, hypAngle, new Color(0.2f, 0.85f, 0.6f));

            BuildLabel(root.transform, new Vector3(adjacentLen / 2f, -0.12f, 0f), "Adjacent", new Color(0.357f, 0.549f, 1f));
            BuildLabel(root.transform, new Vector3(-0.14f, oppositeLen / 2f, 0f), "Opposite", new Color(0.95f, 0.6f, 0.2f));
            BuildLabel(root.transform, new Vector3(adjacentLen / 2f + 0.08f, oppositeLen / 2f + 0.1f, 0f), "Hypotenuse", new Color(0.2f, 0.85f, 0.6f));
            BuildLabel(root.transform, new Vector3(0.08f, 0.08f, 0f), "θ", Color.white);

            var captionGO = new GameObject("SOHCAHTOA Caption");
            captionGO.transform.SetParent(root.transform, false);
            captionGO.transform.localPosition = new Vector3(adjacentLen / 2f, -0.35f, 0f);
            captionGO.transform.localScale = Vector3.one * 0.5f;
            var caption = captionGO.AddComponent<TextMeshPro>();
            caption.text = "sin=O/H   cos=A/H   tan=O/A";
            caption.fontSize = 3f;
            caption.alignment = TextAlignmentOptions.Center;
            caption.color = new Color(0.75f, 0.75f, 0.8f);
        }

        private void BuildEdge(Transform parent, Vector3 localPos, float length, float zRotDeg, Color color)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "Triangle Edge";
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = localPos;
            edge.transform.localRotation = Quaternion.Euler(0f, 0f, zRotDeg);
            edge.transform.localScale = new Vector3(length, 0.02f, 0.02f);
            edge.GetComponent<Renderer>().material.color = color;
            SafeDestroy(edge.GetComponent<Collider>());
        }

        // Concatenates one short sine-wave tone per frequency into a single
        // AudioClip - a tiny self-contained synth so correct/incorrect
        // feedback doesn't depend on an audio asset ever being assigned.
        private static AudioClip BuildToneClip(float[] frequencies, float noteSeconds)
        {
            const int sampleRate = 44100;
            var samplesPerNote = Mathf.CeilToInt(sampleRate * noteSeconds);
            var totalSamples = samplesPerNote * frequencies.Length;
            var data = new float[totalSamples];

            for (var n = 0; n < frequencies.Length; n++)
            {
                var freq = frequencies[n];
                for (var i = 0; i < samplesPerNote; i++)
                {
                    var t = (float)i / sampleRate;
                    // A short fade-out per note avoids an audible click at
                    // the boundary between notes / at the very end.
                    var envelope = 1f - (float)i / samplesPerNote;
                    data[n * samplesPerNote + i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.5f;
                }
            }

            var clip = AudioClip.Create("Tone", totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void BuildLabel(Transform parent, Vector3 localPos, string text, Color color)
        {
            var labelGO = new GameObject("Label " + text);
            labelGO.transform.SetParent(parent, false);
            labelGO.transform.localPosition = localPos;
            labelGO.transform.localScale = Vector3.one * 0.35f;
            var tmp = labelGO.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 4f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
        }

        private void BuildCannon()
        {
            // A visible mount/pedestal so the cannon actually reads as
            // standing ON the ground rather than floating at chest height
            // with nothing under it - base touches y=0, barrel pivot sits on
            // top of it.
            var baseGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseGO.name = "Cannon Base";
            baseGO.transform.SetParent(transform, false);
            baseGO.transform.localPosition = new Vector3(0f, CannonBaseHeight / 2f, 0.6f);
            baseGO.transform.localScale = new Vector3(0.55f, CannonBaseHeight / 2f, 0.55f);
            baseGO.GetComponent<Renderer>().material.color = new Color(0.22f, 0.2f, 0.18f);

            // Correct/incorrect feedback sound - synthesized on the fly
            // rather than depending on an audio asset being dragged into
            // the Inspector, so it works out of the box. A short rising
            // two-note chime for correct, a short low buzz for wrong -
            // distinct enough to tell apart without even looking at the
            // target's glow color.
            _audio = baseGO.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _correctClip = BuildToneClip(new[] { 660f, 990f }, 0.09f);
            _incorrectClip = BuildToneClip(new[] { 220f, 165f }, 0.14f);

            // A real elevation pivot - a separate empty anchored at the top
            // of the base cylinder. Everything that should swing together
            // (barrel visual + both handles) is parented under THIS, offset
            // so the barrel's own base sits right at the pivot's origin.
            // Rotating the mesh directly around its own internal origin (the
            // old approach) rotated it around a point inside the model, not
            // its base - the visible base swung away from the pedestal every
            // time the angle changed, reading as "unattached"/glitchy.
            var pivotGO = new GameObject("Cannon Pivot");
            pivotGO.transform.SetParent(transform, false);
            pivotGO.transform.localPosition = new Vector3(0f, CannonBaseHeight, 0.6f);
            _cannonPivot = pivotGO.transform;

            GameObject bodyGO;
            if (rayGunModel != null)
            {
                bodyGO = Instantiate(rayGunModel, pivotGO.transform);
                bodyGO.name = "Cannon Body";
                bodyGO.transform.localPosition = Vector3.zero;
                bodyGO.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                bodyGO.transform.localScale = Vector3.one * CannonScale;
                ApplyMaterial(bodyGO, rayGunMaterial);

                // RayGun.fbx bundles an unrelated flat "ground_plane_low" mesh
                // alongside the actual gun body ("main_body_low") - a leftover
                // from however the source scene originally exported it. Left in,
                // it reads as two huge grey wings filling the whole view up
                // close. Strip everything except the real gun mesh (before the
                // bounds measurement below, so that stray mesh can't skew it).
                foreach (Transform child in bodyGO.GetComponentsInChildren<Transform>(true))
                    if (child != bodyGO.transform && child.name.StartsWith("ground_plane_low"))
                        SafeDestroy(child.gameObject);

                // A hardcoded "lowest point is 0.09 above origin" fudge factor
                // used to live here - measured once at one scale, it silently
                // stopped matching reality as CannonScale grew (confirmed live:
                // at scale 5.2 the body's real lowest point sat 0.63 ABOVE the
                // pivot, floating well clear of the pedestal - "unable to
                // rotate... make cannon bigger" exposed how wrong the old
                // constant already was). Measuring the actual renderer bounds
                // and dropping the body by exactly that gap is scale- and
                // model-proof - it can never drift out of sync again.
                // Destroy() (as opposed to DestroyImmediate()) only marks the
                // stray mesh for removal at end of frame - GetComponentsInChildren
                // right after would still see it and pull its bounds into this
                // measurement (confirmed live: the shift below silently did
                // nothing at all until this was filtered), so skip it by name
                // exactly like the destroy loop above does instead of trusting
                // it's already gone.
                var boundsSet = false;
                var bounds = new Bounds();
                foreach (var r in bodyGO.GetComponentsInChildren<Renderer>())
                {
                    if (r.transform.name.StartsWith("ground_plane_low")) continue;
                    if (!boundsSet) { bounds = r.bounds; boundsSet = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (boundsSet)
                {
                    var shiftUp = pivotGO.transform.position.y - bounds.min.y;
                    bodyGO.transform.position += new Vector3(0f, shiftUp, 0f);
                }
            }
            else
            {
                bodyGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bodyGO.name = "Cannon Body";
                bodyGO.transform.SetParent(pivotGO.transform, false);
                bodyGO.transform.localPosition = Vector3.zero;
                bodyGO.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                bodyGO.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);
                bodyGO.GetComponent<Renderer>().material.color = new Color(0.25f, 0.27f, 0.33f);
            }

            var muzzleGO = new GameObject("Muzzle");
            muzzleGO.transform.SetParent(bodyGO.transform, false);
            muzzleGO.transform.localPosition = rayGunModel != null ? new Vector3(0f, 0f, 0.2f) : new Vector3(0f, 0.5f, 0f);
            _cannonMuzzle = muzzleGO.transform;

            // The gun body itself no longer needs its own Rigidbody/grab - the
            // pivot is what gets aimed via the two handles now (ported
            // mechanic, see CannonAimHandles.cs), so nothing directly
            // physics-drives the body anymore.
            _aimHandles = pivotGO.AddComponent<CannonAimHandles>();
            _aimHandles.pivot = _cannonPivot;
            // Moved BEHIND the cannon (negative Z - toward the player, away
            // from the muzzle) instead of out to the sides of the barrel.
            // Beside-the-barrel placement put the grip almost directly on
            // top of the pivot's own position, so raising your hands to
            // increase elevation meant reaching straight up and out to the
            // sides - a real person runs out of comfortable shoulder
            // rotation well before actually reaching 90 ("unable to rotate
            // past 45"). A real elevation cannon's crew works a handle at
            // the BREECH (behind, close to the body) - pulling that up and
            // back sweeps a much bigger vertical angle for the same
            // realistic arm motion, which is what this now mirrors.
            _aimHandles.leftHandle = BuildAimHandle(pivotGO.transform, "Left Handle", new Vector3(-0.22f, -0.05f, -0.6f));
            _aimHandles.rightHandle = BuildAimHandle(pivotGO.transform, "Right Handle", new Vector3(0.22f, -0.05f, -0.6f));

            var readoutGO = new GameObject("Angle Readout");
            readoutGO.transform.SetParent(transform, true);
            readoutGO.transform.position = transform.TransformPoint(new Vector3(0.8f, 1.3f, TargetDistance * 0.15f));
            readoutGO.transform.rotation = transform.rotation;
            readoutGO.transform.localScale = Vector3.one * 0.18f;
            _angleReadout = readoutGO.AddComponent<TextMeshPro>();
            _angleReadout.fontSize = 5f;
            _angleReadout.alignment = TextAlignmentOptions.Center;
            _angleReadout.color = Color.white;
            _angleReadout.text = "Grip both handles";

            // A second, bigger readout right at the cannon's own base - "the
            // angle between the cannon and the ground," positioned where the
            // barrel actually meets its mount instead of off in the HUD, with
            // a permanently-visible ground-reference line under it so the
            // number reads as an angle-from-ground measurement, not just a
            // floating label.
            var groundLineGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundLineGO.name = "Angle Ground Reference";
            groundLineGO.transform.SetParent(transform, false);
            groundLineGO.transform.localPosition = new Vector3(0.9f, 0.02f, 0.6f);
            groundLineGO.transform.localScale = new Vector3(1.4f, 0.02f, 0.03f);
            groundLineGO.GetComponent<Renderer>().material.color = new Color(0.9f, 0.85f, 0.3f);
            SafeDestroy(groundLineGO.GetComponent<Collider>());

            var baseAngleGO = new GameObject("Base Angle Readout");
            baseAngleGO.transform.SetParent(transform, true);
            baseAngleGO.transform.position = transform.TransformPoint(new Vector3(1.5f, CannonBaseHeight + 0.3f, 0.6f));
            baseAngleGO.transform.rotation = transform.rotation;
            baseAngleGO.transform.localScale = Vector3.one * 0.22f;
            _baseAngleReadout = baseAngleGO.AddComponent<TextMeshPro>();
            _baseAngleReadout.fontSize = 8f;
            _baseAngleReadout.fontStyle = FontStyles.Bold;
            _baseAngleReadout.alignment = TextAlignmentOptions.Center;
            _baseAngleReadout.color = new Color(1f, 0.85f, 0.2f);
            _baseAngleReadout.text = "θ = --";

            // The actual "<" angle gizmo - same component as the archery
            // range - anchored where the barrel pivots, so the wedge opens
            // from the real elevation point instead of floating disconnected
            // from the cannon.
            var wedgeGO = new GameObject("Aim Angle Wedge");
            wedgeGO.transform.SetParent(transform, false);
            _angleWedge = wedgeGO.AddComponent<AngleWedgeIndicator>();
            _angleWedge.Init(_cannonPivot, transform.forward, 1.3f, new Color(1f, 0.85f, 0.2f)); // scaled up to match the bigger cannon

            // A thin flat disc read as "there is no target at all" in the
            // dungeon's torch-lit gloom (confirmed live - a 0.6-scale, 0.6-alpha
            // disc lying on dark stone floor is nearly invisible at standing eye
            // height). Real bullseye rings (painted via a procedural texture,
            // same idea as the reference archery target) plus emission make it
            // read clearly regardless of room lighting.
            var targetGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            targetGO.name = "Landing Target";
            targetGO.transform.SetParent(transform, false);
            targetGO.transform.localScale = new Vector3(1.1f, 0.03f, 1.1f);
            targetGO.transform.localPosition = new Vector3(0f, 0.02f, TargetDistance);
            SafeDestroy(targetGO.GetComponent<Collider>());
            _targetRingRenderer = targetGO.GetComponent<Renderer>();
            var targetMat = _targetRingRenderer.material;
            targetMat.mainTexture = BuildBullseyeTexture();
            targetMat.color = Color.white;
            targetMat.EnableKeyword("_EMISSION");
            targetMat.SetColor("_EmissionColor", TargetColor * 0.6f);
            _targetRing = targetGO.transform;
        }

        // Classic archery rings, center-out: yellow, red, blue, white, black -
        // matches the reference bullseye rather than a flat tinted disc.
        private static Texture2D BuildBullseyeTexture()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var rings = new[]
            {
                new Color(1f, 0.86f, 0.1f),   // center: yellow
                new Color(0.82f, 0.12f, 0.12f), // red
                new Color(0.12f, 0.4f, 0.75f),  // blue
                new Color(0.93f, 0.93f, 0.93f), // white
                new Color(0.08f, 0.08f, 0.08f), // outer: black
            };
            var center = new Vector2(size / 2f, size / 2f);
            var maxRadius = size / 2f;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxRadius;
                    var ring = Mathf.Clamp(Mathf.FloorToInt(d * rings.Length), 0, rings.Length - 1);
                    pixels[y * size + x] = rings[ring];
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // A small grip the player physically holds to aim (ported from
        // RayShooterController's leftGrab/rightGrab) - trackPosition/
        // trackRotation are OFF so grabbing it doesn't pull it off the gun
        // body, it just registers as held while CannonAimHandles reads the
        // real hand position each frame.
        private XRGrabInteractable BuildAimHandle(Transform parent, string name, Vector3 localPos)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = name;
            handle.transform.SetParent(parent, false);
            handle.transform.localPosition = localPos;
            handle.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            handle.transform.localScale = new Vector3(0.07f, 0.11f, 0.07f); // bumped up to match the now-bigger cannon
            handle.GetComponent<Renderer>().material.color = new Color(0.15f, 0.15f, 0.17f);

            var rb = handle.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var grab = handle.AddComponent<XRGrabInteractable>();
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;

            return grab;
        }

        // [ExecuteAlways] means the Build* methods above now also run in the
        // Editor (outside Play mode) so their content can be edited/saved in
        // the Scene view - plain Destroy() throws there ("Destroy may not be
        // called from edit mode!"), so every collider/stray-mesh cleanup in
        // those methods goes through this instead.
        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
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

        // ---- Rounds ----

        private void NextProblem()
        {
            _round++;

            if (_round > TotalRounds)
            {
                MinigameEnvironment.PlayRoundCompleteVfx(_cannonMuzzle.position);
                // No door/exit-room to walk through in the open clearing -
                // just end the session directly after the completion VFX
                // has a moment to play.
                Invoke(nameof(CompleteSession), 2f);
                return;
            }

            _mistakesThisTask = 0;
            _taskStartTime = Time.time;

            // Pick a function/angle pair from the table - tan(90) is
            // undefined, so that combination is rerolled into 0..60 instead.
            var funcChoice = Random.Range(0, 3);
            _correctAngleIndex = Random.Range(0, StandardAngles.Length);
            if (funcChoice == 2 && _correctAngleIndex == 4) _correctAngleIndex = Random.Range(0, 4);

            float[] table;
            switch (funcChoice)
            {
                case 0: table = SinTable; _correctFuncName = "sin"; _concept = "sine (opposite over hypotenuse)"; break;
                case 1: table = CosTable; _correctFuncName = "cos"; _concept = "cosine (adjacent over hypotenuse)"; break;
                default: table = TanTable; _correctFuncName = "tan"; _concept = "tangent (opposite over adjacent)"; break;
            }
            _correctValue = table[_correctAngleIndex];

            _taskDescription = $"{_correctFuncName}(θ) = {_correctValue:0.00}";
            _tabletText.text = $"Find θ:\n{_correctFuncName}(θ) = {_correctValue:0.00}";
            ConvaiGuide.Speak(_round == 1
                ? $"First one: what angle has {_correctFuncName} of {_correctValue:0.00}? Check the table, set the cannon, fire."
                : $"What angle has {_correctFuncName} of {_correctValue:0.00}?");
            _roundActive = true;
            GameManager.Instance?.RefreshContext();

            // The target used to sit at one fixed distance (11m) no matter
            // which angle the round actually asked for - but LaunchSpeed is
            // constant, so only ONE angle's real trajectory could ever reach
            // any single fixed distance. Every other round the ball was
            // physically incapable of reaching that far and simply fell out
            // of the sky well short of it ("the ball goes through the
            // ground instead of reaching target"). Now the target is placed
            // at the REAL ballistic landing point for this round's correct
            // angle at the fixed LaunchSpeed, so firing the actually-correct
            // angle always, physically, lands the ball on it.
            var correctAngle = StandardAngles[_correctAngleIndex];
            var range = ComputeLandingRange(correctAngle, _cannonPivot.position.y);
            _targetRing.localPosition = new Vector3(0f, 0.02f, _cannonPivot.localPosition.z + range);
            SetTargetGlow(TargetColor * 0.6f);
        }

        // Real projectile range, accounting for the cannon firing from a
        // height above the target's ground-level landing plane (the flat
        // R = v²sin(2θ)/g formula assumes launch and landing at the same
        // height, which would say a level 0-degree shot travels zero
        // distance - wrong for a raised muzzle, where it still carries
        // forward while falling). Derived from solving the vertical drop
        // for time-of-flight, then applying that to the constant horizontal
        // speed - the same two-halves-of-motion idea the readout already
        // teaches, just used to place the target instead of just describing it.
        private static float ComputeLandingRange(float angleDeg, float launchHeight)
        {
            var g = Mathf.Abs(Physics.gravity.y);
            var rad = angleDeg * Mathf.Deg2Rad;
            var vx = LaunchSpeed * Mathf.Cos(rad);
            var vy = LaunchSpeed * Mathf.Sin(rad);
            var t = (vy + Mathf.Sqrt(vy * vy + 2f * g * launchHeight)) / g;
            return vx * t;
        }

        // Tints the bullseye's glow, not its base color - setting Renderer.material.color
        // directly would multiply-tint the ring texture painted in BuildCannon()
        // (BuildBullseyeTexture), muddying the yellow/red/blue/white/black rings
        // instead of just adding a colored glow on top.
        private void SetTargetGlow(Color color)
        {
            if (_targetRingRenderer == null) return;
            _targetRingRenderer.material.SetColor("_EmissionColor", color);
        }

        private void Fire()
        {
            if (!_roundActive) return;

            var firedAngle = _aimHandles.CurrentAngleDegrees;
            var correct = Mathf.Abs(firedAngle - StandardAngles[_correctAngleIndex]) <= AngleToleranceDegrees;

            var projGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projGO.transform.position = _cannonMuzzle.position;
            projGO.transform.localScale = Vector3.one * 0.12f;
            projGO.GetComponent<Renderer>().material.color = Color.yellow;
            var rb = projGO.AddComponent<Rigidbody>();
            var rad = firedAngle * Mathf.Deg2Rad;
            var localVel = new Vector3(0f, Mathf.Sin(rad), Mathf.Cos(rad)) * LaunchSpeed;
            rb.linearVelocity = transform.TransformDirection(localVel);
            projGO.AddComponent<MathCannonProjectile>();
            Destroy(projGO, 5f);

            HandleAngleAnswer(correct, firedAngle);
        }

        private void HandleAngleAnswer(bool correct, float firedAngle)
        {
            if (!_roundActive) return;

            if (correct)
            {
                _roundActive = false;
                _score++;
                SetTargetGlow(CorrectColor);
                if (_audio != null && _correctClip != null) _audio.PlayOneShot(_correctClip);
                ConvaiGuide.Speak($"Right - {_correctFuncName}({firedAngle:0}°) is {_correctValue:0.00}.");
                HandleSuccess();
                Invoke(nameof(NextProblem), 1.4f);
            }
            else
            {
                _mistakesThisTask++;
                SetTargetGlow(WrongColor);
                if (_audio != null && _incorrectClip != null) _audio.PlayOneShot(_incorrectClip);
                if (Time.time - _lastFeedbackTime > 3f)
                {
                    _lastFeedbackTime = Time.time;
                    ConvaiGuide.Speak($"Not quite - {_correctFuncName}({firedAngle:0}°) isn't {_correctValue:0.00}. Check the table again.");
                }
                HandleFailure();
                Invoke(nameof(ResetTargetColor), 1f);
            }
        }

        private void ResetTargetColor()
        {
            SetTargetGlow(TargetColor * 0.6f);
        }

        private void CompleteSession()
        {
            GameManager.Instance?.EndMinigameSession();
            onComplete?.Invoke(_score, TotalRounds);
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
                correctAnswer = $"{StandardAngles[_correctAngleIndex]}°",
                mistakeCount = _mistakesThisTask,
                hintLevel = GameManager.Instance != null ? GameManager.Instance.Hints.CurrentLevel : 0,
                taskTimeSeconds = Time.time - _taskStartTime,
                sessionAccuracy = GameManager.Instance != null ? GameManager.Instance.Score.Accuracy : 1f
            };
        }
    }

    /// <summary>Marker so targets can tell a real fired shot apart from anything else they might touch.</summary>
    public class MathCannonProjectile : MonoBehaviour { }
}
