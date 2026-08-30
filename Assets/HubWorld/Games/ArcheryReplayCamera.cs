using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A golf-broadcast-style "ball cam" - deliberately NOT the player's own
    /// VR view (moving that without matching real head motion is a fast
    /// track to motion sickness). This is a separate, small camera rendering
    /// to its own screen near the range: it tracks the fired arrow in real
    /// time from a side-on broadcast angle (so the actual arc is visible,
    /// not hidden behind a chase-from-directly-behind view), then holds on
    /// the landing point for a few seconds - "preview of where the arrow
    /// landed... like golf games."
    /// </summary>
    public class ArcheryReplayCamera : MonoBehaviour
    {
        private const float HoldSeconds = 3f;
        private const float FollowSmoothing = 6f;
        private const float HoldSmoothing = 4f;

        public Camera cam;

        private Transform _target;
        private bool _following;
        private bool _holding;
        private float _holdTimer;
        private Vector3 _holdPoint;

        public void Follow(Transform target)
        {
            _target = target;
            _following = true;
            _holding = false;
        }

        public void HoldOnLanding(Vector3 point)
        {
            _following = false;
            _target = null;
            _holding = true;
            _holdTimer = HoldSeconds;
            _holdPoint = point;
        }

        public void Idle() => _following = _holding = false;

        private void LateUpdate()
        {
            if (cam == null) return;

            if (_following && _target != null)
            {
                var rb = _target.GetComponent<Rigidbody>();
                var vel = rb != null ? rb.linearVelocity : _target.forward;
                var flatVel = new Vector3(vel.x, 0f, vel.z);
                var travelDir = flatVel.sqrMagnitude > 0.01f ? flatVel.normalized : _target.forward;
                var side = Vector3.Cross(Vector3.up, travelDir);

                // Off to the side and slightly behind, like a real broadcast
                // camera watching the arc pass rather than chasing from
                // directly behind it (which would hide the vertical motion -
                // the whole point of the lesson).
                var desiredPos = _target.position + side * 3.5f + Vector3.up * 1.6f - travelDir * 1.2f;
                transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * FollowSmoothing);
                var lookDir = _target.position - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * FollowSmoothing);
            }
            else if (_holding)
            {
                var desiredPos = _holdPoint + new Vector3(0.3f, 1.3f, -2.2f);
                transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * HoldSmoothing);
                var lookDir = _holdPoint - transform.position;
                if (lookDir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * HoldSmoothing);

                _holdTimer -= Time.deltaTime;
                if (_holdTimer <= 0f) Idle();
            }
        }
    }
}
