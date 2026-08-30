using UnityEngine;

public class PuzzleManager : MonoBehaviour
{
    public int correctAnswerIndex;
    public DoorController door;

    public void SubmitAnswer(int index)
    {
        if (index == correctAnswerIndex)
        {
            Debug.Log("Correct!");
            door.OpenDoor();
            RoomManager.Instance.CompleteRoom();
        }
        else
        {
            Debug.Log("Wrong Answer!");
        }
    }
}