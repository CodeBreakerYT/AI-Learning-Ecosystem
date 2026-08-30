using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class VRButton : MonoBehaviour
{
    public int answerIndex;
    public PuzzleManager puzzle;

    public void OnPressed()
    {
        puzzle.SubmitAnswer(answerIndex);
    }
}