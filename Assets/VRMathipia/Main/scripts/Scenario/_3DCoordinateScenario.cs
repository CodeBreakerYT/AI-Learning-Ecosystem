using UnityEngine;
using TMPro;
using UnityEngine.XR;
using System.Collections;

public class _3DCoordinateScenario : MonoBehaviour
{
    [Header("Dialogue")]
    public GameObject dialogueCanvas;
    public TextMeshProUGUI dialogueText;

    [Header("Monitor")]
    public GameObject monitorCanvas;
    public TextMeshProUGUI monitorText;

    [Header("References")]
    public Transform ship;
    public Transform meteor;
    public AudioSource missionCompleteSound;

    private InputDevice rightHand;
    private bool lastButtonState = false;

    private bool waitForNext = false;
    private bool hasCompleted = false;

    void Start()
    {
        if (dialogueCanvas != null)
            dialogueCanvas.SetActive(false);

        if (monitorCanvas != null)
            monitorCanvas.SetActive(false);

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
                    waitForNext = false;
            }

            lastButtonState = pressed;
        }
    }

    void TryInitDevice()
    {
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    // 🔥 Called after shape analysis completes
    public void StartScenario()
    {
        hasCompleted = false;
        StartCoroutine(IntroDialogue());
    }

    IEnumerator IntroDialogue()
    {
        dialogueCanvas.SetActive(true);
        yield return StartCoroutine(TypeText("Great job figuring out the shape!"));
        yield return StartCoroutine(TypeText("Use the raygun one more time."));
        yield return StartCoroutine(TypeText("We need a single precision point."));
        yield return StartCoroutine(TypeText("Aim for the center to get its coordinates."));
        dialogueCanvas.SetActive(false);
    }

    // 🔥 Called from RayShooter
    public void OnRayHitMeteor(Vector3 realHitPoint)
    {
        if (hasCompleted) return;

        hasCompleted = true;
        StartCoroutine(CalculationSequence(realHitPoint));
    }

    IEnumerator CalculationSequence(Vector3 hitPoint)
    {
        // Dialogue
        dialogueCanvas.SetActive(true);

        yield return StartCoroutine(TypeText("Great job."));
        yield return StartCoroutine(TypeText("Let's calculate coordinate"));
        yield return StartCoroutine(TypeText("of meteorite."));

        dialogueCanvas.SetActive(false);

        // Monitor start
        monitorCanvas.SetActive(true);
        monitorText.text = "";

        Vector3 A = ship.position;
        Vector3 M = hitPoint;
        Vector3 D = (M - A).normalized;
        float t = Vector3.Distance(A, M);

        // STEP 1
        yield return TypeMonitor("--- GIVEN DATA ---\n\n");
        yield return TypeMonitor($"Origin (A) = ({A.x:F1}, {A.y:F1}, {A.z:F1})\n");
        yield return TypeMonitor($"Direction (D) = ({D.x:F2}, {D.y:F2}, {D.z:F2})\n");
        yield return TypeMonitor($"Distance (t)  = {t:F2}\n\n");

        yield return new WaitForSeconds(1f);

        monitorText.text = "";
        // STEP 2
        yield return TypeMonitor("--- FORMULA ---\n\n");
        yield return TypeMonitor("P = A + t * D\n\n");

        yield return new WaitForSeconds(1f);

        monitorText.text = "";
        // STEP 3
        Vector3 P = A + (D * t);

        yield return TypeMonitor("--- CALCULATION ---\n\n");
        yield return TypeMonitor($"P.x = {A.x:F1} + ({t:F1} * {D.x:F2})\n");
        yield return TypeMonitor($"P.y = {A.y:F1} + ({t:F1} * {D.y:F2})\n");
        yield return TypeMonitor($"P.z = {A.z:F1} + ({t:F1} * {D.z:F2})\n\n");

        yield return new WaitForSeconds(1.5f);

        monitorText.text = "";
        yield return TypeMonitor("--- RESULT ---\n\n");
        yield return TypeMonitor($"Meteor Coordinate P = ({P.x:F1}, {P.y:F1}, {P.z:F1})\n\n");

        yield return WaitNext();

        monitorText.text = "";
        // FINAL
        yield return TypeMonitor("--- TARGET LOCKED ---\n\n");
        yield return TypeMonitor("Destroying meteor...");

        yield return new WaitForSeconds(1.5f);

        if (meteor != null)
            meteor.gameObject.SetActive(false);

        monitorCanvas.SetActive(false);

        if (missionCompleteSound != null)
        {
            missionCompleteSound.Play();
        }

        dialogueCanvas.SetActive(true);
        yield return StartCoroutine(TypeText("Mission Complete"));

        dialogueCanvas.SetActive(false);

        yield return new WaitForSeconds(3f);
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }

    // ================= TYPEWRITER =================

    IEnumerator TypeText(string text)
    {
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