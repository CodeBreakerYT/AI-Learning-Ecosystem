using UnityEngine;

public class PushButtonHandler : MonoBehaviour
{
    public Transform buttonTop;
    public float pressDepth = 0.02f;
    public float returnSpeed = 10f;

    private Vector3 initialLocalPos;
    private bool isPressed = false;

    public NewtonSimulate sim; // drag your NewtonSimulate here

    void Start()
    {
        initialLocalPos = buttonTop.localPosition;
    }

    void Update()
    {
        // Smoothly return to original position
        buttonTop.localPosition = Vector3.Lerp(
            buttonTop.localPosition,
            initialLocalPos,
            Time.deltaTime * returnSpeed
        );
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Hand") || other.CompareTag("Button"))
        {
            if (!isPressed)
            {
                isPressed = true;

                // Push down
                buttonTop.localPosition = initialLocalPos - new Vector3(0, pressDepth, 0);

                OnPressed();

                Invoke(nameof(ResetPress), 0.3f);
            }
        }
    }

    void ResetPress()
    {
        isPressed = false;
    }

    void OnPressed()
    {
        Debug.Log("Button Pressed!");

        if (sim != null)
            sim.OnButtonPressed();
    }
}