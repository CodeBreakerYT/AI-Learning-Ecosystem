using UnityEngine;
using UnityEngine.XR;

public class PlayerInteraction : MonoBehaviour
{
    public IntroScenario scenario;
    public float interactDistance = 2f;
    public Transform robot;
    public GameObject pressBUI;

    private InputDevice rightHand;
    private bool canPress = true;

    void Start()
    {
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void Update()
    {
        if (!rightHand.isValid)
            rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        float dist = Vector3.Distance(transform.position, robot.position);

        if (dist < interactDistance && scenario.CanInteract())
        {
            pressBUI.SetActive(true);

            bool pressed;
            if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed))
            {
                if (pressed && canPress)
                {
                    canPress = false;
                    scenario.NextDialogue();
                }
                else if (!pressed)
                {
                    canPress = true;
                }
            }
        }
        else
        {
            pressBUI.SetActive(false);
        }
    }
}