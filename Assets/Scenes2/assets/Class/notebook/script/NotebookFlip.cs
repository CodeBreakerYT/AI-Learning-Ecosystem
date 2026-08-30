using UnityEngine;

public class NotebookFlip : MonoBehaviour
{
    public Animator animator;

    // Flip forward
    public void FlipPage()
    {
        animator.Play("Scene");
    }

    // Flip backward
    public void FlipBack()
    {
        animator.Play("Scene 0");
    }
}