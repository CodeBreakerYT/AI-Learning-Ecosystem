using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Real ballistic flight for one fired arrow (Rigidbody + gravity, no
    /// scripted curve) plus a "motion diagram" - a marker dropped at fixed
    /// time intervals along the actual path. Equal HORIZONTAL spacing
    /// between markers is what constant horizontal velocity looks like;
    /// shrinking-then-growing VERTICAL spacing is what a constant downward
    /// acceleration looks like. Showing both, from a real simulated flight
    /// rather than a drawn diagram, is the whole point of this component.
    /// </summary>
    public class ArcheryArrow : MonoBehaviour
    {
        private const float MarkerInterval = 0.1f;
        private static readonly Color MarkerColor = new Color(1f, 0.82f, 0.25f);

        public event Action<ArcheryArrow, Collision> OnHit;

        private Rigidbody _rb;
        private bool _flying;
        private bool _stuck;
        private float _markerTimer;
        private readonly List<GameObject> _markers = new List<GameObject>();
        private readonly List<Vector3> _tracePoints = new List<Vector3>();
        private LineRenderer _trace;

        public Vector3 InitialVelocity { get; private set; }
        public float HorizontalSpeed { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.mass = 0.05f;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;

            // A stuck (post-hit) arrow still carries its old QuiverArrow-era
            // XRGrabInteractable - confirmed live that grabbing and releasing
            // one left it frozen in mid-air forever ("stuck arrows"). That's
            // because QuiverArrow's own release handler only ever fires the
            // bow-release path once per arrow and never runs again, so
            // nothing ever flips isKinematic/useGravity back on for a picked-
            // up spent arrow. Listening here instead - on THIS component,
            // which every arrow (nocked or already-landed) always has -
            // guarantees a grabbed-then-dropped arrow always resumes real
            // physics and actually falls, whether it was ever fired or not.
            var grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.selectEntered.AddListener(_ =>
                {
                    _flying = false;
                    _rb.isKinematic = false;
                    _rb.useGravity = false; // weightless while held, same feel as any other grabbed prop
                });
                grab.selectExited.AddListener(_ =>
                {
                    _rb.useGravity = true; // let go - falls like a normal spent arrow, doesn't hang in place
                });
            }
        }

        public void Launch(Vector3 velocity)
        {
            InitialVelocity = velocity;
            HorizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;

            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.linearVelocity = velocity;
            transform.rotation = Quaternion.LookRotation(velocity);
            _flying = true;
            _markerTimer = MarkerInterval; // drop one immediately at launch

            // The traced path itself - a growing line through every marker
            // point, drawn live as the arrow flies, not just the isolated
            // dots. This is what actually reads as "the parabola" at a
            // glance, and is what a real motion-tracking rig would show.
            _tracePoints.Clear();
            _tracePoints.Add(transform.position);
            var traceGO = new GameObject("Trajectory Trace");
            _trace = traceGO.AddComponent<LineRenderer>();
            _trace.useWorldSpace = true;
            _trace.startWidth = _trace.endWidth = 0.012f;
            _trace.numCapVertices = 4;
            _trace.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _trace.material.color = MarkerColor;
            _trace.positionCount = 1;
            _trace.SetPosition(0, transform.position);
        }

        public Vector3 CurrentVelocity => _rb.linearVelocity;
        public bool IsFlying => _flying;

        private void Update()
        {
            if (!_flying || _stuck) return;

            if (_rb.linearVelocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity);

            if (_trace != null)
            {
                _tracePoints.Add(transform.position);
                _trace.positionCount = _tracePoints.Count;
                _trace.SetPosition(_tracePoints.Count - 1, transform.position);
            }

            _markerTimer += Time.deltaTime;
            if (_markerTimer >= MarkerInterval)
            {
                _markerTimer = 0f;
                DropMarker();
            }
        }

        private void DropMarker()
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = "Motion Marker";
            Destroy(m.GetComponent<Collider>());
            m.transform.SetParent(null, true);
            m.transform.position = transform.position;
            m.transform.localScale = Vector3.one * 0.035f;
            var mat = m.GetComponent<Renderer>().material;
            mat.color = MarkerColor;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", MarkerColor * 0.6f);
            _markers.Add(m);
        }

        /// <summary>Clears the previous shot's motion-diagram dots and traced path - called right before the next shot fires.</summary>
        public void ClearMarkers()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m);
            _markers.Clear();

            if (_trace != null) Destroy(_trace.gameObject);
            _trace = null;
            _tracePoints.Clear();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_stuck || !_flying) return;
            _stuck = true;
            _flying = false;
            _rb.isKinematic = true;
            _rb.useGravity = false;
            OnHit?.Invoke(this, collision);
        }
    }
}
