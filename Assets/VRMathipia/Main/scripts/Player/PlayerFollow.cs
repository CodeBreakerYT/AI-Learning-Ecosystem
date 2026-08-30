using UnityEngine;

public class PlayerFollow : MonoBehaviour
{
    public Transform player; // XR Rig root or Camera Offset

    [Header("Offset from Player")]
    public Vector3 offset = new Vector3(1.5f, 1.5f, -0.5f);

    [Header("Rotation")]
    public bool facePlayer = true;
    public float rotationSpeed = 5f;

    private Quaternion modelOffset;

    [Header("Model Rotation Offset")]
    public Vector3 rotationOffsetEuler;

    void Start()
    {
        // Parent robot to player
        transform.SetParent(player);

        // Set initial offset position
        transform.localPosition = offset;

        modelOffset = Quaternion.Euler(rotationOffsetEuler);
    }

    void LateUpdate()
    {
        // Maintain exact offset (no drifting)
        transform.localPosition = offset;

        if (facePlayer)
        {
            Vector3 dir = player.position - transform.position;
            dir.y = 0;

            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir) * modelOffset;

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRot,
                    Time.deltaTime * rotationSpeed
                );
            }
        }
    }
}
