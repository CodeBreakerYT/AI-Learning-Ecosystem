using UnityEngine;

public class RobotFloatFollow : MonoBehaviour
{
    public Transform player;
    public float followSpeed = 2f;
    public float height = 1.5f;

    public float sideDistance = 1.5f;
    public float backDistance = 0.5f;

    public float floatAmplitude = 0.2f;
    public float floatSpeed = 2f;

    public bool canFollow = false;
    public Vector3 rotationOffsetEuler = new Vector3(0, 90f, 0);

    private float floatOffset;
    private Quaternion modelOffset;

    void Start()
    {
        modelOffset = Quaternion.Euler(rotationOffsetEuler);
    }

    void Update()
    {
        if (!canFollow || player == null) return;

        floatOffset = Mathf.Sin(Time.time * floatSpeed) * floatAmplitude;

        Vector3 sideOffset = player.right * sideDistance;
        Vector3 backOffset = -player.forward * backDistance;

        Vector3 targetPos = player.position + sideOffset + backOffset;
        targetPos.y = player.position.y + height + floatOffset;

        transform.position = Vector3.Lerp(transform.position, targetPos, followSpeed * Time.deltaTime);

        Vector3 lookDir = player.position - transform.position;
        lookDir.y = 0;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(lookDir) * modelOffset,
            Time.deltaTime * 3f
        );
    }
}