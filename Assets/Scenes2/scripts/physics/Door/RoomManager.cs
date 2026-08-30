using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance;

    public int currentRoom = 0;

    private void Awake()
    {
        Instance = this;
    }

    public void CompleteRoom()
    {
        currentRoom++;
        Debug.Log("Room Completed! Now entering Room: " + currentRoom);
    }
}