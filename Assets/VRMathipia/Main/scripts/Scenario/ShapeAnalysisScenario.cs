using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR;
using UnityEngine.UI;

public class ShapeAnalysisScenario : MonoBehaviour
{
    [Header("Dialogue")]
    public GameObject dialogueCanvas;
    public TextMeshProUGUI dialogueText;

    [Header("Title Canvas")]
    public CanvasGroup titleCanvas;
    public float fadeDuration = 1f;

    [Header("Big Screen")]
    public GameObject bigScreen;
    public Animator bigScreenAnimator;

    [Header("Monitor")]
    public GameObject monitorCanvas;
    public TextMeshProUGUI monitorText;

    [Header("Warning system")]
    public GameObject warningObject; // can be UI panel / image
    public RawImage warningImage;
    public float minAlpha = 0.5f;
    public float maxAlpha = 1f;
    public float pulseSpeed = 2f;
    public GameObject siren;
    public AudioSource warningAudio;
    private bool isPulsing = false;

    [Header("Robot Movement")]
    public RobotFloatFollow robotFollow;
    public Transform robot;
    public Transform[] waypoints;
    public float moveSpeed = 1.5f;
    public float rotationSpeed = 5f;
    public Vector3 rotationOffsetEuler;
    
    [Header("Player")]
    public Transform player;

    [Header("Next Scenario")]
    public _3DCoordinateScenario coordinateScenario;
    public RayShooter rayShooter;
    
    [Header("Hologram Objects")]
    public GameObject hologramCanvasOrText;
    public GameObject hologramShip;
    public GameObject hologramMeteorite;
    public Transform hologramSpawnPoint1;
    public Transform hologramSpawnPoint2;

    [Header("Realtime Mesh Recreation")]
    public int numberOfRays = 100;
    public float scanDuration = 2f;
    public Transform targetMeteoriteObj;
    public Transform rayOrigin;
    public Transform reconstructionSpawnPoint;
    public float reconstructionHeightOffset = 1.2f;
    public SphereCollider constraintCollider;
    public GameObject dotPrefab;
    public Material rayMaterial;
    public float ellipsoidA = 2f;
    public float ellipsoidB = 1.5f;
    public float ellipsoidC = 2f;

    private InputDevice rightHand;
    private bool lastButtonState = false;

    private bool waitForNext = false;
    private bool waitingForInput = false;

    void Start()
    {
        if (dialogueCanvas != null) dialogueCanvas.SetActive(false);
        if (monitorCanvas != null) monitorCanvas.SetActive(false);
        if (bigScreenAnimator != null) bigScreenAnimator.enabled = false;
        
        if (warningObject != null) warningObject.SetActive(false);
        if (warningImage != null) warningImage.gameObject.SetActive(false);
        if (siren != null) siren.SetActive(false);
        if (warningAudio != null) warningAudio.Stop();
        if (bigScreen != null) bigScreen.SetActive(false);
        if (hologramCanvasOrText != null) hologramCanvasOrText.SetActive(false);
        if (hologramShip != null) hologramShip.SetActive(false);
        if (hologramMeteorite != null) hologramMeteorite.SetActive(false);
        if (titleCanvas != null) titleCanvas.gameObject.SetActive(false);

        TryInitDevice();
    }

    void Update()
    {
        if (!rightHand.isValid)
            TryInitDevice();

        bool pressed;
        if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed))
        {
            if (pressed && !lastButtonState)
            {
                if (waitForNext)
                {
                    waitForNext = false;
                }
            }

            lastButtonState = pressed;
        }
    }

    void TryInitDevice()
    {
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    // 🔥 CALLED FROM DOOR
    public void StartScenario()
    {
        StartCoroutine(IntroSequence());
    }

    IEnumerator IntroSequence()
    {
        if (bigScreenAnimator != null)
            bigScreenAnimator.enabled = true;

        if (titleCanvas != null)
        {
            titleCanvas.gameObject.SetActive(true);
            titleCanvas.alpha = 0;
            yield return StartCoroutine(FadeCanvas(0, 1));
            yield return new WaitForSeconds(1.5f);
            yield return StartCoroutine(FadeCanvas(1, 0));
            titleCanvas.gameObject.SetActive(false);
        }

        if (bigScreen != null)
        {
            bigScreen.SetActive(true);
            yield return new WaitForSeconds(1.5f);
        }

        StartWarning();

        dialogueCanvas.SetActive(true);

        yield return StartCoroutine(TypeText("Oh no!"));
        yield return StartCoroutine(TypeText("Meteorite is coming this way"));
        yield return StartCoroutine(TypeText("Come with me"));

        StartCoroutine(MoveSequence());
    }

    IEnumerator MoveSequence()
    {
        dialogueCanvas.SetActive(false);

        if (robotFollow != null) 
        {
            robotFollow.canFollow = false;
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
                yield return StartCoroutine(MoveToPoint(waypoints[i]));
        }

        if (player != null)
        {
            yield return StartCoroutine(SmoothLookAt(player.position));
        }

        dialogueCanvas.SetActive(true);

        // Text specified by user logic
        yield return StartCoroutine(TypeText("Use this RayGun"));
        yield return StartCoroutine(TypeText("Scan the meteor to measure its shape"));

        dialogueCanvas.SetActive(false);
    }

    IEnumerator MoveToPoint(Transform target)
    {
        while (Vector3.Distance(robot.position, target.position) > 0.05f)
        {
            robot.position = Vector3.MoveTowards(
                robot.position,
                target.position,
                moveSpeed * Time.deltaTime
            );

            Vector3 dir = (target.position - robot.position).normalized;
            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir) * Quaternion.Euler(rotationOffsetEuler);
                robot.rotation = Quaternion.Slerp(robot.rotation, targetRot, Time.deltaTime * rotationSpeed);
            }

            yield return null;
        }

        robot.position = target.position;
    }

    IEnumerator SmoothLookAt(Vector3 targetPos)
    {
        Vector3 dir = targetPos - robot.position;
        dir.y = 0; // Keep rotation strictly horizontal

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir) * Quaternion.Euler(rotationOffsetEuler);

            while (Quaternion.Angle(robot.rotation, targetRot) > 1f)
            {
                robot.rotation = Quaternion.Slerp(robot.rotation, targetRot, Time.deltaTime * rotationSpeed);
                yield return null;
            }

            robot.rotation = targetRot;
        }
    }

    // 🔥 CALLED FROM RAY SHOOTER
    public void OnShapeScanComplete(List<Vector3> points)
    {
        StartCoroutine(ShapeSequence(points));
    }

    IEnumerator ShapeSequence(List<Vector3> points)
    {
        monitorCanvas.SetActive(true);
        monitorText.text = "";

        if (hologramCanvasOrText != null) hologramCanvasOrText.SetActive(true);
        if (hologramShip != null) 
        {
            if (hologramSpawnPoint1 != null)
                hologramShip.transform.position = hologramSpawnPoint1.position;
            hologramShip.SetActive(true);
        }
        if (hologramMeteorite != null) 
        {
            if (hologramSpawnPoint2 != null)
                hologramMeteorite.transform.position = hologramSpawnPoint2.position;
            hologramMeteorite.SetActive(true);
        }

        yield return StartCoroutine(TypeMonitor("--- HOLOGRAM RENDERED ---\n\n"));
        yield return StartCoroutine(WaitNext());

        yield return StartCoroutine(TypeMonitor("--- RADAR TELEMETRY ESTABLISHED ---\n\n"));
        yield return StartCoroutine(TypeMonitor("FORMULAS TO BE USED:\n"));
        yield return StartCoroutine(TypeMonitor("1. d=(c*(delta)t)/2\n"));
        yield return StartCoroutine(TypeMonitor("2. P=A+d.D (D -> direction of ray)\n"));
        yield return StartCoroutine(TypeMonitor("3. signal strength I∝1/d²\n"));
        yield return StartCoroutine(TypeMonitor("4. Radar equation\n\n"));

        yield return StartCoroutine(WaitNext());
        
        yield return StartCoroutine(TypeMonitor("--- INITIATING MESH RECREATION ---\n\n"));
        yield return StartCoroutine(TypeMonitor($"Sending {numberOfRays} rays...\n\n"));

        Vector3 center = targetMeteoriteObj != null ? targetMeteoriteObj.position : Vector3.zero;
        Vector3 originPos = rayOrigin != null ? rayOrigin.position : (player != null ? player.position : Vector3.zero);

        float delay = scanDuration / Mathf.Max(1, numberOfRays);

        List<GameObject> spawnedDots = new List<GameObject>();

        for (int i = 0; i < numberOfRays; i++)
        {
            // Random point on ellipsoid
            // We use spherical coordinates to generate points on surface
            float u = UnityEngine.Random.Range(0f, 1f);
            float v = UnityEngine.Random.Range(0f, 1f);
            float theta = u * 2.0f * Mathf.PI;
            float phi = Mathf.Acos(2.0f * v - 1.0f);
            
            float x = ellipsoidA * Mathf.Sin(phi) * Mathf.Cos(theta);
            float y = ellipsoidB * Mathf.Sin(phi) * Mathf.Sin(theta);
            float z = ellipsoidC * Mathf.Cos(phi);

            Vector3 hitPoint = center + (targetMeteoriteObj != null ? targetMeteoriteObj.rotation * new Vector3(x,y,z) : new Vector3(x,y,z));

            // Visuals
            StartCoroutine(ShootRayEffect(originPos, hitPoint));

            if (dotPrefab != null)
            {
                Vector3 dotPos = hitPoint;
                if (reconstructionSpawnPoint != null)
                {
                    dotPos = reconstructionSpawnPoint.position + new Vector3(0, reconstructionHeightOffset, 0) + (reconstructionSpawnPoint.rotation * new Vector3(x, y, z));
                }
                
                // Keep the hologram dots from going outside the serialized sphere bounds boundaries
                if (constraintCollider != null)
                {
                    dotPos = constraintCollider.ClosestPoint(dotPos);
                }
                
                GameObject dot = Instantiate(dotPrefab, dotPos, Quaternion.identity);
                spawnedDots.Add(dot);
            }

            // Print some data dynamically
            if (i % Mathf.Max(1, (numberOfRays / 5)) == 0 || i == numberOfRays - 1)
            {
                float d = Vector3.Distance(originPos, hitPoint);
                float c = 299792458f;
                float dt = (2f * d) / c;
                float I = 1f / (d * d);

                monitorText.text = "--- REAL-TIME ACQUISITION ---\n\n";
                monitorText.text += $"Ray [{i+1}/{numberOfRays}]\n";
                monitorText.text += $"(delta)t = {dt:e2} s\n";
                monitorText.text += $"d = {d:F2} m\n";
                monitorText.text += $"I ∝ {I:e2}\n\n";
            }

            yield return new WaitForSeconds(delay);
        }

        yield return StartCoroutine(WaitNext());

        Vector3 sampleTarget = spawnedDots.Count > 0 ? spawnedDots[spawnedDots.Count-1].transform.position : center; 
        Vector3 sampleDir = (sampleTarget - originPos).normalized;
        float sampleMag = Vector3.Distance(originPos, sampleTarget);

        yield return StartCoroutine(TypeMonitor("--- 1. VECTOR ANALYSIS ---\n\n"));
        yield return StartCoroutine(TypeMonitor("Vector = Magnitude * Direction\n"));
        yield return StartCoroutine(TypeMonitor("Direction (D) = (P_hit - P_origin).normalized\n"));
        yield return StartCoroutine(TypeMonitor("Position (V) = P_origin + (Magnitude * D)\n\n"));
        yield return StartCoroutine(TypeMonitor($"Sample Ray D: {sampleDir.x:F1}, {sampleDir.y:F1}, {sampleDir.z:F1}\n"));
        yield return StartCoroutine(TypeMonitor($"Sample Magnitude: {sampleMag:F2} m\n\n"));
        
        yield return StartCoroutine(WaitNext());

        yield return StartCoroutine(TypeMonitor("--- 2. GEOMETRY MODELLING ---\n\n"));
        yield return StartCoroutine(TypeMonitor("Analyzing Point Cloud Matrix...\n"));
        yield return StartCoroutine(TypeMonitor("Vector limits match Ellipsoid boundaries.\n\n"));
        yield return StartCoroutine(TypeMonitor("Extracted Semi-axes:\n"));
        yield return StartCoroutine(TypeMonitor($"a = {ellipsoidA:F2}\n"));
        yield return StartCoroutine(TypeMonitor($"b = {ellipsoidB:F2}\n"));
        yield return StartCoroutine(TypeMonitor($"c = {ellipsoidC:F2}\n\n"));

        yield return StartCoroutine(WaitNext());
        
        yield return StartCoroutine(TypeMonitor("--- 3. VOLUME CALCULATIONS ---\n\n"));
        yield return StartCoroutine(TypeMonitor("V = (4/3) * π * a * b * c\n\n"));
        float volume = (4f/3f) * Mathf.PI * ellipsoidA * ellipsoidB * ellipsoidC;
        yield return StartCoroutine(TypeMonitor($"V = 1.333 * 3.141 * {ellipsoidA:F2} * {ellipsoidB:F2} * {ellipsoidC:F2}\n\n"));
        
        yield return StartCoroutine(WaitNext());

        yield return StartCoroutine(TypeMonitor("--- 4. SURFACE AREA ---\n\n"));
        yield return StartCoroutine(TypeMonitor("A ≈ 4π * [((ab)^1.6 + (ac)^1.6 + (bc)^1.6)/3]^(1/1.6)\n\n"));
        
        float p = 1.6f;
        float part1 = Mathf.Pow(ellipsoidA * ellipsoidB, p);
        float part2 = Mathf.Pow(ellipsoidA * ellipsoidC, p);
        float part3 = Mathf.Pow(ellipsoidB * ellipsoidC, p);
        float surfaceArea = 4f * Mathf.PI * Mathf.Pow((part1 + part2 + part3) / 3f, 1f / p);

        yield return StartCoroutine(TypeMonitor("Applying Knud Thomsen approximation...\n\n"));
        
        yield return StartCoroutine(WaitNext());

        yield return StartCoroutine(TypeMonitor("--- FINAL RECORDED RESULTS ---\n\n"));
        yield return StartCoroutine(TypeMonitor("Shape Type: Ellipsoid\n"));
        yield return StartCoroutine(TypeMonitor($"Avg Ray Magnitude: {Vector3.Distance(originPos, center):F2} m\n"));
        yield return StartCoroutine(TypeMonitor($"Calculated Volume: {volume:F2} m³\n"));
        yield return StartCoroutine(TypeMonitor($"Calculated Surface Area: {surfaceArea:F2} m²\n\n"));
        
        yield return StartCoroutine(WaitNext());

        monitorText.text = "";
        yield return StartCoroutine(TypeMonitor("Ready to proceed..."));

        yield return StartCoroutine(WaitNext());
        
        monitorCanvas.SetActive(false);
        StopWarning();

        if (rayShooter != null)
        {
            rayShooter.ResetHit();
            rayShooter.currentMode = RayShooter.RayMode.Precision;
        }

        if (coordinateScenario != null)
        {
            coordinateScenario.StartScenario();
        }
    }

    IEnumerator ShootRayEffect(Vector3 start, Vector3 end)
    {
        GameObject lineObj = new GameObject("RadarRayVisual");
        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startWidth = 0.02f;
        lr.endWidth = 0.02f;
        
        if (rayMaterial != null) lr.material = rayMaterial;
        else lr.material = new Material(Shader.Find("Sprites/Default"));
        
        lr.startColor = Color.green;
        lr.endColor = new Color(0, 1, 0, 0);

        float duration = 0.15f;
        float elapsed = 0f;
        while(elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / duration);
            lr.startColor = new Color(0, 1, 0, alpha);
            yield return null;
        }
        
        Destroy(lineObj);
    }

    // ================= WARNING =================

    void StartWarning()
    {
        if (warningObject != null)
            warningObject.SetActive(true);

        if (warningImage != null)
        {
            warningImage.gameObject.SetActive(true);
            StartCoroutine(PulseWarning());
        }

        if (siren != null)
            siren.SetActive(true);

        if (warningAudio != null)
        {
            warningAudio.loop = true;
            warningAudio.Play();
        }
    }

    void StopWarning()
    {
        isPulsing = false;

        if (warningObject != null)
            warningObject.SetActive(false);

        if (warningImage != null)
            warningImage.gameObject.SetActive(false);

        if (siren != null)
            siren.SetActive(false);

        if (warningAudio != null)
            warningAudio.Stop();
    }

    IEnumerator FadeCanvas(float start, float end)
    {
        float time = 0;
        while (time < fadeDuration)
        {
            if (titleCanvas != null)
                titleCanvas.alpha = Mathf.Lerp(start, end, time / fadeDuration);
                
            time += Time.deltaTime;
            yield return null;
        }
        if (titleCanvas != null)
            titleCanvas.alpha = end;
    }

    IEnumerator PulseWarning()
    {
        isPulsing = true;
        if (warningImage == null) yield break;

        Color baseColor = warningImage.color;

        while (isPulsing)
        {
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, Mathf.PingPong(Time.time * pulseSpeed, 1));
            warningImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            yield return null;
        }
    }

    // ================= TYPEWRITER =================

    IEnumerator TypeText(string text)
    {
        dialogueCanvas.SetActive(true);
        dialogueText.text = "";

        foreach (char c in text)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(0.03f);
        }

        waitForNext = true;
        yield return new WaitUntil(() => waitForNext == false);
    }

    IEnumerator TypeMonitor(string text)
    {
        foreach (char c in text)
        {
            monitorText.text += c;
            yield return new WaitForSeconds(0.015f);
        }

        yield return new WaitForSeconds(0.3f);
    }

    IEnumerator WaitNext()
    {
        waitForNext = true;
        yield return new WaitUntil(() => waitForNext == false);
        monitorText.text = "";
    }
}