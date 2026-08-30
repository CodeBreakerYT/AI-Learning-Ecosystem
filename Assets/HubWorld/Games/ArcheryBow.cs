using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// The bow itself: grab it with the LEFT hand to hold/aim it. The arrow
    /// is a genuinely separate object (see ArcheryQuiver/QuiverArrow) that
    /// the player draws from a quiver on their back and nocks onto the
    /// string themselves - this component only knows how to accept a nocked
    /// arrow (TryNock) and read its draw distance/vertical offset once one
    /// is loaded. Pull distance gates whether the draw counts as "full"
    /// enough to fire; pulling the arrow UP or DOWN while drawn sets the
    /// launch ANGLE, read out live so the player can aim for a specific
    /// elevation the way MathCannon's two-handle rig reads out its angle.
    ///
    /// XRGrabInteractable already moves a held arrow to the interactor's
    /// live position every frame it's selected. LateUpdate reprojects that
    /// onto the (back, up) plane through nockAnchor - along -aimSource.forward
    /// for draw distance, along aimSource.up for the angle offset - so a hand
    /// pulling off-axis sideways still only ever draws/aims within that
    /// plane, matching the archer's intended draw motion.
    /// </summary>
    public class ArcheryBow : MonoBehaviour
    {
        public Transform nockAnchor;
        public Transform aimSource;
        public Transform stringTop;
        public Transform stringBottom;
        public float maxDrawDistance = 0.4f;
        // Below this fraction it's not a real draw at all, just a dropped
        // prop - above it, ANY pull fires. Draw distance no longer changes
        // launch SPEED (see launchSpeed below) - it's purely a "did you
        // actually mean to fire" gate now.
        public float minDrawFractionToFire = 0.15f;
        public float maxVerticalOffset = 0.25f; // how far up/down the draw hand can move to sweep the full angle range
        // Was 15-75 - a real hand's comfortable vertical range while also
        // holding a full horizontal draw rarely swings the full
        // maxVerticalOffset in each direction, so most real draws only ever
        // reached the middle of that 60-degree span ("constrained to 30
        // degrees no matter how much I change"). Widening the mapped RANGE
        // to the full 0-90 without changing maxVerticalOffset makes the same
        // physical hand motion map to more degrees (120 deg/m -> 180 deg/m),
        // so the same real-world movement that used to plateau mid-range now
        // actually reaches both ends.
        public float minLaunchAngleDeg = 0f;
        public float maxLaunchAngleDeg = 90f;
        public float nockRadius = 0.15f; // how close a held arrow must get to nockAnchor to snap onto the string
        // Draw strength used to ALSO control speed (Lerp between min/max),
        // making every shot a two-unknown problem - the right angle AND the
        // right pull, at the same time, with no way to isolate which one
        // was wrong when a shot missed. "unable to figure out angle... make
        // it easy to learn" - fixing speed removes one whole variable: with
        // U constant, range depends on angle alone (R = U^2 sin(2*theta)/g),
        // so a miss is unambiguously an angle problem, and the hint text
        // (ArcheryProjectileGame.NextChallenge) can solve that single
        // equation for an exact answer instead of a 2D range of options.
        public float launchSpeed = 18f;

        /// <summary>The real arrow GameObject that was nocked, direction (unit vector), origin, angle in degrees, launch speed in m/s.</summary>
        public event Action<GameObject, Vector3, Vector3, float, float> OnRelease;
        public event Action<float> OnDrawAngleChanged;
        public event Action<bool> OnDrawStateChanged; // true = an arrow is nocked and being drawn

        private XRGrabInteractable _bowGrab;
        private LineRenderer _string;
        private Transform _nockedArrow;
        private XRGrabInteractable _nockedGrab;
        private bool _isDrawn;
        private float _currentAngleDeg;

        public bool HasNockedArrow => _nockedArrow != null;
        public bool IsDrawn => _isDrawn;

        /// <summary>Live world-space shot direction at the current draw angle - same calc ReleaseNockedArrow fires with, exposed so the angle wedge gizmo can show exactly what will happen on release.</summary>
        public Vector3 CurrentAimDirection { get; private set; }
        public Vector3 FlatForward { get; private set; }
        /// <summary>0-1, how far back the string is currently pulled - for the live draw-time teaching readout.</summary>
        public float CurrentDrawFraction { get; private set; }
        /// <summary>What releasing RIGHT NOW would launch at, given the current draw - for the same live readout.</summary>
        public float CurrentDrawSpeed { get; private set; }

        private void Awake()
        {
            if (stringTop != null && stringBottom != null)
            {
                _string = gameObject.AddComponent<LineRenderer>();
                _string.positionCount = 3;
                _string.startWidth = _string.endWidth = 0.004f;
                _string.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _string.material.color = new Color(0.85f, 0.82f, 0.75f);
                _string.useWorldSpace = true;
                _string.numCapVertices = 2;
            }

            _bowGrab = GetComponent<XRGrabInteractable>();
            if (_bowGrab != null) RestrictToHand(_bowGrab, wantLeft: true);
        }

        // Ported from CannonAimHandles.IsFromHand - this project's rig has
        // real "LeftHand"/"RightHand" GameObjects in every scene. Toggling
        // enabled off/on cancels a just-started select from the wrong hand
        // cleanly (same trick used there).
        public static void RestrictToHand(XRGrabInteractable grab, bool wantLeft)
        {
            grab.selectEntered.AddListener(args =>
            {
                var interactorTransform = (args.interactorObject as Component)?.transform;
                if (interactorTransform == null) return;
                if (IsFromHand(interactorTransform, wantLeft)) return;
                grab.enabled = false;
                grab.enabled = true;
            });
        }

        public static bool IsFromHand(Transform interactor, bool wantLeft)
        {
            for (var t = interactor; t != null; t = t.parent)
            {
                if (t.name == "LeftHand") return wantLeft;
                if (t.name == "RightHand") return !wantLeft;
            }
            return false;
        }

        /// <summary>Called by QuiverArrow when a right-hand-held arrow gets close enough to the nock point. Returns false if a real draw is already in progress.</summary>
        public bool TryNock(Transform arrow, XRGrabInteractable arrowGrab)
        {
            if (_nockedArrow != null) return false;
            _nockedArrow = arrow;
            _nockedGrab = arrowGrab;
            _isDrawn = true;
            OnDrawStateChanged?.Invoke(true);
            return true;
        }

        private void LateUpdate()
        {
            // The bow doesn't rotate at all, by design - it stays fixed at
            // its spawned orientation (already correctly facing the target,
            // string running straight through its middle) the whole time.
            // An earlier attempt tracked the controller's yaw so the bow
            // would visually follow where the player aims, but that made the
            // string pull sideways instead of straight back ("south") -
            // every direction this component computes (back/up for the draw,
            // and the shot direction in ReleaseNockedArrow) is derived from
            // aimSource.forward/up, so ANY rotation drift here cascades into
            // the draw motion, the string visual, and the fired arrow's
            // actual flight direction all at once. Leaving rotation alone
            // entirely keeps all three correct and consistent - trackPosition
            // (set where this is grabbed) still lets the player carry/aim
            // the bow with their body normally, just without the mesh itself
            // twisting to match controller orientation.

            if (_isDrawn && _nockedArrow != null)
            {
                var back = -aimSource.forward;
                var up = aimSource.up;
                var offset = _nockedArrow.position - nockAnchor.position;

                var backAmount = Mathf.Clamp(Vector3.Dot(offset, back), 0f, maxDrawDistance);
                var upAmount = Mathf.Clamp(Vector3.Dot(offset, up), -maxVerticalOffset, maxVerticalOffset);

                // Keep the visual arrow within the intended draw plane -
                // off-axis (sideways) hand wobble doesn't drag it off the bow.
                _nockedArrow.position = nockAnchor.position + back * backAmount + up * upAmount;
                _nockedArrow.rotation = Quaternion.LookRotation(aimSource.forward, aimSource.up);

                var t = Mathf.InverseLerp(-maxVerticalOffset, maxVerticalOffset, upAmount);
                _currentAngleDeg = Mathf.Lerp(minLaunchAngleDeg, maxLaunchAngleDeg, t);
                OnDrawAngleChanged?.Invoke(_currentAngleDeg);

                FlatForward = ComputeFlatForward();
                CurrentAimDirection = DirectionForAngle(FlatForward, _currentAngleDeg);

                CurrentDrawFraction = backAmount / maxDrawDistance;
                CurrentDrawSpeed = launchSpeed;
            }

            // Always keep the string running top tip -> nock point -> bottom
            // tip, drawn or not - a bow with no visible string doesn't read
            // as a bow at all.
            if (_string != null)
            {
                var nockPoint = _nockedArrow != null ? _nockedArrow.position : nockAnchor.position;
                _string.SetPosition(0, stringTop.position);
                _string.SetPosition(1, nockPoint);
                _string.SetPosition(2, stringBottom.position);
            }
        }

        /// <summary>Called by QuiverArrow when the nocked arrow's own grab is released.</summary>
        public void ReleaseNockedArrow()
        {
            if (_nockedArrow == null) return;
            var arrow = _nockedArrow;

            _isDrawn = false;
            OnDrawStateChanged?.Invoke(false);

            // Re-reading arrow.position HERE (as this used to) instead of
            // trusting what LateUpdate just computed was the actual bug
            // behind "always 0 vertical/horizontal no matter how much I
            // pull" - XR Interaction Toolkit drives the held interactable's
            // own transform from its OWN Update/LateUpdate, whose order
            // relative to this component's LateUpdate is never guaranteed
            // (Unity doesn't fix execution order between unrelated scripts
            // by default). On the exact frame a release fires, XRI's own
            // pass can run again AFTER this component's LateUpdate already
            // read/constrained the position for that frame, snapping the
            // arrow back toward the raw (still-at-the-controller,
            // effectively near-zero-offset-from-nock) position before this
            // method ever gets a chance to look at it - reading back
            // ~nothing every time, regardless of the real draw. The cached
            // CurrentDrawFraction (set the moment LateUpdate itself computed
            // it) is exactly what the live draw-prediction readout already
            // showed the player, so using that instead guarantees the shot
            // matches what was actually displayed, independent of whatever
            // XRI does to the transform afterward.
            var drawFraction = CurrentDrawFraction;

            _nockedArrow = null;
            _nockedGrab = null;

            if (drawFraction < minDrawFractionToFire)
            {
                // Not drawn far enough for a real shot - let it drop as a
                // normal dropped prop instead of firing.
                var looseRb = arrow.GetComponent<Rigidbody>();
                if (looseRb != null) { looseRb.isKinematic = false; looseRb.useGravity = true; }
                return;
            }

            var direction = DirectionForAngle(ComputeFlatForward(), _currentAngleDeg);

            OnRelease?.Invoke(arrow.gameObject, direction, nockAnchor.position, _currentAngleDeg, launchSpeed);
        }

        // Yaw/roll come from however the bow is actually held (left hand
        // aims left/right); pitch is overridden by the calculated angle from
        // the draw hand's vertical offset, flattening aimSource's own
        // forward to the horizontal plane first so the two don't fight each
        // other. Shared by the live draw-time wedge readout and the actual
        // release direction so what the player sees while drawing is exactly
        // what they get.
        private Vector3 ComputeFlatForward()
        {
            var flatForward = new Vector3(aimSource.forward.x, 0f, aimSource.forward.z);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            return flatForward.normalized;
        }

        private static Vector3 DirectionForAngle(Vector3 flatForward, float angleDeg)
        {
            var right = Vector3.Cross(Vector3.up, flatForward);
            return Quaternion.AngleAxis(-angleDeg, right) * flatForward;
        }
    }
}
