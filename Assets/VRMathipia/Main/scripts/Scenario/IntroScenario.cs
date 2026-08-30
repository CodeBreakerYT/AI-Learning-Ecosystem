using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.XR;

public class IntroScenario : MonoBehaviour
{
    public TextMeshProUGUI dialogueText;
    public GameObject dialogueCanvas;
    public RobotFloatFollow robotFollow;

    public Transform[] waypoints;
    public Transform robot;
    public Transform player;

    [Header("Next Scenarios")]
    public ShapeAnalysisScenario shapeScenario;
    public RayShooter rayShooter;

    [Header("Rotation Offset (Fix Model Forward)")]
    public Vector3 rotationOffsetEuler;

    [Header("Rotation Settings")]
    public float rotationSpeed = 5f;

    private int step = 0;
    private bool isTyping = false;
    private bool isMoving = false;
    private bool waitingForFinalInput = false;

    private Quaternion modelOffset;

    // 🔥 INPUT
    private InputDevice rightHand;
    private bool lastButtonState = false;

    void Start()
    {
        robotFollow.canFollow = false;
        modelOffset = Quaternion.Euler(rotationOffsetEuler);

        TryInitDevice();

        StartCoroutine(IntroSequence());
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
                if (CanInteract())
                    NextDialogue();
            }

            lastButtonState = pressed;
        }
    }

    void TryInitDevice()
    {
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    public bool CanInteract()
    {
        return !isTyping && !isMoving;
    }

    IEnumerator TypeText(string text)
    {
        // 🔥 ALWAYS ensure visible
        if (!dialogueCanvas.activeSelf)
            dialogueCanvas.SetActive(true);

        isTyping = true;
        dialogueText.text = "";

        foreach (char c in text)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(0.03f);
        }

        isTyping = false;
    }

    IEnumerator IntroSequence()
    {
        dialogueCanvas.SetActive(true);

        yield return StartCoroutine(TypeText("Hey Player."));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(TypeText("Welcome to Space."));
        yield return new WaitForSeconds(0.5f);

        // 🔥 WAIT for player input after intro
    }

    void StartShapeAnalysisPhase()
    {
        this.enabled = false;

        if (rayShooter != null)
        {
            rayShooter.currentMode = RayShooter.RayMode.Shape;
            rayShooter.hasHit = false;
        }
        // Now waiting for door trigger
    }

    public void NextDialogue()
    {
        if (isTyping) return;

        if (waitingForFinalInput)
        {
            dialogueCanvas.SetActive(false);
            robotFollow.canFollow = true;
            waitingForFinalInput = false;

            StartShapeAnalysisPhase();
            return;
        }

        step++;

        if (step == 1)
            StartCoroutine(TypeText("My name is Phobo."));
        else if (step == 2)
            StartCoroutine(TypeText("Your space companion :)"));
        else if (step == 3)
            StartCoroutine(TypeText("Follow me."));
        else if (step == 4)
            StartCoroutine(StartMovementSequence());
    }

    IEnumerator StartMovementSequence()
    {
        isMoving = true;
        dialogueCanvas.SetActive(false);
        robotFollow.canFollow = false;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
                yield return StartCoroutine(MoveToPoint(waypoints[i]));
        }

        Transform lastPoint = waypoints[waypoints.Length - 1];
        robot.position = lastPoint.position;

        yield return StartCoroutine(SmoothLookAt(player.position));

        yield return new WaitForSeconds(0.3f);

        dialogueCanvas.SetActive(true);
        yield return StartCoroutine(TypeText("Pass through this door"));

        waitingForFinalInput = true;
        isMoving = false;
    }

    IEnumerator MoveToPoint(Transform target)
    {
        float speed = 3f;

        while (Vector3.Distance(robot.position, target.position) > 0.05f)
        {
            robot.position = Vector3.MoveTowards(
                robot.position,
                target.position,
                speed * Time.deltaTime
            );

            Vector3 dir = (target.position - robot.position).normalized;

            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir) * modelOffset;
                robot.rotation = Quaternion.Slerp(robot.rotation, targetRot, Time.deltaTime * rotationSpeed);
            }

            yield return null;
        }

        robot.position = target.position;
    }

    IEnumerator SmoothLookAt(Vector3 targetPos)
    {
        Vector3 dir = targetPos - robot.position;
        dir.y = 0;

        Quaternion targetRot = Quaternion.LookRotation(dir) * modelOffset;

        while (Quaternion.Angle(robot.rotation, targetRot) > 1f)
        {
            robot.rotation = Quaternion.Slerp(robot.rotation, targetRot, Time.deltaTime * rotationSpeed);
            yield return null;
        }

        robot.rotation = targetRot;
    }
}