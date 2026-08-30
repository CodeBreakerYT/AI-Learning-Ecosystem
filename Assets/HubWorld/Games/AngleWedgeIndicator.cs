using TMPro;
using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// A real "angle between two rays" gizmo - a horizontal reference ray, a
    /// live ray following whatever's actually being aimed, an arc joining
    /// them, and a degree label at the arc's midpoint. This is what "the
    /// angle with respect to bow/cannon and ground" is supposed to look like
    /// (per an annotated screenshot showing a "&lt;" opened between a
    /// reference line and the limb, labelled with the degree value) instead
    /// of a floating number with no visual angle to actually measure.
    /// Shared by ArcheryProjectileGame (elevation while drawing) and
    /// MathCannonGame (elevation while gripping both aim handles) since both
    /// are the same "pick an elevation angle off the ground" gizmo.
    /// </summary>
    public class AngleWedgeIndicator : MonoBehaviour
    {
        private Transform _vertex;
        private Vector3 _baseDirWorld;
        private float _rayLength;
        private LineRenderer _baseRay;
        private LineRenderer _liveRay;
        private LineRenderer _arc;
        private TextMeshPro _label;

        // Idempotent - safe to call again after a domain reload (Play mode
        // entry) when this component's own child GameObjects already exist
        // in the persisted scene but its plain (non-serialized) private
        // fields pointing at them were reset to null. The caller
        // (ArcheryProjectileGame/MathCannonGame's RediscoverReferences) calls
        // this again every time rather than assuming a found component is
        // already usable.
        public void Init(Transform vertex, Vector3 baseDirWorld, float rayLength, Color color)
        {
            _vertex = vertex;
            _baseDirWorld = baseDirWorld.normalized;
            _rayLength = rayLength;

            _baseRay = CreateLine("Angle Base Ray", color * 0.75f);
            _liveRay = CreateLine("Angle Live Ray", color);
            _arc = CreateLine("Angle Arc", color);
            _arc.startWidth = _arc.endWidth = 0.006f;

            var labelT = transform.Find("Angle Wedge Label");
            var labelGO = labelT != null ? labelT.gameObject : new GameObject("Angle Wedge Label");
            labelGO.transform.SetParent(transform, false);
            // World-space TextMeshPro renders at its literal font-size in
            // world units unless scaled down - every other readout in these
            // scenes uses a ~0.15-0.35 local scale for exactly this reason.
            // Missing that here made a "40" bigger than the whole bow.
            labelGO.transform.localScale = Vector3.one * 0.05f;
            _label = labelGO.GetComponent<TextMeshPro>();
            if (_label == null) _label = labelGO.AddComponent<TextMeshPro>();
            _label.fontSize = 6f;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = color;
            _label.text = "";

            SetVisible(false);
        }

        private LineRenderer CreateLine(string name, Color color)
        {
            var existing = transform.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null) lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = 0.012f;
            lr.numCapVertices = 4;
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lr.material.color = color;
            return lr;
        }

        public void SetVisible(bool visible)
        {
            if (_baseRay == null) return;
            _baseRay.enabled = visible;
            _liveRay.enabled = visible;
            _arc.enabled = visible;
            _label.gameObject.SetActive(visible);
        }

        /// <summary>Redraws the wedge for the current angle/direction - call every frame while aiming.</summary>
        public void UpdateAngle(float angleDeg, Vector3 liveDirWorld)
        {
            if (_vertex == null) return;
            SetVisible(true);

            var origin = _vertex.position;
            var liveDir = liveDirWorld.sqrMagnitude > 0.0001f ? liveDirWorld.normalized : _baseDirWorld;

            _baseRay.positionCount = 2;
            _baseRay.SetPosition(0, origin);
            _baseRay.SetPosition(1, origin + _baseDirWorld * _rayLength);

            _liveRay.positionCount = 2;
            _liveRay.SetPosition(0, origin);
            _liveRay.SetPosition(1, origin + liveDir * _rayLength);

            const int segments = 20;
            var arcRadius = _rayLength * 0.4f;
            _arc.positionCount = segments + 1;
            for (var i = 0; i <= segments; i++)
            {
                var t = (float)i / segments;
                var dir = Vector3.Slerp(_baseDirWorld, liveDir, t);
                _arc.SetPosition(i, origin + dir * arcRadius);
            }

            var midDir = Vector3.Slerp(_baseDirWorld, liveDir, 0.5f);
            _label.transform.position = origin + midDir * (arcRadius + 0.12f);
            var cam = Camera.main;
            if (cam != null)
            {
                var toCam = _label.transform.position - cam.transform.position;
                if (toCam.sqrMagnitude > 0.0001f) _label.transform.rotation = Quaternion.LookRotation(toCam);
            }
            _label.text = $"{angleDeg:0}°";
        }
    }
}
