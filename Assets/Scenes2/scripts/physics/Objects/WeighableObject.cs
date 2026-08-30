using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class WeighableObject : MonoBehaviour
{
    private XRGrabInteractable grab;
    private Rigidbody rb;

    [Header("Seesaw Detection")]
    public string seesawTag = "Seesaw";

    private bool isOnSeesaw = false;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        grab.selectExited.AddListener(OnRelease); // when player releases
    }

    void OnRelease(SelectExitEventArgs args)
    {
        rb.isKinematic = false;
        rb.useGravity = true;

        // 🚨 CRITICAL: kill XR throw velocity
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // small downward force so it settles
        rb.AddForce(Vector3.down * 0.5f, ForceMode.Impulse);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(seesawTag))
        {
            isOnSeesaw = true;
            Debug.Log(gameObject.name + " placed on seesaw");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(seesawTag))
        {
            isOnSeesaw = false;
        }
    }

    public bool IsOnSeesaw()
    {
        return isOnSeesaw;
    }
}