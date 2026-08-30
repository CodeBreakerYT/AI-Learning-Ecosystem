using UnityEngine;
using UnityEngine.SceneManagement;

public class _3DScanObjects: MonoBehaviour
{
    // Call this from any button (VR / UI)
    public void LoadScanScene()
    {
    SceneManager.LoadScene(2);
    }
    // Call this from any button (VR / UI)
    public void LoadMenuScene()
    {
        SceneManager.LoadScene(0);
    }
}