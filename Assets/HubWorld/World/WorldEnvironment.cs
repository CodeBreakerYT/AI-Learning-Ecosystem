using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Procedurally lays out World.unity's forest: a ground plane, a walkable
    /// path connecting the spawn point through three quest clearings, and
    /// scattered real environment props (trees/stones/mushrooms from the
    /// Forest Pack, crystals/runestones/bushes from 3D Low Poly Magical
    /// Forest) filling everything else - placed at random points with a
    /// minimum-spacing check against each other and an exclusion check
    /// against the path/clearings, so nothing overlaps or blocks walking.
    /// Prefab fields are wired to the real imported assets via GUID
    /// references (same technique `HubWorldConfig` used for exhibit props),
    /// not `Resources.Load`. This intentionally does not reuse
    /// `StartSceneEnvironment.cs` - that's a sci-fi dome backdrop, this is a
    /// real walkable magic forest, different visual language entirely.
    /// </summary>
    public class WorldEnvironment : MonoBehaviour
    {
        [Header("Forest Pack (mundane) props")]
        public GameObject[] forestProps;

        [Header("3D Low Poly Magical Forest (fantastical) props")]
        public GameObject[] magicalProps;

        [Header("Quest clearing centers (world-space, path runs spawn -> [0] -> [1] -> [2])")]
        public Vector3[] clearingCenters =
        {
            new Vector3(-9f, 0f, 11f),
            new Vector3(8f, 0f, 20f),
            new Vector3(-4f, 0f, 30f)
        };

        [Header("Single-clearing mode (e.g. MathCannon) - one open clearing at the origin, no path/quest clearings")]
        public bool singleClearing;
        // Overridable per-instance (World's 3 quest clearings are fine at the
        // default; MathCannon's cannon+target+tablet footprint runs further
        // out from origin than that, so it asks for a bigger clearing instead
        // of forcing its content into a tighter radius).
        public float clearingRadius = 4.5f;

        private const float GroundRadius = 40f;
        private const float PathHalfWidth = 2.2f;
        private const int PropCount = 220;
        private const float MinSpacing = 1.6f;

        // Small, hand-scale props get made grabbable (Rigidbody + XRGrabInteractable) so
        // the player's hands can pick things up in the forest - trees/bushes/logs/big
        // rocks stay static scenery, since a person can't realistically lift a tree.
        private static readonly HashSet<string> GrabbablePropNames = new HashSet<string>
        {
            "Stone001", "Stone002", "Stone003", "Stone004",
            "Mushroom001", "Mushroom002",
            "Plant001", "Plant004", "Plant007",
            "mushroom_green_small", "mushroom_orange_small",
            "plant_ferny", "plant_flower_lily",
            "crystal_blue", "crystal_glowing"
        };

        private readonly List<Vector3> _placed = new List<Vector3>();

        private void Start()
        {
            SetupLightingAndFog();
            BuildGround();
            ScatterProps();
        }

        private void SetupLightingAndFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color32(58, 74, 56, 255);
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color32(70, 84, 64, 255);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color32(46, 60, 48, 255);
            }

            var dirLightGO = GameObject.Find("Directional Light");
            if (dirLightGO != null)
            {
                var light = dirLightGO.GetComponent<Light>();
                if (light != null)
                {
                    light.color = new Color32(226, 236, 200, 255);
                    light.intensity = 1.1f;
                }
            }
        }

        private void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Forest Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = Vector3.one * (GroundRadius / 5f);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color32(52, 74, 46, 255);
            mat.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().material = mat;
        }

        private void ScatterProps()
        {
            var allProps = new List<GameObject>();
            if (forestProps != null) allProps.AddRange(forestProps);
            if (magicalProps != null) allProps.AddRange(magicalProps);
            allProps.RemoveAll(p => p == null);
            if (allProps.Count == 0) return;

            var placedCount = 0;
            var attempts = 0;
            while (placedCount < PropCount && attempts < PropCount * 15)
            {
                attempts++;
                var point = new Vector3(
                    Random.Range(-GroundRadius + 3f, GroundRadius - 3f),
                    0f,
                    Random.Range(-GroundRadius + 3f, GroundRadius - 3f));

                if (IsInExclusionZone(point) || TooClose(point)) continue;

                var prefab = allProps[Random.Range(0, allProps.Count)];
                var instance = Instantiate(prefab, point, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
                instance.transform.localScale *= Random.Range(0.85f, 1.25f);

                if (GrabbablePropNames.Contains(prefab.name))
                    MakeGrabbable(instance);

                _placed.Add(point);
                placedCount++;
            }
        }

        private static void MakeGrabbable(GameObject instance)
        {
            var meshColliders = instance.GetComponentsInChildren<MeshCollider>();
            foreach (var meshCollider in meshColliders)
                meshCollider.convex = true;

            // A few prop prefabs (e.g. Plant004/Plant007) ship with no collider at all -
            // without one, XRGrabInteractable has nothing for a hand interactor to hover
            // or select, so grabbing silently does nothing. Fall back to a box sized from
            // the combined renderer bounds so every grabbable prop is actually reachable.
            if (meshColliders.Length == 0 && instance.GetComponentInChildren<Collider>() == null)
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    var box = instance.AddComponent<BoxCollider>();
                    box.center = instance.transform.InverseTransformPoint(bounds.center);
                    box.size = Vector3.Scale(bounds.size, new Vector3(
                        1f / instance.transform.lossyScale.x,
                        1f / instance.transform.lossyScale.y,
                        1f / instance.transform.lossyScale.z));
                }
            }

            var rb = instance.GetComponent<Rigidbody>();
            if (rb == null) rb = instance.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.linearDamping = 0.5f;
            rb.angularDamping = 0.5f;

            var grab = instance.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = instance.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = true;
        }

        private bool IsInExclusionZone(Vector3 point)
        {
            if (Vector3.Distance(point, Vector3.zero) < clearingRadius) return true;
            if (singleClearing) return false;

            foreach (var center in clearingCenters)
                if (Vector3.Distance(point, center) < clearingRadius) return true;

            var previous = Vector3.zero;
            foreach (var center in clearingCenters)
            {
                if (DistanceToSegment(point, previous, center) < PathHalfWidth) return true;
                previous = center;
            }

            return false;
        }

        private bool TooClose(Vector3 point)
        {
            foreach (var placed in _placed)
                if (Vector3.Distance(placed, point) < MinSpacing) return true;
            return false;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            var closest = a + t * ab;
            return Vector3.Distance(point, closest);
        }
    }
}
