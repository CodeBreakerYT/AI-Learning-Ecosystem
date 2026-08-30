using UnityEngine;
using System.Collections;

public class NewtonThirdLawCars : MonoBehaviour
{
    [Header("Objects")]
    public GameObject carObj;
    public GameObject truckObj;

    public Rigidbody carRB;
    public Rigidbody truckRB;

    public Transform carStart;
    public Transform truckStart;

    [Header("UI (Already has text)")]
    public GameObject carTextObj;
    public GameObject truckTextObj;

    [Header("Settings")]
    public float forceAmount = 10f;

    private bool triggered = false;

    private void Start()
    {
        carObj.SetActive(false);
        truckObj.SetActive(false);

        // 👇 keep text hidden at start
        if (carTextObj) carTextObj.SetActive(false);
        if (truckTextObj) truckTextObj.SetActive(false);

        SetupRigidbody(carRB);
        SetupRigidbody(truckRB);
    }

    void SetupRigidbody(Rigidbody rb)
    {
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.05f;

        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (triggered) return;

        triggered = true;
        StartCoroutine(StartDemo());
    }

    IEnumerator StartDemo()
    {
        // activate vehicles
        carObj.SetActive(true);
        truckObj.SetActive(true);

        // reset positions
        carRB.position = carStart.position;
        truckRB.position = truckStart.position;

        carRB.linearVelocity = Vector3.zero;
        truckRB.linearVelocity = Vector3.zero;

        yield return new WaitForSeconds(1f);

        // move until collision
        bool hasCollided = false;
        float currentForce = forceAmount * 0.2f;

        while (!hasCollided)
        {
            currentForce = Mathf.Lerp(currentForce, forceAmount, Time.deltaTime * 2f);

            carRB.AddForce(Vector3.right * currentForce, ForceMode.Force);
            truckRB.AddForce(Vector3.left * currentForce, ForceMode.Force);

            if (Vector3.Distance(carRB.position, truckRB.position) < 1.5f)
                hasCollided = true;

            yield return null;
        }

        // small impact push
        carRB.AddForce(Vector3.right * forceAmount, ForceMode.Impulse);
        truckRB.AddForce(Vector3.left * forceAmount, ForceMode.Impulse);

        yield return new WaitForSeconds(0.3f);

        // ✅ JUST ENABLE TEXT OBJECTS
        if (carTextObj) carTextObj.SetActive(true);
        if (truckTextObj) truckTextObj.SetActive(true);
    }
}