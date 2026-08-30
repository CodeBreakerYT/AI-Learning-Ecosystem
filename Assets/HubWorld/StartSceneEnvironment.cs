using UnityEngine;

namespace AILearningEcosystem.Hub
{
    /// <summary>
    /// Builds the same 3D "waiting room" backdrop EcoLearn's own home page shows
    /// behind its login card (see EcoLearn/frontend/scripts/core/xrManager.js,
    /// setupEnvironment()): dark navy fog, a glowing grid floor, a soft glow pool,
    /// a ring of 8 glowing accent-colored pylons, and drifting dust motes. The
    /// real login form is the HTML overlay (Assets/WebGLTemplates/EcoLearn/), so
    /// this only ever matters in an actual WebGL build - but it also means Editor
    /// Play Mode is no longer a flat void, which is what this fixes.
    /// </summary>
    public class StartSceneEnvironment : MonoBehaviour
    {
        private static readonly Color32 SkyTop = new Color32(10, 14, 24, 255);
        private static readonly Color32 SkyHorizon = new Color32(28, 39, 64, 255);
        private static readonly Color[] AccentColors =
        {
            new Color32(91, 140, 255, 255),
            new Color32(34, 211, 238, 255),
            new Color32(167, 139, 250, 255),
            new Color32(52, 211, 153, 255),
            new Color32(244, 114, 182, 255),
            new Color32(251, 191, 36, 255)
        };

        private ParticleSystem _dust;

        private void Start()
        {
            SetupCameraAndFog();
            BuildSkyDome();
            BuildFloor();
            BuildGlowPool();
            BuildPylonRing();
            BuildDust();
            BuildDemoCube();
            SetupLighting();
        }

        // Only touches render settings and the camera's clear color here - never its
        // transform. The camera lives on the XR rig's TrackedPoseDriver (real head
        // tracking); forcing position/rotation on it every Start() would fight that
        // and make the view feel static/wrong the moment a headset moves its head.
        private void SetupCameraAndFog()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = SkyHorizon;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = SkyHorizon;
            RenderSettings.fogDensity = 0.045f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color32(16, 20, 36, 255);
        }

        // A large inverted sphere with a vertical top/horizon gradient texture,
        // approximating the shader-based sky dome in xrManager.js's buildSkyDome().
        private void BuildSkyDome()
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Sky Dome";
            dome.transform.SetParent(transform, false);
            dome.transform.position = Vector3.zero;
            Destroy(dome.GetComponent<Collider>());

            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 64; y++)
            {
                var t = y / 63f;
                tex.SetPixel(0, y, Color.Lerp((Color)SkyHorizon, (Color)SkyTop, t));
            }
            tex.Apply();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            var renderer = dome.GetComponent<Renderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Flip normals inward (invert the sphere) by negating scale on one axis
            // so we see the inside of the dome from within it.
            dome.transform.localScale = new Vector3(-60f, 60f, 60f);
        }

        // Tileable grid texture (fine lines + bolder major lines every 4 cells) -
        // matches buildFloorTexture() in xrManager.js.
        private void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(2f, 1f, 2f); // Unity plane is 10x10 at scale 1 -> 20x20

            var size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            Color baseColor = new Color32(20, 26, 38, 255);
            Color minorLine = new Color32(120, 150, 210, 90);
            Color majorLine = new Color32(91, 140, 255, 140);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = baseColor;

            const int step = 16;
            for (var x = 0; x < size; x += step)
                for (var y = 0; y < size; y++)
                    pixels[y * size + x] = (x % (step * 4) == 0) ? majorLine : minorLine;
            for (var y = 0; y < size; y += step)
                for (var x = 0; x < size; x++)
                    pixels[y * size + x] = (y % (step * 4) == 0) ? majorLine : minorLine;

            tex.SetPixels(pixels);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0.35f);
            mat.mainTextureScale = new Vector2(5f, 5f);
            var renderer = floor.GetComponent<Renderer>();
            renderer.material = mat;
        }

        // A soft glowing "spotlight pool" under the play area - kept as a simple
        // opaque tinted disc (robust across shader/render-pipeline versions)
        // rather than fighting URP's transparent-material script API.
        private void BuildGlowPool()
        {
            var pool = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pool.name = "Glow Pool";
            pool.transform.SetParent(transform, false);
            pool.transform.position = new Vector3(0f, 0.015f, -1f);
            pool.transform.localScale = new Vector3(4.5f, 0.001f, 4.5f);
            Destroy(pool.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var glow = new Color32(30, 40, 66, 255);
            mat.SetColor("_BaseColor", glow);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", (Color)new Color32(91, 140, 255, 255) * 0.08f);
            var renderer = pool.GetComponent<Renderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // A ring of glowing accent pylons around the perimeter, matching
        // buildPylonRing() in xrManager.js.
        private void BuildPylonRing()
        {
            var ringGO = new GameObject("Pylon Ring");
            ringGO.transform.SetParent(transform, false);

            const int count = 8;
            const float radius = 6.5f;
            for (var i = 0; i < count; i++)
            {
                var angle = (i / (float)count) * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius - 1f);
                BuildPylon(ringGO.transform, pos, AccentColors[i % AccentColors.Length]);
            }
        }

        private void BuildPylon(Transform parent, Vector3 position, Color color)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Pylon";
            post.transform.SetParent(parent, false);
            post.transform.position = position + new Vector3(0f, 1f, 0f);
            post.transform.localScale = new Vector3(0.12f, 1f, 0.12f);
            Destroy(post.GetComponent<Collider>());
            var postRenderer = post.GetComponent<Renderer>();
            postRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color32(27, 35, 52, 255)
            };

            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cap.name = "Pylon Cap";
            cap.transform.SetParent(parent, false);
            cap.transform.position = position + new Vector3(0f, 2.05f, 0f);
            cap.transform.localScale = Vector3.one * 0.18f;
            Destroy(cap.GetComponent<Collider>());
            var capMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            capMat.color = color;
            capMat.EnableKeyword("_EMISSION");
            capMat.SetColor("_EmissionColor", color * 2.2f);
            cap.GetComponent<Renderer>().material = capMat;
        }

        // Slow-drifting dust motes, matching buildDust() in xrManager.js.
        private void BuildDust()
        {
            var dustGO = new GameObject("Dust");
            dustGO.transform.SetParent(transform, false);
            _dust = dustGO.AddComponent<ParticleSystem>();

            var main = _dust.main;
            main.loop = true;
            main.startLifetime = 40f;
            main.startSpeed = 0.06f;
            main.startSize = 0.04f;
            main.startColor = (Color)new Color32(143, 179, 255, 90);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _dust.emission;
            emission.rateOverTime = 6f;

            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14f, 0.1f, 14f);
            shape.position = new Vector3(0f, 0f, -1f);

            var velocityOverLifetime = _dust.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            // All three axes must share the same MinMaxCurve mode (TwoConstants here) -
            // mixing e.g. MinMaxCurve(0f) (Constant) with MinMaxCurve(min,max) (TwoConstants)
            // triggers "Particle Velocity curves must all be in the same mode".
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var renderer = _dust.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            renderer.material = mat;
        }

        // Placeholder floating "lesson content" cube, matching demoCube in
        // xrManager.js (a stand-in for a real lesson model).
        private void BuildDemoCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Demo Cube";
            cube.transform.SetParent(transform, false);
            cube.transform.position = new Vector3(0f, 1f, -1.5f);
            cube.transform.localScale = Vector3.one * 0.6f;
            Destroy(cube.GetComponent<Collider>());

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color32(79, 140, 255, 255);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", (Color)new Color32(28, 58, 138, 255) * 1.5f);
            cube.GetComponent<Renderer>().material = mat;

            cube.AddComponent<DemoCubeSpin>();
        }

        private void SetupLighting()
        {
            var dirLightGO = GameObject.Find("Directional Light");
            if (dirLightGO != null)
            {
                var light = dirLightGO.GetComponent<Light>();
                if (light != null)
                {
                    light.color = new Color32(207, 224, 255, 255);
                    light.intensity = 1.3f;
                    dirLightGO.transform.position = new Vector3(3f, 6f, 2f);
                    dirLightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                }
            }

            var rimGO = new GameObject("Rim Light");
            rimGO.transform.SetParent(transform, false);
            rimGO.transform.position = new Vector3(0f, 2.6f, -3f);
            var rim = rimGO.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color32(91, 140, 255, 255);
            rim.intensity = 3f;
            rim.range = 14f;
        }
    }

    // Small spin so the demo cube reads as "alive" rather than a static
    // placeholder - mirrors the idle motion in xrManager.js's environment.
    public class DemoCubeSpin : MonoBehaviour
    {
        private void Update()
        {
            transform.Rotate(Vector3.up, 20f * Time.deltaTime, Space.World);
            transform.Rotate(Vector3.right, 12f * Time.deltaTime, Space.World);
        }
    }
}
