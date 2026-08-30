using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorController2 : MonoBehaviour
{
    [Header("Door Movement")]
    public Vector3 openOffset = new Vector3(0, 3f, 0); // how much the door moves up
    public float openSpeed = 2f;

    [Header("Scene")]
    public string sceneName = "LumoraMenu";

    private Vector3 closedPosition;
    private Vector3 openPosition;
    private bool isOpening = false;
    private bool playerInside = false;

    void Start()
    {
        closedPosition = transform.position;
        openPosition = closedPosition + openOffset;
    }

    void Update()
    {
        if (isOpening)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                openPosition,
                Time.deltaTime * openSpeed
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = true;
            isOpening = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = false;

            // Player passed through → load scene
            LoadMenuScene();
        }
    }

    void LoadMenuScene()
    {
        SceneManager.LoadScene(sceneName);
    }
}