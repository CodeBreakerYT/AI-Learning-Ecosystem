using UnityEngine;

public class ButtonTrigger : MonoBehaviour
{
    public NewtonSimulate sim;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Button"))
        {
            sim.OnButtonPressed();
        }
    }
}