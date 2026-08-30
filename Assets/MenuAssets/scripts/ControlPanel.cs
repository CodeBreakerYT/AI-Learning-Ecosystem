using UnityEngine;

public class ControlPanel : MonoBehaviour
{
    [Header("Canvases")]
    [SerializeField] private GameObject controlsCanvas;
    [SerializeField] private GameObject mainCanvas; // or whatever you want disabled

    // Called by Controls button
    public void OpenControls()
    {
        if (controlsCanvas != null)
            controlsCanvas.SetActive(true);

        if (mainCanvas != null)
            mainCanvas.SetActive(false);
    }

    // Called by Back button
    public void CloseControls()
    {
        if (controlsCanvas != null)
            controlsCanvas.SetActive(false);

        if (mainCanvas != null)
            mainCanvas.SetActive(true);
    }

}
