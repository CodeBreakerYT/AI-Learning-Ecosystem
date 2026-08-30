using UnityEngine;
using TMPro;
using System.Collections;

public class NewtonSimulate2 : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI displayText;
    public float typingSpeed = 0.04f;

    [Header("Race Objects")]
    public Rigidbody car;
    public Rigidbody truck;

    public Transform carStart;
    public Transform truckStart;

    [Header("Race Settings")]
    public float baseSpeed = 5f;
    public float carAcceleration = 3f;   // car gains speed faster
    public float truckAcceleration = 1.5f;

    [Header("Finish UI")]
    public TextMeshProUGUI finishText; // separate UI for finish
    public float zoomSpeed = 2f;
    public float fadeSpeed = 2f;

    private bool raceStarted = false;
    private bool finished = false;

    private Vector3 finishScaleStart;
    private Color finishColorStart;

    void Start()
    {
        // reset UI
        displayText.text = "";
        finishText.text = "";
        finishText.transform.localScale = Vector3.one;
        finishColorStart = finishText.color;

        // reset cars
        ResetCars();
    }

    void ResetCars()
    {
        // 🚫 disable physics FIRST - a kinematic body ignores velocity, so
        // leave it zeroed at rest instead of assigning to it (assigning
        // triggers Unity's "not supported" warning every reset).
        car.isKinematic = true;
        truck.isKinematic = true;

        // set exact positions
        car.transform.position = carStart.position;
        truck.transform.position = truckStart.position;

        car.transform.rotation = carStart.rotation;
        truck.transform.rotation = truckStart.rotation;
    }
    // ================= TRIGGER START =================
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (raceStarted) return;

        raceStarted = true;
        StartCoroutine(RaceSequence());
    }

    IEnumerator RaceSequence()
    {
        yield return StartCoroutine(TypeText("Get Ready"));

        yield return Countdown("3");
        yield return Countdown("2");
        yield return Countdown("1");
        yield return Countdown("GO!");

        StartRace();
    }

    IEnumerator Countdown(string msg)
    {
        displayText.text = msg;
        yield return new WaitForSeconds(1f);
    }

    void StartRace()
    {
        StartCoroutine(RaceMovement());
    }

    IEnumerator RaceMovement()
    {
        // switch to physics-driven movement now that the race is live
        car.isKinematic = false;
        truck.isKinematic = false;

        float carSpeed = baseSpeed;
        float truckSpeed = baseSpeed;

        while (!finished)
        {
            // car accelerates faster
            carSpeed += carAcceleration * Time.deltaTime;
            truckSpeed += truckAcceleration * Time.deltaTime;

            car.linearVelocity = Vector3.forward * carSpeed;
            truck.linearVelocity = Vector3.forward * truckSpeed;

            yield return null;
        }
    }

    // ================= FINISH TRIGGER =================
    public void OnFinishTrigger()
    {
        if (finished) return;

        finished = true;

        // stop movement, then hold position
        car.linearVelocity = Vector3.zero;
        truck.linearVelocity = Vector3.zero;
        car.isKinematic = true;
        truck.isKinematic = true;

        StartCoroutine(FinishSequence());
    }

    IEnumerator FinishSequence()
    {
        // zoom + fade text
        finishText.text = "FINISH!";
        finishText.color = new Color(1, 1, 1, 0);

        float t = 0;

        while (t < 1f)
        {
            t += Time.deltaTime * fadeSpeed;

            finishText.transform.localScale = Vector3.one * (1 + t * zoomSpeed);
            finishText.color = new Color(1, 1, 1, t);

            yield return null;
        }

        yield return new WaitForSeconds(1f);

        // typewriter winner text
        yield return StartCoroutine(TypeFinishText("Car is the winner"));
    }

    IEnumerator TypeFinishText(string msg)
    {
        finishText.text = "";

        foreach (char c in msg)
        {
            finishText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
    }

    // ================= TYPEWRITER =================
    IEnumerator TypeText(string msg)
    {
        displayText.text = "";

        foreach (char c in msg)
        {
            displayText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
    }
}