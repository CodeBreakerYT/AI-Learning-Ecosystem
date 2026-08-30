using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Newton's Laws' own guide - Phobo, the floating robot instructor
    /// ported from ref/VR-Mathipia (Assets/VRMathipia/Main/assets/objects/
    /// phobo/Phobo.fbx), replacing the generic classroom teacher for this
    /// scene. Leads the player station to station in strict pedagogical
    /// order (1st law, then 2nd, then 3rd) - not proximity order: walks a
    /// real NavMeshAgent from wherever he's standing to the WAYPOINT for the
    /// next panel, waits there while that station's own dialogue plays
    /// (driven by NewtonSimulate.OnStageChanged), then once the FINAL line
    /// for that law is dismissed, walks back to the player, says "Follow
    /// me", and walks to the next panel's waypoint.
    ///
    /// [ExecuteAlways] so Phobo and his three waypoint markers ("Waypoint 1"
    /// /"2"/"3", one per station) are real, visible, hand-draggable
    /// GameObjects in the Scene view - built once from an initial NavMesh-
    /// based guess (see TryFindBesidePanelPoint), then left alone on every
    /// later domain reload so hand-placed edits stick. Move a waypoint in
    /// the Scene view and that's exactly where Phobo walks to at runtime -
    /// no need to touch code to retune his positioning.
    ///
    /// This ship's three station decks are genuinely disconnected NavMesh
    /// islands (confirmed live via NavMesh.CalculatePath - PathPartial
    /// between every pair, even after trying NavMeshLinks to bridge them) -
    /// they read as separate levels meant to be reached by elevator/lift in
    /// the original design, not a walkable corridor. A real NavMeshAgent
    /// walk only works WITHIN one island (the player's spawn and station 1
    /// happen to share one). Crossing decks instead uses a floor-safe arc
    /// flight between NavMesh-sampled points, with the agent re-warped onto
    /// the new island's mesh on arrival.
    /// </summary>
    [ExecuteAlways]
    public class PhoboNewtonsGuide : MonoBehaviour
    {
        public GameObject phoboModel; // Assets/VRMathipia/Main/assets/objects/phobo/Phobo.fbx
        public Sprite dialogueSprite; // Assets/VRMathipia/Main/assets/objects/phobo/dialogueBox/dialogueBox.png

        private const int StationCount = 3;

        // Candidate spots to try beside a panel, nearest-connected-one wins -
        // only used to place each Waypoint marker's INITIAL position the
        // first time this is built. Once built, the waypoints are real
        // scene objects - dragging one in the Scene view overrides this
        // guess permanently (Rebuild would regenerate it, hand-edits
        // otherwise persist across domain reloads same as any other
        // GameObject).
        private static readonly Vector3[] BesidePanelOffsets =
        {
            Vector3.right * 2f, Vector3.left * 2f, Vector3.forward * 2f, Vector3.back * 2f,
            Vector3.right * 1f, Vector3.left * 1f, Vector3.forward * 1f, Vector3.back * 1f
        };

        // Phobo.fbx's mesh doesn't face its own transform's forward axis -
        // VR-Mathipia's own IntroScenario.cs already knew this and applies
        // this exact correction (rotationOffsetEuler: (0, 90, 0), confirmed
        // by reading its source scene) every time it points the model at
        // something. Every rotation this script sets for Phobo needs the
        // same correction, or transform.forward math checks out perfectly
        // while he visually faces 90 degrees off from wherever he's
        // "supposed" to be looking.
        private static readonly Quaternion ModelForwardOffset = Quaternion.Euler(0f, 90f, 0f);

        private GameObject _phoboInstance;
        private NpcCaption _caption;
        private NavMeshAgent _agent;
        private Transform _playerTransform;
        private Transform[] _waypoints; // [law1, law2, law3] approach points - real child GameObjects, hand-editable

        private NewtonSimulate[] _stations; // [law1, law2, law3] - fixed teaching order, nulls skipped
        private int _currentIndex = -1;
        private bool _awaitingContinue;
        private bool _awaitingFinalLine;
        private bool _lastBState;
        private bool _runtimeStarted;

        private void Awake()
        {
            if (transform.Find("Phobo Guide") == null)
                BuildEditTime();
            else
                RediscoverReferences();
        }

        // Deliberately separate from Awake(): [ExecuteAlways] means Awake()
        // itself can fire during the edit-to-play transition before
        // Application.isPlaying has actually settled to true for that
        // callback, which silently skipped the runtime kickoff entirely
        // (confirmed live - _runtimeStarted stayed false the whole session,
        // Phobo never even greeted the player). Start() is guaranteed to run
        // after that transition fully completes, so the isPlaying check
        // here is reliable where the same check in Awake() wasn't.
        private void Start()
        {
            if (!Application.isPlaying || _runtimeStarted) return;
            _runtimeStarted = true;
            StartRuntime();
        }

        public void Rebuild()
        {
            ClearBuilt();
            BuildEditTime();
        }

        private void ClearBuilt()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
                SafeDestroy(transform.GetChild(i).gameObject);
        }

        private static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // Builds Phobo himself plus one Waypoint marker per station, all as
        // real children of this GameObject - visible and selectable in the
        // Scene view/Hierarchy without needing Play mode.
        private void BuildEditTime()
        {
            if (phoboModel == null) return;

            _phoboInstance = Instantiate(phoboModel, transform);
            _phoboInstance.name = "Phobo Guide";
            _phoboInstance.transform.localScale = Vector3.one * 0.5f;

            SetupPhoboComponents();

            var stations = FindStationsInOrder();
            _waypoints = new Transform[StationCount];
            for (var i = 0; i < StationCount; i++)
            {
                var wp = new GameObject($"Waypoint {i + 1}");
                wp.transform.SetParent(transform, false);

                var station = stations[i];
                var initialPos = station != null && TryFindBesidePanelPoint(station.transform, out var found)
                    ? found
                    : (station != null ? station.transform.position : transform.position);
                wp.transform.position = initialPos;
                _waypoints[i] = wp.transform;
            }

            // Sit at the first waypoint by default so he's immediately
            // visible somewhere meaningful in the Scene view rather than at
            // this controller's own (possibly arbitrary) position.
            if (_waypoints[0] != null) _phoboInstance.transform.position = _waypoints[0].position;
        }

        private void RediscoverReferences()
        {
            var phoboTransform = transform.Find("Phobo Guide");
            _phoboInstance = phoboTransform != null ? phoboTransform.gameObject : null;
            if (_phoboInstance != null)
            {
                _agent = _phoboInstance.GetComponent<NavMeshAgent>();
                _caption = _phoboInstance.GetComponent<NpcCaption>();
            }

            _waypoints = new Transform[StationCount];
            for (var i = 0; i < StationCount; i++)
                _waypoints[i] = transform.Find($"Waypoint {i + 1}");
        }

        private static NewtonSimulate[] FindStationsInOrder()
        {
            NewtonSimulate law1 = null, law2 = null, law3 = null;
            foreach (var sim in FindObjectsByType<NewtonSimulate>(FindObjectsSortMode.None))
            {
                if (sim.firstLaw) law1 = sim;
                else if (sim.secondLaw) law2 = sim;
                else if (sim.thirdLaw) law3 = sim;
            }
            return new[] { law1, law2, law3 };
        }

        // ---- Runtime-only from here down ----

        private void StartRuntime()
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            _playerTransform = playerGO != null ? playerGO.transform : null;
            PositionAtRuntimeStart(playerGO);

            _stations = FindStationsInOrder();
            foreach (var sim in _stations)
                if (sim != null) sim.OnStageChanged += OnStageChanged;

            StartCoroutine(GuideSequence());
        }

        // Edit-time placement is just "somewhere visible" (the first
        // waypoint) - actual gameplay always starts beside whoever's
        // actually playing, regardless of where he was left sitting in the
        // editor.
        private void PositionAtRuntimeStart(GameObject playerGO)
        {
            if (_phoboInstance == null || playerGO == null) return;

            var spawnPos = playerGO.transform.position + playerGO.transform.right * 1.3f +
                            playerGO.transform.forward * 1.2f + Vector3.up * 0.4f;
            var lookDir = playerGO.transform.position - spawnPos;
            lookDir.y = 0f;
            var spawnRot = lookDir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDir, Vector3.up) * ModelForwardOffset
                : _phoboInstance.transform.rotation;

            _phoboInstance.transform.SetPositionAndRotation(spawnPos, spawnRot);
            if (_agent != null && NavMesh.SamplePosition(spawnPos, out var spawnHit, 3f, NavMesh.AllAreas))
                _agent.Warp(spawnHit.position);
        }

        private IEnumerator GuideSequence()
        {
            yield return new WaitForSeconds(0.6f);
            if (_phoboInstance == null) yield break;

            yield return Say("Hey! I'm Phobo - your guide through Newton's three laws of motion.");
            yield return Say("Follow me - the first station is this way.");

            yield return AdvanceToStation(0);
        }

        private IEnumerator AdvanceToStation(int index)
        {
            _currentIndex = index;
            if (_stations == null || index >= _stations.Length || _stations[index] == null) yield break;

            var panel = _stations[index].transform;
            var destination = (_waypoints != null && index < _waypoints.Length && _waypoints[index] != null)
                ? _waypoints[index].position
                : panel.position;
            yield return GoTo(destination, panel.position);
        }

        // ---- Custom teaching dialogue, in proper pedagogical order ----

        private void OnStageChanged(bool firstLaw, bool secondLaw, bool thirdLaw, int stage)
        {
            string line = null;

            if (firstLaw)
            {
                line = stage switch
                {
                    0 => "I'm Phobo - your guide through Newton's three laws of motion. This station is the FIRST law: inertia. Press the button when you're ready.",
                    1 => "Watch this object sitting still. Newton's First Law says an object at rest stays at rest - it has no reason to start moving on its own. That resistance to changing motion is called inertia.",
                    2 => "Now they're gliding. Notice they don't slow down by themselves - a moving object keeps moving at the same speed and direction forever, unless something else pushes or pulls on it.",
                    3 => "There's the force - a collision. That's the 'unless acted upon' part of the law. Only an outside push changed their motion; nothing about the objects themselves did.",
                    4 => "Your turn - go push that boulder yourself. Feel how it resists starting to move? That resistance is inertia, and it's bigger for objects with more mass.",
                    _ => null
                };
            }
            else if (secondLaw)
            {
                line = stage switch
                {
                    0 => "Second station - the SECOND law of motion. This one has a formula: F equals m times a. Press the button to begin.",
                    1 => "Force equals mass times acceleration. The more force you apply, the faster something speeds up - and the heavier it is, the less it accelerates for the same push.",
                    2 => "That's why a heavier object needs more force to reach the same speed as a lighter one in the same time - mass resists acceleration.",
                    3 => "Try it yourself - put the car and the truck on the seesaw. The heavier one needs more force to lift, exactly what F=ma predicts.",
                    _ => null
                };
            }
            else if (thirdLaw)
            {
                line = stage switch
                {
                    0 => "Final station - the THIRD law. Every action has an equal and opposite reaction. Press the button when you're ready.",
                    1 => "For every force one object applies to another, the second object pushes back just as hard, in the opposite direction. Forces always come in pairs.",
                    2 => "Action force equals reaction force - always the same size, always opposite directions, always happening at the same instant.",
                    3 => "Watch both objects push off each other now. Neither one pushes harder than the other - that's the third law in action, literally.",
                    _ => null
                };
            }

            if (line == null || _caption == null) return;

            // Player-paced instead of a fixed timer (same "press B to
            // continue" convention this project's own VR-Mathipia dialogue
            // system already uses) - a real lesson shouldn't race a
            // reading-speed guess.
            _caption.Show(line + "\n\n<size=70%>(Press B to continue)</size>", 0f);
            _awaitingContinue = true;

            // The last line configured for this law - dismissing THIS one is
            // what triggers "walk back to the player and lead them to the
            // next station" rather than just staying parked here forever.
            _awaitingFinalLine = (firstLaw && stage == 4) || (secondLaw && stage == 3) || (thirdLaw && stage == 3);
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            if (_awaitingContinue && IsBPressed())
            {
                _caption?.HideNow();
                _awaitingContinue = false;
                if (_awaitingFinalLine)
                {
                    _awaitingFinalLine = false;
                    StartCoroutine(ReturnThenAdvance());
                }
            }
        }

        private IEnumerator ReturnThenAdvance()
        {
            if (_playerTransform != null)
                yield return GoTo(_playerTransform.position, _playerTransform.position);

            var nextIndex = _currentIndex + 1;
            if (_stations == null || nextIndex >= _stations.Length || _stations[nextIndex] == null)
            {
                yield return Say("That's all three of Newton's laws - nice work!");
                yield break;
            }

            yield return Say("Follow me - the next station is this way.");
            yield return AdvanceToStation(nextIndex);
        }

        private IEnumerator Say(string line)
        {
            if (_caption == null) yield break;
            _caption.Show(line + "\n\n<size=70%>(Press B to continue)</size>", 0f);
            _awaitingContinue = true;
            while (_awaitingContinue) yield return null;
        }

        // Same secondaryButton-on-either-hand edge-detect this project's own
        // VR-Mathipia dialogue scripts (DialogueTyper.cs/GuideDialogueTrigger.cs)
        // already use for "press B to continue".
        private bool IsBPressed()
        {
            var pressed = false;
            var left = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (left.isValid && left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool lb) && lb) pressed = true;
            if (right.isValid && right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool rb) && rb) pressed = true;

            var isDown = pressed && !_lastBState;
            _lastBState = pressed;
            return isDown;
        }

        // Tries a handful of offsets around the panel and keeps the first
        // one that's both on the NavMesh AND actually path-connected to the
        // panel itself (not a disconnected ledge nearby) - only used to seed
        // a Waypoint marker's starting position when first built.
        private static bool TryFindBesidePanelPoint(Transform panel, out Vector3 point)
        {
            if (!NavMesh.SamplePosition(panel.position, out var panelHit, 3f, NavMesh.AllAreas))
            {
                point = panel.position;
                return false;
            }

            foreach (var offset in BesidePanelOffsets)
            {
                if (!NavMesh.SamplePosition(panelHit.position + offset, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(panelHit.position, hit.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    point = hit.position;
                    return true;
                }
            }

            point = panelHit.position;
            return true;
        }

        // Walks there for real via NavMeshAgent when destination is on the
        // SAME connected NavMesh region Phobo is currently standing on -
        // proper floor-following, no clipping. When it's a genuinely
        // disconnected region (a different deck), flies a floor-safe arc
        // instead and re-warps the agent onto the new island on arrival.
        private IEnumerator GoTo(Vector3 destination, Vector3 faceTarget)
        {
            if (_agent == null || _phoboInstance == null) yield break;

            var path = new NavMeshPath();
            var connected = NavMesh.CalculatePath(_phoboInstance.transform.position, destination, NavMesh.AllAreas, path)
                             && path.status == NavMeshPathStatus.PathComplete;

            if (connected)
            {
                _agent.isStopped = false;
                _agent.SetDestination(destination);
                while (_agent.pathPending || _agent.remainingDistance > 0.3f)
                {
                    // Agent.updateRotation is off (see SetupPhoboComponents) -
                    // driving this by hand from actual velocity, same
                    // technique TeacherFollowPlayer/TeacherWander already use
                    // for their own NavMeshAgent-driven NPCs.
                    if (_agent.velocity.sqrMagnitude > 0.01f)
                    {
                        var moveRot = Quaternion.LookRotation(_agent.velocity.normalized, Vector3.up) * ModelForwardOffset;
                        _phoboInstance.transform.rotation = Quaternion.Slerp(_phoboInstance.transform.rotation, moveRot, 3f * Time.deltaTime);
                    }
                    yield return null;
                }
            }
            else
            {
                // The agent has nowhere valid to path while off-mesh mid-
                // flight (the destination deck is a different island) -
                // disabling it stops it fighting the manual position sets
                // below.
                _agent.enabled = false;
                yield return ArcFlyTo(destination);
                _agent.enabled = true;
                if (NavMesh.SamplePosition(destination, out var hit, 2f, NavMesh.AllAreas)) _agent.Warp(hit.position);
            }

            var lookDir = faceTarget - _phoboInstance.transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.0001f)
                _phoboInstance.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up) * ModelForwardOffset;
        }

        // Rises above both ends' floor height before crossing so a big
        // deck-to-deck vertical gap never clips him through an intermediate
        // floor.
        private IEnumerator ArcFlyTo(Vector3 destination)
        {
            var start = _phoboInstance.transform.position;
            var startRot = _phoboInstance.transform.rotation;
            var lookDir = destination - start;
            var targetRot = lookDir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(lookDir, Vector3.up) * ModelForwardOffset : startRot;

            var peakHeight = Mathf.Max(start.y, destination.y) + 2f;
            var mid = Vector3.Lerp(start, destination, 0.5f);
            mid.y = peakHeight;

            const float travelSeconds = 3.5f;
            var t = 0f;
            while (t < travelSeconds)
            {
                t += Time.deltaTime;
                var f = t / travelSeconds;
                _phoboInstance.transform.position = f < 0.5f
                    ? Vector3.Lerp(start, mid, f * 2f)
                    : Vector3.Lerp(mid, destination, (f - 0.5f) * 2f);
                _phoboInstance.transform.rotation = Quaternion.Slerp(startRot, targetRot, f);
                yield return null;
            }

            _phoboInstance.transform.position = destination;
            _phoboInstance.transform.rotation = targetRot;
        }

        // One-time component setup for the Phobo instance - called only
        // from BuildEditTime, never on rediscovery, so re-adding these on
        // every domain reload doesn't reset user-tweaked Inspector values.
        private void SetupPhoboComponents()
        {
            // A real NavMeshAgent (per an explicit ask) instead of a bare
            // manual lerp for every move - sized to Phobo's own small scale
            // rather than Unity's human-sized default (0.5 radius / 2m
            // height), which would barely fit these ship corridors at all.
            // updateRotation is off - GoTo drives rotation by hand from
            // actual velocity. baseOffset lifts the rendered body above the
            // navmesh surface: his pivot sits well below his geometric
            // center, so 0 left the bottom half of his body buried in the
            // floor; 0.8 clears him with a visible ~0.3m gap underneath - a
            // real levitating hover, not a ground-walker.
            _agent = _phoboInstance.AddComponent<NavMeshAgent>();
            _agent.radius = 0.25f;
            _agent.height = 0.9f;
            _agent.speed = 1f;
            _agent.acceleration = 2.5f;
            _agent.angularSpeed = 240f;
            _agent.autoBraking = true;
            _agent.updateRotation = false;
            _agent.baseOffset = 0.8f;

            // A real ConvaiNPC (and its voice) is a whole separate character
            // setup Phobo's plain FBX doesn't have (no lipsync rig/character
            // ID) - NpcCaption is generic, so attaching it directly here
            // makes the floating subtitle appear above PHOBO specifically.
            _caption = _phoboInstance.AddComponent<NpcCaption>();
            if (dialogueSprite != null) _caption.SetPanelSprite(dialogueSprite);

            // NpcCaption's default size is tuned for a short subtitle line,
            // not the full sentence-length teaching dialogue Phobo actually
            // delivers here.
            _caption.SetSize(new Vector2(1300, 500), 0.0028f, 44);
            _caption.SetHeightOffset(1.5f);

            // Faces the same direction Phobo does and turns with him,
            // matching VR-Mathipia's own dialogueCanvas (a plain child of
            // the robot transform, no per-frame billboarding at all).
            // NpcCaption's own default correction (180 degrees) was tuned
            // for a transform whose forward IS its visual front - Phobo's
            // isn't (see ModelForwardOffset above), so the box needs that
            // same 90-degree correction subtracted out of the 180.
            _caption.SetBillboard(false, Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(ModelForwardOffset));
        }
    }
}
