using UnityEngine;

public class FinishTrigger : MonoBehaviour
{
    public NewtonSimulate2 race;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Car"))
        {
            race.OnFinishTrigger();
        }
    }
}